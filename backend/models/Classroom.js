/**
 * @file Mongoose model for a teacher-hosted classroom session identified by a 4-digit PIN.
 */

'use strict';

const mongoose = require('mongoose');

const ClassroomSchema = new mongoose.Schema(
  {
    // Exactly 4 digits ("0420" is valid, so it is stored as a string).
    roomPin: {
      type: String,
      unique: true,
      required: true,
      minlength: 4,
      maxlength: 4,
      match: /^\d{4}$/
    },
    teacherSocketId: String,
    teacherName: { type: String, trim: true, maxlength: 40 },
    isActive: { type: Boolean, default: true },
    isPaused: { type: Boolean, default: false },
    isMuted: { type: Boolean, default: false },
    createdAt: { type: Date, default: Date.now },
    studentIds: { type: [String], default: [] },
    /** Cumulative Clean Tokens earned by the whole class (community "power the town" meter). */
    communityProgress: { type: Number, default: 0, min: 0 }
  },
  { versionKey: false }
);

module.exports = mongoose.models.Classroom || mongoose.model('Classroom', ClassroomSchema);
