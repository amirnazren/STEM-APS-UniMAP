/**
 * @file Unique 4-digit room PIN generation.
 */

'use strict';

const crypto = require('crypto');
const Classroom = require('../models/Classroom');

/**
 * Generate a random 4-digit PIN ("0000"–"9999") that is not currently used by
 * any *active* classroom. Inactive classrooms holding the PIN are not reused
 * either, because their historical data is preserved and keyed by PIN.
 *
 * @param {number} [maxAttempts=50] Attempts before giving up.
 * @returns {Promise<string>} Unused PIN.
 * @throws {Error} When no free PIN could be found (PIN space exhausted).
 */
async function generateUniquePin(maxAttempts = 50) {
  for (let i = 0; i < maxAttempts; i += 1) {
    const pin = String(crypto.randomInt(0, 10000)).padStart(4, '0');
    // eslint-disable-next-line no-await-in-loop
    const exists = await Classroom.exists({ roomPin: pin });
    if (!exists) return pin;
  }
  const err = new Error('Unable to allocate a free room PIN. Please try again later.');
  err.status = 503;
  throw err;
}

module.exports = { generateUniquePin };
