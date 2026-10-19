/**
 * @file Panel — consistent card container with a header used across the dashboard.
 */
import React from 'react';
import PropTypes from 'prop-types';
import { COLORS } from '../../utils/format';

/**
 * @param {object} props
 * @param {React.ReactNode} props.title Header text/content.
 * @param {React.ReactNode} [props.actions] Right-aligned header content.
 * @param {React.ReactNode} props.children Panel body.
 * @param {object} [props.style] Extra container styles.
 * @param {object} [props.bodyStyle] Extra body styles.
 * @param {string} [props.id] DOM id (used for scroll-into-view).
 * @returns {JSX.Element}
 */
function Panel({ title, actions = null, children = null, style, bodyStyle, id }) {
  return (
    <section id={id} style={{ ...styles.panel, ...style }}>
      <header style={styles.header}>
        <h2 style={styles.title}>{title}</h2>
        {actions && <div style={styles.actions}>{actions}</div>}
      </header>
      <div style={{ ...styles.body, ...bodyStyle }}>{children}</div>
    </section>
  );
}

Panel.propTypes = {
  title: PropTypes.node.isRequired,
  actions: PropTypes.node,
  children: PropTypes.node,
  style: PropTypes.object,
  bodyStyle: PropTypes.object,
  id: PropTypes.string
};

/** Small centred spinner for loading states. */
export function Spinner({ size = 22 }) {
  return (
    <span
      role="status"
      aria-label="Loading"
      style={{
        display: 'inline-block',
        width: size,
        height: size,
        border: `3px solid ${COLORS.border}`,
        borderTopColor: COLORS.green,
        borderRadius: '50%',
        animation: 'eco-spin 0.8s linear infinite'
      }}
    />
  );
}

Spinner.propTypes = { size: PropTypes.number };

const styles = {
  panel: {
    background: COLORS.panel,
    border: `1px solid ${COLORS.border}`,
    borderRadius: 14,
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
    overflow: 'hidden'
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 8,
    padding: '12px 16px',
    borderBottom: `1px solid ${COLORS.border}`,
    background: COLORS.panelAlt
  },
  title: { margin: 0, fontSize: 16, fontWeight: 700, color: COLORS.text },
  actions: { display: 'flex', alignItems: 'center', gap: 8 },
  body: { padding: 14, flex: 1, minHeight: 0, overflowY: 'auto' }
};

export default Panel;
