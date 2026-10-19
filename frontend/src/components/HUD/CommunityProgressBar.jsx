/**
 * @file CommunityProgressBar — animated class-wide progress toward the Clean Token goal.
 */
import React, { useEffect, useRef, useState } from 'react';
import PropTypes from 'prop-types';
import { COLORS, COMMUNITY_TOKEN_GOAL } from '../../utils/format';

/** Milestones rendered as star markers along the bar (fractions of the goal). */
const MILESTONES = [0.25, 0.5, 0.75, 1];

/**
 * @param {object} props
 * @param {number} props.tokens Total Clean Tokens earned by the class.
 * @param {number} [props.goal=1000] Community goal.
 * @param {boolean} [props.large=false] Projector-sized variant (bigger fonts and bar).
 * @returns {JSX.Element}
 */
function CommunityProgressBar({ tokens, goal = COMMUNITY_TOKEN_GOAL, large = false }) {
  const safeGoal = goal > 0 ? goal : COMMUNITY_TOKEN_GOAL;
  const safeTokens = Math.max(0, Number(tokens) || 0);
  const fraction = Math.min(1, safeTokens / safeGoal);
  const percent = Math.round(fraction * 100);

  // Briefly glow whenever the token count increases (e.g. on TOKEN_EARNED).
  const [glow, setGlow] = useState(false);
  const prevTokens = useRef(safeTokens);
  useEffect(() => {
    if (safeTokens > prevTokens.current) {
      setGlow(true);
      const id = setTimeout(() => setGlow(false), 900);
      prevTokens.current = safeTokens;
      return () => clearTimeout(id);
    }
    prevTokens.current = safeTokens;
    return undefined;
  }, [safeTokens]);

  const barHeight = large ? 56 : 34;

  return (
    <section style={{ ...styles.wrapper, padding: large ? '24px 32px' : '16px 18px' }} aria-label="Community progress">
      <div style={styles.headerRow}>
        <h2 style={{ ...styles.title, fontSize: large ? 34 : 18 }}>🌍 Community Clean Energy Goal</h2>
        <span style={{ ...styles.percent, fontSize: large ? 44 : 24 }}>{percent}%</span>
      </div>

      <div
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={safeGoal}
        aria-valuenow={Math.min(safeTokens, safeGoal)}
        aria-valuetext={`${safeTokens} of ${safeGoal} Clean Tokens`}
        style={{ ...styles.track, height: barHeight, borderRadius: barHeight / 2 }}
      >
        <div
          style={{
            ...styles.fill,
            width: `${fraction * 100}%`,
            borderRadius: barHeight / 2,
            boxShadow: glow ? `0 0 24px ${COLORS.green}` : 'none'
          }}
        />
        {MILESTONES.map((m) => {
          const reached = fraction >= m;
          return (
            <div
              key={m}
              style={{ ...styles.milestone, left: `${m * 100}%`, transform: `translateX(${m === 1 ? '-100%' : '-50%'})` }}
              title={`${Math.round(m * 100)}% milestone`}
            >
              <span
                style={{
                  ...styles.star,
                  fontSize: large ? 30 : 18,
                  filter: reached ? 'none' : 'grayscale(1) opacity(0.45)',
                  transform: reached ? 'scale(1.15)' : 'scale(1)'
                }}
                aria-hidden="true"
              >
                ⭐
              </span>
            </div>
          );
        })}
      </div>

      <div style={styles.footerRow}>
        <span style={{ ...styles.count, fontSize: large ? 28 : 16 }}>
          🪙 {safeTokens.toLocaleString()} / {safeGoal.toLocaleString()} Clean Tokens
        </span>
        {fraction >= 1 && <span style={{ ...styles.done, fontSize: large ? 26 : 15 }}>🎉 Goal reached!</span>}
      </div>
      <div style={styles.labels} aria-hidden="true">
        {MILESTONES.map((m) => (
          <span key={m} style={{ ...styles.label, left: `${m * 100}%`, fontSize: large ? 16 : 11 }}>
            {Math.round(m * 100)}%
          </span>
        ))}
      </div>
    </section>
  );
}

CommunityProgressBar.propTypes = {
  tokens: PropTypes.number.isRequired,
  goal: PropTypes.number,
  large: PropTypes.bool
};

const styles = {
  wrapper: {
    background: COLORS.panel,
    border: `1px solid ${COLORS.border}`,
    borderRadius: 14,
    position: 'relative'
  },
  headerRow: { display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 12, marginBottom: 14 },
  title: { margin: 0, fontWeight: 700 },
  percent: { fontWeight: 800, color: COLORS.green },
  track: {
    position: 'relative',
    background: COLORS.bg,
    border: `1px solid ${COLORS.border}`,
    overflow: 'visible'
  },
  fill: {
    height: '100%',
    background: `linear-gradient(90deg, ${COLORS.green} 0%, ${COLORS.greenDark} 100%)`,
    transition: 'width 0.8s cubic-bezier(0.22, 1, 0.36, 1), box-shadow 0.4s ease',
    minWidth: 0
  },
  milestone: {
    position: 'absolute',
    top: '50%',
    marginTop: -2,
    display: 'flex',
    alignItems: 'center',
    height: 0,
    pointerEvents: 'auto'
  },
  star: { transition: 'filter 0.4s ease, transform 0.4s ease', lineHeight: 1 },
  footerRow: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 14, gap: 12, flexWrap: 'wrap' },
  count: { fontWeight: 600, color: COLORS.gold },
  done: { fontWeight: 700, color: COLORS.green, animation: 'eco-pulse 1.6s ease-in-out infinite' },
  labels: { position: 'relative', height: 16, marginTop: 4 },
  label: { position: 'absolute', transform: 'translateX(-50%)', color: COLORS.muted, whiteSpace: 'nowrap' }
};

export default CommunityProgressBar;
