/**
 * @file Helpers for aggregating STEM competency scores across telemetry records.
 */

'use strict';

const { STEM_COMPETENCIES } = require('../config/gameConfig');

/**
 * Round to 3 decimal places (keeps JSON / CSV output tidy).
 * @param {number} n
 * @returns {number}
 */
const round3 = (n) => Math.round(n * 1000) / 1000;

/**
 * Average each STEM competency over a list of telemetry records. Records that
 * lack a score for a given competency are ignored for that competency only.
 *
 * @param {Array<{stemCompetencyScores?: object}>} records
 * @returns {Record<string, number|null>} e.g. { computationalThinking: 0.82, ..., persistence: null }
 */
function averageCompetencies(records) {
  const result = {};
  for (const { key } of STEM_COMPETENCIES) {
    let sum = 0;
    let count = 0;
    for (const r of records || []) {
      const v = r && r.stemCompetencyScores ? r.stemCompetencyScores[key] : undefined;
      if (typeof v === 'number' && Number.isFinite(v)) {
        sum += v;
        count += 1;
      }
    }
    result[key] = count > 0 ? round3(sum / count) : null;
  }
  return result;
}

module.exports = { averageCompetencies, round3 };
