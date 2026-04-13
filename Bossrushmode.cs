using System;
using System.Collections.Generic;
using UnityEngine;
using Constance;

namespace BossRushMod
{
    // =====================================================================
    //  PREDEFINED BOSS LISTS
    //
    //  5-boss (no SlimeNemesis, no Palettus) — tous les 14 bosses dispo
    //  couverts sur les 3 listes :
    //
    //  List 1 "Academy's Wrath"
    //    BrainStoker | MothQueen | Joker | PuppetHandStrings | PukeyBoy
    //  List 2 "Carnival Chaos"
    //    Smasher | AweKing | JugglerBalloons | PuppetHandCorruption | CornelisBoss
    //  List 3 "Void's Edge"
    //    JugglerBalls | JokerInvisible | PuppetHandKungfu | PuppetMaster | MothQueen
    //
    //  10-boss — tous les 16 bosses couverts :
    //
    //  List 1 "The Gauntlet"
    //    BrainStoker | Smasher | MothQueen | AweKing | Joker
    //    JugglerBalloons | SlimeNemesis | PuppetHandStrings | PuppetHandCorruption | PuppetMaster
    //  List 2 "Grand Tour"
    //    Palettus | Smasher | AweKing | CornelisBoss | JugglerBalls
    //    PukeyBoy | PuppetHandKungfu | PuppetMaster | MothQueen | Joker
    //  List 3 "Final Curtain"
    //    BrainStoker | Palettus | CornelisBoss | JokerInvisible | SlimeNemesis
    //    PukeyBoy | PuppetHandStrings | PuppetHandCorruption | PuppetHandKungfu | JugglerBalloons
    //
    //  16-boss : aléatoire (toujours)
    // =====================================================================
    public static class BossLists
    {
        public static readonly Dictionary<int, List<string[]>> Lists =
            new Dictionary<int, List<string[]>>
        {
            {
                5, new List<string[]>
                {
                    // List 1 — Academy's Wrath
                    new[] { "BrainStoker", "MothQueen", "Joker",
                            "PuppetHandStrings", "PukeyBoy" },
                    // List 2 — Carnival Chaos
                    new[] { "Smasher", "AweKing", "JugglerBalloons",
                            "PuppetHandCorruption", "CornelisBoss" },
                    // List 3 — Void's Edge
                    new[] { "JugglerBalls", "JokerInvisible", "PuppetHandKungfu",
                            "PuppetMaster", "MothQueen" },
                }
            },
            {
                10, new List<string[]>
                {
                    // List 1 — The Gauntlet
                    new[] { "BrainStoker", "Smasher", "MothQueen", "AweKing", "Joker",
                            "JugglerBalloons", "SlimeNemesis", "PuppetHandStrings",
                            "PuppetHandCorruption", "PuppetMaster" },
                    // List 2 — Grand Tour
                    new[] { "Palettus", "Smasher", "AweKing", "CornelisBoss", "JugglerBalls",
                            "PukeyBoy", "PuppetHandKungfu", "PuppetMaster",
                            "MothQueen", "Joker" },
                    // List 3 — Final Curtain
                    new[] { "BrainStoker", "Palettus", "CornelisBoss", "JokerInvisible",
                            "SlimeNemesis", "PukeyBoy", "PuppetHandStrings",
                            "PuppetHandCorruption", "PuppetHandKungfu", "JugglerBalloons" },
                }
            },
        };

        public static readonly string[] ListNames5 =
            { "Academy's Wrath", "Carnival Chaos", "Void's Edge" };
        public static readonly string[] ListNames10 =
            { "The Gauntlet", "Grand Tour", "Final Curtain" };

        public static string GetListName(int count, int listNum)
        {
            if (count == 5 && listNum >= 1 && listNum <= 3) return ListNames5[listNum - 1];
            if (count == 10 && listNum >= 1 && listNum <= 3) return ListNames10[listNum - 1];
            if (count == 16) return "Random";
            return "List " + listNum;
        }

        public static string[] GetBosses(int count, int listNum)
        {
            if (!Lists.ContainsKey(count)) return null;
            var lists = Lists[count];
            if (listNum < 1 || listNum > lists.Count) return null;
            return lists[listNum - 1];
        }
    }

