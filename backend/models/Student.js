/**
 * @file Mongoose model for a student (player) in an Eco-Bot Builders classroom.
 */

'use strict';

const mongoose = require('mongoose');
const { v4: uuidv4 } = require('uuid');

/**
 * @typedef {object} StudentDoc
 * @property {string}   studentId          Server-generated UUID (public id used by clients).
 * @property {string}   name               Display name chosen at join time.
 * @property {string}   roomPin            4-digit PIN of the classroom the student belongs to.
 * @property {number}   currentLevel       Highest level unlocked (1..10).
 * @property {number}   cleanTokensBalance Clean Tokens currency balance.
 * @property {string[]} unlockedCosmetics  Cosmetic slugs owned by the student.
 * @property {Date}     joinedAt           First join time.
 * @property {Date}     lastSeen           Last activity / disconnect time.
 * @property {boolean}  isConnected        Whether a live socket is attached.
 * @property {string}   currentSocketId    Socket id of the most recent connection (internal).
 */
const StudentSchema = new mongoose.Schema(
  {
    studentId: { type: String, unique: true, default: uuidv4 },
    name: { type: String, required: true, trim: true, maxlength: 40 },
    roomPin: { type: String, index: true, match: /^\d{4}$/ },
    currentLevel: { type: Number, default: 1, min: 1 },
    cleanTokensBalance: { type: Number, default: 0, min: 0 },
    unlockedCosmetics: { type: [String], default: [] },
    joinedAt: { type: Date, default: Date.now },
    lastSeen: Date,
    isConnected: { type: Boolean, default: false },
    // Internal: lets us ignore stale `disconnect` events when a student reconnects in a new tab.
    currentSocketId: { type: String, select: false }
  },
  { versionKey: false }
);

// A name is unique inside a classroom so that re-joining with the same name resumes progress.
StudentSchema.index({ roomPin: 1, name: 1 }, { unique: true });

module.exports = mongoose.models.Student || mongoose.model('Student', StudentSchema);
