/**
 * @file StudentRoster — live list of students with level, tokens, status and a flag action.
 */
import React, { useCallback, useMemo, useState } from 'react';
import PropTypes from 'prop-types';
import Panel, { Spinner } from '../common/Panel';
import { COLORS, alertShape, levelColor, studentShape } from '../../utils/format';

/**
 * @param {object} props
 * @param {object[]} props.students Roster from useClassroomSocket.
 * @param {object[]} props.alerts Active STUCK_ALERTs (used to float stuck students to the top).
 * @param {string[]} props.flaggedStudentIds Students already flagged by a teacher.
 * @param {(studentId: string) => Promise<{ok: boolean, error?: string}>} props.onFlagStudent
 * @param {boolean} props.isLoading True until the first roster arrives.
 * @returns {JSX.Element}
 */
function StudentRoster({ students, alerts = [], flaggedStudentIds = [], onFlagStudent, isLoading = false }) {
  /** Per-student pending/error state for the flag button. */
  const [flagState, setFlagState] = useState({});

  const stuckIds = useMemo(() => new Set(alerts.map((a) => a.studentId)), [alerts]);

  /** Stuck first, then online before offline, then alphabetical. */
  const sorted = useMemo(
    () =>
      [...students].sort((a, b) => {
        const stuck = Number(stuckIds.has(b.studentId)) - Number(stuckIds.has(a.studentId));
        if (stuck) return stuck;
        const online = Number(Boolean(b.isConnected)) - Number(Boolean(a.isConnected));
        if (online) return online;
        return a.name.localeCompare(b.name);
      }),
    [students, stuckIds]
  );

  const onlineCount = students.filter((s) => s.isConnected).length;

  const handleFlag = useCallback(
    async (studentId) => {
      setFlagState((prev) => ({ ...prev, [studentId]: { pending: true } }));
      const res = await onFlagStudent(studentId);
      setFlagState((prev) => ({ ...prev, [studentId]: { pending: false, error: res && !res.ok ? res.error : null } }));
    },
    [onFlagStudent]
  );

  return (
    <Panel
      title={`👩‍🎓 Students (${students.length})`}
      actions={<span style={styles.onlineCount}>{onlineCount} online</span>}
      style={styles.panel}
    >
      {isLoading && students.length === 0 ? (
        <div style={styles.center}><Spinner /></div>
      ) : students.length === 0 ? (
        <p style={styles.empty}>No students yet. Share the room PIN so students can join! 🎮</p>
      ) : (
        <ul style={styles.list}>
          {sorted.map((s) => {
            const stuck = stuckIds.has(s.studentId);
            const flagged = flaggedStudentIds.includes(s.studentId);
            const state = flagState[s.studentId] || {};
            return (
              <li key={s.studentId} style={{ ...styles.card, ...(stuck ? styles.cardStuck : null) }}>
                <span
                  style={{ ...styles.dot, background: s.isConnected ? COLORS.green : COLORS.grey }}
                  title={s.isConnected ? 'Online' : 'Offline'}
                  aria-label={s.isConnected ? 'Online' : 'Offline'}
                />
                <div style={styles.info}>
                  <div style={styles.nameRow}>
                    {stuck && <span title="Needs help" aria-label="Needs help">⚠️</span>}
                    <span style={styles.name}>{s.name}</span>
                    {flagged && <span title="Flagged" style={styles.flagMark}>🚩</span>}
                  </div>
                  <div style={styles.meta}>
                    <span style={{ ...styles.levelBadge, background: levelColor(s.currentLevel) }}>
                      Lvl {s.currentLevel || 1}
                    </span>
                    <span style={styles.tokens}>🪙 {s.cleanTokensBalance || 0}</span>
                  </div>
                  {state.error && <span style={styles.flagError}>{state.error}</span>}
                </div>
                <button
                  type="button"
                  onClick={() => handleFlag(s.studentId)}
                  disabled={state.pending}
                  style={{ ...styles.flagButton, ...(flagged ? styles.flagButtonActive : null) }}
                  aria-label={`Flag ${s.name} for attention`}
                >
                  {state.pending ? '…' : flagged ? 'Flagged' : 'Flag'}
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </Panel>
  );
}

StudentRoster.propTypes = {
  students: PropTypes.arrayOf(studentShape).isRequired,
  alerts: PropTypes.arrayOf(alertShape),
  flaggedStudentIds: PropTypes.arrayOf(PropTypes.string),
  onFlagStudent: PropTypes.func.isRequired,
  isLoading: PropTypes.bool
};

const styles = {
  panel: { flex: '1 1 55%', minHeight: 260 },
  onlineCount: { fontSize: 13, color: COLORS.green, fontWeight: 600 },
  center: { display: 'flex', justifyContent: 'center', padding: 24 },
  empty: { color: COLORS.muted, textAlign: 'center', margin: '24px 8px' },
  list: { listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', gap: 8 },
  card: {
    display: 'flex',
    alignItems: 'center',
    gap: 10,
    padding: '10px 12px',
    borderRadius: 10,
    background: COLORS.bg,
    border: `1px solid ${COLORS.border}`
  },
  cardStuck: { borderColor: COLORS.amber, boxShadow: `0 0 0 1px ${COLORS.amber} inset` },
  dot: { width: 10, height: 10, borderRadius: '50%', flexShrink: 0 },
  info: { flex: 1, minWidth: 0 },
  nameRow: { display: 'flex', alignItems: 'center', gap: 6 },
  name: { fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  flagMark: { fontSize: 13 },
  meta: { display: 'flex', alignItems: 'center', gap: 10, marginTop: 4 },
  levelBadge: { fontSize: 12, fontWeight: 700, color: '#fff', padding: '2px 8px', borderRadius: 999 },
  tokens: { fontSize: 13, color: COLORS.gold, fontWeight: 600 },
  flagError: { display: 'block', marginTop: 4, fontSize: 12, color: COLORS.amber },
  flagButton: {
    minHeight: 36,
    padding: '0 12px',
    borderRadius: 8,
    border: `1px solid ${COLORS.amber}`,
    background: 'transparent',
    color: COLORS.amber,
    fontWeight: 600,
    cursor: 'pointer',
    flexShrink: 0
  },
  flagButtonActive: { background: COLORS.amber, color: COLORS.bg }
};

export default StudentRoster;
