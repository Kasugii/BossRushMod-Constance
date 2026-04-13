using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Constance;

namespace BossRushMod
{
    public static class BrainStokerState
    {
        /// <summary>True once WaitForArena is over (= inside the arena).</summary>
        public static bool FightStarted = false;

        /// <summary>True when boss death is triggered.
        /// Stops GroundGear even if FightStarted=true.</summary>
        public static bool FightEnded = false;
    }

    // PATCH 1 — Applies difficulty config in StartImpl
    [HarmonyPatch(typeof(CConStateMachine_BrainStoker), "StartImpl")]
    public static class BrainStokerConfigPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CConStateMachine_BrainStoker __instance)
        {
            BrainStokerState.FightStarted = false;
            BrainStokerState.FightEnded = false;

            if (!BossDifficultyRegistry.Difficulties.TryGetValue("BrainStoker", out int level))
                return;

            var cfg = __instance.config;
            if (cfg == null) return;

            Debug.Log($"[BrainStokerPatch] Level {level} applied.");
            switch (level)
            {
                case 1: ApplyEasy(cfg); break;
                case 2: break; // Normal → vanilla config unchanged
                case 3: ApplyHard(cfg); break;
                case 4: ApplyExtreme(cfg); break;
            }
        }

        private static void ApplyEasy(SConEntityConfig_BrainStoker cfg)
        {
            cfg.circuitSuccessDamage = 1;
            cfg.circuitLightningCooldown = 3f;
            cfg.circuitLightningDelay = 0.2f;
            cfg.circuitLightningEchoDelay = 0f;
            cfg.circuitLightningEchoDistance = 1f;
            cfg.circuitLightningPredict = 0f;

            cfg.lightningWallSparkAmount = 15;
            cfg.lightningWallGapDistanceRange1 = new Vector2Int(2, 3);
            cfg.lightningWallGapDistanceRange2 = new Vector2Int(3, 4);
            cfg.lightningWallGapDistanceRangeHurt = new Vector2Int(3, 3);
            cfg.lightningWallPatternAmount = 5;
            cfg.lightningWallFeedbackDelay = 2.5f;
            cfg.lightningWallOuterDelay = 0f;

            cfg.gearAnticipationTime = 1.5f;
        }

        private static void ApplyHard(SConEntityConfig_BrainStoker cfg)
        {
            cfg.circuitSuccessDamage = 2;
            cfg.circuitLightningCooldown = 0.6f;
            cfg.circuitLightningDelay = 0.05f;
            cfg.circuitLightningEchoDelay = 0f;
            cfg.circuitLightningEchoDistance = 1.8f;
            cfg.circuitLightningPredict = 0.3f;

            cfg.lightningWallSparkAmount = 20;
            cfg.lightningWallGapDistanceRange1 = new Vector2Int(5, 9);
            cfg.lightningWallGapDistanceRange2 = new Vector2Int(5, 9);
            cfg.lightningWallGapDistanceRangeHurt = new Vector2Int(1, 1);
            cfg.lightningWallPatternAmount = 10;
            cfg.lightningWallFeedbackDelay = 0.5f;
            cfg.lightningWallOuterDelay = 0f;

            cfg.gearAnticipationTime = 0.5f;
        }

        private static void ApplyExtreme(SConEntityConfig_BrainStoker cfg)
        {
            cfg.circuitSuccessDamage = 3;
            cfg.circuitLightningCooldown = 0.6f;
            cfg.circuitLightningDelay = 0.05f;
            cfg.circuitLightningEchoDelay = 0f;
            cfg.circuitLightningEchoDistance = 1.8f;
            cfg.circuitLightningPredict = 0.3f;

            cfg.lightningWallSparkAmount = 20;
            cfg.lightningWallGapDistanceRange1 = new Vector2Int(4, 8);
            cfg.lightningWallGapDistanceRange2 = new Vector2Int(4, 8);
            cfg.lightningWallGapDistanceRangeHurt = new Vector2Int(1, 1);
            cfg.lightningWallPatternAmount = 10;
            cfg.lightningWallFeedbackDelay = 0.5f;
            cfg.lightningWallOuterDelay = 0f;

            cfg.gearAnticipationTime = 0.25f;
        }
    }

    // PATCH 2 — Start of the real fight + HP boost +25%
    // GetStartState() is called when WaitForArena ends, just before the Hazards phase.
    // IConHealth.ChangeBaseHealth(int) changes the Max AND fills the bar.
    // All phases (vulnerability x3) remain intact — Brian simply has more HP per phase.
    // Entity is accessible via reflection (field inherited from base).
    [HarmonyPatch(typeof(CConStateMachine_BrainStoker.ConStateMachine_BrainStoker), "GetStartState")]
    public static class BrainStokerFightStartPatch
    {
        private const float HP_BOOST = 1.25f;

        private static readonly PropertyInfo _entityProp =
            AccessTools.Property(typeof(CConStateMachine_BrainStoker.ConStateMachine_BrainStoker), "Entity")
            ?? AccessTools.FindIncludingBaseTypes(
                typeof(CConStateMachine_BrainStoker.ConStateMachine_BrainStoker),
                t => t.GetProperty("Entity",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

        [HarmonyPostfix]
        public static void Postfix(CConStateMachine_BrainStoker.ConStateMachine_BrainStoker __instance)
        {
            BrainStokerState.FightStarted = true;

            if (!BossDifficultyRegistry.Difficulties.TryGetValue("BrainStoker", out int level) || level < 3)
                return;

            var entity = _entityProp?.GetValue(__instance) as CConCharacterEntity;
            if (entity == null) { Debug.LogWarning("[BrainStokerPatch] HP boost : unable to access Entity."); return; }

            var health = entity.Get<IConHealth>();
            if (health == null) { Debug.LogWarning("[BrainStokerPatch] HP boost : IConHealth not found."); return; }

            int originalMax = health.Max;
            int newMax = Mathf.RoundToInt(originalMax * HP_BOOST);
            health.ChangeBaseHealth(newMax);

            Debug.Log($"[BrainStokerPatch] HP boost level {level} : {originalMax} → {newMax} (+25%). Phases intact.");
        }
    }

    // PATCH 3 — Boss death : stop the GroundGear
    // Death.OnEnter() sets ShouldBeActive=0 in vanilla, but our GroundGearPatch (FixedUpdate)
    // would overwrite it every frame. FightEnded=true prevents that.
    [HarmonyPatch(typeof(ConState_BrainStoker_Death), "OnEnter")]
    public static class BrainStokerDeathPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BrainStokerState.FightEnded = true;
            BrainStokerState.FightStarted = false;
        }
    }

    // PATCH 4 — GroundGear : activation by difficulty level
    // EASY    → 0 (no gear)
    // HARD    → 1 gear (between FightStarted and FightEnded)
    // EXTREME → 2 gears (between FightStarted and FightEnded)
    [HarmonyPatch(typeof(ConStateAbility_BrainStoker_GroundGear), "FixedUpdate")]
    public static class BrainStokerGroundGearPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ConStateAbility_BrainStoker_GroundGear __instance)
        {
            if (!BossDifficultyRegistry.Difficulties.TryGetValue("BrainStoker", out int level))
                return;

            if (level == 1)
            {
                __instance.ShouldBeActive = 0;
            }
            else if (!BrainStokerState.FightEnded)
            {
                if (level == 3 && BrainStokerState.FightStarted)
                    __instance.ShouldBeActive = 1;
                else if (level == 4 && BrainStokerState.FightStarted)
                    __instance.ShouldBeActive = 2;
            }
        }
    }

    // PATCH 5 — IsEverythingCleared : skip the GroundGear check for levels 3/4
    // With permanent gear active, !GroundGearAbility.Active is always true,
    // so Hazards never ends and Circuit is never triggered.
    // We ignore the gear check and only verify cannons + enemies + loop.
    [HarmonyPatch(typeof(ConStateAbility_BrainStoker_Hazards), "IsEverythingCleared")]
    public static class BrainStokerIsEverythingClearedPatch
    {
        private static readonly FieldInfo _smField =
            AccessTools.Field(typeof(ConStateAbility_BrainStoker_Hazards), "SM")
            ?? AccessTools.FindIncludingBaseTypes(typeof(ConStateAbility_BrainStoker_Hazards),
                t => t.GetField("SM", BindingFlags.Instance | BindingFlags.NonPublic
                                    | BindingFlags.Public | BindingFlags.FlattenHierarchy));

        [HarmonyPrefix]
        public static bool Prefix(ConStateAbility_BrainStoker_Hazards __instance, ref bool __result)
        {
            if (!BossDifficultyRegistry.Difficulties.TryGetValue("BrainStoker", out int level)
                || (level != 3 && level != 4))
                return true;

            var sm = _smField?.GetValue(__instance) as CConStateMachine_BrainStoker.ConStateMachine_BrainStoker;
            if (sm == null)
            {
                Debug.LogWarning("[BrainStokerPatch] IsEverythingCleared : SM not found.");
                return true;
            }

            __result = sm.Component.cannons.All(c => c.ActiveProjectiles == 0)
                       && __instance.ActiveEnemies == 0
                       && !__instance.LoopActive;
            return false;
        }
    }

    // PATCH 6 — Cannon speed x1.8 for HARD and EXTREME
    // CannonSpeedMarker is added to avoid re-applying the multiplier on subsequent Fire() calls.
    [HarmonyPatch(typeof(CConProjectileCannon), "Fire")]
    public static class BrainStokerCannonPatch
    {
        private const float HARD_SPEED_MULT = 1.8f;
        private const float EXTREME_SPEED_MULT = 1.8f;

        private static readonly FieldInfo _configField =
            typeof(CConProjectileCannon).GetField("projectileConfig",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo _speedField =
            typeof(ConProjectileConfig).GetField("corruptedSpeed",
                BindingFlags.Instance | BindingFlags.Public);

        [HarmonyPrefix]
        public static void Prefix(CConProjectileCannon __instance)
        {
            if (!BossDifficultyRegistry.Difficulties.TryGetValue("BrainStoker", out int level)
                || (level != 3 && level != 4))
                return;
            if (_configField == null || _speedField == null) return;
            if (__instance.GetComponent<CannonSpeedMarker>() != null) return;

            object cfgObj = _configField.GetValue(__instance);
            if (cfgObj == null) return;

            float speed = (float)_speedField.GetValue(cfgObj);
            float mult = level == 4 ? EXTREME_SPEED_MULT : HARD_SPEED_MULT;
            _speedField.SetValue(cfgObj, speed * mult);
            _configField.SetValue(__instance, cfgObj);

            var marker = __instance.gameObject.AddComponent<CannonSpeedMarker>();
            marker.OriginalSpeed = speed;
        }
    }

    public class CannonSpeedMarker : UnityEngine.MonoBehaviour
    {
        public float OriginalSpeed;
    }
}
