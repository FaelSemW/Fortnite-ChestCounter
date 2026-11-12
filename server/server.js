"use strict";
const express = require("express");
const bcrypt = require("bcryptjs");
const jwt = require("jsonwebtoken");
const rateLimit = require("express-rate-limit");
const { Pool } = require("pg");

const { DATABASE_URL, JWT_SECRET } = process.env;
if (!DATABASE_URL || !JWT_SECRET || JWT_SECRET.length < 24) {
  console.error("Defina DATABASE_URL e JWT_SECRET (24+ caracteres) nas variaveis de ambiente.");
  process.exit(1);
}

const isLocalDb = /localhost|127\.0\.0\.1/i.test(DATABASE_URL);
const pool = new Pool({ connectionString: DATABASE_URL, ssl: isLocalDb ? false : { rejectUnauthorized: false }, max: 5 });
const app = express();
app.set("trust proxy", 1);
app.use(express.json({ limit: "25mb" }));

const authLimiter = rateLimit({ windowMs: 15 * 60 * 1000, limit: 30, standardHeaders: true, legacyHeaders: false });
const USER_RE = /^[A-Za-z0-9_.-]{3,24}$/;

async function init() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS users (
      id SERIAL PRIMARY KEY,
      username TEXT NOT NULL,
      username_lc TEXT NOT NULL UNIQUE,
      password_hash TEXT NOT NULL,
      session_start TIMESTAMPTZ,
      created_at TIMESTAMPTZ NOT NULL DEFAULT now()
    );
    CREATE TABLE IF NOT EXISTS matches (
      user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
      session_id TEXT NOT NULL,
      analyzed_at TIMESTAMPTZ NOT NULL,
      data JSONB NOT NULL,
      PRIMARY KEY (user_id, session_id)
    );
  `);
}

function sign(user) {
  return jwt.sign({ uid: user.id, u: user.username }, JWT_SECRET, { expiresIn: "180d" });
}

function auth(req, res, next) {
  const h = req.headers.authorization || "";
  const token = h.startsWith("Bearer ") ? h.slice(7) : "";
  try {
    req.user = jwt.verify(token, JWT_SECRET);
    next();
  } catch {
    res.status(401).json({ error: "Sessao expirada. Entre novamente." });
  }
}

app.get("/health", (_req, res) => res.json({ ok: true }));

app.post("/api/register", authLimiter, async (req, res) => {
  const { username, password } = req.body || {};
  if (typeof username !== "string" || !USER_RE.test(username))
    return res.status(400).json({ error: "Usuario: 3 a 24 caracteres (letras, numeros, _ . -)." });
  if (typeof password !== "string" || password.length < 8 || password.length > 100)
    return res.status(400).json({ error: "A senha precisa ter pelo menos 8 caracteres." });
  try {
    const hash = await bcrypt.hash(password, 11);
    const r = await pool.query(
      "INSERT INTO users (username, username_lc, password_hash) VALUES ($1,$2,$3) RETURNING id, username",
      [username, username.toLowerCase(), hash]
    );
    res.json({ token: sign(r.rows[0]), username: r.rows[0].username });
  } catch (e) {
    if (e.code === "23505") return res.status(409).json({ error: "Esse usuario ja existe." });
    console.error(e);
    res.status(500).json({ error: "Erro no servidor." });
  }
});

app.post("/api/login", authLimiter, async (req, res) => {
  const { username, password } = req.body || {};
  if (typeof username !== "string" || typeof password !== "string")
    return res.status(400).json({ error: "Informe usuario e senha." });
  try {
    const r = await pool.query("SELECT id, username, password_hash FROM users WHERE username_lc=$1", [username.toLowerCase()]);
    const u = r.rows[0];
    if (!u || !(await bcrypt.compare(password, u.password_hash)))
      return res.status(401).json({ error: "Usuario ou senha incorretos." });
    res.json({ token: sign(u), username: u.username });
  } catch (e) {
    console.error(e);
    res.status(500).json({ error: "Erro no servidor." });
  }
});

app.get("/api/state", auth, async (req, res) => {
  try {
    const u = await pool.query("SELECT session_start FROM users WHERE id=$1", [req.user.uid]);
    if (!u.rows[0]) return res.status(401).json({ error: "Sessao expirada. Entre novamente." });
    const m = await pool.query(
      "SELECT count(*)::int AS n, COALESCE(sum((data->>'ChestsOpened')::int),0)::int AS chests FROM matches WHERE user_id=$1",
      [req.user.uid]
    );
    res.json({ sessionStartUtc: u.rows[0].session_start ?? null, matches: m.rows[0].n, chests: m.rows[0].chests });
  } catch (e) {
    console.error(e);
    res.status(500).json({ error: "Erro no servidor." });
  }
});

app.put("/api/state", auth, async (req, res) => {
  const s = req.body?.sessionStartUtc;
  const d = s ? new Date(s) : null;
  if (!d || isNaN(d.getTime())) return res.status(400).json({ error: "Data invalida." });
  try {
    await pool.query("UPDATE users SET session_start=$2 WHERE id=$1", [req.user.uid, d.toISOString()]);
    res.json({ ok: true });
  } catch (e) {
    console.error(e);
    res.status(500).json({ error: "Erro no servidor." });
  }
});

app.get("/api/matches/index", auth, async (req, res) => {
  try {
    const r = await pool.query("SELECT session_id, analyzed_at FROM matches WHERE user_id=$1", [req.user.uid]);
    res.json(r.rows.map((x) => ({ id: x.session_id, at: x.analyzed_at.toISOString() })));
  } catch (e) {
    console.error(e);
    res.status(500).json({ error: "Erro no servidor." });
  }
});

app.post("/api/matches/get", auth, async (req, res) => {
  const ids = Array.isArray(req.body?.ids) ? req.body.ids.slice(0, 100).map(String) : [];
  try {
    const r = await pool.query("SELECT data FROM matches WHERE user_id=$1 AND session_id = ANY($2)", [req.user.uid, ids]);
    res.json(r.rows.map((x) => x.data));
  } catch (e) {
    console.error(e);
    res.status(500).json({ error: "Erro no servidor." });
  }
});

app.put("/api/matches", auth, async (req, res) => {
  const list = Array.isArray(req.body?.matches) ? req.body.matches : [];
  if (list.length > 100) return res.status(400).json({ error: "Lote grande demais." });
  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    for (const m of list) {
      const id = m?.SessionId;
      const at = new Date(m?.AnalyzedAtUtc);
      if (typeof id !== "string" || !id || id.length > 200 || isNaN(at.getTime())) continue;
      await client.query(
        `INSERT INTO matches (user_id, session_id, analyzed_at, data) VALUES ($1,$2,$3,$4)
         ON CONFLICT (user_id, session_id) DO UPDATE SET analyzed_at=EXCLUDED.analyzed_at, data=EXCLUDED.data`,
        [req.user.uid, id, at.toISOString(), JSON.stringify(m)]
      );
    }
    await client.query("COMMIT");
    res.json({ ok: true, saved: list.length });
  } catch (e) {
    await client.query("ROLLBACK");
    console.error(e);
    res.status(500).json({ error: "Erro ao salvar." });
  } finally {
    client.release();
  }
});

const port = process.env.PORT || 3000;
init()
  .then(() => app.listen(port, () => console.log("ChestCounter cloud na porta " + port)))
  .catch((e) => { console.error(e); process.exit(1); });
