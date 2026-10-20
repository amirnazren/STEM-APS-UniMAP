/**
 * @file Report routes: PDF certificates and LMS-ready CSV exports. Mounted at /api/reports.
 */

'use strict';

const express = require('express');
const PDFDocument = require('pdfkit');
const { Parser } = require('json2csv');
const Student = require('../models/Student');
const Classroom = require('../models/Classroom');
const LevelTelemetry = require('../models/LevelTelemetry');
const { STEM_COMPETENCIES, COMPETENCY_ACHIEVED_THRESHOLD, MAX_LEVEL } = require('../config/gameConfig');
const { normalizePin, normalizeStudentId } = require('../utils/validators');
const { averageCompetencies } = require('../utils/competency');

const router = express.Router();

const GREEN = '#2E7D32';
const LIGHT_GREEN = '#A5D6A7';
const GOLD = '#FFD700';
const DARK_TEXT = '#1B1B1B';

/**
 * Make a string safe for use inside a Content-Disposition filename.
 * @param {string} s
 * @returns {string}
 */
const safeFilename = (s) => String(s).replace(/[^a-z0-9_-]+/gi, '_').replace(/^_+|_+$/g, '') || 'student';

/**
 * Draw the Eco-Bot logo placeholder: a green circle containing a simple robot face and leaf.
 * @param {PDFKit.PDFDocument} doc
 * @param {number} cx Centre x.
 * @param {number} cy Centre y.
 * @param {number} r  Radius.
 */
function drawLogoPlaceholder(doc, cx, cy, r) {
  doc.save();
  doc.circle(cx, cy, r).lineWidth(3).fillAndStroke(LIGHT_GREEN, GREEN);
  // Robot head
  doc.roundedRect(cx - r * 0.45, cy - r * 0.35, r * 0.9, r * 0.6, 6).fill(GREEN);
  doc.circle(cx - r * 0.2, cy - r * 0.08, r * 0.09).fill('#FFFFFF');
  doc.circle(cx + r * 0.2, cy - r * 0.08, r * 0.09).fill('#FFFFFF');
  // Antenna with a leaf
  doc.moveTo(cx, cy - r * 0.35).lineTo(cx, cy - r * 0.6).lineWidth(2).stroke(GREEN);
  doc.ellipse(cx + r * 0.1, cy - r * 0.65, r * 0.13, r * 0.07).fill(GREEN);
  doc.fillColor(GREEN).font('Helvetica-Bold').fontSize(9)
    .text('ECO-BOT', cx - r, cy + r * 0.38, { width: r * 2, align: 'center' });
  doc.restore();
}

/**
 * GET /api/reports/certificate/:studentId
 * Streams a landscape A4 "Master Systems Engineer" certificate PDF.
 * The certificate is only issued once the student has completed Level 10 (Eco-City Master
 * Mission), i.e. a LevelTelemetry record with levelId === MAX_LEVEL exists.
 * @returns 200 application/pdf | 400 | 404 | 409 { error, code: 'LEVEL_10_INCOMPLETE', ... }
 */
router.get('/certificate/:studentId', async (req, res, next) => {
  try {
    const studentId = normalizeStudentId(req.params.studentId);
    if (!studentId) return res.status(400).json({ error: 'Invalid student id' });

    const student = await Student.findOne({ studentId }).lean();
    if (!student) return res.status(404).json({ error: 'Student not found' });

    const records = await LevelTelemetry.find({ studentId }, { stemCompetencyScores: 1, levelId: 1 }).lean();
    const averages = averageCompetencies(records);
    const levelsCompleted = new Set(records.map((r) => r.levelId)).size;
    if (!records.some((r) => r.levelId === MAX_LEVEL)) {
      return res.status(409).json({
        error: `Certificate unavailable: ${student.name} has not completed Level ${MAX_LEVEL} (Eco-City Master Mission) yet.`,
        code: 'LEVEL_10_INCOMPLETE',
        requiredLevel: MAX_LEVEL,
        levelsCompleted,
        currentLevel: student.currentLevel
      });
    }
    const achieved = STEM_COMPETENCIES.filter(
      (c) => averages[c.key] !== null && averages[c.key] >= COMPETENCY_ACHIEVED_THRESHOLD
    );

    const doc = new PDFDocument({
      size: 'A4',
      layout: 'landscape',
      margin: 0,
      info: { Title: `Master Systems Engineer - ${student.name}`, Author: 'Eco-Bot Builders' }
    });

    res.status(200);
    res.setHeader('Content-Type', 'application/pdf');
    res.setHeader(
      'Content-Disposition',
      `inline; filename="EcoBot_Certificate_${safeFilename(student.name)}.pdf"`
    );
    doc.on('error', (err) => {
      console.error('[reports] PDF generation error:', err);
      res.destroy(err);
    });
    doc.pipe(res);

    const W = doc.page.width; // 841.89
    const H = doc.page.height; // 595.28

    // Borders: thick green outer frame + thin gold inner frame.
    doc.rect(20, 20, W - 40, H - 40).lineWidth(10).stroke(GREEN);
    doc.rect(38, 38, W - 76, H - 76).lineWidth(2).stroke(GOLD);

    drawLogoPlaceholder(doc, W / 2, 105, 45);

    // Title in gold with a subtle green outline so it stays readable on white.
    doc.font('Helvetica-Bold').fontSize(40).fillColor(GOLD).strokeColor(GREEN).lineWidth(0.8)
      .text('MASTER SYSTEMS ENGINEER', 0, 165, { width: W, align: 'center', fill: true, stroke: true });

    doc.font('Helvetica').fontSize(15).fillColor(DARK_TEXT)
      .text('This certificate is proudly presented to', 0, 225, { width: W, align: 'center' });

    doc.font('Helvetica-Bold').fontSize(34).fillColor(GREEN)
      .text(student.name, 60, 252, { width: W - 120, align: 'center' });
    doc.moveTo(W / 2 - 200, 298).lineTo(W / 2 + 200, 298).lineWidth(1).stroke(GOLD);

    doc.font('Helvetica').fontSize(13).fillColor(DARK_TEXT)
      .text(
        `for helping power a cleaner world by completing ${levelsCompleted} of ${MAX_LEVEL} Eco-Bot Builders missions ` +
          `and earning ${student.cleanTokensBalance} Clean Tokens.`,
        90, 310, { width: W - 180, align: 'center' }
      );

    // Competencies list
    doc.font('Helvetica-Bold').fontSize(14).fillColor(GREEN)
      .text('STEM Competencies Achieved', 0, 355, { width: W, align: 'center' });
    doc.font('Helvetica').fontSize(12).fillColor(DARK_TEXT);
    if (achieved.length === 0) {
      doc.text('Keep exploring! Every mission builds new engineering skills.', 0, 378, { width: W, align: 'center' });
    } else {
      let y = 378;
      for (const c of achieved) {
        const pct = Math.round(averages[c.key] * 100);
        doc.text(`\u2022  ${c.label} (${pct}%)`, 0, y, { width: W, align: 'center' });
        y += 18;
      }
    }

    // Footer: date + signature line
    const dateStr = new Date().toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
    doc.font('Helvetica').fontSize(11).fillColor(DARK_TEXT);
    doc.moveTo(110, 500).lineTo(310, 500).lineWidth(1).stroke(DARK_TEXT);
    doc.text(dateStr, 110, 505, { width: 200, align: 'center' });
    doc.text('Date', 110, 520, { width: 200, align: 'center' });
    doc.moveTo(W - 310, 500).lineTo(W - 110, 500).stroke(DARK_TEXT);
    doc.text('Teacher', W - 310, 520, { width: 200, align: 'center' });
    doc.font('Helvetica-Oblique').fontSize(9).fillColor(GREEN)
      .text('Eco-Bot Builders \u2022 Learning to code for a greener planet', 0, H - 62, { width: W, align: 'center' });

    doc.end();
    return undefined;
  } catch (err) {
    return next(err);
  }
});

