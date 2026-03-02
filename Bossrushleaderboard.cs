// =====================================================================
//  BOSS RUSH LEADERBOARD — v3
//  Serveur : https://leaderboard-constance.onrender.com
//
//  Clés leaderboard :
//    5-boss  L1 = 51 | L2 = 52 | L3 = 53
//    10-boss L1 = 101| L2 = 102| L3 = 103
//    17-boss random  = 17
// =====================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace BossRushMod
{
    public static class LBConfig
    {
        public const string ServerUrl = "https://leaderboard-constance.onrender.com";
        public const string UsernameKey = "BossRush_Username";
        public const int MaxNameLength = 20;
        public const int MaxEntries = 50;

        public static string GetUsername() => PlayerPrefs.GetString(UsernameKey, "");
        public static void SaveUsername(string n) => PlayerPrefs.SetString(UsernameKey, n);
        public static bool HasUsername() => !string.IsNullOrEmpty(GetUsername());
    }

    public class LBEntry
    {
        public string username;
        public int category;
        public float time_seconds;
        public string date;
    }

    public static class LBNetwork
    {
        public static IEnumerator Submit(string username, int category, float time,
                                          Action<bool> onDone)
        {
            string body = "{\"username\":\"" + Esc(username) + "\",\"category\":" + category +
                          ",\"time_seconds\":" + time.ToString("F3",
                              System.Globalization.CultureInfo.InvariantCulture) + "}";
            using (var req = new UnityWebRequest(
                LBConfig.ServerUrl.TrimEnd('/') + "/submit", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 12;
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                if (!ok) Debug.LogWarning("[LB] Submit error: " + req.error);
                onDone?.Invoke(ok);
            }
        }

        public static IEnumerator Fetch(int category, Action<List<LBEntry>> onDone)
        {
            string url = LBConfig.ServerUrl.TrimEnd('/') +
                "/leaderboard?category=" + category + "&limit=" + LBConfig.MaxEntries;
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 12;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[LB] Fetch error: " + req.error);
                    onDone?.Invoke(new List<LBEntry>());
                    yield break;
                }
                onDone?.Invoke(ParseArray(req.downloadHandler.text));
            }
        }

        private static List<LBEntry> ParseArray(string json)
        {
            var result = new List<LBEntry>();
            int idx = 0;
            while (true)
            {
                int s = json.IndexOf('{', idx); if (s < 0) break;
                int e = json.IndexOf('}', s); if (e < 0) break;
                string obj = json.Substring(s, e - s + 1);
                try
                {
                    result.Add(new LBEntry
                    {
                        username = Val(obj, "username"),
                        category = int.Parse(Val(obj, "category")),
                        time_seconds = float.Parse(Val(obj, "time_seconds"),
                                           System.Globalization.CultureInfo.InvariantCulture),
                        date = Val(obj, "date"),
                    });
                }
                catch { }
                idx = e + 1;
            }
            return result;
        }

        private static string Val(string json, string key)
        {
            string search = "\"" + key + "\":";
            int ki = json.IndexOf(search); if (ki < 0) return "";
            int vi = ki + search.Length;
            if (vi >= json.Length) return "";
            if (json[vi] == '"')
            {
                int end = json.IndexOf('"', vi + 1);
                return end < 0 ? "" : json.Substring(vi + 1, end - vi - 1);
            }
            int endV = vi;
            while (endV < json.Length && json[endV] != ',' && json[endV] != '}') endV++;
            return json.Substring(vi, endV - vi).Trim();
        }

        private static string Esc(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // =====================================================================
    //  LEADERBOARD UI
    // =====================================================================
    public class BossRushLeaderboardUI : MonoBehaviour
    {
        private static readonly Color C_BG = new Color(0.10f, 0.01f, 0.20f, 0.97f);
        private static readonly Color C_PURPLE = new Color(0.55f, 0.10f, 0.90f, 1f);
        private static readonly Color C_GOLD = new Color(1f, 0.85f, 0.10f, 1f);
        private static readonly Color C_WHITE = Color.white;
        private static readonly Color C_GREY = new Color(0.65f, 0.65f, 0.65f);
        private static readonly Color C_GREEN = new Color(0.30f, 0.90f, 0.30f);
        private static readonly Color C_RED = new Color(0.90f, 0.25f, 0.25f);
        private static readonly Color C_LAVEND = new Color(0.80f, 0.60f, 1.00f);

        // Results state
        private bool _showResults;
        private float _runTime;
        private int _runCategory;   // LB key (51,52,53,101,102,103,17)
        private int _runCount;
        private int _runListNum;
        private bool _submitted, _submitting;
        private string _submitMsg = "";
        private bool _enteringName;
        private string _nameInput = "";

        // Leaderboard state
        private bool _showLB;
        private int _lbCategory;  // current LB key
        private int _lbCount;     // 5/10/17
        private int _lbListNum;   // 1-3 or 0
        private bool _loading;
        private List<LBEntry> _entries = new List<LBEntry>();
        private Vector2 _scroll;

        private const float LB_W = 540f;
        private const float LB_H = 560f;
        private const float RES_W = 520f;

        private Rect _rResults, _rLB;

        private GUIStyle _sWin, _sTitle, _sTimer, _sHdr, _sRow,
                         _sBtn, _sBtnSel, _sInput, _sSep, _sTab, _sTabSel;
        private bool _stylesOk;

        // ── API publique ──────────────────────────────────────────────
        public void ShowResults(float time, int count, int listNum)
        {
            _runTime = time;
            _runCount = count;
            _runListNum = listNum;
            _runCategory = (count == 16) ? 16 : count * 10 + listNum;
            _showResults = true;
            _showLB = false;
            _submitted = false;
            _submitting = false;
            _submitMsg = "";
            _enteringName = !LBConfig.HasUsername();
            _nameInput = LBConfig.GetUsername();
        }

        public void ClosePanel()
        {
            _showLB = false;
        }

        public void OpenStandalone(int count, int listNum)
        {
            _lbCount = count;
            _lbListNum = listNum;
            _lbCategory = (count == 16) ? 16 : count * 10 + listNum;
            _showLB = true;
            LoadEntries(_lbCategory);
        }

        // ── OnGUI ─────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (!_showResults && !_showLB) return;
            InitStyles();

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            Rect boss = BossRushUI.WindowRect;
            float lbX = (boss != Rect.zero) ? boss.x + boss.width + 8f : Screen.width - LB_W - 12f;
            float lbY = (boss != Rect.zero) ? boss.y : 60f;

            if (_showResults)
            {
                float resH = _enteringName ? 330f : 280f;
                _rResults = new Rect(lbX, lbY, RES_W, resH);
                _rResults = GUILayout.Window(77, _rResults, DrawResults, "",
                    _sWin, GUILayout.Width(RES_W), GUILayout.Height(resH));
            }

            if (_showLB)
            {
                float baseY = lbY + (_showResults ? _rResults.height + 6f : 0f);
                _rLB = new Rect(lbX, baseY, LB_W, LB_H);
                _rLB = GUILayout.Window(78, _rLB, DrawLeaderboard, "",
                    _sWin, GUILayout.Width(LB_W), GUILayout.Height(LB_H));
            }
        }

        // ── Résultats ─────────────────────────────────────────────────
        private void DrawResults(int id)
        {
            Color p = GUI.color;
            GUILayout.Space(8);

            GUI.color = C_GOLD;
            GUILayout.Label("RUN COMPLETE", _sTitle);
            GUI.color = p;
            GUILayout.Space(4);

            GUI.color = C_WHITE;
            GUILayout.Label(BossRushModeManager.FormatTime(_runTime), _sTimer);
            GUI.color = p;

            string listName = BossLists.GetListName(_runCount, _runListNum);
            GUI.color = C_LAVEND;
            GUILayout.Label(_runCount + " bosses  —  " + listName, _sHdr);
            GUI.color = p;
            GUILayout.Space(8);

            bool isLB = (_runCategory == 16 || _runCount == 5 || _runCount == 10);

            if (isLB && !_submitted)
            {
                if (_enteringName)
                {
                    GUILayout.Label("Choose your username:", _sHdr);
                    GUILayout.Space(3);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(30);
                    _nameInput = GUILayout.TextField(_nameInput, LBConfig.MaxNameLength,
                        _sInput, GUILayout.Height(30), GUILayout.Width(280));
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                bool canSubmit = !_submitting && (!_enteringName || _nameInput.Trim().Length >= 2);
                GUI.enabled = canSubmit;
                GUI.color = _submitting ? C_GREY : C_GOLD;
                if (GUILayout.Button(_submitting ? "Submitting..." : "Submit score",
                        _sBtn, GUILayout.Height(32), GUILayout.Width(150)))
                    StartCoroutine(DoSubmit());
                GUI.enabled = true;
                GUILayout.Space(8);
                GUI.color = C_PURPLE;
                if (GUILayout.Button("Leaderboard", _sBtn, GUILayout.Height(32), GUILayout.Width(130)))
                    OpenLeaderboardPanel(_runCount, _runListNum);
                GUI.color = p;
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_submitMsg))
                {
                    GUILayout.Space(4);
                    GUI.color = _submitMsg.StartsWith("OK") ? C_GREEN : C_RED;
                    GUILayout.Label(_submitMsg, _sHdr);
                    GUI.color = p;
                }
            }
            else if (_submitted)
            {
                GUI.color = C_GREEN; GUILayout.Label("Score submitted!", _sHdr); GUI.color = p;
                GUI.color = C_PURPLE;
                if (GUILayout.Button("View Leaderboard", _sBtn, GUILayout.Height(32), GUILayout.Width(180)))
                    OpenLeaderboardPanel(_runCount, _runListNum);
                GUI.color = p;
            }

            GUILayout.Space(8);
            GUI.color = C_GREY;
            if (GUILayout.Button("Close", _sBtn, GUILayout.Height(26), GUILayout.Width(80)))
            {
                _showResults = false;
                BossRushModeManager.ResetAfterResults();
            }
            GUI.color = p;
            GUI.DragWindow();
        }

        private IEnumerator DoSubmit()
        {
            string name = _nameInput.Trim();
            if (_enteringName && name.Length >= 2) { LBConfig.SaveUsername(name); _enteringName = false; }
            if (!LBConfig.HasUsername()) yield break;
            _submitting = true;
            yield return LBNetwork.Submit(LBConfig.GetUsername(), _runCategory, _runTime, ok =>
            {
                _submitting = false; _submitted = ok;
                _submitMsg = ok ? "OK  Score submitted!" : "Network error. Try again.";
                if (ok) OpenLeaderboardPanel(_runCount, _runListNum);
            });
        }

        private void OpenLeaderboardPanel(int count, int listNum)
        {
            _lbCount = count;
            _lbListNum = listNum;
            _lbCategory = (count == 16) ? 16 : count * 10 + listNum;
            _showLB = true;
            LoadEntries(_lbCategory);
        }

        private void LoadEntries(int cat)
        {
            if (_loading) return;
            _entries.Clear();
            StartCoroutine(DoFetch(cat));
        }

        private IEnumerator DoFetch(int cat)
        {
            _loading = true;
            yield return LBNetwork.Fetch(cat, list => _entries = list);
            _loading = false;
        }

        // ── Leaderboard ───────────────────────────────────────────────
        private void DrawLeaderboard(int id)
        {
            Color p = GUI.color;
            GUILayout.Space(8);

            // Titre
            string listName = BossLists.GetListName(_lbCount, _lbListNum);
            GUI.color = C_GOLD;
            GUILayout.Label("LEADERBOARD  —  " + _lbCount + " BOSSES  •  " + listName.ToUpper(),
                _sTitle);
            GUI.color = p;
            GUILayout.Space(6);

            // ── Onglets : 5 / 10 / 17 ─────────────────────────────────
            GUILayout.BeginHorizontal();
            foreach (int cnt in new[] { 5, 10, 16 })
            {
                bool sel = _lbCount == cnt;
                GUI.color = sel ? C_GOLD : C_GREY;
                if (GUILayout.Button(cnt + " boss", sel ? _sTabSel : _sTab,
                        GUILayout.Height(26), GUILayout.Width(cnt == 16 ? 80 : 60)))
                {
                    _lbCount = cnt;
                    _lbListNum = (cnt == 17) ? 0 : 1;
                    _lbCategory = (cnt == 16) ? 16 : cnt * 10 + _lbListNum;
                    LoadEntries(_lbCategory);
                }
            }
            GUILayout.FlexibleSpace();
            GUI.color = p;
            GUILayout.EndHorizontal();

            // ── Onglets listes (si 5 ou 10 boss) ──────────────────────
            if (_lbCount == 5 || _lbCount == 10)
            {
                string[] names = (_lbCount == 5) ? BossLists.ListNames5 : BossLists.ListNames10;
                GUILayout.BeginHorizontal();
                for (int i = 1; i <= 3; i++)
                {
                    bool sel = _lbListNum == i;
                    GUI.color = sel ? C_LAVEND : C_GREY;
                    int li = i;
                    if (GUILayout.Button("L" + li + " — " + names[li - 1],
                            sel ? _sTabSel : _sTab, GUILayout.Height(22)))
                    {
                        _lbListNum = li;
                        _lbCategory = _lbCount * 10 + li;
                        LoadEntries(_lbCategory);
                    }
                }
                GUILayout.FlexibleSpace();
                GUI.color = p;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            DrawHLine();

            // ── Contenu ───────────────────────────────────────────────
            if (_loading)
            {
                GUI.color = C_GREY;
                GUILayout.Label("  Loading...", _sRow);
                GUI.color = p;
            }
            else if (_entries.Count == 0)
            {
                GUI.color = C_GREY;
                GUILayout.Label("  No entries yet for this category.", _sRow);
                GUI.color = p;
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.color = C_PURPLE;
                GUILayout.Label("#", _sHdr, GUILayout.Width(38));
                GUILayout.Label("Player", _sHdr, GUILayout.Width(200));
                GUILayout.Label("Time", _sHdr, GUILayout.Width(100));
                GUILayout.Label("Date", _sHdr, GUILayout.Width(160));
                GUI.color = p;
                GUILayout.EndHorizontal();
                DrawHLine();

                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
                string me = LBConfig.GetUsername();
                for (int i = 0; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    GUI.color = (i == 0) ? C_GOLD : (e.username == me) ? C_PURPLE : C_WHITE;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((i == 0 ? " 1" : "  " + (i + 1)), _sRow, GUILayout.Width(38));
                    GUILayout.Label((e.username == me ? "> " : "") + e.username, _sRow, GUILayout.Width(200));
                    GUILayout.Label(BossRushModeManager.FormatTime(e.time_seconds), _sRow, GUILayout.Width(100));
                    GUILayout.Label(FmtDate(e.date), _sRow, GUILayout.Width(160));
                    GUILayout.EndHorizontal();
                    GUI.color = p;
                    if (i < _entries.Count - 1) DrawHLine();
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUI.color = C_PURPLE;
            if (GUILayout.Button("Refresh", _sBtn, GUILayout.Height(26), GUILayout.Width(100)))
                LoadEntries(_lbCategory);
            GUILayout.Space(8);
            GUI.color = C_GREY;
            if (GUILayout.Button("Close", _sBtn, GUILayout.Height(26), GUILayout.Width(80)))
                _showLB = false;
            GUI.color = p;
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        // ── Styles ───────────────────────────────────────────────────
        private void InitStyles()
        {
            if (_stylesOk) return;
            Texture2D bg = Tex(C_BG);
            Texture2D btnN = Tex(new Color(0.30f, 0.05f, 0.55f));
            Texture2D btnH = Tex(new Color(0.50f, 0.10f, 0.85f));
            Texture2D btnS = Tex(new Color(0.45f, 0.18f, 0.08f));
            Texture2D inp = Tex(new Color(0.20f, 0.03f, 0.38f));
            Texture2D sep = Tex(new Color(0.40f, 0.08f, 0.65f, 0.4f));
            Texture2D tab = Tex(new Color(0.22f, 0.04f, 0.40f));
            Texture2D tabs = Tex(new Color(0.40f, 0.10f, 0.70f));

            _sWin = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(14, 14, 14, 14),
                normal = { background = bg }
            };
            _sTitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_GOLD }
            };
            _sTimer = new GUIStyle(GUI.skin.label)
            {
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_WHITE }
            };
            _sHdr = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = C_PURPLE }
            };
            _sRow = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = C_WHITE }
            };
            _sBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = C_WHITE, background = btnN },
                hover = { textColor = C_GOLD, background = btnH },
                active = { textColor = C_GOLD, background = btnH }
            };
            _sBtnSel = new GUIStyle(_sBtn) { normal = { textColor = C_GOLD, background = btnS } };
            _sInput = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 14,
                normal = { textColor = C_WHITE, background = inp }
            };
            _sSep = new GUIStyle(GUI.skin.box)
            {
                normal = { background = sep },
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 2, 2)
            };
            _sTab = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = C_GREY, background = tab },
                hover = { textColor = C_WHITE, background = tabs }
            };
            _sTabSel = new GUIStyle(_sTab) { normal = { textColor = C_GOLD, background = tabs } };
            _stylesOk = true;
        }

        private void DrawHLine() =>
            GUILayout.Box("", _sSep ?? GUI.skin.box,
                GUILayout.ExpandWidth(true), GUILayout.Height(1));

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(2, 2);
            t.SetPixels(new[] { c, c, c, c });
            t.Apply(); return t;
        }

        private static string FmtDate(string iso)
        {
            if (DateTime.TryParse(iso, out DateTime dt))
                return dt.ToString("dd/MM/yy HH:mm");
            return iso ?? "";
        }
    }
}