/**
 * @file Dependency-free smoke tests (node:test). Mongoose models are replaced with in-memory
 * mocks via require.cache so no MongoDB instance is needed. Run with `npm test`.
 */

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

// ---------------------------------------------------------------------------
// In-memory model mocks
// ---------------------------------------------------------------------------
const db = { classrooms: [], students: [], telemetry: [], classroomUpdates: [] };

/** Wrap a value in a mongoose-like query object supporting .lean() / .sort(). */
const query = (fn) => {
  const q = {
    sort: () => q,
    lean: () => Promise.resolve(fn()),
    then: (res, rej) => Promise.resolve(fn()).then(res, rej)
  };
  return q;
};
const matches = (doc, filter) => Object.entries(filter).every(([k, v]) => doc[k] === v);

const ClassroomMock = {
  findOne: (f) => query(() => db.classrooms.find((c) => matches(c, f)) || null),
  findOneAndUpdate: (f, update) =>
    query(() => {
      db.classroomUpdates.push({ filter: f, update });
      return db.classrooms.find((c) => matches(c, f)) || null;
    }),
  updateOne: (f, update) => {
    db.classroomUpdates.push({ filter: f, update });
    const doc = db.classrooms.find((c) => matches(c, f));
    return Promise.resolve({ modifiedCount: doc ? 1 : 0 });
  }
};
const StudentMock = {
  find: (f) => query(() => db.students.filter((s) => matches(s, f))),
  findOne: (f) => query(() => db.students.find((s) => matches(s, f)) || null),
  findOneAndUpdate: (f) => query(() => db.students.find((s) => matches(s, f)) || null),
  updateOne: () => Promise.resolve({ modifiedCount: 0 })
};
const LevelTelemetryMock = {
  find: (f) => query(() => db.telemetry.filter((t) => matches(t, f)))
};

const mockModule = (rel, exportsValue) => {
  const file = require.resolve(path.join(__dirname, '..', rel));
  require.cache[file] = { id: file, filename: file, loaded: true, exports: exportsValue };
};
mockModule('models/Classroom', ClassroomMock);
mockModule('models/Student', StudentMock);
mockModule('models/LevelTelemetry', LevelTelemetryMock);

const initSocketHandler = require('../sockets/socketHandler');
const reportsRouter = require('../routes/reports');

// ---------------------------------------------------------------------------
// Fake Socket.io server / socket
// ---------------------------------------------------------------------------
function createFakeIo() {
  const broadcasts = [];
  let onConnection = null;
  const io = {
    on: (evt, cb) => {
      if (evt === 'connection') onConnection = cb;
    },
    to: (room) => ({ emit: (event, payload) => broadcasts.push({ room, event, payload }) })
  };
  initSocketHandler(io);
  const connect = (id) => {
    const handlers = {};
    const socket = {
      id,
      data: {},
      rooms: new Set(),
      emitted: [],
      on: (evt, cb) => {
        handlers[evt] = cb;
      },
      emit: (event, payload) => socket.emitted.push({ event, payload }),
      join: (rooms) => [].concat(rooms).forEach((r) => socket.rooms.add(r)),
      leave: (r) => socket.rooms.delete(r),
      /** Invoke a registered handler and resolve with the ack payload. */
      call: (evt, payload) =>
        new Promise((resolve) => {
          const maybe = handlers[evt](payload, resolve);
          Promise.resolve(maybe).then(() => setTimeout(() => resolve(undefined), 20));
        }),
      disconnect: (reason = 'transport close') => handlers.disconnect(reason)
    };
    onConnection(socket);
    return socket;
  };
  return { io, broadcasts, connect };
}

const PIN = '4821';
const STUDENT_ID = '3f1c2b7e-9a4d-4c1e-8b2a-6d5e4f3a2b1c';

test.beforeEach(() => {
  db.classrooms = [
    { roomPin: PIN, teacherName: 'Ms Rivera', teacherSocketId: 'teacher-1', isActive: true, isPaused: false, isMuted: false, communityProgress: 120, studentIds: [] }
  ];
  db.students = [
    { studentId: STUDENT_ID, roomPin: PIN, name: 'Ava', currentLevel: 10, cleanTokensBalance: 30, unlockedCosmetics: [], isConnected: true }
  ];
  db.telemetry = [];
  db.classroomUpdates = [];
});

// ---------------------------------------------------------------------------
// Observer role
// ---------------------------------------------------------------------------
test('observer:join joins room + teacher feed without modifying the classroom', async () => {
  const { connect } = createFakeIo();
  const hud = connect('hud-1');
  const ack = await hud.call('observer:join', { roomPin: PIN });

  assert.equal(ack.ok, true);
  assert.equal(ack.classroom.roomPin, PIN);
  assert.equal(hud.data.role, 'observer');
  assert.ok(hud.rooms.has(`room:${PIN}`));
  assert.ok(hud.rooms.has(`teacher:${PIN}`));
  assert.ok(hud.emitted.some((e) => e.event === 'observer:joined'));
  assert.equal(db.classroomUpdates.length, 0, 'observer must not write to the classroom');
  assert.equal(db.classrooms[0].teacherSocketId, 'teacher-1');
});

