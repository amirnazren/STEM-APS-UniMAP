/**
 * @file AlertPanel — STUCK_ALERT intervention cards for students who need help.
 */
import React, { useEffect, useState } from 'react';
import PropTypes from 'prop-types';
import Panel from '../common/Panel';
import { COLORS, alertShape, relativeTime } from '../../utils/format';

/** Maximum number of alert cards rendered at once. */
const MAX_VISIBLE = 10;

/**
 * @param {object} props
 * @param {object[]} props.alerts STUCK_ALERT events, most recent first.
 * @param {(alertId: string) => void} props.onDismiss Remove an alert from the local list.
 * @returns {JSX.Element}
 */
function AlertPanel({ alerts, onDismiss }) {
  // Re-render every 30s so relative timestamps ("2 min ago") stay fresh.
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30000);
    return () => clearInterval(id);
  }, []);

  const visible = alerts.slice(0, MAX_VISIBLE);

  return (
    <Panel
      title={`🚨 Needs Help${alerts.length ? ` (${alerts.length})` : ''}`}
      style={styles.panel}
      bodyStyle={styles.body}
    >
      {visible.length === 0 ? (
        <p style={styles.empty}>All students are on track! 🌱</p>
      ) : (
        <ul style={styles.list} aria-live="polite">
          {visible.map((a) => (
            <li key={a.id} style={styles.card}>
              <div style={styles.content}>
                <strong style={styles.name}>{a.studentName || 'A student'}</strong>
                <span style={styles.detail}>
                  Level {a.levelId ?? '?'} · {a.retriesCount ?? 0} {a.retriesCount === 1 ? 'retry' : 'retries'}
                </span>
                <time style={styles.time} dateTime={a.at}>{relativeTime(a.at, now)}</time>
              </div>
              <button
                type="button"
                onClick={() => onDismiss(a.id)}
                style={styles.dismiss}
                aria-label={`Dismiss alert for ${a.studentName || 'student'}`}
                title="Dismiss"
              >
                ✕
              </button>
            </li>
          ))}
        </ul>
      )}
      {alerts.length > MAX_VISIBLE && (
        <p style={styles.more}>+{alerts.length - MAX_VISIBLE} more alerts</p>
      )}
    </Panel>
  );
}

AlertPanel.propTypes = {
  alerts: PropTypes.arrayOf(alertShape).isRequired,
  onDismiss: PropTypes.func.isRequired
};

const styles = {
  panel: { flex: '1 1 45%', minHeight: 200 },
  body: { maxHeight: 360 },
  empty: { color: COLORS.green, textAlign: 'center', margin: '24px 8px', fontWeight: 600 },
  list: { listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', gap: 8 },
  card: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: 8,
    padding: '10px 12px',
    borderRadius: 10,
    background: COLORS.amber,
    color: '#1E1404',
    animation: 'eco-slide-in 0.25s ease-out'
  },
  content: { flex: 1, display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 },
  name: { fontSize: 15 },
  detail: { fontSize: 13 },
  time: { fontSize: 12, opacity: 0.8 },
  dismiss: {
    width: 30,
    height: 30,
    borderRadius: 6,
    border: 'none',
    background: 'rgba(0,0,0,0.12)',
    color: '#1E1404',
    fontWeight: 700,
    cursor: 'pointer',
    flexShrink: 0
  },
  more: { textAlign: 'center', fontSize: 12, color: COLORS.muted, margin: '8px 0 0' }
};

export default AlertPanel;
