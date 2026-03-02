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
    return res.status(400).json({ error: "Pseudo invalide (2-20 car.)" });

  const validCats = [51, 52, 53, 101, 102, 103, 16];
  if (!validCats.includes(Number(category)))
    return res.status(400).json({ error: "Catégorie invalide" });

  const t = Number(time_seconds);
  if (isNaN(t) || t <= 0 || t > 86400)
    return res.status(400).json({ error: "Temps invalide" });

  const name = username.trim().replace(/[<>"'&]/g, "");
  const cat  = Number(category);

  try {
    // Anti-spam : 1 soumission / 60s par pseudo + catégorie
    const recent = await pool.query(
      `SELECT date FROM scores WHERE username=$1 AND category=$2 ORDER BY id DESC LIMIT 1`,
      [name, cat]);
    if (recent.rows.length > 0) {
      const diff = (Date.now() - new Date(recent.rows[0].date).getTime()) / 1000;
      if (diff < 60) return res.status(429).json({ error: "Attends un peu." });
    }

    await pool.query(
      `INSERT INTO scores (username, category, time_secs) VALUES ($1, $2, $3)`,
      [name, cat, t]);

    console.log("[LB] Submit: " + name + " | cat=" + cat + " | " + t.toFixed(2) + "s");
    res.json({ ok: true });
  } catch (e) {
    console.error("[LB] Submit error:", e.message);
    res.status(500).json({ error: "Erreur serveur: " + e.message });
  }
});

// ── GET /leaderboard ───────────────────────────────────────────────────
app.get("/leaderboard", async (req, res) => {
  if (!requireDB(res)) return;

  const cat   = Number(req.query.category);
  const limit = Math.min(Number(req.query.limit) || 50, 100);

  const validCats2 = [51, 52, 53, 101, 102, 103, 16];
  if (!validCats2.includes(cat))
    return res.status(400).json({ error: "Catégorie invalide" });

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
    res.status(500).json({ error: "Erreur serveur: " + e.message });
  }
});

// ── GET / — statut ─────────────────────────────────────────────────────
app.get("/", async (req, res) => {
  if (!dbReady) return res.send("<h2>Boss Rush LB</h2><p>DB en cours d'initialisation...</p>");
  try {
    const counts = await Promise.all([51,52,53,101,102,103,16].map(async cat => {
      const r = await pool.query(
        "SELECT COUNT(DISTINCT username) AS n FROM scores WHERE category=$1", [cat]);
      const label = cat===16?"16 random":cat<100?"5b-L"+(cat-50):"10b-L"+(cat-100);
      return label + " : " + r.rows[0].n + " joueur(s)";
    }));
    res.send("<h2>Boss Rush Leaderboard</h2><ul>"
      + counts.map(c => "<li>" + c + "</li>").join("") + "</ul>"
      + "<p>POST /submit &middot; GET /leaderboard?category=5</p>");
  } catch (e) {
    res.send("Serveur en ligne — DB: " + e.message);
  }
});

// ── Démarrage ──────────────────────────────────────────────────────────
const PORT = process.env.PORT || 3000;
app.listen(PORT, () => console.log("[LB] Listening on port " + PORT));
initDB(); // async, non-bloquant — le serveur répond 503 le temps que la DB soit prête
