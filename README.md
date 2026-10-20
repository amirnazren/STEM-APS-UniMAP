# Eco-Bot Builders — Full-Stack STEM Game Platform

## Overview

**Eco-Bot Builders** is a classroom-ready STEM game for primary school students (ages 8–11). Students program a friendly robot with drag-and-drop logic blocks to clean, water, power and rebuild an eco-friendly world across 10 levels. Each level teaches a computational thinking idea (sequences, loops, conditionals, sensors, subroutines) through a real-world sustainability problem (recycling, solar, wind and hydro energy, bridges, gardens).

The platform has three parts:

| Part | Tech | Who uses it |
|------|------|-------------|
| **Unity client** | Unity 2022 LTS (URP), C# | Students play the game |
| **Backend** | Node.js, Express, Socket.io, MongoDB (Mongoose) | Classroom rooms, real-time events, telemetry, reports |
| **Teacher dashboard** | React 18, Recharts, jsPDF, socket.io-client | Teachers run the class, see live progress and alerts, export reports |

**Educational goals**

- Build computational thinking aligned to the **CSTA K-12 CS Standards (Level 1B)**.
- Practise the **NGSS Science & Engineering Practices** through energy, water and engineering challenges.
- Encourage persistence with a 3-tier adaptive hint system (observational → conceptual → interactive).
- Give teachers real-time insight: who is stuck, who is thriving, and a 4-axis STEM competency profile per student (Computational Thinking, Systemic Engineering, Mathematical Modeling, Persistence).
- Promote collaboration through a class-wide **Community Progress** meter ("power the town" with 1,000 Clean Tokens).

## Repository Structure

```
eco-bot-builders/
├── README.md                          ← this file
├── schemas/                           ← contracts shared by all three parts
│   ├── websocket-payloads.json        ← JSON Schema (Draft-07) for every Socket.io event
│   ├── database-schemas.json          ← MongoDB collections, indexes, relationships
│   ├── level-configs.json             ← the 10 level definitions (curriculum source of truth)
│   └── unity-block-program-schema.json← serialized student block program
├── backend/                           ← Node.js + Express + Socket.io + MongoDB
│   ├── server.js                      ← entry point (REST + Socket.io, health check, graceful shutdown)
│   ├── .env.example
│   ├── config/gameConfig.js           ← constants: MAX_LEVEL, token goal, cosmetics, competencies
│   ├── models/                        ← Student.js, LevelTelemetry.js, Classroom.js
│   ├── routes/                        ← auth.js, students.js, telemetry.js, reports.js
│   ├── sockets/socketHandler.js       ← all real-time event handlers
│   ├── middleware/errorHandler.js
│   └── utils/                         ← validators.js, competency.js, pin.js
├── frontend/                          ← React teacher dashboard
│   ├── .env.example
│   ├── public/index.html
│   └── src/
│       ├── App.js, index.js, index.css
│       ├── components/
│       │   ├── Dashboard/             ← TeacherLogin, TeacherDashboard, StudentRoster, AlertPanel
│       │   ├── HUD/                   ← ClassroomHUD, CommunityProgressBar, ActivityTicker
│       │   ├── Analytics/             ← STEMRadarChart
│       │   ├── Controls/              ← TeacherControls (pause / mute / exports)
│       │   └── common/                ← Panel
│       ├── hooks/                     ← useClassroomSocket, useWindowWidth
│       └── utils/                     ← api.js, format.js
└── unity/
    └── Assets/Scripts/
        ├── Core/                      ← BlockParserEngine, GridEnvironmentManager, DayNightController
        ├── Levels/                    ← LevelManager
        ├── UI/                        ← HintSystem
        ├── Audio/                     ← AdaptiveAudioEngine
        └── Physics/                   ← PhysicsSimulator (hydraulics + bridge)
```

## Architecture Diagram