    // =====================================================================
    //  BOSS RUSH MODE MANAGER
    // =====================================================================
    public static class BossRushModeManager
    {
        public static readonly List<string> AllBossIds = new List<string>
        {
            "BrainStoker", "Smasher", "Palettus",
            "MothQueen", "AweKing",
            "CornelisBoss", "Joker", "JugglerBalloons", "JugglerBalls", "JokerInvisible",
            "SlimeNemesis", "PukeyBoy",
            "PuppetHandStrings", "PuppetHandCorruption", "PuppetHandKungfu", "PuppetMaster",
        };

        public static bool IsActive { get; private set; }
        public static bool IsFinished { get; private set; }
        public static List<string> Sequence { get; private set; }
        public static int CurrentIndex { get; private set; }
        public static int TargetCount { get; private set; } = 5;
        // 1-3 pour listes fixes, 0 pour random (16 boss)
        public static int CurrentListNum { get; private set; } = 1;

        private static bool _expectingTransition = false;
        private static bool _defeatingInProgress = false;
        private static float _startTime;
        private static float _endTime;

        // Catégories leaderboard : 5-L1, 5-L2, 5-L3, 10-L1, 10-L2, 10-L3, 17
        // Encodage : count * 10 + listNum  (ex: 51=5boss-L1, 103=10boss-L3, 16=16boss-random)
        public static int LeaderboardKey =>
            (TargetCount == 16) ? 16 : TargetCount * 10 + CurrentListNum;
        public static bool IsLeaderboardRun =>
            (TargetCount == 5 || TargetCount == 10 || TargetCount == 16);

        public static readonly int[] LeaderboardCategories = { 5, 10, 16 };

        private static float _pausedElapsed = 0f;
        private static bool _timerPaused = false;
        private static float _pauseStart = 0f;

        public static float ElapsedTime
        {
            get
            {
                if (IsFinished) return _endTime - _startTime;
                if (!IsActive) return 0f;
                if (_timerPaused) return _pausedElapsed;
                return _pausedElapsed + (Time.realtimeSinceStartup - _pauseStart);
            }
        }

        public static void PauseTimer()
        {
            if (!IsActive || _timerPaused) return;
            _pausedElapsed += Time.realtimeSinceStartup - _pauseStart;
            _timerPaused = true;
        }

        public static void ResumeTimer()
        {
            if (!IsActive || !_timerPaused) return;
            _pauseStart = Time.realtimeSinceStartup;
            _timerPaused = false;
        }

        public static string CurrentBossId =>
            (Sequence != null && CurrentIndex < Sequence.Count)
                ? Sequence[CurrentIndex] : null;
        public static int BossesRemaining =>
            Sequence != null ? Sequence.Count - CurrentIndex : 0;

        // ── TRANSITION GUARD ──────────────────────────────────────────
        public static void SignalExpectedTransition()
        {
            _expectingTransition = true;
        }
        public static bool ConsumeExpectedTransition()
        {
            bool was = _expectingTransition;
            _expectingTransition = false;
            return was;
        }

        // ── DÉMARRER ──────────────────────────────────────────────────
        // listNum : 1-3 pour listes fixes, 0 pour random (16 boss)
        public static void StartRun(int count, int listNum = 0)
        {
            TargetCount = count;
            CurrentListNum = listNum;
            IsActive = true;
            IsFinished = false;
            CurrentIndex = 0;
            _expectingTransition = false;
            _defeatingInProgress = false;
            BossArenaMonitor.FlashbackHandledBoss = false;
            _startTime = Time.realtimeSinceStartup;
            _pausedElapsed = 0f;
            _timerPaused = false;
            _pauseStart = Time.realtimeSinceStartup;

            Sequence = BuildSequence(count, listNum);
            Debug.Log("[RushMode] Run lancé : " + count + " boss list=" + listNum
                      + " → " + string.Join(", ", Sequence));

            SignalExpectedTransition();
            BossTrigger.PrepareAndTeleport(Sequence[0]);
        }

