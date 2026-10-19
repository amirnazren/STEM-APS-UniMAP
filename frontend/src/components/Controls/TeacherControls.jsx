/**
 * @file TeacherControls — pause/resume class, mute audio, analytics and roster refresh.
 */
import React, { useCallback, useState } from 'react';
import PropTypes from 'prop-types';
import Panel from '../common/Panel';
import { api, getErrorMessage } from '../../utils/api';
import { COLORS } from '../../utils/format';

/**
 * @param {object} props
 * @param {string} props.roomPin Classroom PIN.
 * @param {boolean} props.isPaused Current pause state.
 * @param {boolean} props.isMuted Current audio mute state.
 * @param {boolean} props.isConnected Socket connection state (controls disabled when offline).
 * @param {() => Promise<object>} props.onPause
 * @param {() => Promise<object>} props.onResume
 * @param {() => Promise<object>} props.onMute
 * @param {() => Promise<object>} props.onUnmute
 * @param {() => void} props.onViewAnalytics Ask the dashboard to refresh and reveal the radar chart.
 * @param {(students: object[]) => void} props.onRosterRefreshed Receives the freshly fetched roster.
 * @returns {JSX.Element}
 */
function TeacherControls({
  roomPin,
  isPaused,
  isMuted,
  isConnected,
  onPause,
  onResume,
  onMute,
  onUnmute,
  onViewAnalytics,
  onRosterRefreshed
}) {
  const [busy, setBusy] = useState(null); // name of the action in flight
  const [message, setMessage] = useState({ text: '', tone: 'info' });

  /** Run an action, tracking its busy state and surfacing `{ ok:false }` errors. */
  const run = useCallback(async (name, action, successText) => {
    setBusy(name);
    setMessage({ text: '', tone: 'info' });
    try {
      const res = await action();
      if (res && res.ok === false) setMessage({ text: res.error || 'Action failed.', tone: 'error' });
      else if (successText) setMessage({ text: successText, tone: 'info' });
    } catch (err) {
      setMessage({ text: await getErrorMessage(err), tone: 'error' });
    } finally {
      setBusy(null);
    }
  }, []);

  const handleRefreshRoster = useCallback(
    () =>
      run('refresh', async () => {
        const { data } = await api.get(`/students/${roomPin}`);
        onRosterRefreshed(data.students || []);
        return { ok: true };
      }, 'Roster refreshed.'),
    [run, roomPin, onRosterRefreshed]
  );

  const handleViewAnalytics = useCallback(() => {
    onViewAnalytics();
    setMessage({ text: 'Analytics refreshed.', tone: 'info' });
  }, [onViewAnalytics]);

  const offline = !isConnected;

  return (
    <Panel title="🎛️ Classroom Controls" style={styles.panel}>
      {isPaused && (
        <div style={styles.pausedBadge} role="status">⏸ CLASS PAUSED — Eyes Up!</div>
      )}

      <div style={styles.grid}>
        {!isPaused ? (
          <button
            type="button"
            style={{ ...styles.button, ...styles.active }}
            onClick={() => run('pause', onPause)}
            disabled={offline || busy === 'pause'}
          >
            {busy === 'pause' ? 'Pausing…' : '⏸ Pause Class / Eyes Up'}
          </button>
        ) : (
          <button
            type="button"
            style={{ ...styles.button, ...styles.resume }}
            onClick={() => run('resume', onResume)}
            disabled={offline || busy === 'resume'}
          >
            {busy === 'resume' ? 'Resuming…' : '▶ Resume Class'}
          </button>
        )}

        <button
          type="button"
          role="switch"
          aria-checked={isMuted}
          style={{ ...styles.button, ...(isMuted ? styles.inactive : styles.active) }}
          onClick={() => run('mute', isMuted ? onUnmute : onMute)}
          disabled={offline || busy === 'mute'}
        >
          {isMuted ? '🔇 Audio Muted (tap to unmute)' : '🔊 Mute Audio'}
        </button>

        <button type="button" style={{ ...styles.button, ...styles.neutral }} onClick={handleViewAnalytics}>
          📊 View Analytics
        </button>

        <button
          type="button"
          style={{ ...styles.button, ...styles.neutral }}
          onClick={handleRefreshRoster}
          disabled={busy === 'refresh'}
        >
          {busy === 'refresh' ? 'Refreshing…' : '🔄 Refresh Roster'}
        </button>
      </div>

      {offline && <p style={styles.offline}>Controls are disabled while reconnecting to the server.</p>}
      {message.text && (
        <p role={message.tone === 'error' ? 'alert' : 'status'} style={{ ...styles.message, color: message.tone === 'error' ? COLORS.amber : COLORS.muted }}>
          {message.tone === 'error' ? '⚠️ ' : ''}{message.text}
        </p>
      )}
    </Panel>
  );
}

TeacherControls.propTypes = {
  roomPin: PropTypes.string.isRequired,
  isPaused: PropTypes.bool.isRequired,
  isMuted: PropTypes.bool.isRequired,
  isConnected: PropTypes.bool.isRequired,
  onPause: PropTypes.func.isRequired,
  onResume: PropTypes.func.isRequired,
  onMute: PropTypes.func.isRequired,
  onUnmute: PropTypes.func.isRequired,
  onViewAnalytics: PropTypes.func.isRequired,
  onRosterRefreshed: PropTypes.func.isRequired
};

const styles = {
  panel: { flex: '0 0 auto' },
  pausedBadge: {
    background: COLORS.amber,
    color: '#1E1404',
    fontWeight: 800,
    textAlign: 'center',
    padding: '10px 12px',
    borderRadius: 10,
    marginBottom: 12,
    letterSpacing: 1,
    animation: 'eco-pulse 1.6s ease-in-out infinite'
  },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 10 },
  button: {
    minHeight: 60,
    borderRadius: 12,
    border: 'none',
    fontSize: 15,
    fontWeight: 700,
    cursor: 'pointer',
    padding: '8px 12px',
    transition: 'transform 0.1s ease, background 0.2s ease'
  },
  active: { background: COLORS.green, color: '#06210F' },
  resume: { background: COLORS.amber, color: '#1E1404' },
  inactive: { background: COLORS.grey, color: '#F4F6F7' },
  neutral: { background: COLORS.panelAlt, color: COLORS.text, border: `1px solid ${COLORS.border}` },
  offline: { color: COLORS.amber, fontSize: 12, margin: '10px 0 0' },
  message: { fontSize: 13, margin: '10px 0 0' }
};

export default TeacherControls;
