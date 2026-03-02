using UnityEngine;
using System.Collections.Generic;
using Constance;

namespace BossRushMod
{
    public static class BossRushTeleporter
    {
        // ── CHECKPOINTS ──────────────────────────────────────────────────────────
        // Key = boss internal ID | Value = "cp_{LevelId}_{UUID}" (from logs)
        // ─────────────────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> _bossCheckpoints =
            new Dictionary<string, string>
        {
            // Floral Foundry
            { "BrainStoker", "cp_Prod_F28_847bed50-d7b0-11ee-977c-67452ce73fec" }, // ✓
            { "Smasher",     "cp_Prod_F21_a7b7b750-3740-11f0-a898-5ff5a9e27b90" }, // ✓
            { "Palettus",    "cp_Prod_F27_ad708110-fec0-11ee-b635-8b09f738361a" }, // ✓

            // Astral Academy
            { "MothQueen",     "cp_Prod_A27_d8bfef9c-93f6-4070-b97d-249aa31a9f65" }, // ✓
            { "AweKing",        "cp_Prod_A16_08a800e0-9b00-11ef-b44d-37183fa010d2" }, // ✓

            // Chaotic Carnival
            { "CornelisBoss",    "cp_Prod_C01_37f700e0-5e50-11f0-829b-8fbd0a2caa51" }, // ✓
            { "Joker",   "cp_Prod_C90_96a21230-ac70-11f0-825c-2f292287f515" }, // ✓
            { "JugglerBalloons",    "cp_Prod_C95_e2cd59fe-b91b-a9df-1e24-ef3fb9769ef6" }, // ✓
            { "JugglerBalls", "cp_Prod_C96_61d0e7b0-5e50-11f0-8f17-836489986c21" }, // ✓
            { "JokerInvisible",  "cp_Prod_C94_199e35f0-8560-11f0-95b6-038d88a61f6e" }, // ✓

            // Vanishing Vaults
            { "SlimeNemesis", "cp_Prod_V11_903743d6-88da-c870-37db-53ab0e9b2d7d" }, // ✓
            { "PukeyBoy",     "cp_Prod_V18_e0305230-e920-11ef-8aab-13dfc45c483b" }, // ✓

            // Voids
            { "PuppetHandStrings",    "cp_Prod_VD03_8c4d3f70-8560-11f0-9198-09c6ccf99122" }, // ✓
            { "PuppetHandCorruption", "cp_Prod_VD01_522feda0-73f0-11ef-91d7-1d5ddf16a67a" }, // ✓
            { "PuppetHandKungfu",     "cp_Prod_VD02_55009bd0-1030-11f0-8fef-01507257725f" }, // ✓
            { "PuppetMaster",         "cp_Prod_VD02_55009bd0-1030-11f0-8fef-01507257725f" }, // ✓
        };

        /// <summary>
        /// Teleports the player to the boss room.
        /// forceReload=true forces the scene to reload (required to reset the boss).
        /// </summary>
        public static bool TeleportToBoss(string bossId, bool forceReload = false)
        {
            if (!_bossCheckpoints.TryGetValue(bossId, out string checkpointStr)
                || string.IsNullOrEmpty(checkpointStr))
            {
                Debug.LogWarning($"[BossRush] No checkpoint for '{bossId}'. Fill in _bossCheckpoints.");
                return false;
            }

            var checkpointId = new ConCheckPointId(checkpointStr);
            if (!checkpointId.IsValid())
            {
                Debug.LogError($"[BossRush] Invalid ConCheckPointId : '{checkpointStr}'");
                return false;
            }

            ConLevelId levelId = checkpointId.ExtractLevelId();
            if (!levelId.IsValid())
            {
                Debug.LogError($"[BossRush] Unable to extract ConLevelId from '{checkpointStr}'");
                return false;
            }

            Debug.Log($"[BossRush] Teleporting → boss='{bossId}' level='{levelId}' cp='{checkpointId}'");

            // Register the active checkpoint (respawn here on death)
            CConSceneRegistry.Instance.CheckPointManager.Check(checkpointId);

            // Build the transition command
            var layerType = typeof(ConTransitionFadeInConfig).GetConstructors()[0].GetParameters()[2].ParameterType;
            object layerValue = System.Enum.ToObject(layerType, 1);
            var fadeConfig = new ConTransitionFadeInConfig(0f, Color.black, (dynamic)layerValue, null);

            var cmd = new ConTransitionCommand_Default(
                checkpointId,
                levelId,
                null,
                default(Vector2),
                fadeConfig,
                forceReload, // true = forced scene reload
                false,
                0,
                null,
                null
            );

            CConSceneRegistry.Instance.Get<CConTransitionManager>().Init(cmd, 0f);
            return true;
        }

        /// <summary>
        /// Press F2 in-game to dump all checkpoints in the scene to the logs.
        /// Useful for filling in the TODO entries above.
        /// </summary>
        public static void DumpCheckpointsInScene()
        {
            var all = Object.FindObjectsOfType<CConCheckPoint>();
            if (all == null || all.Length == 0)
            {
                Debug.Log("[BossRush] No CConCheckPoint found in the scene.");
                return;
            }
            Debug.Log($"[BossRush] ── {all.Length} checkpoint(s) ──");
            foreach (var cp in all)
                Debug.Log($"  [{cp.gameObject.name}] → '{cp.CheckPointId}'");
        }
    }
}