        private static List<string> BuildSequence(int count, int listNum)
        {
            string[] preset = (listNum >= 1) ? BossLists.GetBosses(count, listNum) : null;
            if (preset != null)
            {
                // Copier et shuffler l'ordre
                var list = new List<string>(preset);
                Shuffle(list);
                return list;
            }
            // Random (count == 16, all bosses)
            var pool = new List<string>(AllBossIds);
            Shuffle(pool);
            var result = new List<string>();
            while (result.Count < count)
            {
                for (int i = 0; i < pool.Count && result.Count < count; i++)
                    result.Add(pool[i]);
                Shuffle(pool);
            }
            return result;
        }

        // ── BOSS VAINCU ───────────────────────────────────────────────
        public static bool OnBossDefeated()
        {
            if (!IsActive) return true;
            if (_defeatingInProgress) return false;
            _defeatingInProgress = true;
            CurrentIndex++;
            BossArenaMonitor.FlashbackHandledBoss = false;

            if (CurrentIndex >= Sequence.Count)
            {
                _endTime = Time.realtimeSinceStartup;
                IsActive = false;
                IsFinished = true;
                _defeatingInProgress = false;
                BossRushModeRunner.NotifyRunComplete();
                return true;
            }
            else
            {
                SignalExpectedTransition();
                BossTrigger.PrepareAndTeleport(Sequence[CurrentIndex]);
                _defeatingInProgress = false;
                return false;
            }
        }

        // ── MORT DU JOUEUR ────────────────────────────────────────────
        public static void OnPlayerDied()
        {
            if (!IsActive) return;
            if (_timerPaused) { _endTime = _startTime + _pausedElapsed; }
            else { _endTime = Time.realtimeSinceStartup; }
            IsActive = false;
            _timerPaused = false;
            _defeatingInProgress = false;
            BossStateGuard.RestoreAll();
            BossRushModeRunner.NotifyPlayerDied();
        }

        // ── ABANDON MANUEL ────────────────────────────────────────────
        public static void AbortRun()
        {
            if (!IsActive) return;
            _endTime = Time.realtimeSinceStartup;
            IsActive = false;
            _defeatingInProgress = false;
            BossStateGuard.RestoreAll();
        }

        public static void ResetAfterResults() => IsFinished = false;

        public static string FormatTime(float seconds)
        {
            int m = (int)(seconds / 60);
            float s = seconds % 60f;
            return string.Format("{0:D2}:{1:00.00}", m, s);
        }

        private static readonly System.Random _rng = new System.Random();
        private static void Shuffle(List<string> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                string tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }
    }

    // =====================================================================
    //  RUNNER
    // =====================================================================
    public class BossRushModeRunner : MonoBehaviour
    {
        public static BossRushModeRunner Instance { get; private set; }

        private static bool _pendingRunComplete = false;
        private static bool _pendingPlayerDied = false;

        private BossRushLeaderboardUI _lb;
        private RushModeTimerHUD _hud;

        private bool _gameOverWasActive = false;
        private float _healthCheckTimer = 0f;

        public static void NotifyRunComplete() => _pendingRunComplete = true;
        public static void NotifyPlayerDied() => _pendingPlayerDied = true;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _lb = GetComponent<BossRushLeaderboardUI>()
                ?? gameObject.AddComponent<BossRushLeaderboardUI>();
            _hud = GetComponent<RushModeTimerHUD>()
                ?? gameObject.AddComponent<RushModeTimerHUD>();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // True dès qu'on a chargé au moins une scène "Prod_" pendant la run
        private bool _hasBeenInGameScene = false;

        // Appelé à chaque chargement de scène
        // Logique : si on était dans une scène Prod_ et qu'on charge
        // une scène sans Prod_ → l'utilisateur a quitté la partie
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                   UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            string name = scene.name ?? "";
            bool isProdScene = name.StartsWith("Prod_");

            if (!BossRushModeManager.IsActive)
            {
                // Reset le tracker quand aucune run n'est active
                if (!isProdScene) _hasBeenInGameScene = false;
                return;
            }

            if (isProdScene)
            {
                // On est dans une scène de jeu — noter qu'on y est bien passé
                _hasBeenInGameScene = true;
            }
            else if (_hasBeenInGameScene)
            {
                // On était en jeu et on charge une scène non-Prod_ → retour menu
                Debug.Log("[RushMode] Quitte les scènes Prod_ (" + name + ") → run annulée.");
                _hasBeenInGameScene = false;
                BossRushModeManager.AbortRun();
                _hud.ShowDeathMessage();
            }
        }