/**
 * Format a score (0-1) for CSV output; empty string when missing.
 * @param {number|undefined|null} v
 */
const fmtScore = (v) => (typeof v === 'number' && Number.isFinite(v) ? v.toFixed(2) : '');

/**
 * GET /api/reports/export-csv/:roomPin
 * One row per completed level per student (students without completions get one row
 * with empty scores). Competency headers carry their CSTA K-12 CS Standards alignment.
 * @returns 200 text/csv | 400 | 404
 */
router.get('/export-csv/:roomPin', async (req, res, next) => {
  try {
    const roomPin = normalizePin(req.params.roomPin);
    if (!roomPin) return res.status(400).json({ error: 'Room PIN must be exactly 4 digits' });

    const exists = await Classroom.exists({ roomPin });
    if (!exists) return res.status(404).json({ error: 'Room not found' });

    const students = await Student.find({ roomPin }).sort({ name: 1 }).lean();
    const ids = students.map((s) => s.studentId);
    const records = ids.length
      ? await LevelTelemetry.find({ studentId: { $in: ids } }, { blockProgramSnapshot: 0 })
        .sort({ levelId: 1, completedAt: 1 })
        .lean()
      : [];

    const byStudent = new Map(ids.map((id) => [id, []]));
    for (const r of records) byStudent.get(r.studentId).push(r);

    const rows = [];
    for (const s of students) {
      const recs = byStudent.get(s.studentId);
      const base = { StudentName: s.name, CleanTokens: s.cleanTokensBalance };
      if (recs.length === 0) {
        rows.push({ ...base, Level: '', CompletedAt: '' });
        continue;
      }
      for (const r of recs) {
        const row = { ...base, Level: r.levelId, CompletedAt: r.completedAt ? new Date(r.completedAt).toISOString() : '' };
        for (const c of STEM_COMPETENCIES) row[c.csvHeader] = fmtScore(r.stemCompetencyScores && r.stemCompetencyScores[c.key]);
        rows.push(row);
      }
    }

    const fields = [
      { label: 'StudentName', value: 'StudentName' },
      { label: 'Level', value: 'Level' },
      ...STEM_COMPETENCIES.map((c) => ({ label: `${c.csvHeader} [${c.csta}]`, value: c.csvHeader, default: '' })),
      { label: 'CleanTokens', value: 'CleanTokens' },
      { label: 'CompletedAt', value: 'CompletedAt' }
    ];

    // Guard against CSV/formula injection from student-chosen names (=, +, -, @ prefixes).
    for (const row of rows) {
      if (/^[=+\-@\t\r]/.test(row.StudentName)) row.StudentName = `'${row.StudentName}`;
    }

    const csv = new Parser({ fields, withBOM: true }).parse(rows);
    const date = new Date().toISOString().slice(0, 10);
    res.setHeader('Content-Type', 'text/csv; charset=utf-8');
    res.setHeader('Content-Disposition', `attachment; filename="EcoBot_Class_${roomPin}_${date}.csv"`);
    return res.status(200).send(csv);
  } catch (err) {
    return next(err);
  }
});

module.exports = router;
