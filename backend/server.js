/**
 * @file Entry point for the Eco-Bot Builders backend.
 * Express REST API + Socket.io real-time server backed by MongoDB.
 */

'use strict';

require('dotenv').config();

const http = require('http');
const express = require('express');
const cors = require('cors');
const mongoose = require('mongoose');
const { Server } = require('socket.io');

const authRoutes = require('./routes/auth');
const studentRoutes = require('./routes/students');
const telemetryRoutes = require('./routes/telemetry');
const reportRoutes = require('./routes/reports');
const initSocketHandler = require('./sockets/socketHandler');
const { notFound, errorHandler } = require('./middleware/errorHandler');

const PORT = Number(process.env.PORT) || 3001;
const MONGO_URI = process.env.MONGO_URI || 'mongodb://127.0.0.1:27017/eco-bot-builders';

/** Resolve CORS origin(s) from env: "*" (default) or a comma-separated allow-list. */
const corsOrigin = !process.env.CORS_ORIGIN || process.env.CORS_ORIGIN === '*'
  ? '*'
  : process.env.CORS_ORIGIN.split(',').map((o) => o.trim()).filter(Boolean);

const app = express();
app.disable('x-powered-by');
app.use(cors({ origin: corsOrigin }));
app.use(express.json({ limit: '1mb' }));

/**
 * GET /api/health — liveness/readiness probe.
 * Returns 503 while MongoDB is not connected so load balancers can hold traffic.
 */
app.get('/api/health', (req, res) => {
  const states = ['disconnected', 'connected', 'connecting', 'disconnecting'];
  const dbState = states[mongoose.connection.readyState] || 'unknown';
  const healthy = mongoose.connection.readyState === 1;
  res.status(healthy ? 200 : 503).json({
    status: healthy ? 'ok' : 'degraded',
    db: dbState,
    uptimeSeconds: Math.round(process.uptime()),
    timestamp: new Date().toISOString()
  });
});

app.use('/api/auth', authRoutes);
app.use('/api/students', studentRoutes);
app.use('/api/telemetry', telemetryRoutes);
app.use('/api/reports', reportRoutes);

app.use(notFound);
app.use(errorHandler);

const server = http.createServer(app);
const io = new Server(server, {
  cors: { origin: corsOrigin, methods: ['GET', 'POST'] },
  maxHttpBufferSize: 1e6, // 1 MB — block program snapshots stay well below this
  pingInterval: 25000,
  pingTimeout: 20000
});
initSocketHandler(io);

/**
 * Connect to MongoDB, then start listening.
 * On startup, any classrooms/students left "live" by a previous crash are reset,
 * since no sockets survive a process restart.
 */
async function start() {
  mongoose.set('strictQuery', true);
  mongoose.connection.on('error', (err) => console.error('[db] MongoDB error:', err.message));
  mongoose.connection.on('disconnected', () => console.warn('[db] MongoDB disconnected'));

  await mongoose.connect(MONGO_URI, { serverSelectionTimeoutMS: 10000 });
  console.info('[db] MongoDB connected');

  const Student = require('./models/Student');
  const Classroom = require('./models/Classroom');
  await Promise.all([
    Student.updateMany({ isConnected: true }, { $set: { isConnected: false } }),
    Classroom.updateMany({ teacherSocketId: { $exists: true } }, { $set: { isActive: false }, $unset: { teacherSocketId: '' } })
  ]);

  await new Promise((resolve) => server.listen(PORT, resolve));
  console.info(`[server] Eco-Bot Builders backend listening on port ${PORT}`);
}

/**
 * Graceful shutdown: stop accepting connections, close sockets and DB.
 * @param {string} signal
 */
async function shutdown(signal) {
  console.info(`[server] ${signal} received, shutting down...`);
  const forceExit = setTimeout(() => process.exit(1), 10000);
  forceExit.unref();
  try {
    io.close();
    await new Promise((resolve) => server.close(() => resolve()));
    await mongoose.connection.close();
    process.exit(0);
  } catch (err) {
    console.error('[server] Error during shutdown:', err);
    process.exit(1);
  }
}

process.on('SIGINT', () => shutdown('SIGINT'));
process.on('SIGTERM', () => shutdown('SIGTERM'));
process.on('unhandledRejection', (reason) => console.error('[server] Unhandled promise rejection:', reason));

if (require.main === module) {
  start().catch((err) => {
    console.error('[server] Failed to start:', err);
    process.exit(1);
  });
}

module.exports = { app, server, io, start };
