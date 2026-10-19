/**
 * @file Shared constants, formatting helpers and PropTypes shapes.
 */
import PropTypes from 'prop-types';

/** Class-wide Clean Token goal (mirrors backend COMMUNITY_TOKEN_GOAL). */
export const COMMUNITY_TOKEN_GOAL = 1000;

/** Total number of game levels. */
export const MAX_LEVEL = 10;

/** Theme palette used by inline styles. */
export const COLORS = Object.freeze({
  bg: '#0D1B2A',
  panel: '#1B263B',
  panelAlt: '#22314A',
  border: '#2C3E5C',
  text: '#E8F1F8',
  muted: '#8FA3BF',
  green: '#2ECC71',
  greenDark: '#27AE60',
  amber: '#F39C12',
  blue: '#3498DB',
  gold: '#F1C40F',
  red: '#E74C3C',
  grey: '#7F8C8D'
});

/** One distinct colour per level (index 0 = level 1). */
const LEVEL_COLORS = [
  '#1ABC9C', '#2ECC71', '#3498DB', '#9B59B6', '#34495E',
  '#F1C40F', '#E67E22', '#E74C3C', '#EC407A', '#F39C12'
];

/**
 * Badge colour for a level number (clamped to 1..MAX_LEVEL).
 * @param {number} level
 * @returns {string}
 */
export function levelColor(level) {
  const n = Math.min(MAX_LEVEL, Math.max(1, Math.round(Number(level) || 1)));
  return LEVEL_COLORS[n - 1];
}

/**
 * Human friendly relative time ("just now", "2 min ago", "3 h ago").
 * @param {string|number|Date} time
 * @param {number} [now=Date.now()]
 * @returns {string}
 */
export function relativeTime(time, now = Date.now()) {
  const t = new Date(time).getTime();
  if (!Number.isFinite(t)) return '';
  const sec = Math.max(0, Math.round((now - t) / 1000));
  if (sec < 45) return 'just now';
  const min = Math.round(sec / 60);
  if (min < 60) return `${min} min ago`;
  const hours = Math.round(min / 60);
  if (hours < 24) return `${hours} h ago`;
  return new Date(t).toLocaleDateString();
}

/**
 * Clock time (HH:MM:SS) for ticker entries.
 * @param {string|number|Date} time
 * @returns {string}
 */
export function clockTime(time) {
  const d = new Date(time);
  return Number.isFinite(d.getTime()) ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '';
}

/** Generate a reasonably unique client-side id for list keys. */
export function uid() {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

// ---------------------------------------------------------------------------
// PropTypes shapes shared across components
// ---------------------------------------------------------------------------

/** A student as serialised by the backend (`serializeStudent`). */
export const studentShape = PropTypes.shape({
  studentId: PropTypes.string.isRequired,
  name: PropTypes.string.isRequired,
  currentLevel: PropTypes.number,
  cleanTokensBalance: PropTypes.number,
  unlockedCosmetics: PropTypes.arrayOf(PropTypes.string),
  isConnected: PropTypes.bool,
  lastSeen: PropTypes.string
});

/** A STUCK_ALERT event enriched with a client id. */
export const alertShape = PropTypes.shape({
  id: PropTypes.string.isRequired,
  studentId: PropTypes.string,
  studentName: PropTypes.string,
  levelId: PropTypes.number,
  retriesCount: PropTypes.number,
  at: PropTypes.string
});

/** An activity-feed entry produced by useClassroomSocket. */
export const activityShape = PropTypes.shape({
  id: PropTypes.string.isRequired,
  type: PropTypes.oneOf(['TOKEN_EARNED', 'LEVEL_COMPLETED', 'ENERGY_CONTRIBUTED']).isRequired,
  studentId: PropTypes.string,
  studentName: PropTypes.string,
  amount: PropTypes.number,
  levelId: PropTypes.number,
  kw: PropTypes.number,
  gameCompleted: PropTypes.bool,
  at: PropTypes.string.isRequired
});
