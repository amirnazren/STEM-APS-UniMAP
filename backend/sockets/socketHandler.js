/**
 * @file Socket.io real-time event handler for Eco-Bot Builders.
 *
 * Rooms
 * -----
 *  - `room:<PIN>`    every participant (teacher + students) of a classroom.
 *  - `teacher:<PIN>` teacher dashboard(s) only — private alerts and roster updates.
 *  - `student:<id>`  a single student's sockets (all tabs/devices) — personal rewards.
 *
 * Every client event accepts an optional acknowledgement callback as the last
 * argument. It receives `{ ok: true, ...data }` or `{ ok: false, error }`, which
 * lets the Unity client and dashboard react to failures without extra events.
 */

'use strict';

const Classroom = require('../models/Classroom');
const Student = require('../models/Student');
const LevelTelemetry = require('../models/LevelTelemetry');
const {
  MAX_LEVEL,
  MAX_TOKENS_PER_EVENT,
  MAX_KW_PER_EVENT,
  COMMUNITY_TOKEN_GOAL,
  LEVEL_COSMETIC_REWARDS
} = require('../config/gameConfig');
const {
  isPlainObject,
  normalizePin,
  sanitizeName,
  normalizeStudentId,
  normalizeLevelId,
  normalizePositiveNumber,
  normalizeNonNegativeInt,
  normalizeTelemetry
} = require('../utils/validators');

/** Room name helpers. */
const roomOf = (pin) => `room:${pin}`;
const teacherRoomOf = (pin) => `teacher:${pin}`;
const studentRoomOf = (id) => `student:${id}`;

/**
 * Structured warning logger for malformed / unauthorised events.
 * @param {import('socket.io').Socket} socket
 * @param {string} event
 * @param {string} reason
 * @param {*} [payload]
 */
function warn(socket, event, reason, payload) {
  let preview;
  try {
    preview = JSON.stringify(payload);
    if (preview && preview.length > 300) preview = `${preview.slice(0, 300)}…`;
  } catch (e) {
    preview = '[unserialisable]';
  }
  console.warn(`[socket:warn] ${event} from ${socket.id}: ${reason}${preview ? ` | payload=${preview}` : ''}`);
}

/**
 * Safely invoke an acknowledgement callback if the client supplied one.
 * @param {Function|undefined} ack
 * @param {object} response
 */
function respond(ack, response) {
  if (typeof ack === 'function') {
    try {
      ack(response);
    } catch (e) {
      /* client-side ack errors must never crash the server */
    }
  }
}

/**
 * Build the public classroom state sent to the teacher dashboard.
 * @param {object} classroom Classroom document (lean or hydrated).
 * @param {object[]} students Student documents.
 */
function serializeClassroom(classroom, students) {
  return {
    roomPin: classroom.roomPin,
    teacherName: classroom.teacherName,
    isActive: classroom.isActive,
    isPaused: classroom.isPaused,
    isMuted: classroom.isMuted,
    createdAt: classroom.createdAt,
    communityProgress: classroom.communityProgress,
    communityGoal: COMMUNITY_TOKEN_GOAL,
    students: students.map(serializeStudent)
  };
}

/**
 * Public student representation (never leaks internal socket ids).
 * @param {object} s Student document.
 */
function serializeStudent(s) {
  return {
    studentId: s.studentId,
    name: s.name,
    currentLevel: s.currentLevel,
    cleanTokensBalance: s.cleanTokensBalance,
    unlockedCosmetics: s.unlockedCosmetics || [],
    joinedAt: s.joinedAt,
    lastSeen: s.lastSeen,
    isConnected: s.isConnected
  };
}

/**
 * Push the full roster of a classroom to its teacher room.
 * @param {import('socket.io').Server} io
 * @param {string} roomPin
 */
async function broadcastRoster(io, roomPin) {
  const students = await Student.find({ roomPin }).sort({ name: 1 }).lean();
  io.to(teacherRoomOf(roomPin)).emit('roster:updated', {
    roomPin,
    students: students.map(serializeStudent),
    connectedCount: students.filter((s) => s.isConnected).length,
    totalCount: students.length
  });
}

/**
 * Wrap an async event handler: validates that the payload is an object, extracts
 * the optional ack callback and converts thrown errors into ack failures + logs.
 *
 * @param {import('socket.io').Socket} socket
 * @param {string} event
 * @param {(payload: object, ack?: Function) => Promise<void>} handler
 * @returns {Function}
 */
