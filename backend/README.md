# Eco-Bot Builders — Backend

Express 4 + Socket.io 4 + Mongoose 7 real-time backend.

```bash
cp .env.example .env   # set MONGO_URI, PORT (default 3001), CLIENT_JOIN_URL
npm install
npm start
```

Tests (no MongoDB needed, models are mocked): `npm test` (node:test smoke tests for the socket handlers and the certificate gate) and `npm run check` (syntax check of every source file).

## REST API
| Method | Path | Description |
|---|---|---|
| GET | `/api/health` | Liveness (503 when DB down) |
| POST | `/api/auth/create-room` | `{teacherName?}` → `{roomPin, joinUrl, qrCodeDataUrl, classroom}` |
| POST | `/api/auth/validate-pin` | `{roomPin}` → `{valid, teacherName, studentCount}` |
| GET | `/api/auth/room/:roomPin` | Full classroom state + roster |
| GET | `/api/students/:roomPin` | Students in classroom |
| PATCH | `/api/students/:studentId/cosmetic` | `{cosmeticId}` add cosmetic |
| GET | `/api/students/:studentId/progress` | Progress summary |
| GET | `/api/telemetry/student/:studentId` | Records (`?includeSnapshots=true`) |
| GET | `/api/telemetry/class/:roomPin` | Class / per-student / per-level competency aggregates |
| POST | `/api/telemetry/record` | Save a LevelTelemetry record |
| GET | `/api/reports/certificate/:studentId` | PDF certificate (A4 landscape). Returns **409** `{error, code:'LEVEL_10_INCOMPLETE', requiredLevel:10, levelsCompleted, currentLevel}` until the student has a Level 10 telemetry record |
| GET | `/api/reports/export-csv/:roomPin` | LMS CSV (CSTA 1B-aligned headers) |

## Socket.io events
All client events accept an optional ack callback → `{ ok, ...data }` or `{ ok:false, error }`.

**Teacher → server:** `teacher:join {roomPin, teacherName}`, `teacher:pause_class`, `teacher:resume_class`,
`teacher:mute_audio`, `teacher:unmute_audio` (all `{roomPin}`), `teacher:flag_student {roomPin, studentId}`.

**Student → server:** `student:join {roomPin, studentName, studentId?}`, `student:token_earned {roomPin, studentId, amount, levelId}`,
`student:level_completed {roomPin, studentId, levelId, telemetry}`, `student:energy_contributed {roomPin, studentId, kw, levelId?}`,
`student:stuck_alert {roomPin, studentId, levelId, retriesCount}`.

**Observer → server (projector HUD, read-only):** `observer:join {roomPin, observerName?}`. It joins an existing classroom's
`room:<PIN>` and `teacher:<PIN>` without claiming it (it never changes `teacherSocketId`/`isActive`, and never fires `teacher:disconnected`).
Teacher control events and student game events from an observer are rejected.

**Any client → server:** `client:heartbeat {sentAt?}`. No broadcast. The ack is `{ok:true, serverTime (ISO), sentAt|null}`, used by the Unity
NetworkManager for latency and liveness.

**Server → clients:** `teacher:joined`, `observer:joined`, `observer:join_error`, `roster:updated` (teacher room), `student:joined`, `student:join_error`,
`class:paused`, `class:resumed`, `audio:mute`, `audio:unmute`, `student:flagged` (teacher), `TOKEN_EARNED`,
`COMMUNITY_PROGRESS`, `LEVEL_COMPLETED`, `COSMETIC_UNLOCKED` (that student only), `ENERGY_CONTRIBUTED`,
`STUCK_ALERT` (teacher only), `teacher:disconnected`.

Rooms: `room:<PIN>` (everyone), `teacher:<PIN>` (dashboards + observers), `student:<id>` (one student's devices).
Game events are accepted only from a socket that joined as that student in that room.
