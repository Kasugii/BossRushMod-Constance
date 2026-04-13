// PROBLEM 1 — Dialogue plays anyway
//   → The dialogue is launched in ConState_PuppetMaster_Death.TimelineSeq()
//      (method overridden in this class). We block it entirely with a prefix.
//
// PROBLEM 2 — TeleportToShrine never called
//   → Exit() is never triggered after PM death (no state change).
//      We patch CConBossManager.EndFight() which is called right after
//      TimelineSeq(). This is the reliable hook.

using System.Collections;
using HarmonyLib;
using UnityEngine;
using Constance;

namespace BossRushMod
{
    public static class PuppetMasterRushState
    {
        /// <summary>Armed in BossRushArenaForcer.Start() for the PM scene.</summary>
        public static bool Active = false;
    }

    // PATCH 1 — Arms the flag in BossRushArenaForcer.Start()
    [HarmonyPatch(typeof(BossRushArenaForcer), "Start")]
    public static class PuppetMasterRushArmPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BossRushArenaForcer __instance)
        {
            PuppetMasterRushState.Active = false;

            if (string.Equals(__instance.BossId, "PuppetMaster",
                System.StringComparison.OrdinalIgnoreCase))
            {
                PuppetMasterRushState.Active = true;
                Debug.Log("[PuppetMasterDeathPatch] Active=true armed.");
            }
        }
    }

    // PATCH 2 — Blocks ConState_PuppetMaster_Death.TimelineSeq()
    // TimelineSeq() is a coroutine overridden in ConState_PuppetMaster_Death.
    // It launches the dialogue AND the cutscene. We replace it with an empty coroutine.
    [HarmonyPatch(typeof(ConState_PuppetMaster_Death), "TimelineSeq")]
    public static class PuppetMasterTimelineSeqPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref IEnumerator __result)
        {
            if (!PuppetMasterRushState.Active) return true;

            Debug.Log("[PuppetMasterDeathPatch] TimelineSeq() blocked → dialogue + cutscene skipped.");
            __result = EmptyCoroutine();
            return false;
        }

        private static IEnumerator EmptyCoroutine() { yield break; }
    }

    // PATCH 3 — CConBossManager.EndFight() → TeleportToShrine
    // Called in AConState_Boss_Death.Seq() right after TimelineSeq().
    // This is the last reliable hook before the game returns control.
    [HarmonyPatch(typeof(CConBossManager), "EndFight")]
    public static class PuppetMasterEndFightPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IConBossInfo boss, bool __result)
        {
            if (!__result) return;
            if (!PuppetMasterRushState.Active) return;

            string bossId = boss?.Id.StringValue;
            if (!string.Equals(bossId, "PuppetMaster", System.StringComparison.OrdinalIgnoreCase))
                return;

            Debug.Log("[PuppetMasterDeathPatch] EndFight(PuppetMaster) → TeleportToShrine.");
            PuppetMasterRushState.Active = false;
            BossRushUtils.TeleportToShrine();
        }
    }
}