test('observer disconnect does not mark the classroom inactive or emit teacher:disconnected', async () => {
  const { connect, broadcasts } = createFakeIo();
  const hud = connect('hud-2');
  await hud.call('observer:join', { roomPin: PIN });
  await hud.disconnect();

  assert.equal(db.classroomUpdates.length, 0);
  assert.equal(broadcasts.filter((b) => b.event === 'teacher:disconnected').length, 0);
});

test('observer:join rejects unknown rooms and invalid PINs', async () => {
  const { connect } = createFakeIo();
  const hud = connect('hud-3');
  const missing = await hud.call('observer:join', { roomPin: '9999' });
  assert.equal(missing.ok, false);
  assert.ok(hud.emitted.some((e) => e.event === 'observer:join_error'));
  const invalid = await hud.call('observer:join', { roomPin: '12' });
  assert.equal(invalid.ok, false);
});

test('observers cannot send teacher control events', async () => {
  const { connect, broadcasts } = createFakeIo();
  const hud = connect('hud-4');
  await hud.call('observer:join', { roomPin: PIN });
  const ack = await hud.call('teacher:pause_class', { roomPin: PIN });
  assert.equal(ack.ok, false);
  assert.equal(broadcasts.filter((b) => b.event === 'class:paused').length, 0);
});

test('teacher disconnect still marks the classroom inactive (regression)', async () => {
  const { connect, broadcasts } = createFakeIo();
  const teacher = connect('teacher-1');
  teacher.data.role = 'teacher';
  teacher.data.roomPin = PIN;
  await teacher.disconnect();
  assert.equal(db.classroomUpdates.length, 1);
  assert.equal(broadcasts.filter((b) => b.event === 'teacher:disconnected').length, 1);
});

test('client:heartbeat acks with server time and echoes sentAt', async () => {
  const { connect } = createFakeIo();
  const s = connect('unity-1');
  const ack = await s.call('client:heartbeat', { sentAt: 1234 });
  assert.equal(ack.ok, true);
  assert.equal(ack.sentAt, 1234);
  assert.ok(!Number.isNaN(Date.parse(ack.serverTime)));
});

// ---------------------------------------------------------------------------
// Certificate gating
// ---------------------------------------------------------------------------
function certificateHandler() {
  const layer = reportsRouter.stack.find((l) => l.route && l.route.path === '/certificate/:studentId');
  return layer.route.stack[0].handle;
}

function fakeRes() {
  const res = {
    statusCode: 200,
    headers: {},
    body: undefined,
    status(code) {
      res.statusCode = code;
      return res;
    },
    json(body) {
      res.body = body;
      return res;
    },
    setHeader(k, v) {
      res.headers[k.toLowerCase()] = v;
    }
  };
  return res;
}

test('certificate returns 409 LEVEL_10_INCOMPLETE when Level 10 has not been completed', async () => {
  db.telemetry = [1, 2, 3].map((levelId) => ({ studentId: STUDENT_ID, levelId, stemCompetencyScores: {} }));
  const res = fakeRes();
  await certificateHandler()({ params: { studentId: STUDENT_ID } }, res, (err) => { throw err; });
  assert.equal(res.statusCode, 409);
  assert.equal(res.body.code, 'LEVEL_10_INCOMPLETE');
  assert.equal(res.body.levelsCompleted, 3);
  assert.match(res.body.error, /Level 10/);
});

test('certificate returns 404 for unknown students and 400 for invalid ids', async () => {
  const res404 = fakeRes();
  await certificateHandler()({ params: { studentId: '00000000-0000-4000-8000-000000000000' } }, res404, () => {});
  assert.equal(res404.statusCode, 404);
  const res400 = fakeRes();
  await certificateHandler()({ params: { studentId: 'nope' } }, res400, () => {});
  assert.equal(res400.statusCode, 400);
});

test('certificate passes the Level 10 gate when Level 10 telemetry exists', async () => {
  db.telemetry = Array.from({ length: 10 }, (_, i) => ({ studentId: STUDENT_ID, levelId: i + 1, stemCompetencyScores: {} }));
  const res = fakeRes();
  // Not a real HTTP response, so piping the PDF throws; we only assert the gate let us through.
  await certificateHandler()({ params: { studentId: STUDENT_ID } }, res, () => {});
  assert.notEqual(res.statusCode, 409);
  assert.equal(res.headers['content-type'], 'application/pdf');
});
