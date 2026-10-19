/**
 * @file useClassroomSocket — real-time classroom state for the teacher dashboard and HUD.
 *
 * Connects to the Eco-Bot Builders Socket.io server, joins the classroom as a teacher
 * (`teacher:join`) and keeps a live, de-duplicated view of the roster, alerts, activity
 * feed and community progress. Re-joins automatically after every reconnect.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { io } from 'socket.io-client';
import { BACKEND_URL } from '../utils/api';
import { COMMUNITY_TOKEN_GOAL, uid } from '../utils/format';

/** Maximum STUCK_ALERT entries retained (most recent first). */
const MAX_ALERTS = 20;
/** Maximum activity-feed entries retained (chronological, newest last). */
const MAX_FEED = 50;
/** Reconnection policy: exponential backoff 1s → 2s → 4s → 8s → 16s, then give up. */
const RECONNECT_ATTEMPTS = 5;
const RECONNECT_DELAY_MS = 1000;
const RECONNECT_DELAY_MAX_MS = 16000;

/**
 * Emit an event with an acknowledgement, resolving to the server's `{ ok, ... }` response.
 * Rejects after `timeoutMs` if the server never answers.
 * @param {import('socket.io-client').Socket|null} socket
 * @param {string} event
 * @param {object} payload
 * @param {number} [timeoutMs=5000]
 * @returns {Promise<object>}
 */
function emitWithAck(socket, event, payload, timeoutMs = 5000) {
  return new Promise((resolve) => {
    if (!socket || !socket.connected) {
      resolve({ ok: false, error: 'Not connected to the classroom server' });
      return;
    }
    socket.timeout(timeoutMs).emit(event, payload, (err, response) => {
      if (err) resolve({ ok: false, error: 'The server did not respond in time' });
      else resolve(response || { ok: true });
    });
  });
}

/**
 * Live classroom connection.
 *
 * @param {string} roomPin 4-digit classroom PIN.
 * @param {string} [teacherName='Teacher'] Display name sent with `teacher:join`.
 * @returns {{
 *   students: object[],
 *   alerts: object[],
 *   activityFeed: object[],
 *   communityProgress: number,
 *   communityTokens: number,
 *   communityGoal: number,
 *   flaggedStudentIds: string[],
 *   isPaused: boolean,
 *   isMuted: boolean,
 *   isConnected: boolean,
 *   connectionError: string|null,
 *   pauseClass: () => Promise<object>,
 *   resumeClass: () => Promise<object>,
 *   muteAudio: () => Promise<object>,
 *   unmuteAudio: () => Promise<object>,
 *   flagStudent: (studentId: string) => Promise<object>,
 *   dismissAlert: (alertId: string) => void,
 *   replaceStudents: (students: object[]) => void,
 *   reconnect: () => void
 * }}
 */