        private void Update()
        {
            if (_pendingRunComplete)
            {
                _pendingRunComplete = false;
                _lb.ShowResults(BossRushModeManager.ElapsedTime,
                                BossRushModeManager.TargetCount,
                                BossRushModeManager.CurrentListNum);
            }
            if (_pendingPlayerDied)
            {
                _pendingPlayerDied = false;
                _hud.ShowDeathMessage();
            }

            if (BossRushModeManager.IsActive)
            {
                _healthCheckTimer += Time.unscaledDeltaTime;
                if (_healthCheckTimer >= 0.1f)
                {
                    _healthCheckTimer = 0f;
                    CheckGameOverUI();
                }
            }
            else
            {
                _gameOverWasActive = false;
            }
        }

        private bool _settingsWasOpen = false;

        private void CheckGameOverUI()
        {
            try
            {
                // Mort du joueur
                var panel = UnityEngine.Object.FindObjectOfType<CConUiPanel_GameOver>();
                bool isActive = panel != null && panel.gameObject.activeInHierarchy;
                if (isActive && !_gameOverWasActive)
                {
                    _gameOverWasActive = true;
                    Debug.Log("[RushMode] CConUiPanel_GameOver → run annulé.");
                    BossRushModeManager.OnPlayerDied();
                    TryDisablePersevereButtons(panel);
                }
                else if (!isActive) _gameOverWasActive = false;

                // Journal ouvert (ESC) → pause le chrono
                bool journalOpen = IsJournalPanelOpen();
                if (journalOpen && !_settingsWasOpen)
                {
                    _settingsWasOpen = true;
                    BossRushModeManager.PauseTimer();
                    Debug.Log("[RushMode] Journal ouvert → timer en pause.");
                }
                else if (!journalOpen && _settingsWasOpen)
                {
                    _settingsWasOpen = false;
                    BossRushModeManager.ResumeTimer();
                    Debug.Log("[RushMode] Journal fermé → timer repris.");
                }
            }
            catch { }
        }