```
 ┌──────────────────────────┐                         ┌──────────────────────────────┐
 │   Unity Student Client   │                         │  React Teacher Dashboard     │
 │  (Unity 2022 LTS, URP)   │                         │  (React 18, Recharts, jsPDF) │
 │                          │                         │                              │
 │  BlockParserEngine       │                         │  Live HUD · Roster · Alerts  │
 │  GridEnvironmentManager  │                         │  STEM Radar · Controls       │
 │  LevelManager · Hints    │                         │  PDF certificates · CSV      │
 └─────────────┬────────────┘                         └──────────────┬───────────────┘
               │  Socket.io (student:* events)                        │  Socket.io (teacher:* events)
               │  REST  /api/auth/validate-pin                        │  REST  /api/auth, /api/telemetry,
               │        /api/telemetry/record                         │        /api/students, /api/reports
               ▼                                                      ▼
        ┌─────────────────────────────────────────────────────────────────────┐
        │                 Node.js Backend  (Express + Socket.io)  :3001       │
        │                                                                     │
        │  rooms:  room:<PIN>  ── students + teacher (broadcasts)             │
        │          teacher:<PIN> ── teacher only (STUCK_ALERT, roster)        │
        │          student:<id> ── one student (COSMETIC_UNLOCKED)            │
        │                                                                     │
        │  validators · competency aggregation · PDF (pdfkit) · CSV (json2csv)│
        └───────────────────────────────┬─────────────────────────────────────┘
                                        │ Mongoose
                                        ▼
                         ┌──────────────────────────────┐
                         │           MongoDB            │
                         │  classrooms · students ·     │
                         │  leveltelemetries            │
                         └──────────────────────────────┘
```

Typical flow: the teacher creates a room (4-digit PIN + QR code) → students join from Unity with the PIN and their name → gameplay events stream to the backend → the backend persists telemetry and broadcasts to the room → the dashboard updates the HUD, radar charts and alerts in real time.

## Quick Start

Prerequisites: Node.js 18+, npm, MongoDB 6+ (local or Atlas), Unity 2022.3 LTS.

### Backend

```bash
cd backend
npm install
cp .env.example .env   # fill in MONGO_URI
npm start              # starts on port 3001
```

Check it is running: `curl http://localhost:3001/api/health` → `{"status":"ok","db":"connected",...}`. Use `npm run dev` for auto-reload with nodemon.

### Frontend (Teacher Dashboard)

```bash
cd frontend
npm install
cp .env.example .env   # optional: set REACT_APP_BACKEND_URL if the backend is not on localhost:3001
npm start              # starts on port 3000
```

Open `http://localhost:3000`, enter your name, and create a classroom. Share the PIN or QR code with your students.

### Unity Client

1. Open **Unity Hub** → **New project** → template **3D (URP)** with **Unity 2022.3 LTS**.
2. Copy the contents of `unity/Assets/` into your project's `Assets/` folder (keep the `Scripts/` sub-folders).
3. Install the dependencies of `NetworkManager.cs`:
   - **SocketIOUnity**: Package Manager → *Add package from git URL…* → `https://github.com/itisnajim/SocketIOUnity.git` (it wraps *SocketIOClient* / socket.io-client-csharp).
   - **Newtonsoft Json**: `com.unity.nuget.newtonsoft-json`.
   > ⚠️ `NetworkManager.cs` was written against the SocketIOClient 3.x API (`EmitAsync` with ack callbacks; handlers are marshalled to the main thread through its own queue). Its use of the SocketIOUnity API is **unverified against the real package**. It has only been compile-checked against a stub of `SocketIOUnity` plus the real `SocketIOClient` 3.0.8 NuGet, not inside the Unity Editor. Expect small signature fixes depending on the package version you install.