export default function useClassroomSocket(roomPin, teacherName = 'Teacher') {
  const [students, setStudents] = useState([]);
  const [alerts, setAlerts] = useState([]);
  const [activityFeed, setActivityFeed] = useState([]);
  const [communityTokens, setCommunityTokens] = useState(0);
  const [communityGoal, setCommunityGoal] = useState(COMMUNITY_TOKEN_GOAL);
  const [flaggedStudentIds, setFlaggedStudentIds] = useState([]);
  const [isPaused, setIsPaused] = useState(false);
  const [isMuted, setIsMuted] = useState(false);
  const [isConnected, setIsConnected] = useState(false);
  const [connectionError, setConnectionError] = useState(null);

  const socketRef = useRef(null);
  /** Latest roster for name look-ups inside socket listeners without re-subscribing. */
  const studentsRef = useRef([]);
  useEffect(() => {
    studentsRef.current = students;
  }, [students]);

  /** Resolve a display name for events that only carry a studentId (e.g. ENERGY_CONTRIBUTED). */
  const nameFor = useCallback((studentId, fallback) => {
    if (fallback) return fallback;
    const s = studentsRef.current.find((x) => x.studentId === studentId);
    return s ? s.name : 'A student';
  }, []);

  /** Append an entry to the activity feed (chronological, capped). */
  const pushActivity = useCallback((entry) => {
    setActivityFeed((prev) => [...prev, { id: uid(), at: new Date().toISOString(), ...entry }].slice(-MAX_FEED));
  }, []);

  /** Shallow-merge an update into one student of the roster. */
  const patchStudent = useCallback((studentId, patch) => {
    setStudents((prev) => prev.map((s) => (s.studentId === studentId ? { ...s, ...patch } : s)));
  }, []);

  useEffect(() => {
    if (!roomPin || !/^\d{4}$/.test(String(roomPin))) {
      setConnectionError('Invalid room PIN');
      return undefined;
    }

    const socket = io(BACKEND_URL, {
      transports: ['websocket', 'polling'],
      reconnection: true,
      reconnectionAttempts: RECONNECT_ATTEMPTS,
      reconnectionDelay: RECONNECT_DELAY_MS,
      reconnectionDelayMax: RECONNECT_DELAY_MAX_MS,
      randomizationFactor: 0.3,
      timeout: 10000
    });
    socketRef.current = socket;

    // ---------------- connection lifecycle ----------------
    const handleConnect = () => {
      setIsConnected(true);
      setConnectionError(null);
      // (Re-)join on every connect so a reconnect restores room membership.
      socket.emit('teacher:join', { roomPin: String(roomPin), teacherName: teacherName || 'Teacher' }, (res) => {
        if (res && res.ok === false) setConnectionError(res.error || 'Could not join the classroom');
      });
    };
    const handleDisconnect = (reason) => {
      setIsConnected(false);
      if (reason === 'io server disconnect') setConnectionError('Disconnected by the server');
    };
    const handleConnectError = () => {
      setIsConnected(false);
      setConnectionError(`Cannot reach the classroom server (${BACKEND_URL}). Retrying…`);
    };
    const handleReconnectFailed = () => {
      setConnectionError('Connection lost. Automatic reconnection failed — click "Reconnect" to try again.');
    };

    // ---------------- classroom state ----------------
    const handleJoined = (state) => {
      if (!state) return;
      setStudents(Array.isArray(state.students) ? state.students : []);
      setIsPaused(Boolean(state.isPaused));
      setIsMuted(Boolean(state.isMuted));
      setCommunityTokens(Number(state.communityProgress) || 0);
      if (state.communityGoal) setCommunityGoal(state.communityGoal);
    };
    const handleRoster = (payload) => {
      if (payload && Array.isArray(payload.students)) setStudents(payload.students);
    };
    const handleCommunity = (payload) => {
      if (!payload) return;
      setCommunityTokens(Number(payload.communityProgress) || 0);
      if (payload.communityGoal) setCommunityGoal(payload.communityGoal);
    };

    // ---------------- gameplay events ----------------
    const handleToken = (p) => {
      if (!p) return;
      if (typeof p.newBalance === 'number') patchStudent(p.studentId, { cleanTokensBalance: p.newBalance });
      pushActivity({
        type: 'TOKEN_EARNED',
        studentId: p.studentId,
        studentName: nameFor(p.studentId, p.studentName),
        amount: p.amount,
        levelId: p.levelId
      });
    };
    const handleLevel = (p) => {
      if (!p) return;
      if (typeof p.currentLevel === 'number') patchStudent(p.studentId, { currentLevel: p.currentLevel });
      // A completed level resolves any stuck alert the student raised for it.
      setAlerts((prev) => prev.filter((a) => !(a.studentId === p.studentId && a.levelId === p.levelId)));
      pushActivity({
        type: 'LEVEL_COMPLETED',
        studentId: p.studentId,
        studentName: nameFor(p.studentId, p.studentName),
        levelId: p.levelId,
        gameCompleted: Boolean(p.gameCompleted)
      });
    };
    const handleEnergy = (p) => {
      if (!p) return;
      pushActivity({
        type: 'ENERGY_CONTRIBUTED',
        studentId: p.studentId,
        studentName: nameFor(p.studentId, p.studentName),
        kw: p.kw,
        levelId: p.levelId
      });
    };
    const handleStuck = (p) => {
      if (!p) return;
      const alert = {
        id: uid(),
        studentId: p.studentId,
        studentName: nameFor(p.studentId, p.studentName),
        levelId: p.levelId,
        retriesCount: p.retriesCount,
        at: p.at || new Date().toISOString()
      };
      // Keep one alert per student+level (the newest), most recent first.
      setAlerts((prev) =>
        [alert, ...prev.filter((a) => !(a.studentId === alert.studentId && a.levelId === alert.levelId))].slice(0, MAX_ALERTS)
      );
    };
    const handleFlagged = (p) => {
      if (p && p.studentId) setFlaggedStudentIds((prev) => (prev.includes(p.studentId) ? prev : [...prev, p.studentId]));
    };

    // ---------------- teacher controls echo ----------------
    const handlePaused = () => setIsPaused(true);
    const handleResumed = () => setIsPaused(false);
    const handleMute = () => setIsMuted(true);
    const handleUnmute = () => setIsMuted(false);

    const listeners = {
      connect: handleConnect,
      disconnect: handleDisconnect,
      connect_error: handleConnectError,
      'teacher:joined': handleJoined,
      'roster:updated': handleRoster,
      COMMUNITY_PROGRESS: handleCommunity,
      TOKEN_EARNED: handleToken,
      LEVEL_COMPLETED: handleLevel,
      ENERGY_CONTRIBUTED: handleEnergy,
      STUCK_ALERT: handleStuck,
      'student:flagged': handleFlagged,
      'class:paused': handlePaused,
      'class:resumed': handleResumed,
      'audio:mute': handleMute,
      'audio:unmute': handleUnmute
    };
    Object.entries(listeners).forEach(([event, fn]) => socket.on(event, fn));
    socket.io.on('reconnect_failed', handleReconnectFailed);

    return () => {
      Object.entries(listeners).forEach(([event, fn]) => socket.off(event, fn));
      socket.io.off('reconnect_failed', handleReconnectFailed);
      socket.disconnect();
      socketRef.current = null;
    };
  }, [roomPin, teacherName, nameFor, patchStudent, pushActivity]);

  // ---------------- emit helpers ----------------
  const pin = String(roomPin);

  /** Optimistically toggle local state, revert if the server rejects. */
  const toggle = useCallback(
    async (event, setter, value) => {
      setter(value);
      const res = await emitWithAck(socketRef.current, event, { roomPin: pin });
      if (!res.ok) setter(!value);
      return res;
    },
    [pin]
  );

  const pauseClass = useCallback(() => toggle('teacher:pause_class', setIsPaused, true), [toggle]);
  const resumeClass = useCallback(() => toggle('teacher:resume_class', setIsPaused, false), [toggle]);
  const muteAudio = useCallback(() => toggle('teacher:mute_audio', setIsMuted, true), [toggle]);
  const unmuteAudio = useCallback(() => toggle('teacher:unmute_audio', setIsMuted, false), [toggle]);

  /** Flag a student for attention (broadcast to all teacher dashboards of the room). */
  const flagStudent = useCallback(
    (studentId) => emitWithAck(socketRef.current, 'teacher:flag_student', { roomPin: pin, studentId }),
    [pin]
  );

  /** Remove an alert from the local list (does not affect other dashboards). */
  const dismissAlert = useCallback((alertId) => {
    setAlerts((prev) => prev.filter((a) => a.id !== alertId));
  }, []);

  /** Replace the roster with a freshly fetched list (e.g. GET /api/students/:roomPin). */
  const replaceStudents = useCallback((list) => {
    if (Array.isArray(list)) setStudents(list);
  }, []);

  /** Manually restart the connection after automatic retries are exhausted. */
  const reconnect = useCallback(() => {
    const socket = socketRef.current;
    if (socket && !socket.connected) {
      setConnectionError(null);
      socket.connect();
    }
  }, []);

  return {
    students,
    alerts,
    activityFeed,
    communityProgress: communityGoal > 0 ? Math.min(1, communityTokens / communityGoal) : 0,
    communityTokens,
    communityGoal,
    flaggedStudentIds,
    isPaused,
    isMuted,
    isConnected,
    connectionError,
    pauseClass,
    resumeClass,
    muteAudio,
    unmuteAudio,
    flagStudent,
    dismissAlert,
    replaceStudents,
    reconnect
  };
}
