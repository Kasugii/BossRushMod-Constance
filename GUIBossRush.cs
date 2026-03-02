using UnityEngine;
using BepInEx;
using System.Collections.Generic;
using Constance;

namespace BossRushMod
{
    public static class BossDifficultyRegistry
    {
        public static readonly Dictionary<string, int> Difficulties =
            new Dictionary<string, int>() { { "BrainStoker", 2 } };
    }

    public static class BossRushUtils
    {
        private static readonly System.Reflection.FieldInfo _arenaStateField =
            typeof(CConArenaEvent_Boss).GetField("_state",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);

        public static bool IsInActiveBossFight(out string activeBossId)
        {
            activeBossId = null;
            try
            {
                var arena = UnityEngine.Object.FindObjectOfType<CConArenaEvent_Boss>();
                if (arena == null) return false;
                byte state = 0;
                if (_arenaStateField != null)
                    state = System.Convert.ToByte(_arenaStateField.GetValue(arena));
                if (state == 1 || state == 2)
                {
                    activeBossId = arena.bossId.IsValid() ? arena.bossId.StringValue : null;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static bool IsHealBlocked(out string reason)
        {
            reason = null;
            if (BossRushModeManager.IsActive)
            {
                reason = "Rush Mode active — healing disabled!";
                return true;
            }
            if (!IsInActiveBossFight(out string bossId)) return false;
            if (bossId != null &&
                BossDifficultyRegistry.Difficulties.TryGetValue(bossId, out int diff))
            {
                if (diff == 4) { reason = "EXTREME — heal disabled!"; return true; }
                return false;
            }
            reason = "Fight in progress — heal disabled!";
            return true;
        }

        public static bool HealPlayer()
        {
            if (IsHealBlocked(out string reason))
            {
                Debug.LogWarning("[BossRush] HealPlayer blocked : " + reason);
                return false;
            }
            try
            {
                var registry = CConSceneRegistry.Instance;
                if (registry == null) return false;
                var player = registry.PlayerOne;
                if (player == null) return false;
                player.Health.Fill();
                (player as CConPlayerEntity)?.Paint.Fill();
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BossRush] HealPlayer: " + e.Message);
                return false;
            }
        }

        public static void TeleportToShrine()
        {
            try
            {
                var registry = CConSceneRegistry.Instance;
                if (registry == null) return;
                ConCheckPointId shrineId = registry.CheckPointManager.ShrineCheckPoint;
                if (!shrineId.IsValid()) return;
                ConLevelId levelId = shrineId.ExtractLevelId();
                if (!levelId.IsValid()) return;
                HealPlayer();
                var cmd = new ConTransitionCommand_Default(
                    shrineId, levelId, null, Vector2.zero,
                    registry.GlobalConfig.respawnTransitionIn,
                    true, false, ConRespawnOrigin.Cocoon, null, null);
                registry.Get<CConTransitionManager>().Init(cmd, 0f);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[BossRush] TeleportToShrine: " + e.Message);
            }
        }
    }

    [BepInPlugin("com.votre.bossrush", "Boss Selector UI", "2.3.0")]
    public class BossRushUI : BaseUnityPlugin
    {
        public static Rect WindowRect { get; private set; }

        private bool _showGui = false;
        private bool _isMinimized = false;
        private Rect _windowRect;
        private Vector2 _scrollPos;
        private bool _firstFrame = true;

        // Rush Mode panel state
        private bool _showRushPanel = false;
        // 0 = top level (choose 5/10/17)
        // 1 = choose list for 5
        // 2 = choose list for 10
        private int _rushSubPage = 0;
        // Which list preview is expanded (-1 = none)
        private int _previewList = -1;
        // Which boss is expanded for difficulty (-1 = none)
        private string _expandedBoss = null;
        // Leaderboard window visible
        private bool _showLB = false;

        // Styles
        private GUIStyle _regionStyle, _diffLabelStyle, _utilButtonStyle,
                         _rushBigBtn, _rushCounterBtn, _subTextStyle,
                         _listBtn, _listBtnSelected, _previewBox;
        private bool _stylesReady = false;

        private string _statusMsg = "";
        private float _statusTimer = 0f;

        private static readonly string[] DiffLabels = { "", "EASY", "NORMAL", "HARD", "EXTREME" };
        private static readonly Color[] DiffColors =
        {
            Color.white,
            new Color(0.4f, 1f, 0.4f),
            new Color(0.9f, 0.9f, 0.9f),
            new Color(1f, 0.65f, 0.1f),
            new Color(1f, 0.2f, 0.2f),
        };
        private static readonly HashSet<string> DifficultySupportedBosses =
            new HashSet<string> { "BrainStoker" };

        private readonly Dictionary<string, string> _idToName =
            new Dictionary<string, string>
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

        private readonly Dictionary<string, string[]> _bossRegions =
            new Dictionary<string, string[]>
        {
            { "Floral Foundry",   new[] { "Palettus", "Smasher", "BrainStoker" } },
            { "Astral Academy",   new[] { "MothQueen", "AweKing" } },
            { "Chaotic Carnival", new[] { "CornelisBoss", "Joker", "JugglerBalloons",
                                          "JugglerBalls", "JokerInvisible" } },
            { "Vanishing Vaults", new[] { "SlimeNemesis", "PukeyBoy" } },
            { "Voids",            new[] { "PuppetHandStrings", "PuppetHandCorruption",
                                          "PuppetHandKungfu" } },
            { "Hide Voids",       new[] { "PuppetMaster" } },
        };

        void Awake()
        {
            var harmony = new HarmonyLib.Harmony("com.votre.bossrush");
            harmony.PatchAll();
            VoidDebuffBlockPatch.Register(harmony);
            Logger.LogInfo("[BossRush] Patches registered.");
            var hookGo = new GameObject("[BossRushSceneHook]");
            hookGo.AddComponent<BossRushSceneHook>();
            BossRushModeRunner.EnsureExists();
        }

        void Update()
        {
            if (UnityInput.Current.GetKeyDown(KeyCode.F1)) _showGui = !_showGui;
            if (UnityInput.Current.GetKeyDown(KeyCode.F2)) BossRushTeleporter.DumpCheckpointsInScene();
            if (UnityInput.Current.GetKeyDown(KeyCode.F4))
            {
                if (BossRushModeManager.IsActive) BossRushModeManager.AbortRun();
                BossRushUtils.TeleportToShrine();
                ShowStatus("TP Shrine!");
            }
            if (UnityInput.Current.GetKeyDown(KeyCode.F5))
            {
                if (!BossRushUtils.HealPlayer())
                    ShowStatus(BossRushUtils.IsHealBlocked(out string r) ? r : "Heal unavailable.");
                else ShowStatus("Healed!");
            }
            if (_statusTimer > 0f) _statusTimer -= Time.deltaTime;
        }

        void OnGUI()
        {
            if (!_showGui) return;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            InitStyles();

            if (_firstFrame)
            {
                _windowRect = new Rect(
                    Screen.width / 2f - 175f,
                    Screen.height - 520f,
                    360f, 490f);
                _firstFrame = false;
            }

            float height = _isMinimized ? 25f : ComputeWindowHeight();
            _windowRect = GUI.Window(0,
                new Rect(_windowRect.x, _windowRect.y, _windowRect.width, height),
                DrawWindow, "BOSS SELECTOR");
            WindowRect = _windowRect;
        }

        private float ComputeWindowHeight()
        {
            if (!_showRushPanel) return 490f;
            if (BossRushModeManager.IsActive) return 540f;
            // top page: 3 big buttons
            if (_rushSubPage == 0) return 540f;
            // list choice page: 3 list buttons + optional preview
            return 580f + (_previewList >= 0 ? 120f : 0f);
        }

        void DrawWindow(int id)
        {
            if (GUI.Button(new Rect(_windowRect.width - 30, 2, 25, 20),
                    _isMinimized ? "+" : "-"))
                _isMinimized = !_isMinimized;
            if (_isMinimized) { GUI.DragWindow(); return; }

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);

            // ── UTILITIES ────────────────────────────────────────────
            GUILayout.Space(4);
            GUILayout.Label("-- Utilities --", _regionStyle);
            GUILayout.Space(3);
            GUILayout.BeginHorizontal();
            Color prev = GUI.backgroundColor;

            GUI.backgroundColor = new Color(0.3f, 0.5f, 1f);
            if (GUILayout.Button("TP Shrine  [F4]", _utilButtonStyle, GUILayout.Height(30)))
            {
                if (BossRushModeManager.IsActive) BossRushModeManager.AbortRun();
                BossRushUtils.TeleportToShrine();
                CloseGui(); ShowStatus("TP Shrine launched!");
            }
            GUILayout.Space(4);

            bool healBlocked = BossRushUtils.IsHealBlocked(out string blockReason);
            GUI.backgroundColor = healBlocked
                ? new Color(0.4f, 0.4f, 0.4f) : new Color(0.3f, 0.8f, 0.3f);
            GUI.enabled = !healBlocked;
            if (GUILayout.Button("Heal  [F5]", _utilButtonStyle, GUILayout.Height(30)))
            { BossRushUtils.HealPlayer(); ShowStatus("Healed!"); }
            GUI.enabled = true;
            GUI.backgroundColor = prev;
            GUILayout.EndHorizontal();

            if (healBlocked && !string.IsNullOrEmpty(blockReason))
            {
                GUI.contentColor = new Color(1f, 0.35f, 0.35f);
                GUILayout.Label("  " + blockReason, _subTextStyle);
                GUI.contentColor = Color.white;
            }
            else if (_statusTimer > 0f && !string.IsNullOrEmpty(_statusMsg))
            {
                GUI.contentColor = new Color(1f, 1f, 0.3f);
                GUILayout.Label(_statusMsg, _subTextStyle);
                GUI.contentColor = Color.white;
            }
            else GUILayout.Space(20);

            // ── RUSH MODE ────────────────────────────────────────────
            GUILayout.Space(4);
            DrawRushModeSection();

            // ── BOSS LIST ────────────────────────────────────────────
            GUILayout.Space(4);
            GUILayout.Label("-- Bosses --", _regionStyle);
            foreach (var region in _bossRegions)
            {
                GUILayout.Space(6);
                GUILayout.Label(region.Key, _regionStyle);
                foreach (string bossId in region.Value)
                {
                    string name = _idToName.ContainsKey(bossId) ? _idToName[bossId] : bossId;
                    bool hasDiff = DifficultySupportedBosses.Contains(bossId) &&
                                   BossDifficultyRegistry.Difficulties.ContainsKey(bossId);
                    bool isExpanded = _expandedBoss == bossId;

                    if (hasDiff)
                    {
                        // Boss avec difficulté : bouton toggle expand
                        Color bPrev = GUI.backgroundColor;
                        GUI.backgroundColor = isExpanded
                            ? new Color(0.3f, 0.1f, 0.5f) : new Color(0.25f, 0.25f, 0.3f);
                        if (GUILayout.Button((isExpanded ? "v  " : ">  ") + name,
                                GUILayout.Height(28)))
                            _expandedBoss = isExpanded ? null : bossId;
                        GUI.backgroundColor = bPrev;

                        if (isExpanded)
                            DrawExpandedBoss(bossId, name);
                    }
                    else
                    {
                        // Boss sans difficulté : tp direct
                        if (GUILayout.Button(name, GUILayout.Height(28)))
                        {
                            if (BossTrigger.PrepareAndTeleport(bossId)) CloseGui();
                            else BossTrigger.LaunchCurrentBoss();
                        }
                    }
                }
            }

            // ── LEADERBOARD ──────────────────────────────────────────
            GUILayout.Space(8);
            Color lbPrev = GUI.backgroundColor;
            GUI.backgroundColor = _showLB
                ? new Color(0.5f, 0.1f, 0.85f) : new Color(0.35f, 0.05f, 0.6f);
            if (GUILayout.Button("LEADERBOARD", _utilButtonStyle, GUILayout.Height(30)))
            {
                _showLB = !_showLB;
                if (_showLB)
                    BossRushModeRunner.OpenLeaderboard(5, 1);
                else
                    BossRushModeRunner.CloseLeaderboard();
            }
            GUI.backgroundColor = lbPrev;

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        // ─────────────────────────────────────────────────────────────
        //  RUSH MODE SECTION
        // ─────────────────────────────────────────────────────────────
        void DrawRushModeSection()
        {
            Color prev = GUI.backgroundColor;

            // En-tête
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.35f, 0.05f, 0.6f);
            if (GUILayout.Button((_showRushPanel ? "v" : ">") + "  RUSH MODE",
                    _utilButtonStyle, GUILayout.Height(30)))
            {
                _showRushPanel = !_showRushPanel;
                _rushSubPage = 0;
                _previewList = -1;
            }
            GUI.backgroundColor = prev;
            GUILayout.EndHorizontal();

            if (!_showRushPanel) return;

            // ── Run en cours ──────────────────────────────────────────
            if (BossRushModeManager.IsActive)
            {
                int cur = BossRushModeManager.CurrentIndex + 1;
                int total = BossRushModeManager.Sequence?.Count ?? 0;
                string listName = BossLists.GetListName(
                    BossRushModeManager.TargetCount,
                    BossRushModeManager.CurrentListNum);

                GUI.contentColor = new Color(1f, 0.85f, 0.1f);
                GUILayout.Label("  " +
                    BossRushModeManager.FormatTime(BossRushModeManager.ElapsedTime) +
                    "   Boss " + cur + " / " + total, _subTextStyle);
                GUI.contentColor = new Color(0.75f, 0.5f, 1f);
                GUILayout.Label("  " + listName, _subTextStyle);
                GUI.contentColor = new Color(1f, 0.35f, 0.35f);
                GUILayout.Label("  Healing disabled for the entire session", _subTextStyle);
                GUI.contentColor = Color.white;

                prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.7f, 0.15f, 0.15f);
                if (GUILayout.Button("Cancel run", _utilButtonStyle, GUILayout.Height(26)))
                {
                    BossRushModeManager.AbortRun();
                    BossRushUtils.TeleportToShrine();
                    CloseGui();
                }
                GUI.backgroundColor = prev;
                return;
            }

            GUILayout.Space(6);

            // ── Page 0 : choix du mode (5 / 10 / 17) ────────────────
            if (_rushSubPage == 0)
            {
                GUI.contentColor = new Color(0.8f, 0.8f, 0.8f);
                GUILayout.Label("  Choose a run type:", _subTextStyle);
                GUI.contentColor = Color.white;
                GUILayout.Space(4);

                // Bouton 5 boss
                DrawModeButton("5 BOSSES  — 3 curated lists", new Color(0.55f, 0.1f, 0.9f), () =>
                {
                    _rushSubPage = 1; _previewList = -1;
                });

                GUILayout.Space(4);

                // Bouton 10 boss
                DrawModeButton("10 BOSSES  — 3 curated lists", new Color(0.4f, 0.08f, 0.7f), () =>
                {
                    _rushSubPage = 2; _previewList = -1;
                });

                GUILayout.Space(4);

                // Bouton 17 boss
                DrawModeButton("16 BOSSES  — All bosses, random order", new Color(0.25f, 0.05f, 0.5f), () =>
                {
                    BossRushModeManager.StartRun(16, 0);
                    CloseGui();
                    ShowStatus("Rush Mode: 16 boss full run!");
                });

                GUILayout.Space(6);
                GUI.contentColor = new Color(1f, 0.35f, 0.35f);
                GUILayout.Label("  Healing disabled for the entire session", _subTextStyle);
                GUI.contentColor = Color.white;
            }

            // ── Page 1/2 : choix de la liste ─────────────────────────
            else
            {
                int count = (_rushSubPage == 1) ? 5 : 10;
                string[] listNames = (count == 5)
                    ? BossLists.ListNames5 : BossLists.ListNames10;

                GUILayout.BeginHorizontal();
                prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.2f, 0.2f, 0.35f);
                if (GUILayout.Button("< Back", GUILayout.Width(60), GUILayout.Height(24)))
                { _rushSubPage = 0; _previewList = -1; }
                GUI.backgroundColor = prev;
                GUI.contentColor = new Color(1f, 0.85f, 0.1f);
                GUILayout.Label("  " + count + " BOSSES", _subTextStyle);
                GUI.contentColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                for (int i = 0; i < 3; i++)
                {
                    int listNum = i + 1;
                    DrawListChoice(count, listNum, listNames[i]);
                    GUILayout.Space(2);
                }

                GUILayout.Space(6);
                GUI.contentColor = new Color(1f, 0.35f, 0.35f);
                GUILayout.Label("  Healing disabled for the entire session", _subTextStyle);
                GUI.contentColor = Color.white;
            }
        }

