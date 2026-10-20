/**
 * @file Student roster & progress routes. Mounted at /api/students.
 */

'use strict';

const express = require('express');
const Student = require('../models/Student');
const Classroom = require('../models/Classroom');
const LevelTelemetry = require('../models/LevelTelemetry');
const { MAX_LEVEL, STEM_COMPETENCIES, COMPETENCY_ACHIEVED_THRESHOLD } = require('../config/gameConfig');
const { normalizePin, normalizeStudentId, normalizeCosmeticId } = require('../utils/validators');
const { averageCompetencies } = require('../utils/competency');
const { serializeStudent } = require('../sockets/socketHandler');

const router = express.Router();

/**
 * GET /api/students/:studentId/progress
 * Full progress summary: profile, per-level bests, competency averages and totals.
 * (Declared before /:roomPin for readability; paths do not overlap.)
 * @returns 200 summary | 400 | 404
 */
router.get('/:studentId/progress', async (req, res, next) => {
  try {
    const studentId = normalizeStudentId(req.params.studentId);
    if (!studentId) return res.status(400).json({ error: 'Invalid student id' });

    const student = await Student.findOne({ studentId }).lean();
    if (!student) return res.status(404).json({ error: 'Student not found' });

    const records = await LevelTelemetry.find({ studentId }, { blockProgramSnapshot: 0 })
      .sort({ levelId: 1, completedAt: 1 })
      .lean();

    // Collapse multiple attempts into one summary per level.
    const levelsMap = new Map();
    for (const r of records) {
      const lvl = levelsMap.get(r.levelId) || {
        levelId: r.levelId,
        completions: 0,
        totalRetries: 0,
        bestTimeSeconds: null,
        subroutinesUsed: false,
        firstCompletedAt: r.completedAt,
        lastCompletedAt: r.completedAt,
        records: []
      };
      lvl.completions += 1;
      lvl.totalRetries += r.retriesCount || 0;
      if (typeof r.timeSpentSeconds === 'number') {
        lvl.bestTimeSeconds = lvl.bestTimeSeconds === null ? r.timeSpentSeconds : Math.min(lvl.bestTimeSeconds, r.timeSpentSeconds);
      }
      lvl.subroutinesUsed = lvl.subroutinesUsed || !!r.subroutinesUsed;
      lvl.lastCompletedAt = r.completedAt;
      lvl.records.push(r);
      levelsMap.set(r.levelId, lvl);
    }
    const levels = [...levelsMap.values()].map(({ records: recs, ...rest }) => ({
      ...rest,
      competencyAverages: averageCompetencies(recs)
    }));

    const competencyAverages = averageCompetencies(records);
    const achievedCompetencies = STEM_COMPETENCIES.filter(
      (c) => competencyAverages[c.key] !== null && competencyAverages[c.key] >= COMPETENCY_ACHIEVED_THRESHOLD
    ).map((c) => c.label);

    return res.status(200).json({
      student: serializeStudent(student),
      roomPin: student.roomPin,
      levelsCompleted: levels.length,
      totalLevels: MAX_LEVEL,
      percentComplete: Math.round((levels.length / MAX_LEVEL) * 100),
      totalTimeSeconds: records.reduce((s, r) => s + (r.timeSpentSeconds || 0), 0),
      totalRetries: records.reduce((s, r) => s + (r.retriesCount || 0), 0),
      competencyAverages,
      achievedCompetencies,
      levels
    });
  } catch (err) {
    return next(err);
  }
});

/**
 * PATCH /api/students/:studentId/cosmetic
 * Body: { cosmeticId }
 * Adds a cosmetic to the student's collection (idempotent).
 * @returns 200 { studentId, unlockedCosmetics, added } | 400 | 404
 */
router.patch('/:studentId/cosmetic', async (req, res, next) => {
  try {
    const studentId = normalizeStudentId(req.params.studentId);
    if (!studentId) return res.status(400).json({ error: 'Invalid student id' });
    const cosmeticId = normalizeCosmeticId(req.body && req.body.cosmeticId);
    if (!cosmeticId) {
      return res.status(400).json({ error: 'cosmeticId must be 1-40 characters (letters, numbers, _ or -)' });
    }

    const before = await Student.findOne({ studentId }, { unlockedCosmetics: 1 }).lean();
    if (!before) return res.status(404).json({ error: 'Student not found' });

    const updated = await Student.findOneAndUpdate(
      { studentId },
      { $addToSet: { unlockedCosmetics: cosmeticId } },
      { new: true }
    ).lean();

    return res.status(200).json({
      studentId,
      unlockedCosmetics: updated.unlockedCosmetics,
      added: !(before.unlockedCosmetics || []).includes(cosmeticId)
    });
  } catch (err) {
    return next(err);
  }
});

/**
 * GET /api/students/:roomPin
 * All students in a classroom with their current state.
 * @returns 200 { roomPin, count, students } | 400 | 404
 */
router.get('/:roomPin', async (req, res, next) => {
  try {
    const roomPin = normalizePin(req.params.roomPin);
    if (!roomPin) return res.status(400).json({ error: 'Room PIN must be exactly 4 digits' });

    const exists = await Classroom.exists({ roomPin });
    if (!exists) return res.status(404).json({ error: 'Room not found' });

    const students = await Student.find({ roomPin }).sort({ name: 1 }).lean();
    return res.status(200).json({ roomPin, count: students.length, students: students.map(serializeStudent) });
  } catch (err) {
    return next(err);
  }
});

module.exports = router;