4. Set the backend address. `NetworkManager` (`Assets/Scripts/Network/NetworkManager.cs`) exposes a `backendUrl` Inspector field (default `http://localhost:3001`). It is overridden, in priority order, by the `-BACKEND_URL=<url>` command-line argument, the `BACKEND_URL` environment variable, and then the value saved in PlayerPrefs. Classroom devices need your server's LAN IP or HTTPS URL.
5. Create a scene with the core components (`NetworkManager`, `GridEnvironmentManager`, `BlockParserEngine`, `LevelManager`, `HintSystem`, `DayNightController`, `AdaptiveAudioEngine`, `PhysicsSimulator`) attached to GameObjects. Assign the references in the Inspector, then press **Play**. Students join with `NetworkManager.Instance.JoinClassroom(pin, name)`. `ResumeLastSession()` re-joins the classroom saved in PlayerPrefs.

#### Unity NetworkManager.cs

- A singleton (`NetworkManager.Instance`, `DontDestroyOnLoad`) that bridges the game to the backend over Socket.io. Event names and payloads follow `schemas/websocket-payloads.json`.
- Send methods: `SendTokenEarned`, `SendLevelCompleted(levelId, LevelTelemetryData)`, `SendEnergyContributed`, `SendStuckAlert`. Every emit uses an ack timeout. Events emitted while offline are queued, persisted in PlayerPrefs, and flushed in order after a successful re-join.
- Application heartbeat: `client:heartbeat {sentAt}` is sent every `heartbeatIntervalSeconds` (default 10 s) and acked by the server with `{ ok, serverTime, sentAt }`. The ack drives `OnLatencyUpdated`. If several heartbeats or acks are missed in a row, the connection is treated as dead and recycled.
- Main-thread C# events: `OnJoined`, `OnJoinError`, `OnClassPaused`/`OnClassResumed` (`HintSystem` hides the current hint while paused and restores it on resume, and `LevelManager` holds level progression), `OnAudioMuted`/`OnAudioUnmuted`, `OnCosmeticUnlocked`, `OnCommunityProgress`, `OnTeacherDisconnected`, and others.

## Environment Variables

| Variable | Scope | Description | Default |
|----------|-------|-------------|---------|
| `PORT` | backend | HTTP port for Express + Socket.io | `3001` |
| `MONGO_URI` | backend | MongoDB connection string | `mongodb://127.0.0.1:27017/eco-bot-builders` |
| `CLIENT_JOIN_URL` | backend | Base URL students open to join; encoded into the room QR code | `http://localhost:5173/join` |
| `CORS_ORIGIN` | backend | Comma-separated list of allowed origins, or `*` for any | `*` |
| `NODE_ENV` | backend | `production` hides internal error details in API responses | *(unset)* |
| `REACT_APP_BACKEND_URL` | frontend | URL of the backend (REST + Socket.io) | `http://localhost:3001` |

## API Reference (REST)

All endpoints are prefixed by the backend URL (default `http://localhost:3001`) and return JSON unless noted. Authentication is **PIN-based**: there are no user accounts; access is scoped by the 4-digit room PIN or the student's UUID. Put the API behind HTTPS and a restricted `CORS_ORIGIN` in production.

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| `GET` | `/api/health` | Liveness/readiness probe (503 while MongoDB is disconnected) | None |
| `POST` | `/api/auth/create-room` | Create a classroom; body `{ teacherName? }`; returns `roomPin` + QR code data URL | None |
| `POST` | `/api/auth/validate-pin` | Check a room exists; body `{ roomPin }` | None |
| `GET` | `/api/auth/room/:roomPin` | Full room state (flags, community progress, roster) | Room PIN |
| `GET` | `/api/students/:roomPin` | Student roster of a classroom | Room PIN |
| `GET` | `/api/students/:studentId/progress` | Student progress summary aggregated from telemetry | Student ID |
| `PATCH` | `/api/students/:studentId/cosmetic` | Unlock a cosmetic; body `{ cosmeticId }` | Student ID |
| `GET` | `/api/telemetry/student/:studentId` | Telemetry records for one student; `?includeSnapshots=true` adds block programs | Student ID |
| `GET` | `/api/telemetry/class/:roomPin` | Class-wide competency averages (per student / per level) | Room PIN |
| `POST` | `/api/telemetry/record` | Save a telemetry record (REST fallback to `student:level_completed`); body `{ studentId, levelId, retriesCount?, timeSpentSeconds?, subroutinesUsed?, stemCompetencyScores?, blockProgramSnapshot? }` | Student ID |
| `GET` | `/api/reports/certificate/:studentId` | Downloadable PDF achievement certificate. Returns **409** `{ error, code: "LEVEL_10_INCOMPLETE", requiredLevel: 10, levelsCompleted, currentLevel }` until the student has a Level 10 telemetry record (the dashboard shows the message and does not fall back to its client-side PDF) | Student ID |
| `GET` | `/api/reports/export-csv/:roomPin` | CSV export for LMS gradebooks, aligned to CSTA standards | Room PIN |

