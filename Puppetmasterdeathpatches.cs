// =====================================================================
//  PUPPET MASTER DEATH PATCHES — Boss Rush Mod  (v4)
//
//  Two real problems identified via logs :
//
//  PROBLEM 1 — Dialogue plays anyway
//    → The dialogue is launched in ConState_PuppetMaster_Death.TimelineSeq()
//       (method overridden in this class). We block it entirely
//       with a prefix that returns immediately.
//
//  PROBLEM 2 — TeleportToShrine never called
//    → Exit() is never triggered after PM death (no state change).
//       We patch CConBossManager.EndFight() which is called right after
//       TimelineSeq(). This is the reliable hook.
//
//  No other file is modified.
// =====================================================================

using System.Collections;
using HarmonyLib;
using UnityEngine;
using Constance;

namespace BossRushMod
{
    // ═══════════════════════════════════════════════════════════════════
    //  GLOBAL STATE
    // ═══════════════════════════════════════════════════════════════════
    public static class PuppetMasterRushState
    {
        /// <summary>Armed in BossRushArenaForcer.Start() for the PM scene.</summary>
        public static bool Active = false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PATCH 1 — Arms the flag in BossRushArenaForcer.Start()
    // ═══════════════════════════════════════════════════════════════════
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

    // ═══════════════════════════════════════════════════════════════════
    //  PATCH 2 — Blocks ConState_PuppetMaster_Death.TimelineSeq()
    //
    //  TimelineSeq() is a coroutine (IEnumerator) overridden in
    //  ConState_PuppetMaster_Death. It launches the dialogue AND the
    //  cutscene. We replace it with an empty coroutine.
    //
    //  Harmony can patch methods returning IEnumerator
    //  via a prefix that returns false + assigns __result = empty.
    // ═══════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(ConState_PuppetMaster_Death), "TimelineSeq")]
    public static class PuppetMasterTimelineSeqPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref IEnumerator __result)
        {
            if (!PuppetMasterRushState.Active)
                return true; // vanilla behaviour

            Debug.Log("[PuppetMasterDeathPatch] TimelineSeq() blocked → dialogue + cutscene skipped.");

            // Return an empty coroutine : no dialogue, no cutscene
            __result = EmptyCoroutine();
            return false; // blocks the original
        }

        private static IEnumerator EmptyCoroutine()
        {
            yield break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PATCH 3 — CConBossManager.EndFight() → TeleportToShrine
    //
    //  Called in AConState_Boss_Death.Seq() right after TimelineSeq().
    //  This is the last reliable hook before the game returns control.
    //  We verify it is PuppetMaster via boss.Id.StringValue.
    // ═══════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(CConBossManager), "EndFight")]
    public static class PuppetMasterEndFightPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IConBossInfo boss, bool __result)
        {
            if (!__result) return;             // EndFight returned false → not the right boss
            if (!PuppetMasterRushState.Active) return;

            // Verify it is PuppetMaster
            string bossId = boss?.Id.StringValue;
            if (!string.Equals(bossId, "PuppetMaster",
                System.StringComparison.OrdinalIgnoreCase))
                return;

            Debug.Log("[PuppetMasterDeathPatch] EndFight(PuppetMaster) → TeleportToShrine.");
            PuppetMasterRushState.Active = false;
            BossRushUtils.TeleportToShrine();
        }
    }
}