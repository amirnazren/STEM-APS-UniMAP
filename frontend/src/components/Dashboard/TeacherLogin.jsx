/**
 * @file TeacherLogin — sign in to an existing classroom or create a new one (PIN + QR code).
 */
import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, getErrorMessage } from '../../utils/api';
import { COLORS } from '../../utils/format';

/** sessionStorage key used to remember the teacher's name across routes. */
export const TEACHER_NAME_KEY = 'ecobot.teacherName';

/** Teacher names follow the backend rule: 1-40 printable characters. */
const MAX_NAME_LENGTH = 40;

/**
 * Read the remembered teacher name (safe when storage is unavailable).
 * @returns {string}
 */
export function getStoredTeacherName() {
  try {
    return sessionStorage.getItem(TEACHER_NAME_KEY) || '';
  } catch (e) {
    return '';
  }
}

/** @param {string} name */
function storeTeacherName(name) {
  try {
    sessionStorage.setItem(TEACHER_NAME_KEY, name);
  } catch (e) {
    /* private mode — the dashboard falls back to "Teacher" */
  }
}

/**
 * Login page.
 * - "Join Classroom": validates the PIN via POST /api/auth/validate-pin then opens the dashboard.
 * - "Create New Room": POST /api/auth/create-room, shows the generated PIN and QR code for students.
 * @returns {JSX.Element}
 */
function TeacherLogin() {
  const navigate = useNavigate();
  const [teacherName, setTeacherName] = useState(getStoredTeacherName);
  const [roomPin, setRoomPin] = useState('');
  const [error, setError] = useState('');
  const [joining, setJoining] = useState(false);
  const [creating, setCreating] = useState(false);
  const [createdRoom, setCreatedRoom] = useState(null);

  useEffect(() => {
    document.title = 'Eco-Bot Builders | Teacher Login';
  }, []);

  /** Validate the form fields; returns an error message or ''. */
  const validate = useCallback((requirePin) => {
    if (!teacherName.trim()) return 'Please enter your name.';
    if (teacherName.trim().length > MAX_NAME_LENGTH) return `Name must be ${MAX_NAME_LENGTH} characters or fewer.`;
    if (requirePin && !/^\d{4}$/.test(roomPin)) return 'Room PIN must be exactly 4 digits.';
    return '';
  }, [teacherName, roomPin]);

  /** Submit: validate PIN with the backend, then navigate to the dashboard. */
  const handleJoin = useCallback(async (e) => {
    e.preventDefault();
    const msg = validate(true);
    if (msg) {
      setError(msg);
      return;
    }
    setError('');
    setJoining(true);
    try {
      const { data } = await api.post('/auth/validate-pin', { roomPin });
      if (!data.valid) {
        // An inactive room (teacher closed the tab) can be reclaimed by its teacher;
        // only a non-existent room is a hard failure.
        try {
          await api.get(`/auth/room/${roomPin}`);
        } catch (lookupErr) {
          setError(data.reason || 'Room not found. Check the PIN or create a new room.');
          return;
        }
      }
      storeTeacherName(teacherName.trim());
      navigate(`/dashboard/${roomPin}`);
    } catch (err) {
      setError(await getErrorMessage(err, 'Could not validate the room PIN.'));
    } finally {
      setJoining(false);
    }
  }, [validate, roomPin, teacherName, navigate]);

  /** Create a fresh classroom and display its PIN + QR code. */
  const handleCreate = useCallback(async () => {
    const msg = validate(false);
    if (msg) {
      setError(msg);
      return;
    }
    setError('');
    setCreating(true);
    try {
      const { data } = await api.post('/auth/create-room', { teacherName: teacherName.trim() });
      storeTeacherName(teacherName.trim());
      setCreatedRoom(data);
      setRoomPin(data.roomPin);
    } catch (err) {
      setError(await getErrorMessage(err, 'Could not create a new room.'));
    } finally {
      setCreating(false);
    }
  }, [validate, teacherName]);

  const busy = joining || creating;

  return (
    <main style={styles.page}>
      <section style={styles.card} aria-labelledby="login-title">
        <div style={styles.logo} aria-hidden="true">🤖🌱</div>
        <h1 id="login-title" style={styles.title}>Eco-Bot Builders</h1>
        <p style={styles.subtitle}>Teacher Dashboard</p>

        <form onSubmit={handleJoin} noValidate style={styles.form}>
          <label htmlFor="teacherName" style={styles.label}>Teacher Name</label>
          <input
            id="teacherName"
            type="text"
            autoComplete="name"
            value={teacherName}
            maxLength={MAX_NAME_LENGTH}
            onChange={(e) => setTeacherName(e.target.value)}
            placeholder="e.g. Ms. Rivera"
            style={styles.input}
            disabled={busy}
          />

          <label htmlFor="roomPin" style={styles.label}>Room PIN</label>
          <input
            id="roomPin"
            type="text"
            inputMode="numeric"
            pattern="\d{4}"
            autoComplete="off"
            value={roomPin}
            onChange={(e) => setRoomPin(e.target.value.replace(/\D/g, '').slice(0, 4))}
            placeholder="4-digit PIN"
            style={{ ...styles.input, ...styles.pinInput }}
            aria-describedby={error ? 'login-error' : undefined}
            disabled={busy}
          />

          {error && (
            <p id="login-error" role="alert" style={styles.error}>⚠️ {error}</p>
          )}

          <button type="submit" style={styles.primaryButton} disabled={busy}>
            {joining ? 'Checking PIN…' : 'Join Classroom'}
          </button>
        </form>

        <div style={styles.divider}><span style={styles.dividerText}>or</span></div>

        <button type="button" onClick={handleCreate} style={styles.secondaryButton} disabled={busy}>
          {creating ? 'Creating room…' : '➕ Create New Room'}
        </button>

        {createdRoom && (
          <div style={styles.createdBox} aria-live="polite">
            <p style={styles.createdLabel}>Your new classroom PIN</p>
            <p style={styles.createdPin}>{createdRoom.roomPin}</p>
            {createdRoom.qrCodeDataUrl ? (
              <img
                src={createdRoom.qrCodeDataUrl}
                alt={`QR code for students to join room ${createdRoom.roomPin}`}
                style={styles.qr}
              />
            ) : (
              <p style={styles.muted}>QR code unavailable.</p>
            )}
            <p style={styles.muted}>Students scan the QR code or enter the PIN in the game.</p>
            {createdRoom.joinUrl && <p style={styles.joinUrl}>{createdRoom.joinUrl}</p>}
            <button
              type="button"
              style={styles.primaryButton}
              onClick={() => navigate(`/dashboard/${createdRoom.roomPin}`)}
            >
              Open Dashboard →
            </button>
          </div>
        )}
      </section>
    </main>
  );
}

