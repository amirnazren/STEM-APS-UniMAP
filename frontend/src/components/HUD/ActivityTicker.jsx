/**
 * @file ActivityTicker — auto-scrolling timeline of classroom gameplay events.
 */
import React, { useEffect, useRef } from 'react';
import PropTypes from 'prop-types';
import Panel from '../common/Panel';
import { COLORS, activityShape, clockTime } from '../../utils/format';

/** Maximum entries rendered. */
const MAX_ENTRIES = 50;

/** Icon + accent colour + description per event type. */
const EVENT_STYLE = {
  LEVEL_COMPLETED: {
    icon: '🏆',
    color: COLORS.green,
    describe: (e) => (e.gameCompleted ? `finished the final level ${e.levelId}! 🎉` : `completed Level ${e.levelId}`)
  },
  TOKEN_EARNED: {
    icon: '🪙',
    color: COLORS.gold,
    describe: (e) => `earned ${e.amount} Clean Token${e.amount === 1 ? '' : 's'}${e.levelId ? ` on Level ${e.levelId}` : ''}`
  },
  ENERGY_CONTRIBUTED: {
    icon: '⚡',
    color: COLORS.blue,
    describe: (e) => `contributed ${Number(e.kw).toLocaleString(undefined, { maximumFractionDigits: 1 })} kW to the grid`
  }
};

/**
 * @param {object} props
 * @param {object[]} props.events Chronological activity entries (newest last).
 * @param {boolean} [props.large=false] Projector-sized fonts.
 * @param {object} [props.style] Extra panel styles.
 * @returns {JSX.Element}
 */
function ActivityTicker({ events, large = false, style }) {
  const listRef = useRef(null);
  const visible = events.filter((e) => EVENT_STYLE[e.type]).slice(-MAX_ENTRIES);
  const lastId = visible.length ? visible[visible.length - 1].id : null;

  // Auto-scroll to the newest entry whenever a new one arrives.
  useEffect(() => {
    const el = listRef.current;
    if (el) el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
  }, [lastId]);

  return (
    <Panel
      title={<span style={{ fontSize: large ? 26 : 16 }}>📣 Live Activity</span>}
      style={{ ...styles.panel, ...style }}
      bodyStyle={styles.body}
    >
      <div ref={listRef} style={styles.scroller}>
        {visible.length === 0 ? (
          <p style={{ ...styles.empty, fontSize: large ? 22 : 14 }}>Waiting for the first eco-bot to power up… ⚙️</p>
        ) : (
          <ol style={styles.list} aria-live="polite">
            {visible.map((e) => {
              const cfg = EVENT_STYLE[e.type];
              return (
                <li key={e.id} style={{ ...styles.entry, borderLeftColor: cfg.color, padding: large ? '12px 14px' : '8px 10px' }}>
                  <span style={{ ...styles.icon, fontSize: large ? 30 : 20 }} aria-hidden="true">{cfg.icon}</span>
                  <div style={styles.text}>
                    <span style={{ fontSize: large ? 22 : 14 }}>
                      <strong style={{ color: cfg.color }}>{e.studentName || 'A student'}</strong> {cfg.describe(e)}
                    </span>
                    <time style={{ ...styles.time, fontSize: large ? 15 : 11 }} dateTime={e.at}>{clockTime(e.at)}</time>
                  </div>
                </li>
              );
            })}
          </ol>
        )}
      </div>
    </Panel>
  );
}

ActivityTicker.propTypes = {
  events: PropTypes.arrayOf(activityShape).isRequired,
  large: PropTypes.bool,
  style: PropTypes.object
};

const styles = {
  panel: { flex: 1, minHeight: 280 },
  body: { padding: 0, display: 'flex', flexDirection: 'column', overflow: 'hidden' },
  scroller: { flex: 1, overflowY: 'auto', padding: 12, minHeight: 0 },
  empty: { color: COLORS.muted, textAlign: 'center', margin: '28px 8px' },
  list: { listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', gap: 6 },
  entry: {
    display: 'flex',
    alignItems: 'center',
    gap: 10,
    background: COLORS.bg,
    borderRadius: 8,
    borderLeft: '4px solid',
    animation: 'eco-slide-in 0.3s ease-out'
  },
  icon: { lineHeight: 1, flexShrink: 0 },
  text: { flex: 1, display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 },
  time: { color: COLORS.muted }
};

export default ActivityTicker;