        // Détecte si le panel Journal (ESC) est ouvert via sa classe Unity directe
        private static bool IsJournalPanelOpen()
        {
            try
            {
                var journal = UnityEngine.Object.FindObjectOfType<CConUiPanel_Journal>();
                return journal != null && journal.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        private static void TryDisablePersevereButtons(CConUiPanel_GameOver panel)
        {
            try
            {
                foreach (var btn in panel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    string n = btn.gameObject.name.ToLower();
                    if (n.Contains("persevere") || n.Contains("continue") ||
                        n.Contains("retry") || n.Contains("revive"))
                    {
                        btn.interactable = false;
                        btn.gameObject.SetActive(false);
                        Debug.Log("[RushMode] Disabled: " + btn.gameObject.name);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[RushMode] DisableButtons: " + e.Message); }
        }

        public static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("[BossRushModeRunner]");
            DontDestroyOnLoad(go);
            go.AddComponent<BossRushModeRunner>();
        }

        public static void OpenLeaderboard(int category, int listNum = 0)
        {
            Instance?.GetComponent<BossRushLeaderboardUI>()?.OpenStandalone(category, listNum);
        }

        public static void CloseLeaderboard()
        {
            Instance?.GetComponent<BossRushLeaderboardUI>()?.ClosePanel();
        }
    }

    // =====================================================================
    //  TIMER HUD
    // =====================================================================
    public class RushModeTimerHUD : MonoBehaviour
    {
        private float _deathMsgTimer = 0f;
        private const float DeathMsgDuration = 3.5f;
        private GUIStyle _sTime, _sSub, _sDeath;
        private bool _stylesOk;

        private static readonly Color C_BG = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color C_GOLD = new Color(1f, 0.85f, 0.1f, 1f);
        private static readonly Color C_GREY = new Color(0.7f, 0.7f, 0.7f, 1f);
        private static readonly Color C_RED = new Color(1f, 0.25f, 0.25f, 1f);
        private static readonly Color C_GREEN = new Color(0.3f, 0.95f, 0.3f, 1f);
        private static readonly Color C_PURP = new Color(0.65f, 0.15f, 1f, 1f);

        public void ShowDeathMessage() => _deathMsgTimer = DeathMsgDuration;
        private void Update() { if (_deathMsgTimer > 0f) _deathMsgTimer -= Time.deltaTime; }

        private void OnGUI()
        {
            bool active = BossRushModeManager.IsActive;
            bool finished = BossRushModeManager.IsFinished;
            bool dying = _deathMsgTimer > 0f;
            if (!active && !finished && !dying) return;
            InitStyles();

            float panelW = 260f;
            float panelX = Screen.width - panelW - 12f;
            float panelY = 12f;
            float panelH = dying ? 90f : (active ? 86f : 70f);

            GUI.color = C_BG;
            GUI.DrawTexture(new Rect(panelX - 8, panelY - 6, panelW + 16, panelH + 12),
                Texture2D.whiteTexture);
            GUI.color = Color.white;

            float y = panelY;
            if (dying)
            {
                GUI.color = C_RED;
                GUI.Label(new Rect(panelX, y, panelW, 28), "  RUN CANCELLED", _sDeath);
                y += 30f;
                GUI.color = C_GREY;
                GUI.Label(new Rect(panelX, y, panelW, 20),
                    "  " + BossRushModeManager.FormatTime(BossRushModeManager.ElapsedTime), _sSub);
                GUI.color = Color.white;
                return;
            }
            if (finished)
            {
                GUI.color = C_GREEN;
                GUI.Label(new Rect(panelX, y, panelW, 20), "  RUN COMPLETE", _sSub);
                y += 22f;
                GUI.color = C_GOLD;
                GUI.Label(new Rect(panelX, y, panelW, 36),
                    BossRushModeManager.FormatTime(BossRushModeManager.ElapsedTime), _sTime);
                GUI.color = Color.white;
                return;
            }
            int cur = BossRushModeManager.CurrentIndex + 1;
            int total = BossRushModeManager.Sequence?.Count ?? 0;
            GUI.color = C_PURP;
            GUI.Label(new Rect(panelX, y, panelW, 18), "  Boss " + cur + " / " + total, _sSub);
            y += 20f;
            GUI.color = C_GOLD;
            GUI.Label(new Rect(panelX, y, panelW, 36),
                BossRushModeManager.FormatTime(BossRushModeManager.ElapsedTime), _sTime);
            y += 36f;
            if (cur < total && BossRushModeManager.BossesRemaining > 1)
            {
                string nxtId = BossRushModeManager.Sequence[BossRushModeManager.CurrentIndex + 1];
                GUI.color = C_GREY;
                GUI.Label(new Rect(panelX, y, panelW, 16),
                    "  Next: " + BossRushFriendlyNames.Get(nxtId), _sSub);
            }
            GUI.color = Color.white;
        }

        private void InitStyles()
        {
            if (_stylesOk) return;
            _sTime = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = C_GOLD }
            };
            _sSub = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = C_GREY }
            };
            _sDeath = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = C_RED }
            };
            _stylesOk = true;
        }
    }
    // =====================================================================
    //  FRIENDLY NAMES — partagé par HUD et GUI
    // =====================================================================
    public static class BossRushFriendlyNames
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> _names =
            new System.Collections.Generic.Dictionary<string, string>
        {
            { "BrainStoker",          "Brian" },
            { "Smasher",              "Cubicus" },
            { "Palettus",             "Palettus" },
            { "MothQueen",            "High Patia" },
            { "AweKing",              "Awe King" },
            { "CornelisBoss",         "Cornelis" },
            { "Joker",                "The Jester" },
            { "JugglerBalloons",      "The Manipulator" },
            { "JugglerBalls",         "The Manipulator, Encore" },
            { "JokerInvisible",       "Jester, Encore" },
            { "PuppetHandStrings",    "Forsaken Will" },
            { "PuppetHandCorruption", "Corrupted Mind" },
            { "PuppetHandKungfu",     "Wounded Vessel" },
            { "PuppetMaster",         "Final Boss" },
            { "SlimeNemesis",         "Lord Korba" },
            { "PukeyBoy",             "Sir Barfalot" },
        };

        public static string Get(string id)
        {
            if (id == null) return "?";
            return _names.TryGetValue(id, out string n) ? n : id;
        }
    }

}