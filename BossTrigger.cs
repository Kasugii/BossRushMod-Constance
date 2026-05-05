using System.Collections.Generic;
using UnityEngine;
using Constance;

namespace BossRushMod
{
    // ═══════════════════════════════════════════════════════════════════
    //  BOSS STATE GUARD
    // ═══════════════════════════════════════════════════════════════════
    public static class BossStateGuard
    {
        private static readonly Dictionary<string, bool> _original =
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);

        public static void Capture(string bossId)
        {
            if (string.IsNullOrEmpty(bossId) || _original.ContainsKey(bossId)) return;
            var id = new ConBossId(bossId);
            if (!id.IsValid()) return;
            bool state = id.Beaten;
            _original[bossId] = state;
            Debug.Log($"[BossStateGuard] Capture '{bossId}' = {state}");
        }

        public static void RestoreAll()
        {
            if (_original.Count == 0) return;
            foreach (var kvp in _original)
            {
                var id = new ConBossId(kvp.Key);
                if (id.IsValid()) id.Beaten = kvp.Value;
            }
            _original.Clear();
            Debug.Log("[BossStateGuard] RestoreAll done.");
        }

        public static void Clear() => _original.Clear();
    }


    // ═══════════════════════════════════════════════════════════════════
    //  BOSS TRIGGER
    // ═══════════════════════════════════════════════════════════════════
    public static class BossTrigger
    {
        public static string PendingBossId = null;
        public static ConCheckPointId LastBossCheckpointId;

        public static bool PrepareAndTeleport(string bossId)
        {
            PendingBossId = bossId;

            BossStateGuard.Capture(bossId);
            ResetBeatenFlag(bossId);

            // PuppetMaster : 3 mains Beaten=true
            if (bossId == "PuppetMaster")
            {
                foreach (var hId in new[] { "PuppetHandKungfu", "PuppetHandStrings", "PuppetHandCorruption" })
                {
                    BossStateGuard.Capture(hId);
                    var h = new ConBossId(hId);
                    if (h.IsValid()) h.Beaten = true;
                }
            }

            // Signaler la transition AVANT de la lancer (protection contre mort)
            // Note : déjà posé par BossRushModeManager.StartRun / OnBossDefeated
            // On s'assure juste qu'il est actif ici aussi
            BossRushModeManager.SignalExpectedTransition();

            return BossRushTeleporter.TeleportToBoss(bossId, forceReload: true);
        }

        public static bool LaunchCurrentBoss()
        {
            var arenaEvent = UnityEngine.Object.FindObjectOfType<CConArenaEvent_Boss>();
            if (arenaEvent == null) return false;
            if (arenaEvent.bossId.IsValid())
            {
                BossStateGuard.Capture(arenaEvent.bossId.StringValue);
                ResetBeatenFlag(arenaEvent.bossId.StringValue);
            }
            arenaEvent.ForceArenaStart();
            return true;
        }

        public static void ResetBeatenFlag(string bossId)
        {
            var id = new ConBossId(bossId);
            if (!id.IsValid()) return;
            id.Beaten = false;
        }
    }
}