function guarded(socket, event, handler) {
  return async (...args) => {
    const ack = typeof args[args.length - 1] === 'function' ? args.pop() : undefined;
    const payload = args[0];
    if (!isPlainObject(payload)) {
      warn(socket, event, 'payload must be an object', payload);
      respond(ack, { ok: false, error: 'Invalid payload' });
      return;
    }
    try {
      await handler(payload, ack);
    } catch (err) {
      console.error(`[socket:error] ${event} from ${socket.id}:`, err);
      respond(ack, { ok: false, error: 'Server error' });
    }
  };
}

/**
 * Initialise all Socket.io handlers.
 * @param {import('socket.io').Server} io
 */
function initSocketHandler(io) {
  io.on('connection', (socket) => {
    /** Per-connection identity, populated by teacher:join / student:join. */
    socket.data.role = null;
    socket.data.roomPin = null;
    socket.data.studentId = null;

    /**
     * Ensure the sender is the teacher of `roomPin` on this socket.
     * @returns {string|null} Normalised PIN when authorised.
     */
    const requireTeacher = (event, payload, ack) => {
      const roomPin = normalizePin(payload.roomPin);
      if (!roomPin) {
        warn(socket, event, 'invalid roomPin', payload);
        respond(ack, { ok: false, error: 'Invalid room PIN' });
        return null;
      }
      if (socket.data.role !== 'teacher' || socket.data.roomPin !== roomPin) {
        warn(socket, event, 'sender is not the teacher of this room', payload);
        respond(ack, { ok: false, error: 'Not authorised for this room' });
        return null;
      }
      return roomPin;
    };

    /**
     * Ensure the sender is the joined student `studentId` in `roomPin`.
     * @returns {{roomPin: string, studentId: string}|null}
     */
    const requireStudent = (event, payload, ack) => {
      const roomPin = normalizePin(payload.roomPin);
      const studentId = normalizeStudentId(payload.studentId);
      if (!roomPin || !studentId) {
        warn(socket, event, 'invalid roomPin or studentId', payload);
        respond(ack, { ok: false, error: 'Invalid room PIN or student id' });
        return null;
      }
      if (socket.data.role !== 'student' || socket.data.roomPin !== roomPin || socket.data.studentId !== studentId) {
        warn(socket, event, 'sender has not joined as this student', payload);
        respond(ack, { ok: false, error: 'Join the room before sending game events' });
        return null;
      }
      return { roomPin, studentId };
    };

    /**
     * Toggle a classroom flag and broadcast the change to the whole room.
     * @param {string} event Incoming event name.
     * @param {object} update Mongo $set update.
     * @param {string} outEvent Event broadcast to the room.
     */
    const teacherToggle = (event, update, outEvent) =>
      guarded(socket, event, async (payload, ack) => {
        const roomPin = requireTeacher(event, payload, ack);
        if (!roomPin) return;
        const classroom = await Classroom.findOneAndUpdate({ roomPin }, { $set: update }, { new: true }).lean();
        if (!classroom) {
          respond(ack, { ok: false, error: 'Classroom not found' });
          return;
        }
        io.to(roomOf(roomPin)).emit(outEvent, { roomPin, at: new Date().toISOString() });
        respond(ack, { ok: true, isPaused: classroom.isPaused, isMuted: classroom.isMuted });
      });

    // ------------------------------------------------------------------ //
    // Teacher events
    // ------------------------------------------------------------------ //

    /** teacher:join { roomPin, teacherName } — create/claim a classroom and join its rooms. */
    socket.on(
      'teacher:join',
      guarded(socket, 'teacher:join', async (payload, ack) => {
        const roomPin = normalizePin(payload.roomPin);
        const teacherName = sanitizeName(payload.teacherName) || 'Teacher';
        if (!roomPin) {
          warn(socket, 'teacher:join', 'invalid roomPin', payload);
          respond(ack, { ok: false, error: 'Room PIN must be exactly 4 digits' });
          return;
        }

        // Upsert: the room may have been created via POST /api/auth/create-room or not at all.
        const classroom = await Classroom.findOneAndUpdate(
          { roomPin },
          {
            $set: { teacherSocketId: socket.id, teacherName, isActive: true },
            $setOnInsert: { roomPin, createdAt: new Date() }
          },
          { new: true, upsert: true, setDefaultsOnInsert: true }
        ).lean();

        // A socket can only belong to one classroom at a time.
        if (socket.data.roomPin && socket.data.roomPin !== roomPin) {
          socket.leave(roomOf(socket.data.roomPin));
          socket.leave(teacherRoomOf(socket.data.roomPin));
        }
        socket.data.role = 'teacher';
        socket.data.roomPin = roomPin;
        socket.join([roomOf(roomPin), teacherRoomOf(roomPin)]);

        const students = await Student.find({ roomPin }).sort({ name: 1 }).lean();
        const state = serializeClassroom(classroom, students);
        socket.emit('teacher:joined', state);
        respond(ack, { ok: true, classroom: state });
        console.info(`[socket] teacher "${teacherName}" joined room ${roomPin}`);
      })
    );

    // ------------------------------------------------------------------ //
    // Observer events (read-only projector HUD)
    // ------------------------------------------------------------------ //

    /**
     * observer:join { roomPin, observerName? } — read-only join for the projector HUD.
     * Unlike teacher:join it never creates or modifies the classroom: it does not touch
     * teacherSocketId / isActive, so closing the HUD tab cannot mark the room inactive or
     * fire teacher:disconnected. Observers receive every room broadcast plus the teacher-room
     * feed (roster:updated, STUCK_ALERT, student:flagged) but requireTeacher/requireStudent
     * reject any control or game event they send.
     */
    socket.on(
      'observer:join',
      guarded(socket, 'observer:join', async (payload, ack) => {
        const roomPin = normalizePin(payload.roomPin);
        const observerName = sanitizeName(payload.observerName) || 'Projector HUD';
        if (!roomPin) {
          warn(socket, 'observer:join', 'invalid roomPin', payload);
          respond(ack, { ok: false, error: 'Room PIN must be exactly 4 digits' });
          socket.emit('observer:join_error', { error: 'Room PIN must be exactly 4 digits' });
          return;
        }

        const classroom = await Classroom.findOne({ roomPin }).lean();
        if (!classroom) {
          warn(socket, 'observer:join', 'room PIN does not exist', payload);
          respond(ack, { ok: false, error: 'Room not found. Open the teacher dashboard for this PIN first.' });
          socket.emit('observer:join_error', { error: 'Room not found. Open the teacher dashboard for this PIN first.' });
          return;
        }

        if (socket.data.roomPin && socket.data.roomPin !== roomPin) {
          socket.leave(roomOf(socket.data.roomPin));
          socket.leave(teacherRoomOf(socket.data.roomPin));
        }
        socket.data.role = 'observer';
        socket.data.roomPin = roomPin;
        socket.data.studentId = null;
        socket.join([roomOf(roomPin), teacherRoomOf(roomPin)]);

        const students = await Student.find({ roomPin }).sort({ name: 1 }).lean();
        const state = serializeClassroom(classroom, students);
        socket.emit('observer:joined', state);
        respond(ack, { ok: true, classroom: state });
        console.info(`[socket] observer "${observerName}" joined room ${roomPin}`);
      })
    );

    /**
     * client:heartbeat { sentAt? } — application-level ping used by the Unity NetworkManager to
     * measure round-trip latency and detect half-open connections. Any role (or none) may send it.
     */
    socket.on('client:heartbeat', (...args) => {
      const ack = typeof args[args.length - 1] === 'function' ? args.pop() : undefined;
      const payload = isPlainObject(args[0]) ? args[0] : {};
      respond(ack, {
        ok: true,
        serverTime: new Date().toISOString(),
        sentAt: typeof payload.sentAt === 'number' ? payload.sentAt : null
      });
    });

    socket.on('teacher:pause_class', teacherToggle('teacher:pause_class', { isPaused: true }, 'class:paused'));
    socket.on('teacher:resume_class', teacherToggle('teacher:resume_class', { isPaused: false }, 'class:resumed'));
    socket.on('teacher:mute_audio', teacherToggle('teacher:mute_audio', { isMuted: true }, 'audio:mute'));
    socket.on('teacher:unmute_audio', teacherToggle('teacher:unmute_audio', { isMuted: false }, 'audio:unmute'));

    /** teacher:flag_student { roomPin, studentId } — mark a student for attention on all teacher dashboards. */
    socket.on(
      'teacher:flag_student',
      guarded(socket, 'teacher:flag_student', async (payload, ack) => {
        const roomPin = requireTeacher('teacher:flag_student', payload, ack);
        if (!roomPin) return;
        const studentId = normalizeStudentId(payload.studentId);
        if (!studentId) {
          warn(socket, 'teacher:flag_student', 'invalid studentId', payload);
          respond(ack, { ok: false, error: 'Invalid student id' });
          return;
        }
        const student = await Student.findOne({ studentId, roomPin }).lean();
        if (!student) {
          respond(ack, { ok: false, error: 'Student not found in this room' });
          return;
        }
        io.to(teacherRoomOf(roomPin)).emit('student:flagged', {
          studentId,
          studentName: student.name,
          flaggedAt: new Date().toISOString()
        });
        respond(ack, { ok: true });
      })
    );

    // ------------------------------------------------------------------ //
    // Student events
    // ------------------------------------------------------------------ //

    /**
     * student:join { roomPin, studentName, studentId? }
     * Joining again with the same name (or a previously issued studentId) resumes the
     * existing record instead of creating a duplicate.
     */
    socket.on(
      'student:join',
      guarded(socket, 'student:join', async (payload, ack) => {
        const roomPin = normalizePin(payload.roomPin);
        const studentName = sanitizeName(payload.studentName);
        if (!roomPin || !studentName) {
          warn(socket, 'student:join', 'invalid roomPin or studentName', payload);
          respond(ack, { ok: false, error: 'Enter a 4-digit room PIN and your name' });
          socket.emit('student:join_error', { error: 'Enter a 4-digit room PIN and your name' });
          return;
        }

        const classroom = await Classroom.findOne({ roomPin }).lean();
        if (!classroom) {
          warn(socket, 'student:join', 'room PIN does not exist', payload);
          respond(ack, { ok: false, error: 'Room not found. Check the PIN with your teacher.' });
          socket.emit('student:join_error', { error: 'Room not found. Check the PIN with your teacher.' });
          return;
        }

        const now = new Date();
        const liveUpdate = { isConnected: true, lastSeen: now, currentSocketId: socket.id };

        // 1) Resume by explicit studentId (reconnect from the same device).
        let student = null;
        const requestedId = normalizeStudentId(payload.studentId);
        if (requestedId) {
          student = await Student.findOneAndUpdate(
            { studentId: requestedId, roomPin },
            { $set: liveUpdate },
            { new: true }
          ).lean();
        }

        // 2) Resume by name or create (atomic upsert on the unique {roomPin, name} index).
        if (!student) {
          try {
            student = await Student.findOneAndUpdate(
              { roomPin, name: studentName },
              { $set: liveUpdate, $setOnInsert: { roomPin, name: studentName, joinedAt: now } },
              { new: true, upsert: true, setDefaultsOnInsert: true }
            ).lean();
          } catch (err) {
            // Two simultaneous joins with the same name raced the upsert; the other one won.
            if (err.code !== 11000) throw err;
            student = await Student.findOneAndUpdate(
              { roomPin, name: studentName },
              { $set: liveUpdate },
              { new: true }
            ).lean();
          }
        }

        await Classroom.updateOne({ roomPin }, { $addToSet: { studentIds: student.studentId } });

        if (socket.data.roomPin && socket.data.roomPin !== roomPin) socket.leave(roomOf(socket.data.roomPin));
        if (socket.data.studentId && socket.data.studentId !== student.studentId) {
          socket.leave(studentRoomOf(socket.data.studentId));
        }
        socket.data.role = 'student';
        socket.data.roomPin = roomPin;
        socket.data.studentId = student.studentId;
        socket.join([roomOf(roomPin), studentRoomOf(student.studentId)]);

        const joined = {
          studentId: student.studentId,
          studentName: student.name,
          currentLevel: student.currentLevel,
          cleanTokensBalance: student.cleanTokensBalance,
          unlockedCosmetics: student.unlockedCosmetics || [],
          isPaused: classroom.isPaused,
          isMuted: classroom.isMuted,
          communityProgress: classroom.communityProgress,
          communityGoal: COMMUNITY_TOKEN_GOAL
        };
        socket.emit('student:joined', joined);
        respond(ack, { ok: true, ...joined });
        await broadcastRoster(io, roomPin);
        console.info(`[socket] student "${student.name}" (${student.studentId}) joined room ${roomPin}`);
      })
    );

    /** student:token_earned { roomPin, studentId, amount, levelId } */
    socket.on(
      'student:token_earned',
      guarded(socket, 'student:token_earned', async (payload, ack) => {
        const ids = requireStudent('student:token_earned', payload, ack);
        if (!ids) return;
        const amount = normalizePositiveNumber(payload.amount, MAX_TOKENS_PER_EVENT, { integer: true });
        const levelId = normalizeLevelId(payload.levelId);
        if (amount === null || levelId === null) {
          warn(socket, 'student:token_earned', `amount must be an integer 1..${MAX_TOKENS_PER_EVENT} and levelId 1..${MAX_LEVEL}`, payload);
          respond(ack, { ok: false, error: 'Invalid amount or levelId' });
          return;
        }

        const student = await Student.findOneAndUpdate(
          { studentId: ids.studentId, roomPin: ids.roomPin },
          { $inc: { cleanTokensBalance: amount }, $set: { lastSeen: new Date() } },
          { new: true }
        ).lean();
        if (!student) {
          respond(ack, { ok: false, error: 'Student not found' });
          return;
        }
        const classroom = await Classroom.findOneAndUpdate(
          { roomPin: ids.roomPin },
          { $inc: { communityProgress: amount } },
          { new: true }
        ).lean();

        io.to(roomOf(ids.roomPin)).emit('TOKEN_EARNED', {
          studentId: student.studentId,
          studentName: student.name,
          amount,
          levelId,
          newBalance: student.cleanTokensBalance,
          communityTokens: classroom ? classroom.communityProgress : null
        });
        if (classroom) {
          const percent = Math.min(100, Math.round((classroom.communityProgress / COMMUNITY_TOKEN_GOAL) * 100));
          io.to(roomOf(ids.roomPin)).emit('COMMUNITY_PROGRESS', {
            tokens: classroom.communityProgress,
            goal: COMMUNITY_TOKEN_GOAL,
            percentage: percent,
            // Legacy aliases kept for existing dashboard consumers.
            communityProgress: classroom.communityProgress,
            communityGoal: COMMUNITY_TOKEN_GOAL,
            percent
          });
        }
        respond(ack, { ok: true, newBalance: student.cleanTokensBalance });
      })
    );

    /** student:level_completed { roomPin, studentId, levelId, telemetry } */
    socket.on(
      'student:level_completed',
      guarded(socket, 'student:level_completed', async (payload, ack) => {
        const ids = requireStudent('student:level_completed', payload, ack);
        if (!ids) return;
        const levelId = normalizeLevelId(payload.levelId);
        if (levelId === null) {
          warn(socket, 'student:level_completed', `levelId must be an integer 1..${MAX_LEVEL}`, payload);
          respond(ack, { ok: false, error: 'Invalid levelId' });
          return;
        }
        const telemetry = normalizeTelemetry(payload.telemetry);
        if (!telemetry.ok) {
          warn(socket, 'student:level_completed', telemetry.error, payload.telemetry);
          respond(ack, { ok: false, error: telemetry.error });
          return;
        }

        const student = await Student.findOne({ studentId: ids.studentId, roomPin: ids.roomPin }).lean();
        if (!student) {
          respond(ack, { ok: false, error: 'Student not found' });
          return;
        }

        const record = await LevelTelemetry.create({ studentId: ids.studentId, levelId, ...telemetry.value });

        // Unlock the next level (never move backwards on replays; cap at MAX_LEVEL).
        const nextLevel = Math.min(MAX_LEVEL, levelId + 1);
        const update = { $max: { currentLevel: nextLevel }, $set: { lastSeen: new Date() } };

        // Award the level cosmetic only the first time it is earned.
        const cosmetic = LEVEL_COSMETIC_REWARDS[levelId];
        const newCosmetic = cosmetic && !(student.unlockedCosmetics || []).includes(cosmetic) ? cosmetic : null;
        if (newCosmetic) update.$addToSet = { unlockedCosmetics: newCosmetic };

        const updated = await Student.findOneAndUpdate({ studentId: ids.studentId }, update, { new: true }).lean();

        io.to(roomOf(ids.roomPin)).emit('LEVEL_COMPLETED', {
          studentId: ids.studentId,
          studentName: updated.name,
          levelId,
          cosmeticUnlocked: newCosmetic,
          currentLevel: updated.currentLevel,
          gameCompleted: levelId === MAX_LEVEL
        });
        if (newCosmetic) {
          io.to(studentRoomOf(ids.studentId)).emit('COSMETIC_UNLOCKED', { studentId: ids.studentId, cosmeticId: newCosmetic, levelId });
        }
        respond(ack, {
          ok: true,
          telemetryId: record._id.toString(),
          currentLevel: updated.currentLevel,
          cosmeticUnlocked: newCosmetic
        });
        await broadcastRoster(io, ids.roomPin);
      })
    );

    /** student:energy_contributed { roomPin, studentId, kw, levelId } */
    socket.on(
      'student:energy_contributed',
      guarded(socket, 'student:energy_contributed', async (payload, ack) => {
        const ids = requireStudent('student:energy_contributed', payload, ack);
        if (!ids) return;
        const kw = normalizePositiveNumber(payload.kw, MAX_KW_PER_EVENT);
        const levelId = payload.levelId === undefined ? null : normalizeLevelId(payload.levelId);
        if (kw === null || (payload.levelId !== undefined && levelId === null)) {
          warn(socket, 'student:energy_contributed', `kw must be a number in (0, ${MAX_KW_PER_EVENT}]`, payload);
          respond(ack, { ok: false, error: 'Invalid kw or levelId' });
          return;
        }
        const contributor = await Student.findOne({ studentId: ids.studentId }, { name: 1 }).lean();
        io.to(roomOf(ids.roomPin)).emit('ENERGY_CONTRIBUTED', {
          studentId: ids.studentId,
          studentName: contributor ? contributor.name : undefined,
          kw,
          levelId
        });
        respond(ack, { ok: true });
      })
    );

    /** student:stuck_alert { roomPin, studentId, levelId, retriesCount } — teacher-only alert. */
    socket.on(
      'student:stuck_alert',
      guarded(socket, 'student:stuck_alert', async (payload, ack) => {
        const ids = requireStudent('student:stuck_alert', payload, ack);
        if (!ids) return;
        const levelId = normalizeLevelId(payload.levelId);
        const retriesCount = normalizeNonNegativeInt(payload.retriesCount);
        if (levelId === null || retriesCount === null) {
          warn(socket, 'student:stuck_alert', 'invalid levelId or retriesCount', payload);
          respond(ack, { ok: false, error: 'Invalid levelId or retriesCount' });
          return;
        }
        const student = await Student.findOne({ studentId: ids.studentId }).lean();
        const timestamp = new Date().toISOString();
        io.to(teacherRoomOf(ids.roomPin)).emit('STUCK_ALERT', {
          studentId: ids.studentId,
          studentName: student ? student.name : undefined,
          levelId,
          retriesCount,
          timestamp,
          at: timestamp // legacy alias read by the teacher dashboard
        });
        respond(ack, { ok: true });
      })
    );

    // ------------------------------------------------------------------ //
    // Disconnect
    // ------------------------------------------------------------------ //

    socket.on('disconnect', async (reason) => {
      const { role, roomPin, studentId } = socket.data;
      // On server shutdown the DB is closing; live flags are reset at next startup instead.
      if (reason === 'server shutting down') return;
      try {
        if (role === 'student' && studentId) {
          // Only mark offline if this socket is still the student's latest connection
          // (a reconnect in another tab must not be overwritten by the old tab closing).
          const res = await Student.updateOne(
            { studentId, currentSocketId: socket.id },
            { $set: { isConnected: false, lastSeen: new Date() } }
          );
          if (res.modifiedCount > 0) await broadcastRoster(io, roomPin);
        } else if (role === 'teacher' && roomPin) {
          // Preserve all data; just mark the classroom inactive if this was the active teacher socket.
          const res = await Classroom.updateOne(
            { roomPin, teacherSocketId: socket.id },
            { $set: { isActive: false }, $unset: { teacherSocketId: '' } }
          );
          if (res.modifiedCount > 0) {
            io.to(roomOf(roomPin)).emit('teacher:disconnected', { roomPin, at: new Date().toISOString() });
          }
        }
        // role === 'observer': nothing to clean up — observers never own classroom state.
        console.info(`[socket] ${role || 'anonymous'} ${socket.id} disconnected (${reason})`);
      } catch (err) {
        console.error(`[socket:error] disconnect cleanup for ${socket.id}:`, err);
      }
    });
  });
}

module.exports = initSocketHandler;
module.exports.serializeClassroom = serializeClassroom;
module.exports.serializeStudent = serializeStudent;