## Socket.io Events Reference

Full payload schemas, examples and acknowledgement shapes: [`schemas/websocket-payloads.json`](schemas/websocket-payloads.json). Every client → server event accepts an optional ack callback that receives `{ ok: boolean, error?: string, ... }`.

**Rooms:** `room:<PIN>` = everyone in the class · `teacher:<PIN>` = teacher dashboards and read-only observers (projector HUD) · `student:<studentId>` = one student.

#### Client → Server (Student, Unity)

| Event | Payload | Server effect |
|-------|---------|---------------|
| `student:join` | `{ roomPin, studentName, studentId? }` | Creates or resumes the student → `student:joined` / `student:join_error`, `roster:updated` |
| `student:token_earned` | `{ roomPin, studentId, amount (1–100), levelId }` | Updates balance + community meter → `TOKEN_EARNED`, `COMMUNITY_PROGRESS` |
| `student:level_completed` | `{ roomPin, studentId, levelId, telemetry }` | Saves telemetry, unlocks next level → `LEVEL_COMPLETED`, `COSMETIC_UNLOCKED`, `roster:updated` |
| `student:energy_contributed` | `{ roomPin, studentId, kw (0–1000], levelId }` | → `ENERGY_CONTRIBUTED` |
| `student:stuck_alert` | `{ roomPin, studentId, levelId, retriesCount }` | → `STUCK_ALERT` (teacher only) |

#### Client → Server (Teacher, Dashboard)

| Event | Payload | Server effect |
|-------|---------|---------------|
| `teacher:join` | `{ roomPin, teacherName }` | Claims/creates the classroom → `teacher:joined` |
| `teacher:pause_class` | `{ roomPin }` | → `class:paused` |
| `teacher:resume_class` | `{ roomPin }` | → `class:resumed` |
| `teacher:mute_audio` | `{ roomPin }` | → `audio:mute` |
| `teacher:unmute_audio` | `{ roomPin }` | → `audio:unmute` |
| `teacher:flag_student` | `{ roomPin, studentId }` | → `student:flagged` |

#### Client → Server (Observer, Projector HUD)

| Event | Payload | Server effect |
|-------|---------|---------------|
| `observer:join` | `{ roomPin, observerName? }` | Joins an **existing** classroom as a read-only observer (`room:<PIN>` + `teacher:<PIN>`) → `observer:joined` / `observer:join_error`. Does not claim the room, and does not trigger `teacher:disconnected` when it leaves. Teacher control events and student game events from an observer socket are rejected. |

#### Client → Server (Any client)

| Event | Payload | Server effect |
|-------|---------|---------------|
| `client:heartbeat` | `{ sentAt? }` (client clock, ms) | No broadcast. The ack returns `{ ok: true, serverTime, sentAt }` (`sentAt` is `null` if it was not sent) for latency measurement and liveness. Used by Unity `NetworkManager`. |

#### Server → Client

