using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Constance;

namespace BossRushMod
{
    public class BossRushSceneHook : MonoBehaviour
    {
        private static Harmony _harmony;

        public static readonly System.Collections.Generic.HashSet<string> DelayedArenaBosses =
            new System.Collections.Generic.HashSet<string>
            { "Smasher", "PukeyBoy", "CornelisBoss", "MothQueen", "AweKing" };

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (_harmony == null)
            {
                _harmony = new Harmony("com.bossrush.patches");
                var transitionInit = typeof(CConTransitionManager).GetMethod(
                    "Init", BindingFlags.Public | BindingFlags.Instance);
                if (transitionInit != null)
                    _harmony.Patch(transitionInit, prefix: new HarmonyMethod(
                        typeof(FlashbackPatch).GetMethod("PrefixTransitionInit")));
                else
                {
                    var levelLoad = typeof(CConLevelSceneManager).GetMethod(
                        "TriggerLevelLoad", BindingFlags.Public | BindingFlags.Instance,
                        null, new[] { typeof(ConLevelId), typeof(bool) }, null);
                    if (levelLoad != null)
                        _harmony.Patch(levelLoad, prefix: new HarmonyMethod(
                            typeof(FlashbackPatch).GetMethod("PrefixTriggerLevelLoad")));
                }
            }
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Nettoyer les navigateurs de timeline résiduels (évite le spam NullRef)
            try
            {
                foreach (var nav in FindObjectsOfType<CConTimelinePlayerController>())
                {
                    if (nav != null) Destroy(nav.gameObject);
                }
            }
            catch { }

            string bossId = BossTrigger.PendingBossId;
            if (string.IsNullOrEmpty(bossId)) return;

            BossStateGuard.Capture(bossId);
            BossTrigger.ResetBeatenFlag(bossId);

            var arena = FindObjectOfType<CConArenaEvent_Boss>();
            if (arena != null && arena.bossId.IsValid())
            {
                string actualId = arena.bossId.StringValue;
                if (!string.Equals(actualId, bossId, StringComparison.OrdinalIgnoreCase))
                {
                    BossStateGuard.Capture(actualId);
                    BossTrigger.ResetBeatenFlag(actualId);
                }
            }

            // Arène monitor : force TP pour les bosses avec coffre (pas de Flashback)
            var monitorGo = new GameObject("[BossArenaMonitor]");
            SceneManager.MoveGameObjectToScene(monitorGo, scene);
            var monitor = monitorGo.AddComponent<BossArenaMonitor>();
            monitor.BossId = bossId;

            var forcerGo = new GameObject("[BossRushArenaForcer]");
            SceneManager.MoveGameObjectToScene(forcerGo, scene);
            var forcer = forcerGo.AddComponent<BossRushArenaForcer>();
            forcer.BossId = bossId;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  BOSS ARENA MONITOR
    //
    //  Surveille arena._state. Quand il passe à 3 (Beaten) :
    //    - Attend 3.5s pour l'animation de mort
    //    - Si Flashback l'a déjà géré → ne fait rien
    //    - Sinon (boss avec coffre) → force TeleportToShrine / boss suivant
    // ─────────────────────────────────────────────────────────────────
    public class BossArenaMonitor : MonoBehaviour
    {
        public string BossId;

        // Posé par FlashbackPatch quand il gère un boss vaincu
        public static bool FlashbackHandledBoss = false;
        // Évite le double déclenchement
        private bool _handled = false;

        private CConArenaEvent_Boss _arena;

