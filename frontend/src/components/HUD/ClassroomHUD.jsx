/**
 * @file ClassroomHUD — full-screen projector view for the whole class (no teacher controls).
 *
 * Large fonts and high contrast so students can read it from the back of the room.
 * The mouse cursor auto-hides after 3 seconds of inactivity.
 */
import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import useClassroomSocket from '../../hooks/useClassroomSocket';
import useWindowWidth from '../../hooks/useWindowWidth';
import { COLORS, MAX_LEVEL, levelColor, studentShape } from '../../utils/format';
import { getStoredTeacherName } from '../Dashboard/TeacherLogin';
import CommunityProgressBar from './CommunityProgressBar';
import ActivityTicker from './ActivityTicker';

/** Idle time before the cursor is hidden. */
const CURSOR_HIDE_MS = 3000;

/**
 * Hide the cursor after a period of mouse inactivity.
 * @param {number} delay
 * @returns {boolean} true while the cursor should be hidden.
 */
function useIdleCursor(delay) {
  const [hidden, setHidden] = useState(false);
  const timer = useRef(null);
  useEffect(() => {
    const reset = () => {
      setHidden(false);
      clearTimeout(timer.current);
      timer.current = setTimeout(() => setHidden(true), delay);
    };
    reset();
    window.addEventListener('mousemove', reset);
    window.addEventListener('mousedown', reset);
    window.addEventListener('keydown', reset);
    return () => {
      clearTimeout(timer.current);
      window.removeEventListener('mousemove', reset);
      window.removeEventListener('mousedown', reset);
      window.removeEventListener('keydown', reset);
    };
  }, [delay]);
  return hidden;
}

/**
 * One student "pod": name, level, mini progress through the 10 levels and live tokens.
 * @param {object} props
 * @param {object} props.student
 * @returns {JSX.Element}
 */
function StudentPod({ student }) {
  const level = Math.min(MAX_LEVEL, Math.max(1, student.currentLevel || 1));
  const color = levelColor(level);
  return (
    <div style={{ ...podStyles.pod, borderColor: color, opacity: student.isConnected ? 1 : 0.5 }}>
      <div style={podStyles.name} title={student.name}>{student.name}</div>
      <div style={{ ...podStyles.level, color }}>Level {level}</div>
      <div style={podStyles.steps} aria-label={`Level ${level} of ${MAX_LEVEL}`}>
        {Array.from({ length: MAX_LEVEL }, (_, i) => (
          <span key={i} style={{ ...podStyles.step, background: i < level ? color : COLORS.border }} />
        ))}
      </div>
      <div style={podStyles.tokens}>🪙 {student.cleanTokensBalance || 0}</div>
    </div>
  );
}

StudentPod.propTypes = { student: studentShape.isRequired };

/**
 * Route component for `/class-hud/:roomPin`.
 * @returns {JSX.Element}
 */
function ClassroomHUD() {
  const { roomPin } = useParams();
  const [teacherName] = useState(() => getStoredTeacherName() || 'Teacher');
  const { isDesktop, isMobile } = useWindowWidth();
  const cursorHidden = useIdleCursor(CURSOR_HIDE_MS);
  const { students, activityFeed, communityTokens, communityGoal, isPaused, isConnected } =
    useClassroomSocket(roomPin, teacherName);

  useEffect(() => {
    document.title = `Eco-Bot Builders | Class HUD ${roomPin}`;
  }, [roomPin]);

  /** Online students first, then by level (desc), then name. */
  const pods = useMemo(
    () =>
      [...students].sort(
        (a, b) =>
          Number(Boolean(b.isConnected)) - Number(Boolean(a.isConnected)) ||
          (b.currentLevel || 1) - (a.currentLevel || 1) ||
          a.name.localeCompare(b.name)
      ),
    [students]
  );

  return (
    <div style={{ ...styles.page, cursor: cursorHidden ? 'none' : 'default' }}>
      <header style={styles.header}>
        <span style={styles.brand}>🤖 Eco-Bot Builders</span>
        <span style={styles.join}>
          Join with PIN <strong style={styles.pin}>{roomPin}</strong>
          <span
            style={{ ...styles.dot, background: isConnected ? COLORS.green : COLORS.red }}
            title={isConnected ? 'Live' : 'Reconnecting'}
          />
        </span>
      </header>

      <CommunityProgressBar tokens={communityTokens} goal={communityGoal} large />

      <div style={{ ...styles.body, flexDirection: isMobile ? 'column' : 'row' }}>
        <section style={styles.podsArea} aria-label="Student progress">
          {pods.length === 0 ? (
            <p style={styles.empty}>
              {isConnected ? `Waiting for builders to join room ${roomPin}… 🌱` : 'Connecting to the classroom…'}
            </p>
          ) : (
            <div style={{ ...styles.podGrid, gridTemplateColumns: `repeat(auto-fill, minmax(${isDesktop ? 220 : 180}px, 1fr))` }}>
              {pods.map((s) => <StudentPod key={s.studentId} student={s} />)}
            </div>
          )}
        </section>
        <ActivityTicker
          events={activityFeed}
          large
          style={{ ...styles.ticker, flex: isMobile ? '1 1 auto' : '0 0 32%' }}
        />
      </div>

      {isPaused && (
        <div style={styles.pauseOverlay} role="status">
          <div style={styles.pauseText}>⏸ Eyes Up!</div>
          <div style={styles.pauseSub}>Your teacher has paused the class</div>
        </div>
      )}
    </div>
  );
}

ClassroomHUD.propTypes = {};

const styles = {
  page: {
    minHeight: '100vh',
    height: '100vh',
    display: 'flex',
    flexDirection: 'column',
    gap: 20,
    padding: 24,
    background: COLORS.bg,
    overflow: 'hidden'
  },
  header: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12 },
  brand: { fontSize: 40, fontWeight: 800, color: COLORS.green },
  join: { display: 'flex', alignItems: 'center', gap: 12, fontSize: 30, color: COLORS.text },
  pin: { color: COLORS.amber, letterSpacing: 8, fontSize: 44 },
  dot: { width: 18, height: 18, borderRadius: '50%', display: 'inline-block' },
  body: { flex: 1, display: 'flex', gap: 20, minHeight: 0 },
  podsArea: { flex: 1, overflowY: 'auto', minHeight: 0 },
  podGrid: { display: 'grid', gap: 16 },
  empty: { fontSize: 30, color: COLORS.muted, textAlign: 'center', marginTop: 60 },
  ticker: { minHeight: 0 },
  pauseOverlay: {
    position: 'fixed',
    inset: 0,
    background: 'rgba(13,27,42,0.92)',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    zIndex: 100
  },
  pauseText: { fontSize: 120, fontWeight: 900, color: COLORS.amber, animation: 'eco-pulse 2s ease-in-out infinite' },
  pauseSub: { fontSize: 36, color: COLORS.text, marginTop: 12 }
};

const podStyles = {
  pod: {
    background: COLORS.panel,
    border: '3px solid',
    borderRadius: 16,
    padding: '16px 18px',
    display: 'flex',
    flexDirection: 'column',
    gap: 8,
    transition: 'opacity 0.4s ease, border-color 0.4s ease'
  },
  name: { fontSize: 28, fontWeight: 800, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  level: { fontSize: 22, fontWeight: 700 },
  steps: { display: 'grid', gridTemplateColumns: `repeat(${MAX_LEVEL}, 1fr)`, gap: 4 },
  step: { height: 10, borderRadius: 4, transition: 'background 0.4s ease' },
  tokens: { fontSize: 26, fontWeight: 700, color: COLORS.gold }
};

export default ClassroomHUD;
