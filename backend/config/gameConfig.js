/**
 * @file Central game constants shared by sockets and REST routes.
 * Keeping these in one place guarantees that the server, dashboard exports
 * and certificate generator all agree on level counts, rewards and limits.
 */

'use strict';

/** Total number of playable levels in Eco-Bot Builders. */
const MAX_LEVEL = 10;

/** Upper bound on Clean Tokens a single `student:token_earned` event may award (anti-abuse). */
const MAX_TOKENS_PER_EVENT = 100;

/** Upper bound on kW a single `student:energy_contributed` event may report. */
const MAX_KW_PER_EVENT = 1000;

/** Clean Tokens the whole class must earn together to "power the town" (100 % community progress). */
const COMMUNITY_TOKEN_GOAL = 1000;

/** Maximum number of blocks accepted in a block-program snapshot. */
const MAX_SNAPSHOT_BLOCKS = 500;

/** Average competency score (0–1) at or above which a competency is considered "achieved". */
const COMPETENCY_ACHIEVED_THRESHOLD = 0.6;

/**
 * Cosmetic unlocked when a level is completed for the first time.
 * Keys are level ids; values are cosmetic slugs understood by the Unity client.
 * @type {Record<number, string>}
 */
const LEVEL_COSMETIC_REWARDS = Object.freeze({
  1: 'leaf_antenna',
  2: 'solar_visor',
  3: 'water_wheel_wheels',
  4: 'wind_turbine_hat',
  5: 'recycled_armor',
  6: 'hydro_jetpack',
  7: 'bridge_builder_boots',
  8: 'compost_cape',
  9: 'aurora_paint',
  10: 'golden_gear_crown'
});

/**
 * The four STEM competencies tracked per level, with human-readable labels
 * and their alignment to the CSTA K-12 Computer Science Standards (Level 1B, ages 8-11).
 */
const STEM_COMPETENCIES = Object.freeze([
  {
    key: 'computationalThinking',
    label: 'Computational Thinking',
    csvHeader: 'ComputationalThinking',
    csta: 'CSTA 1B-AP-08, 1B-AP-10 (Algorithms & Programming: algorithms, sequences, loops, conditionals)'
  },
  {
    key: 'systemicEngineering',
    label: 'Systemic Engineering',
    csvHeader: 'SystemicEngineering',
    csta: 'CSTA 1B-AP-11, 1B-CS-02 (Decomposition; Computing Systems: hardware/software models)'
  },
  {
    key: 'mathModeling',
    label: 'Mathematical Modeling',
    csvHeader: 'MathModeling',
    csta: 'CSTA 1B-AP-09, 1B-DA-07 (Variables; Data & Analysis: cause-and-effect models)'
  },
  {
    key: 'persistence',
    label: 'Persistence & Debugging',
    csvHeader: 'Persistence',
    csta: 'CSTA 1B-AP-13, 1B-AP-15 (Iterative development; testing and debugging)'
  }
]);

module.exports = {
  MAX_LEVEL,
  MAX_TOKENS_PER_EVENT,
  MAX_KW_PER_EVENT,
  COMMUNITY_TOKEN_GOAL,
  MAX_SNAPSHOT_BLOCKS,
  COMPETENCY_ACHIEVED_THRESHOLD,
  LEVEL_COSMETIC_REWARDS,
  STEM_COMPETENCIES
};
