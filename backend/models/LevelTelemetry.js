/**
 * @file Mongoose model storing per-level learning telemetry for a student.
 * One document is written per level completion (replays create additional documents).
 */

'use strict';

const mongoose = require('mongoose');

/** Score sub-document: every competency is a normalised value between 0 and 1. */
const scoreField = { type: Number, min: 0, max: 1 };

const LevelTelemetrySchema = new mongoose.Schema(
  {
    studentId: { type: String, required: true },
    levelId: { type: Number, required: true, min: 1 },
    retriesCount: { type: Number, default: 0, min: 0 },
    timeSpentSeconds: { type: Number, min: 0 },
    subroutinesUsed: { type: Boolean, default: false },
    completedAt: { type: Date, default: Date.now },
    stemCompetencyScores: {
      computationalThinking: scoreField,
      systemicEngineering: scoreField,
      mathModeling: scoreField,
      persistence: scoreField
    },
    blockProgramSnapshot: [mongoose.Schema.Types.Mixed]
  },
  { versionKey: false }
);

LevelTelemetrySchema.index({ studentId: 1, levelId: 1 });

module.exports = mongoose.models.LevelTelemetry || mongoose.model('LevelTelemetry', LevelTelemetrySchema);