| Event | Audience | Payload |
|-------|----------|---------|
| `TOKEN_EARNED` | room | `{ studentId, studentName, amount, levelId, newBalance, communityTokens }` |
| `LEVEL_COMPLETED` | room | `{ studentId, studentName, levelId, cosmeticUnlocked, currentLevel, gameCompleted }` |
| `ENERGY_CONTRIBUTED` | room | `{ studentId, studentName, kw, levelId }` |
| `STUCK_ALERT` | teacher | `{ studentId, studentName, levelId, retriesCount, timestamp }` |
| `COMMUNITY_PROGRESS` | room | `{ tokens, goal: 1000, percentage }` |
| `COSMETIC_UNLOCKED` | student | `{ studentId, cosmeticId, levelId }` |
| `class:paused` / `class:resumed` | room | `{ roomPin, at }` |
| `audio:mute` / `audio:unmute` | room | `{ roomPin, at }` |
| `roster:updated` | teacher | `{ roomPin, students[], connectedCount, totalCount }` |
| `student:flagged` | teacher | `{ studentId, studentName, flaggedAt }` |
| `student:joined` / `student:join_error` | sender | join result |
| `teacher:joined` | sender | full classroom state |
| `observer:joined` / `observer:join_error` | sender | classroom state (same shape as `teacher:joined`) / `{ error }` |
| `teacher:disconnected` | room | `{ roomPin, at }` |

## Level Curriculum Map

Source of truth: [`schemas/level-configs.json`](schemas/level-configs.json) (also includes hint tiers, grid size, code capacity and NGSS practices).

| Level | Title | STEM Domain | Key Blocks | CSTA Standards | Success Condition |
|-------|-------|-------------|------------|----------------|-------------------|
| 1 | Sky-Isle Cleanup | Logic | MOVE, TURN, CLEAN | 1B-AP-08, 1B-AP-10 | 100% of trash tiles cleaned |
| 2 | Solar Panel Path | Sequencing & Loops | MOVE, CLEAN, REPEAT | 1B-AP-10, 1B-AP-11 | 100% of solar panels cleaned |
| 3 | Water Wheel Village | Nested Loops | MOVE, TURN, WATER, REPEAT | 1B-AP-10, 1B-AP-11, 1B-AP-15 | 100% of plants watered |
| 4 | Wind Farm Ridge | Energy Systems | MOVE, TURN, ROTATE_TURBINE, REPEAT | 1B-AP-10, 1B-CS-02 | Energy output ≥ 50 kW |
| 5 | Recycling Plant Sort | Conditionals | IF, ELSE, CLEAN, MOVE, REPEAT | 1B-AP-10, 1B-AP-12 | ≥ 90% items sorted correctly |
| 6 | Hydro Dam Hydraulics | Mathematical Modeling | REPEAT, IF, ROTATE_TURBINE, WATER, BATTERY_CHARGE | 1B-AP-09, 1B-DA-07, 1B-AP-10 | Reservoir in safe range ≥ 95% of the time |
| 7 | Bridge Builder Canyon | Structural Engineering | MOVE, TURN, REPEAT, SUBROUTINE | 1B-AP-11, 1B-AP-13, 1B-AP-15 | Bridge supports the truck load |
| 8 | Compost Garden Sensors | Sensors & Events | ALWAYS, MOISTURE_SENSOR, IF, WATER, MOVE | 1B-AP-10, 1B-CS-02, 1B-DA-06 | ≥ 90% plants healthy with water saved |
| 9 | Night Grid Balancer | Systems Thinking | ALWAYS, BATTERY_CHARGE, IF, ELSE, SUBROUTINE, ROTATE_TURBINE | 1B-AP-09, 1B-AP-11, 1B-CS-02, 1B-DA-07 | Town power uptime ≥ 95% across day/night |
| 10 | Eco-City Master Mission | Integrated Engineering | All 12 block types | 1B-AP-08, 1B-AP-10, 1B-AP-11, 1B-AP-13, 1B-AP-15 | City sustainability score ≥ 85% |

Each first-time level completion unlocks a robot cosmetic (`leaf_antenna` … `golden_gear_crown`, see `backend/config/gameConfig.js`).

## Unity Script Reference