TeacherLogin.propTypes = {};

const styles = {
  page: {
    minHeight: '100vh',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 24,
    background: `radial-gradient(circle at 30% 20%, #16314a 0%, ${COLORS.bg} 60%)`
  },
  card: {
    width: '100%',
    maxWidth: 420,
    background: COLORS.panel,
    border: `1px solid ${COLORS.border}`,
    borderRadius: 16,
    padding: '32px 28px',
    boxShadow: '0 20px 50px rgba(0,0,0,0.45)',
    textAlign: 'center'
  },
  logo: { fontSize: 44, marginBottom: 4 },
  title: { margin: 0, fontSize: 28, color: COLORS.green },
  subtitle: { margin: '4px 0 24px', color: COLORS.muted },
  form: { display: 'flex', flexDirection: 'column', textAlign: 'left' },
  label: { fontSize: 14, fontWeight: 600, margin: '12px 0 6px', color: COLORS.text },
  input: {
    padding: '12px 14px',
    fontSize: 16,
    borderRadius: 10,
    border: `1px solid ${COLORS.border}`,
    background: COLORS.bg,
    color: COLORS.text
  },
  pinInput: { fontSize: 24, letterSpacing: 10, textAlign: 'center', fontWeight: 700 },
  error: {
    margin: '14px 0 0',
    padding: '10px 12px',
    borderRadius: 8,
    background: 'rgba(243,156,18,0.12)',
    border: `1px solid ${COLORS.amber}`,
    color: COLORS.amber,
    fontSize: 14
  },
  primaryButton: {
    marginTop: 20,
    minHeight: 50,
    border: 'none',
    borderRadius: 10,
    background: COLORS.green,
    color: '#06210F',
    fontSize: 17,
    fontWeight: 700,
    cursor: 'pointer',
    width: '100%'
  },
  secondaryButton: {
    minHeight: 46,
    width: '100%',
    borderRadius: 10,
    border: `2px solid ${COLORS.green}`,
    background: 'transparent',
    color: COLORS.green,
    fontSize: 16,
    fontWeight: 600,
    cursor: 'pointer'
  },
  divider: { position: 'relative', margin: '22px 0', borderTop: `1px solid ${COLORS.border}` },
  dividerText: {
    position: 'absolute',
    top: -11,
    left: '50%',
    transform: 'translateX(-50%)',
    background: COLORS.panel,
    padding: '0 10px',
    color: COLORS.muted,
    fontSize: 13
  },
  createdBox: {
    marginTop: 22,
    padding: 18,
    borderRadius: 12,
    background: COLORS.bg,
    border: `1px dashed ${COLORS.green}`,
    animation: 'eco-slide-in 0.3s ease-out'
  },
  createdLabel: { margin: 0, color: COLORS.muted, fontSize: 14 },
  createdPin: { margin: '4px 0 12px', fontSize: 44, fontWeight: 800, letterSpacing: 12, color: COLORS.green },
  qr: { width: 200, height: 200, borderRadius: 8, background: '#fff', padding: 6 },
  muted: { color: COLORS.muted, fontSize: 13, margin: '10px 0 0' },
  joinUrl: { color: COLORS.blue, fontSize: 12, wordBreak: 'break-all', margin: '6px 0 0' }
};

export default TeacherLogin;
