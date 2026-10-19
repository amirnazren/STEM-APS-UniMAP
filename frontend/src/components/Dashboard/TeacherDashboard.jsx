/**
 * @file TeacherDashboard — main three-column teacher view for a classroom.
 *
 * Layout (desktop ≥1280px): roster + alerts (30%) | progress + activity (40%) | analytics + controls (30%).
 * Tablet (768–1279px): two columns with analytics/controls spanning the full width below.
 * Narrow screens: single column.
 */
import React, { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import useClassroomSocket from '../../hooks/useClassroomSocket';
import useWindowWidth from '../../hooks/useWindowWidth';
import { COLORS } from '../../utils/format';
import { getStoredTeacherName } from './TeacherLogin';
import StudentRoster from './StudentRoster';
import AlertPanel from './AlertPanel';
import CommunityProgressBar from '../HUD/CommunityProgressBar';
import ActivityTicker from '../HUD/ActivityTicker';
import STEMRadarChart from '../Analytics/STEMRadarChart';
import TeacherControls from '../Controls/TeacherControls';

/**
 * Route component for `/dashboard/:roomPin`.
 * @returns {JSX.Element}
 */
function TeacherDashboard() {
  const { roomPin } = useParams();
  const [teacherName] = useState(() => getStoredTeacherName() || 'Teacher');
  const { isDesktop, isTablet } = useWindowWidth();
  const [analyticsKey, setAnalyticsKey] = useState(0);
  const [hasJoined, setHasJoined] = useState(false);

  const classroom = useClassroomSocket(roomPin, teacherName);
  const {
    students, alerts, activityFeed, communityTokens, communityGoal, flaggedStudentIds,
    isPaused, isMuted, isConnected, connectionError,
    pauseClass, resumeClass, muteAudio, unmuteAudio, flagStudent, dismissAlert, replaceStudents, reconnect
  } = classroom;

  // The roster is "loading" until the first successful connection.
  useEffect(() => {
    if (isConnected) setHasJoined(true);
  }, [isConnected]);

  useEffect(() => {
    document.title = `Eco-Bot Builders | Room ${roomPin}`;
  }, [roomPin]);

  /** Refresh analytics and scroll the radar into view. */
  const handleViewAnalytics = useCallback(() => {
    setAnalyticsKey((k) => k + 1);
    const el = document.getElementById('stem-radar');
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }, []);

  const openHud = useCallback(() => {
    window.open(`/class-hud/${roomPin}`, '_blank', 'noopener,noreferrer');
  }, [roomPin]);

  if (!/^\d{4}$/.test(roomPin || '')) {
    return (
      <main style={styles.invalid}>
        <p>⚠️ “{roomPin}” is not a valid 4-digit room PIN.</p>
        <Link to="/" style={styles.link}>← Back to login</Link>
      </main>
    );
  }

  const gridStyle = isDesktop
    ? { gridTemplateColumns: '3fr 4fr 3fr' }
    : isTablet
      ? { gridTemplateColumns: '1fr 1fr' }
      : { gridTemplateColumns: '1fr' };
  const rightColumnStyle = isTablet ? { gridColumn: '1 / -1', flexDirection: 'row', flexWrap: 'wrap' } : null;

  return (
    <div style={styles.page}>
      <header style={styles.topBar}>
        <div style={styles.brand}>
          <span style={styles.brandName}>🤖 Eco-Bot Builders</span>
          <span style={styles.sep}>|</span>
          <span>Room PIN: <strong style={styles.pin}>{roomPin}</strong></span>
        </div>
        <div style={styles.topRight}>
          <span style={styles.status} title={isConnected ? 'Connected' : 'Disconnected'}>
            <span
              style={{
                ...styles.statusDot,
                background: isConnected ? COLORS.green : COLORS.red,
                animation: isConnected ? 'none' : 'eco-pulse 1.2s ease-in-out infinite'
              }}
            />
            {isConnected ? 'Live' : 'Offline'}
          </span>
          <span style={styles.teacher}>👩‍🏫 {teacherName}</span>
          <button type="button" style={styles.hudButton} onClick={openHud}>🖥️ Open HUD</button>
          <Link to="/" style={styles.link}>Exit</Link>
        </div>
      </header>

      {connectionError && (
        <div role="alert" style={styles.banner}>
          ⚠️ {connectionError}
          {!isConnected && (
            <button type="button" style={styles.bannerButton} onClick={reconnect}>Reconnect</button>
          )}
        </div>
      )}

      <main style={{ ...styles.grid, ...gridStyle }}>
        <div style={styles.column}>
          <StudentRoster
            students={students}
            alerts={alerts}
            flaggedStudentIds={flaggedStudentIds}
            onFlagStudent={flagStudent}
            isLoading={!hasJoined}
          />
          <AlertPanel alerts={alerts} onDismiss={dismissAlert} />
        </div>

        <div style={styles.column}>
          <CommunityProgressBar tokens={communityTokens} goal={communityGoal} />
          {/* Fixed height so the ticker scrolls internally instead of growing the page. */}
          <ActivityTicker events={activityFeed} style={{ height: 540, flex: '0 0 auto' }} />
        </div>

        <div style={{ ...styles.column, ...rightColumnStyle }}>
          <div style={isTablet ? styles.half : styles.full}>
            <STEMRadarChart roomPin={roomPin} students={students} refreshKey={analyticsKey} />
          </div>
          <div style={isTablet ? styles.half : styles.full}>
            <TeacherControls
              roomPin={roomPin}
              isPaused={isPaused}
              isMuted={isMuted}
              isConnected={isConnected}
              onPause={pauseClass}
              onResume={resumeClass}
              onMute={muteAudio}
              onUnmute={unmuteAudio}
              onViewAnalytics={handleViewAnalytics}
              onRosterRefreshed={replaceStudents}
            />
          </div>
        </div>
      </main>
    </div>
  );
}

TeacherDashboard.propTypes = {};

const styles = {
  page: { minHeight: '100vh', display: 'flex', flexDirection: 'column' },
  topBar: {
    position: 'sticky',
    top: 0,
    zIndex: 10,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    flexWrap: 'wrap',
    gap: 12,
    padding: '12px 20px',
    background: COLORS.panel,
    borderBottom: `1px solid ${COLORS.border}`
  },
  brand: { display: 'flex', alignItems: 'center', gap: 10, fontSize: 18, flexWrap: 'wrap' },
  brandName: { fontWeight: 800, color: COLORS.green },
  sep: { color: COLORS.border },
  pin: { color: COLORS.amber, letterSpacing: 3, fontSize: 20 },
  topRight: { display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap' },
  status: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 14, fontWeight: 600 },
  statusDot: { width: 12, height: 12, borderRadius: '50%', display: 'inline-block' },
  teacher: { color: COLORS.muted, fontSize: 14 },
  hudButton: {
    minHeight: 40,
    padding: '0 16px',
    borderRadius: 10,
    border: 'none',
    background: COLORS.green,
    color: '#06210F',
    fontWeight: 700,
    cursor: 'pointer'
  },
  link: { color: COLORS.muted, fontSize: 14 },
  banner: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 12,
    padding: '10px 16px',
    background: 'rgba(243,156,18,0.15)',
    borderBottom: `1px solid ${COLORS.amber}`,
    color: COLORS.amber,
    fontSize: 14,
    flexWrap: 'wrap'
  },
  bannerButton: {
    padding: '6px 14px',
    borderRadius: 8,
    border: `1px solid ${COLORS.amber}`,
    background: 'transparent',
    color: COLORS.amber,
    fontWeight: 700,
    cursor: 'pointer'
  },
  grid: { flex: 1, display: 'grid', gap: 16, padding: 16, alignItems: 'start' },
  column: { display: 'flex', flexDirection: 'column', gap: 16, minWidth: 0 },
  half: { flex: '1 1 340px', minWidth: 0, display: 'flex', flexDirection: 'column' },
  full: { display: 'flex', flexDirection: 'column' },
  invalid: { padding: 40, textAlign: 'center', color: COLORS.amber }
};

export default TeacherDashboard;