| Script | Location | Responsibility |
|--------|----------|----------------|
| `BlockParserEngine.cs` | `Assets/Scripts/Core/` | Visual Logic Engine: parses the block tree (`LogicBlock`), validates code capacity, executes actions step by step, serializes programs to the block-program schema |
| `GridEnvironmentManager.cs` | `Assets/Scripts/Core/` | Builds the tile grid (`ConfigureGrid`/`SetTileType`, or `LevelData`), tracks soil `MoistureLevel` for watering levels, tracks the `EcoBot` position/facing, applies move/turn/clean/water actions and reports win-condition state |
| `DayNightController.cs` | `Assets/Scripts/Core/` | Day/night cycle: lighting, skybox transitions, solar/wind output, battery charge and phase-change events |
| `LevelManager.cs` | `Assets/Scripts/Levels/` | Level definitions, loading and progression. The 10 levels mirror `schemas/level-configs.json`. On a failed run it calls `HintSystem.ShowHint(level, retryCount)` and sends `student:stuck_alert` once the per-level retry threshold is reached. On completion it builds `LevelTelemetryData` (competency scores + `BlockProgramSerializer` snapshot) for `NetworkManager.SendLevelCompleted`, and evaluates the win condition from grid, physics and energy state |
| `HintSystem.cs` | `Assets/Scripts/UI/` | Adaptive tiered hints (observational → conceptual → interactive) driven by retries. Hides the current hint on `NetworkManager.OnClassPaused` and restores it on `OnClassResumed` |
| `AdaptiveAudioEngine.cs` | `Assets/Scripts/Audio/` | Adaptive music and SFX with day/night crossfades; honours teacher mute/unmute |
| `PhysicsSimulator.cs` | `Assets/Scripts/Physics/` | Simplified hydraulics network (`HydraulicNode`) and bridge load/stress model (`BridgeSegment`) for levels 6 and 7 |
| `NetworkManager.cs` | `Assets/Scripts/Network/` | Socket.io bridge to the backend (SocketIOUnity; API unverified against the real package). Handles join/resume, `student:*` emits with ack timeouts, an offline queue, the `client:heartbeat` latency check, and main-thread events for broadcasts |

## Testing

```bash
cd backend && npm test && npm run check    # node:test smoke tests (Mongoose mocked, no MongoDB needed) + syntax check
cd frontend && CI=true npm run build       # production build, warnings treated as errors
```

The Unity scripts have only been checked with an out-of-repo .NET stub compile (hand-written UnityEngine stubs). They have not been compiled in the Unity Editor.

## Deployment Notes

- **Database:** use **MongoDB Atlas** (or a managed replica set). Set `MONGO_URI` to the SRV connection string and restrict network access to the backend host.
- **Backend process:** run with a process manager, e.g. `npm install --omit=dev && pm2 start server.js --name eco-bot-backend && pm2 save`. Set `NODE_ENV=production`, `PORT`, `CORS_ORIGIN` (the dashboard origin) and `CLIENT_JOIN_URL`. The server shuts down gracefully on SIGTERM/SIGINT and resets stale connection flags on startup.
- **Single instance or sticky sessions:** Socket.io rooms are held in memory. For more than one backend instance, enable sticky sessions on the load balancer and add the `@socket.io/redis-adapter`.
- **Reverse proxy / TLS:** put Nginx (or a cloud load balancer) in front with HTTPS and WebSocket upgrade headers (`Upgrade`, `Connection`). Point health checks at `/api/health`.
- **Teacher dashboard:** `cd frontend && REACT_APP_BACKEND_URL=https://api.your-school.org npm run build`, then serve `frontend/build/` as static files (Nginx, S3 + CloudFront, Netlify, or `npx serve -s build`). Configure SPA fallback to `index.html`.
- **Unity builds:** set `BACKEND_URL` to the public HTTPS URL before building for WebGL, Windows/macOS or tablets. WebGL builds need the backend's `CORS_ORIGIN` to include the game's origin.
- **Privacy:** only a first name/nickname and gameplay telemetry are stored. Review your district's student-data policies (e.g. COPPA/FERPA) before deployment.

## License

MIT License — Copyright (c) 2026 Eco-Bot Builders contributors.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