        void DrawModeButton(string label, Color col, System.Action onClick)
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = col;
            if (GUILayout.Button(label, _rushBigBtn, GUILayout.Height(36)))
                onClick();
            GUI.backgroundColor = prev;
        }

        void DrawListChoice(int count, int listNum, string listName)
        {
            Color prev = GUI.backgroundColor;
            bool expanded = (_previewList == listNum);

            // Ligne principale : [  L1 — Name  ] [?] [ START ]
            GUILayout.BeginHorizontal();

            // Badge liste
            GUI.backgroundColor = new Color(0.35f, 0.05f, 0.6f);
            GUILayout.Label("L" + listNum, _rushCounterBtn,
                GUILayout.Width(28), GUILayout.Height(32));

            // Nom de la liste
            GUI.backgroundColor = new Color(0.2f, 0.02f, 0.38f);
            GUILayout.Label(listName, _listBtn,
                GUILayout.Height(32), GUILayout.Width(152));

            // Bouton voir/cacher les bosses
            GUI.backgroundColor = expanded
                ? new Color(0.5f, 0.3f, 0.05f) : new Color(0.2f, 0.2f, 0.35f);
            if (GUILayout.Button(expanded ? "v" : "?",
                    _rushCounterBtn, GUILayout.Width(28), GUILayout.Height(32)))
                _previewList = expanded ? -1 : listNum;

            // Bouton START
            GUI.backgroundColor = new Color(0.55f, 0.10f, 0.90f);
            if (GUILayout.Button("START", _utilButtonStyle,
                    GUILayout.Width(60), GUILayout.Height(32)))
            {
                BossRushModeManager.StartRun(count, listNum);
                CloseGui();
                ShowStatus("Rush Mode: " + count + " boss — " + listName + "!");
            }
            GUI.backgroundColor = prev;
            GUILayout.EndHorizontal();

            // Preview des bosses si expansé
            if (expanded)
            {
                string[] bosses = BossLists.GetBosses(count, listNum);
                if (bosses != null)
                {
                    GUI.backgroundColor = new Color(0.12f, 0.02f, 0.22f);
                    GUILayout.BeginVertical(_previewBox);
                    string line = "";
                    for (int b = 0; b < bosses.Length; b++)
                    {
                        string n = _idToName.ContainsKey(bosses[b]) ? _idToName[bosses[b]] : bosses[b];
                        line += (b == 0 ? "" : "  •  ") + n;
                    }
                    GUI.contentColor = new Color(0.9f, 0.75f, 1f);
                    GUILayout.Label(line, _subTextStyle);
                    GUI.contentColor = Color.white;
                    GUILayout.EndVertical();
                    GUI.backgroundColor = prev;
                }
            }
        }



        // ─────────────────────────────────────────────────────────────
        //  MISC HELPERS
        // ─────────────────────────────────────────────────────────────
        private void DrawExpandedBoss(string bossId, string displayName)
        {
            int level = BossDifficultyRegistry.Difficulties[bossId];
            Color prev = GUI.backgroundColor;

            // Fond légèrement teinté pour l'encadré
            GUI.backgroundColor = new Color(0.15f, 0.05f, 0.25f);
            GUILayout.BeginVertical(_previewBox);

            // Sélecteur de difficulté
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.2f, 0.2f, 0.35f);
            if (GUILayout.Button("<", GUILayout.Width(26), GUILayout.Height(22)))
                BossDifficultyRegistry.Difficulties[bossId] = Mathf.Clamp(level - 1, 1, 4);
            GUI.contentColor = DiffColors[level];
            GUILayout.Label(DiffLabels[level], _diffLabelStyle,
                GUILayout.Height(22), GUILayout.ExpandWidth(true));
            GUI.contentColor = Color.white;
            if (GUILayout.Button(">", GUILayout.Width(26), GUILayout.Height(22)))
                BossDifficultyRegistry.Difficulties[bossId] = Mathf.Clamp(level + 1, 1, 4);
            GUILayout.EndHorizontal();

            // Bouton LAUNCH
            GUI.backgroundColor = new Color(0.3f, 0.55f, 0.3f);
            if (GUILayout.Button("LAUNCH  " + displayName, _utilButtonStyle, GUILayout.Height(26)))
            {
                if (BossTrigger.PrepareAndTeleport(bossId)) CloseGui();
                else BossTrigger.LaunchCurrentBoss();
                _expandedBoss = null;
            }
            GUI.backgroundColor = prev;
            GUILayout.EndVertical();
            GUILayout.Space(2);
        }

        private void CloseGui()
        {
            _showGui = false;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void ShowStatus(string msg) { _statusMsg = msg; _statusTimer = 2.5f; }

        private void InitStyles()
        {
            if (_stylesReady) return;
            _regionStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.4f, 0.7f, 1f) }
            };
            _diffLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 11
            };
            _utilButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            _rushBigBtn = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _rushCounterBtn = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _subTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            _listBtn = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.9f, 0.75f, 1f) }
            };
            _listBtnSelected = new GUIStyle(_listBtn)
            {
                normal = { textColor = new Color(1f, 0.85f, 0.1f) }
            };
            _previewBox = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 4, 4)
            };
            _stylesReady = true;
        }
    }
}