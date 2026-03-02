// =====================================================================
//  PUPPET HAND PATCHES — Boss Rush Mod
//
//  Problem : after a PuppetHand dies in boss rush, two things can happen
//  in CConArenaEvent_PuppetMaster.ArenaSequence :
//
//    CASE A : FindPuppetHandDeathTeleport() returns a valid checkpoint
//             → OnHandDeath() launches a navigator + InitTransition to the next VOID
//
//    CASE B : FindPuppetHandDeathTeleport() returns empty (other hands already beaten)
//             → ArenaSequence launches PuppetMaster directly in the same scene
//
//  Solution :
//    PATCH 1 — Arms the flag in BossRushArenaForcer.Start()
//    PATCH 2 — Blocks OnHandDeath() → TeleportToShrine     (CASE A)
//    PATCH 3 — Forces FindPuppetHandDeathTeleport → empty   (safety CASE A)
//    PATCH 4 — Blocks ConStateMachine_PuppetMaster.StartImpl (CASE B)
//              → TeleportToShrine if in a PuppetHand boss rush
//
//  No other file is modified.
// =====================================================================

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Constance;

namespace BossRushMod
{
    // ═══════════════════════════════════════════════════════════════════
    //  GLOBAL STATE
    // ═══════════════════════════════════════════════════════════════════
    public static class PuppetHandRushState
    {
        /// <summary>
        /// True during a boss rush fight against a PuppetHand.
        /// Armed in BossRushArenaForcer.Start(), turned off after use.
        /// </summary>
        public static bool Active = false;

        public static readonly HashSet<string> HandBossIds =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "PuppetHandStrings",
                "PuppetHandCorruption",
                "PuppetHandKungfu",
            };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PATCH 1 — Arms the flag on boss rush scene entry
    // ═══════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(BossRushArenaForcer), "Start")]
    public static class PuppetHandBossRushArmPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BossRushArenaForcer __instance)
        {
            // Reset in all cases (clears for other bosses)
            PuppetHandRushState.Active = false;

            if (string.IsNullOrEmpty(__instance.BossId)) return;

            if (PuppetHandRushState.HandBossIds.Contains(__instance.BossId))
            {
                PuppetHandRushState.Active = true;
                Debug.Log($"[PuppetHandPatch] Boss rush PuppetHand armed : '{__instance.BossId}'.");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PATCH 2 — Blocks OnHandDeath → TeleportToShrine  (CASE A)
    //
    //  OnHandDeath() launches a navigator that moves the player to the
    //  VOID exit, then InitTransition to the next VOID.
    //  We replace all of that with TeleportToShrine().
    // ═══════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(CConArenaEvent_PuppetMaster), "OnHandDeath")]
    public static class PuppetHandDeathRedirectPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!PuppetHandRushState.Active)
                return true; // vanilla behaviour

            Debug.Log("[PuppetHandPatch] OnHandDeath blocked → TeleportToShrine (CASE A).");
            PuppetHandRushState.Active = false;
            BossRushUtils.TeleportToShrine();
            return false; // blocks StartNavigator()
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PATCH 3 — Forces FindPuppetHandDeathTeleport to return empty
    //
    //  Prevents ArenaSequence from launching an InitTransition to the
    //  next VOID even if OnHandDeath was already blocked (safety CASE A).
    //  Pushes ArenaSequence toward the PuppetMaster branch (CASE B),
    //  which is then blocked by PATCH 4.
    // ═══════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(CConArenaEvent_PuppetMaster), "FindPuppetHandDeathTeleport")]
    public static class PuppetHandFindTeleportPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref ConCheckPointId __result)
        {
            if (!PuppetHandRushState.Active) return;

            Debug.Log("[PuppetHandPatch] FindPuppetHandDeathTeleport → forced empty (boss rush).");
            __result = default(ConCheckPointId);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PATCH 4 — Blocks PuppetMaster startup  (CASE B)
    //
    //  When FindPuppetHandDeathTeleport returns empty, ArenaSequence
    //  calls StartArenaAndWaitToFinish(player, puppetMaster) which emits
    //  ConEntitySignals.ArenaStarted on PuppetMaster.
    //  PuppetMaster responds by starting its state machine via StartImpl().
    //
    //  We intercept StartImpl() : if in a PuppetHand boss rush,
    //  we call TeleportToShrine() and return WaitForArena (inert state)
    //  to avoid any crash — the scene will change anyway.
    // ═══════════════════════════════════════════════════════════════════
    [HarmonyPatch(
        typeof(CConStateMachine_PuppetMaster.ConStateMachine_PuppetMaster),
        "StartImpl")]
    public static class PuppetMasterStartImplBlockPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            CConStateMachine_PuppetMaster.ConStateMachine_PuppetMaster __instance,
            ref IConStateUpdate __result)
        {
            if (!PuppetHandRushState.Active)
                return true; // vanilla behaviour

            Debug.Log("[PuppetHandPatch] PuppetMaster.StartImpl blocked → TeleportToShrine (CASE B).");
            PuppetHandRushState.Active = false;

            // StartImpl() normally hides the parkour + laser + hitbox before the fight.
            // Since we block it entirely, we do it manually here to avoid
            // the final boss scenery remaining displayed statically in the scene.
            try
            {
                __instance.Component.parkourTransform.SetGoActive(false);
                __instance.Component.headLaser.SetGoActive(false);
                __instance.Component.hbHead.SetSimulated(false);
                __instance.Component.camParkour.Value.SetEnabled(false);
                Debug.Log("[PuppetHandPatch] PuppetMaster parkour / laser / hitbox hidden.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PuppetHandPatch] PuppetMaster cleanup: {e.Message}");
            }

            BossRushUtils.TeleportToShrine();

            // Return WaitForArena as a neutral state (the scene will change anyway)
            __result = __instance.WaitForArena.Init();
            return false; // blocks original StartImpl()
        }
    }
}