// =====================================================================
//  BOSS RUSH LEADERBOARD SERVER — PostgreSQL (Render.com)
//
//  SETUP :
//  1. Render → New → PostgreSQL → "bossrush-db" → Free → Create
//  2. Render → New → Web Service → connecter GitHub
//     Build: npm install | Start: node server.js | Plan: Free
//     Env var: DATABASE_URL = [Internal Database URL]
//  3. Coller l'URL du service dans BossRushLeaderboard.cs → ServerUrl
// =====================================================================

const express  = require("express");
const { Pool } = require("pg");
const app      = express();

app.use(express.json());
app.use((req, res, next) => {
  res.header("Access-Control-Allow-Origin",  "*");
  res.header("Access-Control-Allow-Headers", "Content-Type");
  res.header("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
  if (req.method === "OPTIONS") return res.sendStatus(200);
  next();
});

// ── DB ────────────────────────────────────────────────────────────────
let pool = null;
let dbReady = false;

async function initDB() {
  if (!process.env.DATABASE_URL) {
    console.warn("[LB] DATABASE_URL non défini — mode dégradé (pas de DB).");
    return;
  }

  pool = new Pool({
    connectionString: process.env.DATABASE_URL,
    ssl: { rejectUnauthorized: false },
    connectionTimeoutMillis: 10000,
    idleTimeoutMillis: 30000,
    max: 5,
  });

  // Retry jusqu'à 10 fois (30s) pour attendre que Postgres soit prêt
  for (let attempt = 1; attempt <= 10; attempt++) {
    try {
      const client = await pool.connect();
      await client.query(`
        CREATE TABLE IF NOT EXISTS scores (
          id        SERIAL PRIMARY KEY,
          username  TEXT    NOT NULL,
          category  INTEGER NOT NULL,
          time_secs REAL    NOT NULL,
          date      TIMESTAMPTZ DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_cat ON scores (category, time_secs);
      `);
      client.release();
      dbReady = true;
      console.log("[LB] DB prête (tentative " + attempt + ")");
      return;
    } catch (e) {
      console.warn("[LB] DB init tentative " + attempt + " échouée : " + e.message);
      if (attempt < 10) await sleep(3000);
    }
  }
  console.error("[LB] Impossible d'initialiser la DB après 10 tentatives.");
}

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

function requireDB(res) {
  if (!dbReady) {
    res.status(503).json({ error: "Database not ready. Retry in a few seconds." });
    return false;
  }
  return true;
}

// ── POST /submit ───────────────────────────────────────────────────────
app.post("/submit", async (req, res) => {
  if (!requireDB(res)) return;

  const { username, category, time_seconds } = req.body;

  if (!username || typeof username !== "string"
      || username.trim().length < 2 || username.length > 20)
    return res.status(400).json({ error: "Invalid username (2-20 chars)" });

  const validCats = [51, 52, 53, 101, 102, 103, 16];
  if (!validCats.includes(Number(category)))
    return res.status(400).json({ error: "Invalid category" });

  const t = Number(time_seconds);
  if (isNaN(t) || t <= 0 || t > 86400)
    return res.status(400).json({ error: "Invalid time" });

  const name = username.trim().replace(/[<>"'&]/g, "");
  const cat  = Number(category);

  try {
    // Anti-spam : 1 soumission / 60s par pseudo + catégorie
    const recent = await pool.query(
      `SELECT date FROM scores WHERE username=$1 AND category=$2 ORDER BY id DESC LIMIT 1`,
      [name, cat]);
    if (recent.rows.length > 0) {
      const diff = (Date.now() - new Date(recent.rows[0].date).getTime()) / 1000;
      if (diff < 60) return res.status(429).json({ error: "Too many requests, wait a moment." });
    }

    await pool.query(
      `INSERT INTO scores (username, category, time_secs) VALUES ($1, $2, $3)`,
      [name, cat, t]);

    console.log("[LB] Submit: " + name + " | cat=" + cat + " | " + t.toFixed(2) + "s");
    res.json({ ok: true });
  } catch (e) {
    console.error("[LB] Submit error:", e.message);
    res.status(500).json({ error: "Server error: " + e.message });
  }
});

// ── GET /leaderboard ───────────────────────────────────────────────────
app.get("/leaderboard", async (req, res) => {
  if (!requireDB(res)) return;

  const cat   = Number(req.query.category);
  const limit = Math.min(Number(req.query.limit) || 50, 100);

  const validCats2 = [51, 52, 53, 101, 102, 103, 16];
  if (!validCats2.includes(cat))
    return res.status(400).json({ error: "Invalid category" });

  try {
    const result = await pool.query(
      `SELECT username, MIN(time_secs) AS time_seconds, MAX(date) AS date
       FROM scores WHERE category=$1
       GROUP BY username
       ORDER BY MIN(time_secs) ASC LIMIT $2`,
      [cat, limit]);

    res.json(result.rows.map(r => ({
      username:     r.username,
      category:     cat,
      time_seconds: parseFloat(r.time_seconds),
      date:         r.date,
    })));
  } catch (e) {
    console.error("[LB] Fetch error:", e.message);
    res.status(500).json({ error: "Server error: " + e.message });
  }
});

// ── GET / — leaderboard page ───────────────────────────────────────────
app.get("/", (req, res) => {
  res.send(`<!DOCTYPE html>
<html lang="fr">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Boss Rush — Constance</title>
<link href="https://fonts.googleapis.com/css2?family=Cinzel:wght@400;600;900&family=Crimson+Pro:ital,wght@0,300;0,400;1,300&display=swap" rel="stylesheet">
<style>
  :root {
    --bg:       #100d18;
    --bg2:      #18122a;
    --purple:   #9b6dff;
    --purple2:  #c49bff;
    --gold:     #e8a020;
    --gold2:    #f5c842;
    --text:     #e8dff5;
    --muted:    #8a7aaa;
    --border:   rgba(155,109,255,0.18);
  }

  * { margin:0; padding:0; box-sizing:border-box; }

  body {
    background: var(--bg);
    color: var(--text);
    font-family: 'Crimson Pro', Georgia, serif;
    min-height: 100vh;
    overflow-x: hidden;
  }

  /* ── BACKGROUND ── */
  body::before {
    content: '';
    position: fixed; inset: 0; z-index: 0;
    background:
      radial-gradient(ellipse 80% 60% at 10% 20%, rgba(100,40,180,0.18) 0%, transparent 60%),
      radial-gradient(ellipse 60% 50% at 90% 80%, rgba(60,20,120,0.22) 0%, transparent 55%),
      radial-gradient(ellipse 40% 40% at 50% 50%, rgba(155,109,255,0.06) 0%, transparent 70%);
    pointer-events: none;
  }

  /* ── PAINT STROKES (SVG background) ── */
  body::after {
    content: '';
    position: fixed; inset: 0; z-index: 0;
    background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='1200' height='800' viewBox='0 0 1200 800'%3E%3Cellipse cx='150' cy='120' rx='280' ry='28' fill='%239b6dff' opacity='0.04' transform='rotate(-12 150 120)'/%3E%3Cellipse cx='1050' cy='680' rx='320' ry='22' fill='%23e8a020' opacity='0.05' transform='rotate(8 1050 680)'/%3E%3Cellipse cx='600' cy='400' rx='500' ry='18' fill='%239b6dff' opacity='0.03' transform='rotate(-3 600 400)'/%3E%3C/svg%3E");
    background-size: cover;
    pointer-events: none;
  }

  /* ── LAYOUT ── */
  .wrapper {
    position: relative; z-index: 1;
    max-width: 860px;
    margin: 0 auto;
    padding: 60px 24px 80px;
  }

  /* ── HEADER ── */
  header { text-align: center; margin-bottom: 56px; }

  .eyebrow {
    font-family: 'Cinzel', serif;
    font-size: 11px;
    letter-spacing: 0.35em;
    color: var(--gold);
    text-transform: uppercase;
    opacity: 0.85;
    margin-bottom: 14px;
  }

  h1 {
    font-family: 'Cinzel', serif;
    font-size: clamp(2.4rem, 6vw, 4rem);
    font-weight: 900;
    letter-spacing: 0.04em;
    line-height: 1;
    background: linear-gradient(135deg, var(--purple2) 0%, var(--gold2) 100%);
    -webkit-background-clip: text;
    -webkit-text-fill-color: transparent;
    background-clip: text;
    margin-bottom: 10px;
  }

  .subtitle {
    font-size: 1.05rem;
    color: var(--muted);
    font-style: italic;
    letter-spacing: 0.02em;
  }

  /* ── PAINT LINE DIVIDER ── */
  .paint-line {
    width: 120px; height: 3px;
    background: linear-gradient(90deg, transparent, var(--purple), var(--gold), transparent);
    border-radius: 2px;
    margin: 20px auto 0;
    opacity: 0.6;
  }

  /* ── CATEGORY TABS ── */
  .tabs {
    display: flex; flex-wrap: wrap; gap: 8px;
    justify-content: center;
    margin-bottom: 40px;
  }

  .tab {
    font-family: 'Cinzel', serif;
    font-size: 11px;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    padding: 8px 18px;
    border-radius: 3px;
    border: 1px solid var(--border);
    background: rgba(155,109,255,0.06);
    color: var(--muted);
    cursor: pointer;
    transition: all 0.2s;
  }

  .tab:hover {
    border-color: rgba(155,109,255,0.4);
    color: var(--purple2);
  }

  .tab.active {
    background: rgba(155,109,255,0.15);
    border-color: var(--purple);
    color: var(--purple2);
  }

  /* ── SIZE GROUPS ── */
  .size-group { margin-bottom: 12px; }

  .size-label {
    font-family: 'Cinzel', serif;
    font-size: 10px;
    letter-spacing: 0.3em;
    color: var(--gold);
    text-transform: uppercase;
    opacity: 0.6;
    margin-bottom: 8px;
    padding-left: 4px;
  }

  .tab-row { display: flex; flex-wrap: wrap; gap: 6px; }

  /* ── LEADERBOARD TABLE ── */
  .board-wrap {
    background: rgba(24,18,42,0.7);
    border: 1px solid var(--border);
    border-radius: 8px;
    overflow: hidden;
    backdrop-filter: blur(12px);
  }

  .board-header {
    padding: 22px 28px 16px;
    border-bottom: 1px solid var(--border);
    display: flex; align-items: baseline; gap: 14px;
  }

  .board-title {
    font-family: 'Cinzel', serif;
    font-size: 1rem;
    font-weight: 600;
    color: var(--purple2);
    letter-spacing: 0.08em;
  }

  .board-count {
    font-size: 0.8rem;
    color: var(--muted);
    font-style: italic;
  }

  table {
    width: 100%;
    border-collapse: collapse;
  }

  thead th {
    font-family: 'Cinzel', serif;
    font-size: 9.5px;
    letter-spacing: 0.25em;
    text-transform: uppercase;
    color: var(--muted);
    padding: 10px 28px;
    text-align: left;
    border-bottom: 1px solid var(--border);
  }

  thead th:last-child { text-align: right; }

  tbody tr {
    transition: background 0.15s;
    animation: fadeUp 0.4s ease both;
  }

  tbody tr:hover { background: rgba(155,109,255,0.06); }

  tbody td {
    padding: 14px 28px;
    font-size: 1rem;
    border-bottom: 1px solid rgba(155,109,255,0.07);
  }

  tbody tr:last-child td { border-bottom: none; }

  /* ── RANK ── */
  .rank {
    font-family: 'Cinzel', serif;
    font-weight: 600;
    font-size: 0.85rem;
    width: 40px;
  }

  .rank-1 { color: var(--gold2); }
  .rank-2 { color: #c8c8d8; }
  .rank-3 { color: #cd7f32; }
  .rank-other { color: var(--muted); opacity: 0.7; }

  /* ── MEDAL ICONS ── */
  .medal { font-size: 1.1rem; }

  /* ── USERNAME ── */
  .username {
    font-size: 1.05rem;
    font-weight: 400;
    color: var(--text);
    letter-spacing: 0.01em;
  }

  /* ── TIME ── */
  .time-cell { text-align: right; }

  .time {
    font-family: 'Cinzel', serif;
    font-size: 0.95rem;
    color: var(--purple2);
    letter-spacing: 0.05em;
  }

  .time-1 { color: var(--gold2); }

  /* ── DATE ── */
  .date {
    font-size: 0.78rem;
    color: var(--muted);
    font-style: italic;
  }

  /* ── EMPTY STATE ── */
  .empty {
    padding: 60px 28px;
    text-align: center;
    color: var(--muted);
    font-style: italic;
    font-size: 1rem;
  }

  /* ── LOADING ── */
  .loading {
    padding: 60px 28px;
    text-align: center;
    color: var(--muted);
  }

  .spinner {
    width: 28px; height: 28px;
    border: 2px solid var(--border);
    border-top-color: var(--purple);
    border-radius: 50%;
    animation: spin 0.8s linear infinite;
    margin: 0 auto 16px;
  }

  /* ── ANIMATIONS ── */
  @keyframes spin { to { transform: rotate(360deg); } }

  @keyframes fadeUp {
    from { opacity: 0; transform: translateY(8px); }
    to   { opacity: 1; transform: translateY(0); }
  }

  /* ── FOOTER ── */
  footer {
    text-align: center;
    margin-top: 48px;
    font-size: 0.78rem;
    color: var(--muted);
    opacity: 0.5;
    font-style: italic;
    letter-spacing: 0.05em;
  }
</style>
</head>
<body>
<div class="wrapper">

  <header>
    <p class="eyebrow">Constance Mod</p>
    <h1>Boss Rush</h1>
    <p class="subtitle">Leaderboard — Best Times</p>
    <div class="paint-line"></div>
  </header>

  <div class="tabs">
    <div class="size-group">
      <div class="size-label">5 Boss</div>
      <div class="tab-row">
        <button class="tab active" data-cat="51" data-label="Academy's Wrath">L1 — Academy's Wrath</button>
        <button class="tab" data-cat="52" data-label="Carnival Chaos">L2 — Carnival Chaos</button>
        <button class="tab" data-cat="53" data-label="Void's Edge">L3 — Void's Edge</button>
      </div>
    </div>
    <div class="size-group">
      <div class="size-label">10 Boss</div>
      <div class="tab-row">
        <button class="tab" data-cat="101" data-label="The Gauntlet">L1 — The Gauntlet</button>
        <button class="tab" data-cat="102" data-label="Grand Tour">L2 — Grand Tour</button>
        <button class="tab" data-cat="103" data-label="Final Curtain">L3 — Final Curtain</button>
      </div>
    </div>
    <div class="size-group">
      <div class="size-label">16 Boss</div>
      <div class="tab-row">
        <button class="tab" data-cat="16" data-label="All Bosses — Random">All Bosses — Random</button>
      </div>
    </div>
  </div>

  <div class="board-wrap">
    <div class="board-header">
      <span class="board-title" id="boardTitle">Academy's Wrath</span>
      <span class="board-count" id="boardCount"></span>
    </div>
    <div id="boardBody">
      <div class="loading"><div class="spinner"></div>Loading...</div>
    </div>
  </div>

  <footer>Boss Rush Mod — Constance &nbsp;·&nbsp; github.com/Kasugii/BossRushMod-Constance</footer>

</div>

<script>
  function fmtTime(s) {
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = (s % 60).toFixed(2).padStart(5, '0');
    return h > 0
      ? h + 'h ' + String(m).padStart(2,'0') + 'm ' + sec + 's'
      : m > 0
        ? m + 'm ' + sec + 's'
        : sec + 's';
  }

  function fmtDate(d) {
    return new Date(d).toLocaleDateString('en-GB', { day:'2-digit', month:'short', year:'numeric' });
  }

  const medals = ['🥇','🥈','🥉'];

  async function loadBoard(cat, label) {
    document.getElementById('boardTitle').textContent = label;
    document.getElementById('boardCount').textContent = '';
    document.getElementById('boardBody').innerHTML =
      '<div class="loading"><div class="spinner"></div>Loading...</div>';

    try {
      const r = await fetch('/leaderboard?category=' + cat + '&limit=50');
      const data = await r.json();

      if (!Array.isArray(data) || data.length === 0) {
        document.getElementById('boardBody').innerHTML =
          '<div class="empty">No scores recorded for this category.</div>';
        document.getElementById('boardCount').textContent = '0 player';
        return;
      }

      document.getElementById('boardCount').textContent =
        data.length + ' player' + (data.length > 1 ? 's' : '');

      let html = '<table><thead><tr>'
        + '<th>#</th><th>Player</th><th>Date</th><th>Time</th>'
        + '</tr></thead><tbody>';

      data.forEach((row, i) => {
        const rank = i + 1;
        const rankClass = rank <= 3 ? 'rank-' + rank : 'rank-other';
        const rankDisplay = rank <= 3 ? '<span class="medal">' + medals[i] + '</span>' : rank;
        const timeClass = rank === 1 ? 'time time-1' : 'time';
        html += '<tr style="animation-delay:' + (i * 0.05) + 's">'
          + '<td><span class="rank ' + rankClass + '">' + rankDisplay + '</span></td>'
          + '<td><span class="username">' + row.username + '</span></td>'
          + '<td><span class="date">' + fmtDate(row.date) + '</span></td>'
          + '<td class="time-cell"><span class="' + timeClass + '">' + fmtTime(row.time_seconds) + '</span></td>'
          + '</tr>';
      });

      html += '</tbody></table>';
      document.getElementById('boardBody').innerHTML = html;
    } catch(e) {
      document.getElementById('boardBody').innerHTML =
        '<div class="empty">Loading error.</div>';
    }
  }

  document.querySelectorAll('.tab').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
      btn.classList.add('active');
      loadBoard(btn.dataset.cat, btn.dataset.label);
    });
  });

  loadBoard(51, "Academy's Wrath");
</script>
</body>
</html>`);
});

// ── Démarrage ──────────────────────────────────────────────────────────
const PORT = process.env.PORT || 3000;
app.listen(PORT, () => console.log("[LB] Listening on port " + PORT));
initDB(); // async, non-bloquant — le serveur répond 503 le temps que la DB soit prête
