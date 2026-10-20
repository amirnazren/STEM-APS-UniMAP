# Eco-Bot Builders — Teacher Dashboard (React 18)

## Run
```bash
cp .env.example .env        # optional: set REACT_APP_BACKEND_URL (default http://localhost:3001)
npm install
npm start                   # http://localhost:3000
```

## Routes
| Path | View |
|------|------|
| `/` | Teacher login / create room (PIN + QR) |
| `/dashboard/:roomPin` | Teacher dashboard: roster, alerts, community progress, activity, STEM radar, controls |
| `/class-hud/:roomPin` | Full-screen projector HUD (no controls, cursor auto-hides) |

## Structure
- `src/hooks/useClassroomSocket.js` — Socket.io connection (`teacher:join`, or `observer:join` with `{ role: 'observer' }`; live events, emit helpers, 5-attempt exponential backoff)
- `src/components/Dashboard` — `TeacherLogin`, `TeacherDashboard`, `StudentRoster`, `AlertPanel`
- `src/components/HUD` — `CommunityProgressBar`, `ActivityTicker`, `ClassroomHUD`
- `src/components/Analytics/STEMRadarChart.jsx` — Recharts radar, CSV export, certificate download (jsPDF fallback if the server PDF fails)
- `src/components/Controls/TeacherControls.jsx` — pause/resume, mute, analytics, roster refresh

## Roles
- The **dashboard** joins with `teacher:join` and owns the classroom (its controls emit `teacher:*` events).
- The **projector HUD** (`/class-hud/:roomPin`) joins as a read-only **observer** via `observer:join`, using `useClassroomSocket(pin, name, { role: 'observer' })`. It receives every room broadcast and the teacher-room feed, but cannot send control events. Closing the HUD tab never marks the room inactive and never fires `teacher:disconnected`. The room must already exist: open the dashboard for that PIN first, or the HUD receives `observer:join_error`.
- Heartbeat: the dashboard relies on Socket.io's built-in ping. The application-level `client:heartbeat` event is used by the Unity client.

## Certificate gate
The server returns **409** `LEVEL_10_INCOMPLETE` until the student has completed Level 10. `STEMRadarChart` shows the server's message in that case. The client-side jsPDF certificate is generated only for network errors and 5xx responses. Note that this fallback does not re-check Level 10 completion.
