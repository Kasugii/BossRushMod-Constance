# Boss Rush Mod — Constance

A **BepInEx** mod for the game **Constance** that adds a full Boss Rush mode with a global online leaderboard.

---

## Features

- **Boss Rush Mode** — Fight all 16 bosses back-to-back without stopping
- **3 run sizes** — 5 bosses, 10 bosses, or all 16 (random order)
- **Curated lists** — 3 handcrafted lists per size (5-boss and 10-boss), each covering all available bosses
- **Live timer** — On-screen HUD showing elapsed time, current boss, and next boss
- **Timer pause** — Automatically pauses when the Journal (ESC menu) is open
- **Run cancellation** — Run is cancelled on death or when quitting to main menu
- **Online leaderboard** — Submit and view scores by category, hosted on Render + Neon PostgreSQL
- **Brian difficulty selector** — Choose Easy / Normal / Hard / Extreme before each Brian fight
- **Boss fixes** — Multiple vanilla bugs fixed for Rush Mode (Cornelis eyes, AweKing direction, Void debuff, PuppetHand death flow, PuppetMaster cutscene skip, navigator cleanup)
- **Skin mod** — Toggle the corrupted skin permanently with F3 (separate plugin)

---

## Requirements

- [BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases) for Constance
- The game **Constance**

---

## Installation

1. Install BepInEx into your Constance game folder
2. Build the project (see below) or download the latest release `.dll`
3. Copy the compiled `BossRushMod+Leaderboard.dll` into `BepInEx/plugins/`
4. Launch the game — the Boss Rush shrine will appear in the hub area

---

## Building

```bash
# Requires .NET / MSBuild
# Edit BossRushMod+Leaderboard.csproj to point GamePath to your Constance install
dotnet build BossRushMod+Leaderboard.csproj
```

The `.csproj` references game DLLs from your local Constance install. You'll need to update the `<GamePath>` property to match your installation directory.

---

## Leaderboard Server

The leaderboard backend lives in `leaderboard_server/`. It's a Node.js + Express app using PostgreSQL.

### Self-hosting

1. Create a free PostgreSQL database on [Neon](https://neon.tech) (or any provider)
2. Create a free Web Service on [Render](https://render.com) connected to this repo
3. Set the environment variable `DATABASE_URL` to your PostgreSQL connection string
4. Update `ServerUrl` in `Bossrushleaderboard.cs` to point to your deployed service

```csharp
// Bossrushleaderboard.cs
public const string ServerUrl = "https://your-service.onrender.com";
```

### API

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/submit` | POST | Submit a run score |
| `/leaderboard?category=51` | GET | Fetch top scores for a category |
| `/` | GET | Status page |

**Categories:**
- `51` = 5 bosses, List 1 (Academy's Wrath)
- `52` = 5 bosses, List 2 (Carnival Chaos)
- `53` = 5 bosses, List 3 (Void's Edge)
- `101` = 10 bosses, List 1 (The Gauntlet)
- `102` = 10 bosses, List 2 (Grand Tour)
- `103` = 10 bosses, List 3 (Final Curtain)
- `16` = 16 bosses, random

---

## Boss Lists

### 5-boss
| List | Name | Bosses |
|------|------|--------|
| L1 | Academy's Wrath | Brian, High Patia, The Jester, Forsaken Will, Sir Barfalot |
| L2 | Carnival Chaos | Cubicus, Awe King, The Manipulator, Corrupted Mind, Cornelis |
| L3 | Void's Edge | The Manipulator Encore, Jester Encore, Wounded Vessel, Final Boss, High Patia |

### 10-boss
| List | Name | Bosses |
|------|------|--------|
| L1 | The Gauntlet | Brian, Cubicus, High Patia, Awe King, The Jester, The Manipulator, Lord Korba, Forsaken Will, Corrupted Mind, Final Boss |
| L2 | Grand Tour | Palettus, Cubicus, Awe King, Cornelis, The Manipulator Encore, Sir Barfalot, Wounded Vessel, Final Boss, High Patia, The Jester |
| L3 | Final Curtain | Brian, Palettus, Cornelis, Jester Encore, Lord Korba, Sir Barfalot, Forsaken Will, Corrupted Mind, Wounded Vessel, The Manipulator |

### 16-boss
All bosses in random order.

---

## File Overview

| File | Description |
|------|-------------|
| `Bossrushmode.cs` | Core Rush Mode logic, timer, sequence management |
| `BossRushSceneHook.cs` | Arena forcer, door control, scene hooks |
| `GUIBossRush.cs` | In-game GUI (shrine interface) |
| `Bossrushleaderboard.cs` | Leaderboard UI and HTTP client |
| `BossRushTeleport.cs` | Boss checkpoint teleportation |
| `BossTrigger.cs` | Boss state guard and trigger logic |
| `BossRushBugFixes.cs` | Bug fixes (Cornelis, AweKing, Void debuff) |
| `BrainStokerPatches.cs` | Brian difficulty system |
| `Puppethandpatches.cs` | PuppetHand death flow fix |
| `Puppetmasterdeathpatches.cs` | PuppetMaster cutscene skip |
| `Simpleskinmod.cs` | Corrupted skin toggle (F3) |
| `leaderboard_server/` | Node.js leaderboard backend |

---

## License

MIT — see [LICENSE](LICENSE)
