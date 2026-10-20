/**
 * @file Classroom authentication routes: room creation (PIN + QR) and PIN validation.
 * Mounted at /api/auth.
 */

'use strict';

const express = require('express');
const QRCode = require('qrcode');
const Classroom = require('../models/Classroom');
const Student = require('../models/Student');
const { generateUniquePin } = require('../utils/pin');
const { normalizePin, sanitizeName } = require('../utils/validators');
const { serializeClassroom } = require('../sockets/socketHandler');

const router = express.Router();

/**
 * Build the URL students open to join a room (encoded in the QR code).
 * @param {string} roomPin
 * @returns {string}
 */
function buildJoinUrl(roomPin) {
  const base = process.env.CLIENT_JOIN_URL || 'http://localhost:5173/join';
  const url = new URL(base);
  url.searchParams.set('pin', roomPin);
  return url.toString();
}

/**
 * POST /api/auth/create-room
 * Body (optional): { teacherName }
 * Creates a classroom with a unique 4-digit PIN.
 * @returns 201 { roomPin, joinUrl, qrCodeDataUrl, classroom }
 */
router.post('/create-room', async (req, res, next) => {
  try {
    const teacherName = sanitizeName(req.body && req.body.teacherName) || 'Teacher';

    // Retry on the (unlikely) race where another request grabs the same PIN between check and insert.
    let classroom = null;
    for (let attempt = 0; attempt < 3 && !classroom; attempt += 1) {
      const roomPin = await generateUniquePin();
      try {
        classroom = await Classroom.create({ roomPin, teacherName });
      } catch (err) {
        if (err.code !== 11000) throw err;
      }
    }
    if (!classroom) {
      return res.status(503).json({ error: 'Could not allocate a room PIN, please retry' });
    }

    const joinUrl = buildJoinUrl(classroom.roomPin);
    const qrCodeDataUrl = await QRCode.toDataURL(joinUrl, {
      errorCorrectionLevel: 'M',
      margin: 2,
      width: 320,
      color: { dark: '#2E7D32', light: '#FFFFFF' }
    });

    return res.status(201).json({
      roomPin: classroom.roomPin,
      joinUrl,
      qrCodeDataUrl,
      classroom: serializeClassroom(classroom.toObject(), [])
    });
  } catch (err) {
    return next(err);
  }
});

/**
 * POST /api/auth/validate-pin
 * Body: { roomPin }
 * @returns 200 { valid, teacherName?, studentCount?, reason? }  | 400 on malformed PIN
 */
router.post('/validate-pin', async (req, res, next) => {
  try {
    const roomPin = normalizePin(req.body && req.body.roomPin);
    if (!roomPin) {
      return res.status(400).json({ valid: false, reason: 'Room PIN must be exactly 4 digits' });
    }
    const classroom = await Classroom.findOne({ roomPin }).lean();
    if (!classroom) {
      return res.status(200).json({ valid: false, reason: 'Room not found' });
    }
    if (!classroom.isActive) {
      return res.status(200).json({ valid: false, reason: 'This room is not active right now' });
    }
    const studentCount = await Student.countDocuments({ roomPin });
    return res.status(200).json({ valid: true, teacherName: classroom.teacherName, studentCount });
  } catch (err) {
    return next(err);
  }
});

/**
 * GET /api/auth/room/:roomPin
 * @returns 200 full classroom state (incl. student roster) | 400 | 404
 */
router.get('/room/:roomPin', async (req, res, next) => {
  try {
    const roomPin = normalizePin(req.params.roomPin);
    if (!roomPin) return res.status(400).json({ error: 'Room PIN must be exactly 4 digits' });

    const classroom = await Classroom.findOne({ roomPin }).lean();
    if (!classroom) return res.status(404).json({ error: 'Room not found' });

    const students = await Student.find({ roomPin }).sort({ name: 1 }).lean();
    return res.status(200).json(serializeClassroom(classroom, students));
  } catch (err) {
    return next(err);
  }
});

module.exports = router;