        private static readonly FieldInfo _stateField =
            typeof(CConArenaEvent_Boss).GetField("_state",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private byte _lastState = 0;

        private void Start()
        {
            _arena = FindObjectOfType<CConArenaEvent_Boss>();
            FlashbackHandledBoss = false;
        }

        private void Update()
        {
            if (_arena == null || _handled) return;
            byte state = GetState();
            if (state == 3 && _lastState != 3)
            {
                _handled = true;
                Debug.Log("[BossArenaMonitor] Arena beaten detected (state=3). Waiting for animation...");
                StartCoroutine(HandleBossBeaten());
            }
            _lastState = state;
        }

        private IEnumerator HandleBossBeaten()
        {
            // Laisser 3.5s pour l'animation de mort + cutscene coffre
            yield return new WaitForSeconds(3.5f);

            if (FlashbackHandledBoss)
            {
                Debug.Log("[BossArenaMonitor] Flashback already handled → skip.");
                Destroy(gameObject);
                yield break;
            }

            Debug.Log("[BossArenaMonitor] No Flashback detected (chest boss) → forcing TP.");

            // Signaler la transition AVANT de la lancer (pas une mort)
            BossRushModeManager.SignalExpectedTransition();

            if (BossRushModeManager.IsActive)
            {
                bool done = BossRushModeManager.OnBossDefeated();
                if (!done)
                {
                    // Next boss already launched by OnBossDefeated
                    Destroy(gameObject);
                    yield break;
                }
                // Run terminé → shrine normal
            }

            BossStateGuard.RestoreAll();
            try
            {
                var sound = CConSceneRegistry.Instance?.Get<IConSoundScapeManager>();
                if (sound != null) { sound.RestoreLevelMusic(1f); sound.StopAllLevelSfx(); }
            }
            catch { }

            BossRushUtils.TeleportToShrine();
            Destroy(gameObject);
        }

        private byte GetState()
        {
            if (_arena == null || _stateField == null) return 0;
            try { return Convert.ToByte(_stateField.GetValue(_arena)); }
            catch { return 0; }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  FLASHBACK PATCH
    //
    //  Flashback = transition déclenchée après mort du boss OU mort du joueur.
    //
    //  Détection :
    //    arenaState == 3 → boss vaincu  → TeleportToShrine / boss suivant
    //    arenaState == 1 ou 2 → joueur mort → OnPlayerDied()
    //    arenaState == 0 → transition neutre → ignorer
    // ─────────────────────────────────────────────────────────────────
    public static class FlashbackPatch
    {
        private static readonly FieldInfo _arenaStateField =
            typeof(CConArenaEvent_Boss).GetField("_state",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static byte GetArenaState()
        {
            try
            {
                var arena = UnityEngine.Object.FindObjectOfType<CConArenaEvent_Boss>();
                if (arena == null || _arenaStateField == null) return 0;
                return Convert.ToByte(_arenaStateField.GetValue(arena));
            }
            catch { return 0; }
        }

        public static bool PrefixTransitionInit(
            ref IConTransitionCommand transitionCommand, float delay)
        {
            if (transitionCommand == null) return true;
            try
            {
                string target = null;
                var type = transitionCommand.GetType();
                while (type != null && target == null)
                {
                    var prop = type.GetProperty("ToLevel",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop != null) target = prop.GetValue(transitionCommand)?.ToString();
                    type = type.BaseType;
                }

                bool isFlashback = !string.IsNullOrEmpty(target) && target.Contains("Flashback");

                if (isFlashback)
                {
                    byte arenaState = GetArenaState();

                    // Boss mort (state=3) → gérer normalement
                    if (arenaState == 3 || arenaState == 0)
                    {
                        // Signaler au BossArenaMonitor que Flashback a géré ça
                        BossArenaMonitor.FlashbackHandledBoss = true;
                        BossRushModeManager.SignalExpectedTransition();

                        var arena2 = UnityEngine.Object.FindObjectOfType<CConArenaEvent_Boss>();
                        if (arena2 != null) arena2.SetDoorsOpen(true);

                        BossRushUtils.TeleportToShrine();
                        return false;
                    }

                    // Joueur mort (arène encore active = state 1 ou 2)
                    if (BossRushModeManager.IsActive && (arenaState == 1 || arenaState == 2))
                    {
                        Debug.Log($"[RushMode] Flashback + arenaState={arenaState} → joueur mort → run annulé.");
                        BossRushModeManager.OnPlayerDied();
                        return true; // laisser la transition de mort se dérouler
                    }

                    // Fallback : laisser passer
                    return true;
                }

                // Transition non-Flashback
                if (BossRushModeManager.IsActive)
                {
                    bool expected = BossRushModeManager.ConsumeExpectedTransition();
                    if (!expected)
                    {
                        byte arenaState = GetArenaState();
                        if (arenaState == 1 || arenaState == 2)
                        {
                            Debug.Log($"[RushMode] Transition inattendue arenaState={arenaState} → mort → run annulé.");
                            BossRushModeManager.OnPlayerDied();
                        }
                    }
                }
                else
                {
                    BossRushModeManager.ConsumeExpectedTransition();
                }
            }
            catch (Exception e) { Debug.LogWarning("[BossRushHook] PrefixTransitionInit: " + e.Message); }
            return true;
        }

        public static bool PrefixTriggerLevelLoad(ConLevelId toLoad)
        {
            string id = toLoad.ToString();
            if (!string.IsNullOrEmpty(id) && id.Contains("Flashback"))
            { Debug.Log("[BossRushHook] TriggerLevelLoad BLOCKED."); return false; }
            return true;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  ARENA FORCER
    //
    //  En Rush Mode :
    //    - Portes ouvertes en permanence
    //    - Portes ouvertes 4s puis démarre automatiquement
    //    - Timeout 30s au cas où
    //  Hors Rush Mode :
    //    - Comportement habituel (avec délais pour certains bosses)
    // ─────────────────────────────────────────────────────────────────
    [DefaultExecutionOrder(100)]
    public class BossRushArenaForcer : MonoBehaviour
    {
        public string BossId;

        // Distance au centre de l'arène à partir de laquelle on démarre le boss
        private static readonly FieldInfo _saveAfterField =
            typeof(CConArenaEvent_Boss).GetField("saveAfter",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _onBeatenFromPersistenceField =
            typeof(CConArenaEvent_Boss).GetField("onBeatenFromPersistence",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _stateField =
            typeof(CConArenaEvent_Boss).GetField("_state",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo _initArenaSequenceMethod =
            typeof(CConArenaEvent_Boss).GetMethod("InitArenaSequence",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(IConPlayerEntity) }, null);

        private void Start()
        {
            BossTrigger.PendingBossId = null;

            var arena = FindObjectOfType<CConArenaEvent_Boss>();
            if (arena == null) { Destroy(gameObject); return; }

            string actualBossId = arena.bossId.IsValid()
                ? arena.bossId.StringValue : BossId ?? "?";

            if (!string.IsNullOrEmpty(BossId))
            {
                BossStateGuard.Capture(BossId);
                BossTrigger.ResetBeatenFlag(BossId);
            }
            if (!string.Equals(actualBossId, BossId, StringComparison.OrdinalIgnoreCase))
            {
                BossStateGuard.Capture(actualBossId);
                BossTrigger.ResetBeatenFlag(actualBossId);
            }

            if (string.Equals(BossId, "PuppetMaster", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var hId in new[] { "PuppetHandKungfu", "PuppetHandStrings", "PuppetHandCorruption" })
                {
                    BossStateGuard.Capture(hId);
                    var h = new ConBossId(hId);
                    if (h.IsValid()) h.Beaten = true;
                }
            }

            _saveAfterField?.SetValue(arena, false);
            _onBeatenFromPersistenceField?.SetValue(arena, new UnityEvent());

            byte state = GetState(arena);
            if (state == 3)
            {
                SetState(arena, 0);
                arena.SetDoorsOpen(false);
                TryReactivateBossEntity(arena);
            }

            bool inRushMode = BossRushModeManager.IsActive;

            if (inRushMode)
            {
                // Rush Mode : portes ouvertes, déclencher quand le joueur entre
                StartCoroutine(RushModeWaitForPlayer(arena));
            }
            else if (BossRushSceneHook.DelayedArenaBosses.Contains(BossId ?? ""))
            {
                float delay = string.Equals(BossId, "MothQueen",
                    StringComparison.OrdinalIgnoreCase) ? 2f : 3f;
                StartCoroutine(DelayedArenaEnable(arena, delay));
            }
            else
            {
                StartCoroutine(ForceStartCoroutine(arena));
            }
        }

        // ─── RUSH MODE ─────────────────────────────────────────────────
        // Portes ouvertes 4s → le joueur peut entrer calmement → démarre.
        // Délai fixe : fonctionne pour toutes les arènes y compris tentes.
        private IEnumerator RushModeWaitForPlayer(CConArenaEvent_Boss arena)
        {
            arena.SetDoorsOpen(true);
            Debug.Log("[BossRushForcer] Rush Mode: doors open, waiting 4s for player to enter.");
            yield return new WaitForSeconds(4f);
            if (arena == null) yield break;
            Debug.Log("[BossRushForcer] Rush Mode: starting boss.");
            StartCoroutine(ForceStartCoroutine(arena));
        }

        private IEnumerator DelayedArenaEnable(CConArenaEvent_Boss arena, float delay)
        {
            arena.enabled = false;
            yield return new WaitForSeconds(delay);
            arena.enabled = true;
        }

        private IEnumerator ForceStartCoroutine(CConArenaEvent_Boss arena)
        {
            yield return new WaitForSeconds(0.8f);

            if (!string.IsNullOrEmpty(BossId))
                BossTrigger.ResetBeatenFlag(BossId);

            if (string.Equals(BossId, "PuppetMaster", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var hId in new[] { "PuppetHandKungfu", "PuppetHandStrings", "PuppetHandCorruption" })
                {
                    var h = new ConBossId(hId);
                    if (h.IsValid() && !h.Beaten) h.Beaten = true;
                }
            }

            float timeout = 10f, elapsed = 0f;
            while (elapsed < timeout)
            {
                if (arena == null) yield break;
                byte st = GetState(arena);
                if (st != 0)
                {
                    // Arena started (state 1+) — close doors regardless of mode
                    arena.SetDoorsOpen(false);
                    Destroy(gameObject);
                    yield break;
                }
                ForceInitArena(arena);
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            Destroy(gameObject);
        }

        private static void ForceInitArena(CConArenaEvent_Boss arena)
        {
            if (GetState(arena) != 0) return;
            try
            {
                IConPlayerEntity player = CConSceneRegistry.Instance.PlayerOne;
                if (player != null && _initArenaSequenceMethod != null)
                { _initArenaSequenceMethod.Invoke(arena, new object[] { player }); return; }
            }
            catch (Exception e) { Debug.LogWarning("[Forcer] InitArenaSequence: " + e.Message); }
            try { arena.DebugStart(); }
            catch (Exception e) { Debug.LogWarning("[Forcer] DebugStart: " + e.Message); }
        }

        private static void TryReactivateBossEntity(CConArenaEvent_Boss arena)
        {
            try
            {
                var entityRef = typeof(CConArenaEvent_Boss)
                    .GetField("bossEntity", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(arena);
                if (entityRef != null) { dynamic d = entityRef; d.Value.GameObject.SetActive(true); }
            }
            catch { }
        }

        private static byte GetState(CConArenaEvent_Boss arena)
        {
            object val = _stateField?.GetValue(arena);
            return val != null ? Convert.ToByte(val) : (byte)0;
        }

        private static void SetState(CConArenaEvent_Boss arena, byte value)
        {
            if (_stateField != null)
                _stateField.SetValue(arena, Enum.ToObject(_stateField.FieldType, value));
        }
    }
}