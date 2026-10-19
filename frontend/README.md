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
- `src/hooks/useClassroomSocket.js` — Socket.io connection (`teacher:join`, live events, emit helpers, 5-attempt exponential backoff)
- `src/components/Dashboard` — `TeacherLogin`, `TeacherDashboard`, `StudentRoster`, `AlertPanel`
- `src/components/HUD` — `CommunityProgressBar`, `ActivityTicker`, `ClassroomHUD`
- `src/components/Analytics/STEMRadarChart.jsx` — Recharts radar, CSV export, certificate download (jsPDF fallback if the server PDF fails)
- `src/components/Controls/TeacherControls.jsx` — pause/resume, mute, analytics, roster refresh

Note: the HUD joins the room as a teacher socket (the backend only streams room events to joined sockets).
