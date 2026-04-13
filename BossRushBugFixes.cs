using Constance;
using HarmonyLib;
using Leo;
using System;
using System.Reflection;
using UnityEngine;

namespace BossRushMod
{
    // FIX 1 — Intercepts TeleportToShrine to handle boss rush flow
    [HarmonyPatch(typeof(BossRushUtils), nameof(BossRushUtils.TeleportToShrine))]
    public static class TeleportToShrinePatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (BossRushModeManager.IsActive)
            {
                bool runDone = BossRushModeManager.OnBossDefeated();
                if (!runDone) return false;
            }

            BossRushModeManager.SignalExpectedTransition();
            BossStateGuard.RestoreAll();

            try
            {
                var sound = CConSceneRegistry.Instance?.Get<IConSoundScapeManager>();
                if (sound != null)
                {
                    sound.RestoreLevelMusic(1f);
                    sound.StopAllLevelSfx();
                }
            }
            catch (Exception e) { Debug.LogWarning("[BossRushFix] Son: " + e.Message); }

            return true;
        }
    }

    // FIX 2 — Blocks void corruption debuff during PuppetHand / PuppetMaster rush fights
    public static class VoidDebuffBlockPatch
    {
        public static void Register(Harmony harmony)
        {
            var target = typeof(CConVoidLevel).GetMethod(
                "SetVoidDebuffActive",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (target == null)
            {
                Debug.LogWarning("[VoidDebuffBlock] SetVoidDebuffActive introuvable.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(VoidDebuffBlockPatch).GetMethod(
                    nameof(Prefix),
                    BindingFlags.Static | BindingFlags.Public)));
        }

        public static bool Prefix(ref bool active)
        {
            if (PuppetHandRushState.Active || PuppetMasterRushState.Active)
                active = false;
            return true;
        }
    }

    // FIX 3 — Cornelis requires the 3 headlight eyes to be activated in save data
    // before teleporting, otherwise the boss arena won't load correctly.
    [HarmonyPatch(typeof(BossTrigger), nameof(BossTrigger.PrepareAndTeleport))]
    public static class BossTriggerCornelisPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string bossId)
        {
            if (string.Equals(bossId, "CornelisBoss", StringComparison.OrdinalIgnoreCase))
                CornelisFix.TryActivateEyes();
        }
    }

    public static class CornelisFix
    {
        private static readonly ConPersistenceId[] EyeIds =
        {
            new ConPersistenceId("ps_Prod_C03_HeadlightEye"),
            new ConPersistenceId("ps_Prod_C04_HeadlightEye"),
            new ConPersistenceId("ps_Prod_C05_HeadlightEye"),
        };
        private static bool[] _originalStates;
        private static bool _originalJackie;
        private static bool _overridden = false;

        public static void TryActivateEyes()
        {
            try
            {
                var save = CConSceneRegistry.Instance?.Save;
                if (save == null) return;
                bool[] current = System.Linq.Enumerable.ToArray(
                    System.Linq.Enumerable.Select(EyeIds, id => save.GetBoolOrDefault(id, false)));
                if (System.Linq.Enumerable.All(current, v => v)) { _overridden = false; return; }
                _originalStates = current;
                _originalJackie = CConCarnivalHeadlightEye.JackieTentSpawned;
                _overridden = true;
                foreach (var id in EyeIds)
                    save.SetBool(id, true, default(PersistenceEntry.Options));
                CConCarnivalHeadlightEye.JackieTentSpawned = true;
            }
            catch (Exception e) { Debug.LogWarning("[CornelisFix] TryActivateEyes: " + e.Message); }
        }

        public static void RestoreEyes()
        {
            if (!_overridden) return;
            try
            {
                var save = CConSceneRegistry.Instance?.Save;
                if (save == null) return;
                for (int i = 0; i < EyeIds.Length; i++)
                    save.SetBool(EyeIds[i], _originalStates[i], default(PersistenceEntry.Options));
                CConCarnivalHeadlightEye.JackieTentSpawned = _originalJackie;
                _overridden = false;
            }
            catch (Exception e) { Debug.LogWarning("[CornelisFix] RestoreEyes: " + e.Message); }
        }
    }

    public class CornelisBugFixCleanupComponent : MonoBehaviour
    {
        private void OnDestroy() => CornelisFix.RestoreEyes();
    }

    [HarmonyPatch(typeof(BossRushArenaForcer), "Start")]
    public static class CornelisBugFixArmPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BossRushArenaForcer __instance)
        {
            if (string.Equals(__instance.BossId, "CornelisBoss", StringComparison.OrdinalIgnoreCase)
                && __instance.GetComponent<CornelisBugFixCleanupComponent>() == null)
                __instance.gameObject.AddComponent<CornelisBugFixCleanupComponent>();
        }
    }

    // FIX 4 — AweKing must face right when spawning, otherwise the fight breaks.
    // We override the teleport entirely to pass DirectionX.Right in the transition command.
    [HarmonyPatch(typeof(BossRushTeleporter), nameof(BossRushTeleporter.TeleportToBoss))]
    public static class AweKingTeleportPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(string bossId, bool forceReload, ref bool __result)
        {
            if (!string.Equals(bossId, "AweKing", StringComparison.OrdinalIgnoreCase))
                return true;

            const string cpStr = "cp_Prod_A16_08a800e0-9b00-11ef-b44d-37183fa010d2";
            var checkpointId = new ConCheckPointId(cpStr);
            if (!checkpointId.IsValid()) { __result = false; return false; }

            ConLevelId levelId = checkpointId.ExtractLevelId();
            if (!levelId.IsValid()) { __result = false; return false; }

            CConSceneRegistry.Instance.CheckPointManager.Check(checkpointId);

            var layerType = typeof(ConTransitionFadeInConfig)
                .GetConstructors()[0].GetParameters()[2].ParameterType;
            var fadeConfig = new ConTransitionFadeInConfig(
                0f, Color.black, (dynamic)Enum.ToObject(layerType, 1), null);

            var cmd = new ConTransitionCommand_Default(
                checkpointId, levelId, null, default(Vector2),
                fadeConfig, forceReload, false, ConRespawnOrigin.Undefined,
                DirectionX.Right, default(ConCheckPointId), null);

            CConSceneRegistry.Instance.Get<CConTransitionManager>().Init(cmd, 0f);
            __result = true;
            return false;
        }
    }
}
