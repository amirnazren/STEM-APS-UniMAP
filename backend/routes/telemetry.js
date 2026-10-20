/**
 * @file Learning-telemetry routes. Mounted at /api/telemetry.
 */

'use strict';

const express = require('express');
const LevelTelemetry = require('../models/LevelTelemetry');
const Student = require('../models/Student');
const Classroom = require('../models/Classroom');
const { STEM_COMPETENCIES, MAX_LEVEL } = require('../config/gameConfig');
const {
  isPlainObject,
  normalizePin,
  normalizeStudentId,
  normalizeLevelId,
  normalizeTelemetry
} = require('../utils/validators');
const { averageCompetencies, round3 } = require('../utils/competency');

const router = express.Router();

/**
 * GET /api/telemetry/student/:studentId
 * Query: ?includeSnapshots=true to include block program snapshots (omitted by default — they can be large).
 * @returns 200 { studentId, count, records } | 400 | 404
 */
router.get('/student/:studentId', async (req, res, next) => {
  try {
    const studentId = normalizeStudentId(req.params.studentId);
    if (!studentId) return res.status(400).json({ error: 'Invalid student id' });

    const exists = await Student.exists({ studentId });
    if (!exists) return res.status(404).json({ error: 'Student not found' });

    const projection = req.query.includeSnapshots === 'true' ? {} : { blockProgramSnapshot: 0 };
    const records = await LevelTelemetry.find({ studentId }, projection).sort({ levelId: 1, completedAt: 1 }).lean();
    return res.status(200).json({ studentId, count: records.length, records });
  } catch (err) {
    return next(err);
  }
});

/**
 * GET /api/telemetry/class/:roomPin
 * Aggregates telemetry across the classroom: class-wide STEM competency averages,
 * per-student averages (for radar charts) and per-level statistics.
 * @returns 200 aggregate | 400 | 404
 */
router.get('/class/:roomPin', async (req, res, next) => {
  try {
    const roomPin = normalizePin(req.params.roomPin);
    if (!roomPin) return res.status(400).json({ error: 'Room PIN must be exactly 4 digits' });

    const classroom = await Classroom.findOne({ roomPin }).lean();
    if (!classroom) return res.status(404).json({ error: 'Room not found' });

    const students = await Student.find({ roomPin }).lean();
    const studentIds = students.map((s) => s.studentId);
    const records = studentIds.length
      ? await LevelTelemetry.find({ studentId: { $in: studentIds } }, { blockProgramSnapshot: 0 }).lean()
      : [];

    // Group records by student and by level.
    const byStudent = new Map(studentIds.map((id) => [id, []]));
    const byLevel = new Map();
    for (const r of records) {
      byStudent.get(r.studentId).push(r);
      if (!byLevel.has(r.levelId)) byLevel.set(r.levelId, []);
      byLevel.get(r.levelId).push(r);
    }

    const studentSummaries = students.map((s) => {
      const recs = byStudent.get(s.studentId) || [];
      return {
        studentId: s.studentId,
        name: s.name,
        currentLevel: s.currentLevel,
        cleanTokensBalance: s.cleanTokensBalance,
        isConnected: s.isConnected,
        levelsCompleted: new Set(recs.map((r) => r.levelId)).size,
        totalRetries: recs.reduce((sum, r) => sum + (r.retriesCount || 0), 0),
        competencyAverages: averageCompetencies(recs)
      };
    });

    const levelStats = [...byLevel.entries()]
      .sort(([a], [b]) => a - b)
      .map(([levelId, recs]) => {
        const timed = recs.filter((r) => typeof r.timeSpentSeconds === 'number');
        return {
          levelId,
          completions: recs.length,
          uniqueStudents: new Set(recs.map((r) => r.studentId)).size,
          avgRetries: round3(recs.reduce((s, r) => s + (r.retriesCount || 0), 0) / recs.length),
          avgTimeSeconds: timed.length ? round3(timed.reduce((s, r) => s + r.timeSpentSeconds, 0) / timed.length) : null,
          subroutineUsageRate: round3(recs.filter((r) => r.subroutinesUsed).length / recs.length),
          competencyAverages: averageCompetencies(recs)
        };
      });

    return res.status(200).json({
      roomPin,
      studentCount: students.length,
      recordCount: records.length,
      competencies: STEM_COMPETENCIES.map(({ key, label, csta }) => ({ key, label, csta })),
      classAverages: averageCompetencies(records),
      students: studentSummaries,
      levels: levelStats
    });
  } catch (err) {
    return next(err);
  }
});

/**
 * POST /api/telemetry/record
 * Body: { studentId, levelId, retriesCount?, timeSpentSeconds?, subroutinesUsed?,
 *         completedAt?, stemCompetencyScores?, blockProgramSnapshot? }
 * @returns 201 saved record | 400 | 404
 */
router.post('/record', async (req, res, next) => {
  try {
    if (!isPlainObject(req.body)) return res.status(400).json({ error: 'JSON body required' });
    const studentId = normalizeStudentId(req.body.studentId);
    const levelId = normalizeLevelId(req.body.levelId);
    if (!studentId) return res.status(400).json({ error: 'Invalid student id' });
    if (levelId === null) return res.status(400).json({ error: `levelId must be an integer between 1 and ${MAX_LEVEL}` });

    const telemetry = normalizeTelemetry(req.body);
    if (!telemetry.ok) return res.status(400).json({ error: telemetry.error });

    const exists = await Student.exists({ studentId });
    if (!exists) return res.status(404).json({ error: 'Student not found' });

    const record = await LevelTelemetry.create({ studentId, levelId, ...telemetry.value });
    return res.status(201).json(record.toObject());
  } catch (err) {
    return next(err);
  }
});

module.exports = router;
