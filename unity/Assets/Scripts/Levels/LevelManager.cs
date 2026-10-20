// =============================================================================
// LevelManager.cs — Eco-Bot Builders
// =============================================================================
// Orchestrates all 10 Sky-Isle levels: configures the grid from the central
// level-configs schema, drives the block-parser and grid-environment systems,
// evaluates win conditions, records telemetry and notifies the NetworkManager
// of each event.
//
// Dependencies (sibling scripts in the same Unity project):
//   Core/BlockParserEngine.cs      — visual block execution engine
//   Core/GridEnvironmentManager.cs — 2.5D isometric grid + EcoBot
//   Core/DayNightController.cs     — solar/wind/battery simulation
//   UI/HintSystem.cs               — tiered scaffolded hint system
//   Network/NetworkManager.cs      — Socket.io connection to Node.js backend
//   Physics/PhysicsSimulator.cs    — bridge and hydraulic physics
//
// This file does NOT re-declare any type from the above scripts. Telemetry is
// sent as Network.LevelTelemetryData (StemCompetencyScores + BlockSnapshot list
// built by BlockProgramSerializer) so the payload matches
// schemas/websocket-payloads.json.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EcoBotBuilders.Core;
using EcoBotBuilders.Network;
using EcoBotBuilders.UI;
using UnityEngine;
using UnityEngine.Events;
using PhysicsSimulator = EcoBotBuilders.Physics.PhysicsSimulator;

namespace EcoBotBuilders.Levels
{
    // -------------------------------------------------------------------------
    // Level configuration — mirrors schemas/level-configs.json
    // -------------------------------------------------------------------------

    /// <summary>
    /// Every quantifiable success metric supported by the win-condition evaluator.
    /// Names match the <c>successMetric</c> strings in schemas/level-configs.json.
    /// </summary>
    public enum SuccessMetric
    {
        TRASH_CLEANED_PERCENTAGE,
        PANELS_CLEANED_PERCENTAGE,
        PLANTS_WATERED_PERCENTAGE,
        ENERGY_OUTPUT_KW,
        ITEMS_SORTED_ACCURACY,
        RESERVOIR_LEVEL_IN_RANGE,
        BRIDGE_LOAD_SUPPORTED,
        PLANTS_HEALTHY_AND_WATER_SAVED,
        UPTIME_PERCENTAGE,
        CITY_SUSTAINABILITY_SCORE
    }

    /// <summary>
    /// Full description of one playable level. Mirrors one object in
    /// <c>schemas/level-configs.json</c>.
    /// </summary>
    [Serializable]
    public class LevelConfig
    {
        [Header("Identity")]
        public int levelId;
        public string title;
        public string stemDomain;
        [TextArea(2, 6)]
        public string description;

        [Header("Curriculum")]
        public string[] requiredBlockTypes;
        public string[] cstaStandards;
        public string[] ngssPractices;

        [Header("Mechanics")]
        public int codeCapacityLimit = 10;
        public SuccessMetric successMetric;
        public float successThreshold = 1f;
        [Tooltip("Failed runs before a stuck alert is sent to the teacher dashboard.")]
        public int stuckRetryThreshold = 4;
        public string cosmeticReward;

        [Header("Environment")]
        public string timeOfDay = "DAY";
        public string weather = "CLEAR";

        [Header("Grid (gridSize.columns x gridSize.rows)")]
        public int gridWidth = 5;
        public int gridHeight = 5;
        public Vector2Int botStart = Vector2Int.zero;
        public Direction botFacing = Direction.East;

        [Header("Hints (tier1 / tier2 text from the schema)")]
        public string hintTier1;
        public string hintTier2;
        public string hintTier3ToolType;

        /// <summary>True when the level runs the day/night energy simulation.</summary>
        public bool UsesDayNightCycle => timeOfDay == "DAY_NIGHT_CYCLE";
    }

    /// <summary>
    /// Derives the four STEM competency axes (each 0–1) from raw play data and
    /// returns them in the network DTO shape expected by the backend.
    /// </summary>
    public static class StemCompetencyCalculator
    {
        /// <param name="retriesCount">Number of failed run attempts.</param>
        /// <param name="timeSpentSeconds">Real time elapsed this level.</param>
        /// <param name="subroutinesUsed">Whether any SUBROUTINE_CALL block is in the program.</param>
        /// <param name="blockCount">Total blocks in the final submitted program.</param>
        /// <param name="capacityLimit">Max blocks allowed this level.</param>
        public static StemCompetencyScores Compute(
            int retriesCount, float timeSpentSeconds,
            bool subroutinesUsed, int blockCount, int capacityLimit)
        {
            // Persistence: still finishing after several retries shows grit, but
            // first-time success is rewarded too (floor of 0.4).
            float persistenceScore = Mathf.Clamp01(1f - retriesCount / 5f);
            persistenceScore = Mathf.Max(0.4f, persistenceScore);

            // Computational thinking: subroutine bonus + block-efficiency bonus.
            float efficiencyRatio = capacityLimit > 0 ? 1f - (float)blockCount / capacityLimit : 0f;
            float ctScore = Mathf.Clamp01(
                0.5f * (subroutinesUsed ? 1f : 0f)
                + 0.5f * Mathf.Clamp01(efficiencyRatio + 0.25f));

            // Systemic engineering: faster than 2 min = 1.0, slower than 10 min = 0.0.
            float seScore = Mathf.Clamp01(Mathf.InverseLerp(600f, 120f, timeSpentSeconds));

            // Math modelling: blend of CT and SE.
            float mmScore = Mathf.Clamp01((ctScore + seScore) / 2f);

            return new StemCompetencyScores
            {
                computationalThinking = Round2(ctScore),
                systemicEngineering = Round2(seScore),
                mathModeling = Round2(mmScore),
                persistence = Round2(persistenceScore)
            };
        }

        private static float Round2(float value) => Mathf.Round(value * 100f) / 100f;
    }

    // -------------------------------------------------------------------------
    // LevelManager
    // -------------------------------------------------------------------------

    /// <summary>
    /// Central level orchestrator. Place exactly one instance in the scene alongside
    /// <see cref="BlockParserEngine"/>, <see cref="GridEnvironmentManager"/>,
    /// <see cref="DayNightController"/>, <see cref="HintSystem"/>,
    /// <see cref="NetworkManager"/> and <see cref="PhysicsSimulator"/>.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public const int MaxLevel = 10;

        [Header("Required component references")]
        [SerializeField] private BlockParserEngine blockParser;
        [SerializeField] private GridEnvironmentManager gridManager;
        [SerializeField] private DayNightController dayNightController;
        [SerializeField] private HintSystem hintSystem;
        [Tooltip("Optional; falls back to NetworkManager.Instance.")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private PhysicsSimulator physicsSimulator;

        [Header("Behaviour")]
        [Tooltip("Apply ACTION_MOVE / ACTION_TURN / ACTION_CLEAN / ACTION_WATER blocks to the grid as they execute.")]
        [SerializeField] private bool driveGridFromBlocks = true;
        [Tooltip("Moisture added to a Soil tile by one ACTION_WATER block.")]
        [SerializeField, Range(0.1f, 1f)] private float waterPerAction = 0.7f;
        [SerializeField] private int startLevelId = 1;
        [SerializeField] private float advanceDelaySeconds = 3f;

        [Header("Level catalogue (pre-populated at Awake from schemas/level-configs.json; overridable)")]
        [SerializeField] private LevelConfig[] levels;

        [Header("Events")]
        public UnityEvent<int> onLevelLoaded = new UnityEvent<int>();
        public UnityEvent<int> onLevelComplete = new UnityEvent<int>();
        public UnityEvent onLevelFailed = new UnityEvent();

        /// <summary>The config for the level currently being played.</summary>
        public LevelConfig CurrentLevel { get; private set; }

        /// <summary>Id of the level currently loaded (1-based, matching the schema).</summary>
        public int CurrentLevelId { get; private set; } = -1;

        /// <summary>Failed runs on the current level.</summary>
        public int RetriesCount => _retriesCount;

        /// <summary>True while the teacher has paused the class.</summary>
        public bool IsPaused => _paused;

        private int _retriesCount;
        private float _levelStartTime;
        private bool _levelActive;
        private bool _paused;
        private bool _stuckAlertSent;
        private NetworkManager _subscribedNetwork;

        // Accumulators for metrics fed by external controllers.
        private int _itemsSortedCorrect;
        private int _itemsSortedTotal;
        private float _uptimeOnlineSeconds;
        private float _uptimeOfflineSeconds;
        private int _initialTrashCount;
        private int _initialDirtyPanelCount;
        private int _waterActions;

        private NetworkManager Network => networkManager != null ? networkManager : NetworkManager.Instance;

        // ------------------------------------------------------------------
        // Unity lifecycle
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (levels == null || levels.Length == 0)
            {
                levels = BuildLevelCatalogue();
            }
        }

        private void Start()
        {
            if (blockParser != null)
            {
                blockParser.OnProgramFinished += HandleProgramFinished;
                blockParser.OnBlockHighlighted += HandleBlockHighlighted;
            }

            if (gridManager != null)
            {
                gridManager.OnTokenEarned += HandleTokenEarned;
                gridManager.OnLevelComplete += HandleGridObjectivesComplete;
            }

            SubscribeToNetwork();
            LoadLevel(Mathf.Clamp(startLevelId, 1, MaxLevel));
        }

        private void OnDestroy()
        {
            if (blockParser != null)
            {
                blockParser.OnProgramFinished -= HandleProgramFinished;
                blockParser.OnBlockHighlighted -= HandleBlockHighlighted;
            }

            if (gridManager != null)
            {
                gridManager.OnTokenEarned -= HandleTokenEarned;
                gridManager.OnLevelComplete -= HandleGridObjectivesComplete;
            }

            UnsubscribeFromNetwork();

            if (_paused)
            {
                Time.timeScale = 1f;
            }
        }

        private void SubscribeToNetwork()
        {
            NetworkManager network = Network;
            if (network == null || network == _subscribedNetwork)
            {
                return;
            }

            UnsubscribeFromNetwork();
            _subscribedNetwork = network;
            _subscribedNetwork.OnClassPaused += HandleClassPaused;
            _subscribedNetwork.OnClassResumed += HandleClassResumed;
            _subscribedNetwork.OnTeacherDisconnected += HandleTeacherDisconnected;
        }

        private void UnsubscribeFromNetwork()
        {
            if (_subscribedNetwork == null)
            {
                return;
            }

            _subscribedNetwork.OnClassPaused -= HandleClassPaused;
            _subscribedNetwork.OnClassResumed -= HandleClassResumed;
            _subscribedNetwork.OnTeacherDisconnected -= HandleTeacherDisconnected;
            _subscribedNetwork = null;
        }

        private void HandleClassPaused(RoomSignalData _) => PauseLevel();

        private void HandleClassResumed(RoomSignalData _) => ResumeLevel();

        private void HandleTeacherDisconnected(RoomSignalData _)
        {
            Debug.LogWarning("[LevelManager] Teacher disconnected; play continues offline.");
        }

        // ------------------------------------------------------------------
        // Level loading
        // ------------------------------------------------------------------

        /// <summary>
        /// Loads a level by id (1–10): resets runtime state and configures the grid,
        /// parser capacity, subroutines and day/night simulation.
        /// </summary>
        public void LoadLevel(int levelId)
        {
            LevelConfig cfg = GetConfig(levelId);
            if (cfg == null)
            {
                Debug.LogError($"[LevelManager] No config found for level {levelId}.");
                return;
            }

            StopAllCoroutines();
            _levelActive = false;
            CurrentLevel = cfg;
            CurrentLevelId = levelId;

            _retriesCount = 0;
            _stuckAlertSent = false;
            _levelStartTime = Time.realtimeSinceStartup;
            _itemsSortedCorrect = 0;
            _itemsSortedTotal = 0;
            _uptimeOnlineSeconds = 0f;
            _uptimeOfflineSeconds = 0f;
            _waterActions = 0;

            if (blockParser != null)
            {
                blockParser.StepThroughMode = false;
                blockParser.CodeCapacityLimit = cfg.codeCapacityLimit;
                blockParser.BlockProgram.Clear();
                blockParser.ResetSubroutines();
                if (levelId == MaxLevel)
                {
                    RegisterCapstoneSubroutines();
                }
            }

            if (gridManager != null)
            {
                gridManager.ConfigureGrid(cfg.gridWidth, cfg.gridHeight, cfg.botStart, cfg.botFacing);
                SeedLevelLayout(levelId);
                _initialTrashCount = gridManager.TileMap.Count(t => t != null && t.IsDirty && !t.HasSolarPanel);
                _initialDirtyPanelCount = gridManager.TileMap.Count(t => t != null && t.IsDirty && t.HasSolarPanel);
            }

            if (dayNightController != null)
            {
                bool energyLevel = cfg.UsesDayNightCycle ||
                                   cfg.successMetric == SuccessMetric.ENERGY_OUTPUT_KW ||
                                   cfg.successMetric == SuccessMetric.RESERVOIR_LEVEL_IN_RANGE;
                dayNightController.enabled = energyLevel;
                if (energyLevel)
                {
                    dayNightController.RefreshNow();
                }
            }

            if (hintSystem != null)
            {
                hintSystem.HideHint();
            }

            _levelActive = true;
            Debug.Log($"[LevelManager] Loaded Level {levelId}: {cfg.title} ({cfg.stemDomain})");
            onLevelLoaded.Invoke(levelId);
        }

        /// <summary>
        /// Places the objective tiles for grid-based levels. Layouts are deterministic so
        /// every student in a classroom gets the same puzzle.
        /// </summary>
        private void SeedLevelLayout(int levelId)
        {
            switch (levelId)
            {
                case 1: // 5x5: scattered trash
                    SetTiles(TileType.Trash, new Vector2Int(2, 0), new Vector2Int(4, 0), new Vector2Int(4, 2), new Vector2Int(2, 2));
                    break;
                case 2: // 7x3: dusty panels in the middle row
                    for (int x = 1; x <= 5; x++)
                    {
                        PlaceDirtyPanel(new Vector2Int(x, 1));
                    }
                    break;
                case 3: // 6x6: garden beds around a 4x4 square
                    for (int i = 1; i <= 4; i++)
                    {
                        SetTiles(TileType.Soil, new Vector2Int(i, 1), new Vector2Int(i, 4), new Vector2Int(1, i), new Vector2Int(4, i));
                    }
                    break;
                case 7: // 9x5: canyon gap the bridge must span
                    for (int y = 0; y < 5; y++)
                    {
                        if (y != 2)
                        {
                            SetTiles(TileType.Blocked, new Vector2Int(4, y));
                        }
                    }
                    break;
                case 8: // 6x6: mixed dry and already-moist plants
                    SeedSoil(new Vector2Int(1, 1), 0.2f);
                    SeedSoil(new Vector2Int(3, 1), 0.8f);
                    SeedSoil(new Vector2Int(1, 3), 0.8f);
                    SeedSoil(new Vector2Int(3, 3), 0.2f);
                    SeedSoil(new Vector2Int(4, 4), 0.2f);
                    SeedSoil(new Vector2Int(2, 4), 0.8f);
                    break;
                case 10: // 10x8: streets to clean and parks to water
                    SetTiles(TileType.Trash, new Vector2Int(2, 1), new Vector2Int(5, 1), new Vector2Int(8, 3), new Vector2Int(3, 6));
                    SeedSoil(new Vector2Int(6, 5), 0.2f);
                    SeedSoil(new Vector2Int(7, 5), 0.2f);
                    SeedSoil(new Vector2Int(1, 4), 0.8f);
                    break;
            }
        }

        private void SetTiles(TileType type, params Vector2Int[] positions)
        {
            foreach (Vector2Int position in positions)
            {
                gridManager.SetTileType(position, type);
            }
        }

        private void PlaceDirtyPanel(Vector2Int position)
        {
            if (gridManager.SetTileType(position, TileType.SolarPanel))
            {
                GridTile tile = FindTile(position);
                if (tile != null)
                {
                    tile.IsDirty = true;
                }
            }
        }

        private void SeedSoil(Vector2Int position, float moisture)
        {
            if (gridManager.SetTileType(position, TileType.Soil))
            {
                GridTile tile = FindTile(position);
                if (tile != null)
                {
                    tile.MoistureLevel = moisture;
                }
            }
        }

        private GridTile FindTile(Vector2Int position) =>
            gridManager.TileMap.FirstOrDefault(t => t != null && t.Coordinates == position);

        // ------------------------------------------------------------------
        // Execution control — called by the UI "Run" / "Step" buttons
        // ------------------------------------------------------------------

        /// <summary>Validates and runs the student's current block program.</summary>
        public void RunProgram()
        {
            if (!_levelActive || _paused || blockParser == null)
            {
                return;
            }

            List<string> errors = blockParser.ValidateProgram();
            if (errors != null && errors.Count > 0)
            {
                Debug.LogWarning($"[LevelManager] Validation errors: {string.Join("; ", errors)}");
                // An invalid program counts as a failed run so hints escalate.
                RegisterFailedRun(EvaluateMetric(CurrentLevel.successMetric));
                return;
            }

            blockParser.StartProgramExecution();
        }

        /// <summary>Enables or disables step-through debugging mode.</summary>
        public void SetStepThroughMode(bool enabled)
        {
            if (blockParser != null)
            {
                blockParser.StepThroughMode = enabled;
            }
        }

        /// <summary>Advances one step while in step-through mode.</summary>
        public void StepAdvance()
        {
            if (blockParser != null && !_paused)
            {
                blockParser.OnStepAdvance();
            }
        }

        /// <summary>Restarts the current level from scratch.</summary>
        public void RestartLevel()
        {
            if (CurrentLevelId > 0)
            {
                LoadLevel(CurrentLevelId);
            }
        }

        // ------------------------------------------------------------------
        // Pause / Resume (teacher "Eyes Up" broadcast via NetworkManager)
        // ------------------------------------------------------------------

        /// <summary>Freezes the game clock. HintSystem hides hints on the same event.</summary>
        public void PauseLevel()
        {
            if (_paused)
            {
                return;
            }

            _paused = true;
            Time.timeScale = 0f;
            Debug.Log("[LevelManager] Level paused by teacher.");
        }

        /// <summary>Restores normal time scale after the teacher resumes the class.</summary>
        public void ResumeLevel()
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            Time.timeScale = 1f;
            Debug.Log("[LevelManager] Level resumed.");
        }

        // ------------------------------------------------------------------
        // Grid callbacks
        // ------------------------------------------------------------------

        private void HandleTokenEarned(int amount)
        {
            // NetworkManager queues the emit while offline, so no IsJoined guard is needed.
            Network?.SendTokenEarned(amount, CurrentLevelId);
        }

        /// <summary>
        /// GridEnvironmentManager.OnLevelComplete fires when no dirty tile remains; its
        /// argument is the bot's tokensEarned (not a level id).
        /// </summary>
        private void HandleGridObjectivesComplete(int tokensEarned)
        {
            if (!_levelActive || CurrentLevel == null)
            {
                return;
            }

            SuccessMetric metric = CurrentLevel.successMetric;
            if ((metric == SuccessMetric.TRASH_CLEANED_PERCENTAGE || metric == SuccessMetric.PANELS_CLEANED_PERCENTAGE) &&
                EvaluateMetric(metric) >= CurrentLevel.successThreshold)
            {
                CompleteLevel();
            }
        }

        // ------------------------------------------------------------------
        // Block-parser callbacks
        // ------------------------------------------------------------------

        private void HandleBlockHighlighted(LogicBlock block)
        {
            if (block == null || !driveGridFromBlocks || gridManager == null || !_levelActive)
            {
                return;
            }

            if (block.BlockType != BlockType.Action)
            {
                return;
            }

            string action = (block.ActionName ?? string.Empty).Trim().ToUpperInvariant();
            switch (action)
            {
                case "ACTION_MOVE":
                case "MOVE":
                    gridManager.MoveForward();
                    break;
                case "ACTION_TURN":
                case "ACTION_TURN_RIGHT":
                case "TURN_RIGHT":
                    gridManager.TurnRight();
                    break;
                case "ACTION_TURN_LEFT":
                case "TURN_LEFT":
                    gridManager.TurnLeft();
                    break;
                case "ACTION_CLEAN":
                case "CLEAN":
                    gridManager.CleanCurrentTile();
                    break;
                case "ACTION_WATER":
                case "WATER":
                    _waterActions++;
                    gridManager.WaterCurrentTile(waterPerAction);
                    break;
            }
        }

        private void HandleProgramFinished(bool executed)
        {
            if (!_levelActive || CurrentLevel == null)
            {
                return;
            }

            float metricValue = EvaluateMetric(CurrentLevel.successMetric);
            if (executed && metricValue >= CurrentLevel.successThreshold)
            {
                CompleteLevel();
            }
            else
            {
                RegisterFailedRun(metricValue);
            }
        }

        private void RegisterFailedRun(float metricValue)
        {
            _retriesCount++;
            Debug.Log($"[LevelManager] Level {CurrentLevelId} attempt {_retriesCount} failed. " +
                      $"{CurrentLevel.successMetric} = {metricValue:F2} (need {CurrentLevel.successThreshold:F2})");

            // HintSystem tiers are 0-based (0 = Gentle), so the first failure shows the gentlest hint.
            if (hintSystem != null)
            {
                hintSystem.ShowHint(CurrentLevelId, _retriesCount - 1);
            }

            if (!_stuckAlertSent && _retriesCount >= CurrentLevel.stuckRetryThreshold)
            {
                _stuckAlertSent = true;
                Network?.SendStuckAlert(CurrentLevelId, _retriesCount);
            }

            onLevelFailed.Invoke();
        }

        // ------------------------------------------------------------------
        // Win condition
        // ------------------------------------------------------------------

        private void CompleteLevel()
        {
            if (!_levelActive)
            {
                return;
            }

            _levelActive = false;
            LevelTelemetryData telemetry = BuildTelemetry();
            StemCompetencyScores scores = telemetry.stemCompetencyScores;
            Debug.Log($"[LevelManager] Level {CurrentLevelId} COMPLETE! CT={scores.computationalThinking:F2} " +
                      $"SE={scores.systemicEngineering:F2} MM={scores.mathModeling:F2} P={scores.persistence:F2}");

            NetworkManager network = Network;
            if (network != null)
            {
                network.SendLevelCompleted(CurrentLevelId, telemetry);

                if (dayNightController != null && dayNightController.enabled)
                {
                    float surplus = Mathf.Max(0f, dayNightController.PowerBalance);
                    if (surplus > 0f)
                    {
                        network.SendEnergyContributed(surplus, CurrentLevelId);
                    }
                }
            }

            if (hintSystem != null)
            {
                hintSystem.HideHint();
            }

            onLevelComplete.Invoke(CurrentLevelId);
            StartCoroutine(AdvanceToNextLevel());
        }

        /// <summary>Builds the level_completed payload expected by the backend.</summary>
        private LevelTelemetryData BuildTelemetry()
        {
            float timeSpent = Mathf.Max(0f, Time.realtimeSinceStartup - _levelStartTime);
            bool subroutinesUsed = blockParser != null && blockParser.UsesSubroutines();
            int blockCount = blockParser != null ? blockParser.CountBlocks() : 0;
            IReadOnlyList<LogicBlock> program = blockParser != null
                ? (IReadOnlyList<LogicBlock>)blockParser.BlockProgram
                : Array.Empty<LogicBlock>();

            return new LevelTelemetryData
            {
                retriesCount = _retriesCount,
                timeSpentSeconds = Mathf.Round(timeSpent * 10f) / 10f,
                subroutinesUsed = subroutinesUsed,
                completedAt = DateTime.UtcNow.ToString("o"),
                stemCompetencyScores = StemCompetencyCalculator.Compute(
                    _retriesCount, timeSpent, subroutinesUsed, blockCount, CurrentLevel.codeCapacityLimit),
                blockProgramSnapshot = BlockProgramSerializer.Serialize(program)
            };
        }

        private IEnumerator AdvanceToNextLevel()
        {
            // Realtime so a teacher pause during the celebration does not stall forever.
            yield return new WaitForSecondsRealtime(advanceDelaySeconds);
            while (_paused)
            {
                yield return null;
            }

            int nextId = CurrentLevelId + 1;
            if (nextId <= MaxLevel)
            {
                LoadLevel(nextId);
            }
            else
            {
                Debug.Log("[LevelManager] All levels complete — Eco-City restored!");
            }
        }

        // ------------------------------------------------------------------
        // Metric evaluation
        // ------------------------------------------------------------------

        /// <summary>
        /// Current value of <paramref name="metric"/>, in the same units as
        /// <see cref="LevelConfig.successThreshold"/>.
        /// </summary>
        public float EvaluateMetric(SuccessMetric metric)
        {
            switch (metric)
            {
                case SuccessMetric.TRASH_CLEANED_PERCENTAGE:
                    return TrashCleanedFraction();

                case SuccessMetric.PANELS_CLEANED_PERCENTAGE:
                    return PanelsCleanedFraction();

                case SuccessMetric.PLANTS_WATERED_PERCENTAGE:
                    return WateredFraction(0.6f);

                case SuccessMetric.ENERGY_OUTPUT_KW:
                    return dayNightController != null
                        ? dayNightController.SolarOutput + dayNightController.WindOutput
                        : 0f;

                case SuccessMetric.ITEMS_SORTED_ACCURACY:
                    return SortingAccuracy(0f);

                case SuccessMetric.RESERVOIR_LEVEL_IN_RANGE:
                    return physicsSimulator != null && physicsSimulator.IsNetworkBalanced() ? 1f : 0f;

                case SuccessMetric.BRIDGE_LOAD_SUPPORTED:
                    return physicsSimulator != null && physicsSimulator.AreAllBridgesStable() ? 1f : 0f;

                case SuccessMetric.PLANTS_HEALTHY_AND_WATER_SAVED:
                    return PlantHealthAndWaterSavingScore();

                case SuccessMetric.UPTIME_PERCENTAGE:
                    float total = _uptimeOnlineSeconds + _uptimeOfflineSeconds;
                    return total > 0f ? _uptimeOnlineSeconds / total : 0f;

                case SuccessMetric.CITY_SUSTAINABILITY_SCORE:
                    return ComputeCitySustainabilityScore();

                default:
                    Debug.LogWarning($"[LevelManager] Unhandled metric: {metric}");
                    return 0f;
            }
        }

        /// <summary>Fraction of the level's original trash/dirt tiles that are now clean.</summary>
        private float TrashCleanedFraction()
        {
            if (gridManager == null)
            {
                return 0f;
            }

            if (_initialTrashCount <= 0)
            {
                return 1f;
            }

            int remaining = gridManager.TileMap.Count(t => t != null && t.IsDirty && !t.HasSolarPanel);
            return Mathf.Clamp01(1f - (float)remaining / _initialTrashCount);
        }

        /// <summary>Fraction of solar panels that are clean (HasSolarPanel and not IsDirty).</summary>
        private float PanelsCleanedFraction()
        {
            if (gridManager == null)
            {
                return 0f;
            }

            int panels = gridManager.TileMap.Count(t => t != null && t.HasSolarPanel);
            if (panels == 0)
            {
                return _initialDirtyPanelCount > 0 ? 0f : 1f;
            }

            int clean = gridManager.TileMap.Count(t => t != null && t.HasSolarPanel && !t.IsDirty);
            return (float)clean / panels;
        }

        private List<GridTile> SoilTiles() =>
            gridManager == null
                ? new List<GridTile>()
                : gridManager.TileMap.Where(t => t != null && t.TileType == TileType.Soil).ToList();

        private float WateredFraction(float moistureThreshold)
        {
            List<GridTile> soil = SoilTiles();
            if (soil.Count == 0)
            {
                return gridManager != null ? 1f : 0f;
            }

            return (float)soil.Count(t => t.MoistureLevel >= moistureThreshold) / soil.Count;
        }

        /// <summary>
        /// L8: healthy plants (moisture ≥ 0.5) AND frugal water use (no tile over-watered
        /// to ≥ 0.95, which happens when already-moist plants are watered again).
        /// </summary>
        private float PlantHealthAndWaterSavingScore()
        {
            List<GridTile> soil = SoilTiles();
            if (soil.Count == 0)
            {
                return 0f;
            }

            float healthy = (float)soil.Count(t => t.MoistureLevel >= 0.5f) / soil.Count;
            float efficient = (float)soil.Count(t => t.MoistureLevel >= 0.5f && t.MoistureLevel < 0.95f) / soil.Count;
            return (healthy + efficient) / 2f;
        }

        private float SortingAccuracy(float fallback) =>
            _itemsSortedTotal > 0 ? (float)_itemsSortedCorrect / _itemsSortedTotal : fallback;

        /// <summary>L10 capstone: average of the trash, energy, water, sorting and structure sub-scores.</summary>
        private float ComputeCitySustainabilityScore()
        {
            float trash = TrashCleanedFraction();
            float energy = dayNightController != null
                ? Mathf.Clamp01(dayNightController.BatteryNormalized + Mathf.Max(0f, dayNightController.PowerBalance) / 20f)
                : 0f;
            float water = WateredFraction(0.5f);
            float sorting = SortingAccuracy(1f);
            float structure = physicsSimulator != null && physicsSimulator.AreAllBridgesStable() ? 1f : 0.5f;
            return (trash + energy + water + sorting + structure) / 5f;
        }

        // ------------------------------------------------------------------
        // External calls from specialised controllers (levels 5 and 9)
        // ------------------------------------------------------------------

        /// <summary>Recycling-belt UI: record one sorted item (Level 5 ITEMS_SORTED_ACCURACY).</summary>
        public void RecordSortingResult(bool wasCorrect)
        {
            if (!_levelActive)
            {
                return;
            }

            _itemsSortedTotal++;
            if (wasCorrect)
            {
                _itemsSortedCorrect++;
            }
        }

        /// <summary>Night-grid controller: record whether the town had power this tick (Level 9 UPTIME_PERCENTAGE).</summary>
        public void RecordUptimeTick(bool systemOnline, float deltaTime)
        {
            if (!_levelActive || _paused || deltaTime <= 0f)
            {
                return;
            }

            if (systemOnline)
            {
                _uptimeOnlineSeconds += deltaTime;
            }
            else
            {
                _uptimeOfflineSeconds += deltaTime;
            }
        }

        // ------------------------------------------------------------------
        // Capstone subroutines (Level 10)
        // ------------------------------------------------------------------

        /// <summary>Registers the three master subroutines the Level 10 program calls by name.</summary>
        private void RegisterCapstoneSubroutines()
        {
            blockParser.RegisterSubroutine("Manage_Energy", new List<LogicBlock>
            {
                LogicBlock.CreateAction("ACTION_ROTATE_TURBINE", "Rotate Turbine")
            });
            blockParser.RegisterSubroutine("Auto_Water", new List<LogicBlock>
            {
                LogicBlock.CreateAction("ACTION_WATER", "Water Plant")
            });
            blockParser.RegisterSubroutine("Run_Recycle_Sorter", new List<LogicBlock>
            {
                LogicBlock.CreateAction("ACTION_CLEAN", "Clean Sorter")
            });
        }

        // ------------------------------------------------------------------
        // Catalogue
        // ------------------------------------------------------------------

        /// <summary>Returns the <see cref="LevelConfig"/> for a given id, or null.</summary>
        public LevelConfig GetConfig(int levelId) =>
            levels?.FirstOrDefault(l => l != null && l.levelId == levelId);

        /// <summary>Read-only view of the level catalogue.</summary>
        public IReadOnlyList<LevelConfig> Levels => levels;

        /// <summary>
        /// The 10 canonical levels, generated from <c>schemas/level-configs.json</c>.
        /// Keep in sync with the schema (title, limits, metrics, thresholds, grid sizes, rewards, hints).
        /// </summary>
        public static LevelConfig[] BuildLevelCatalogue()
        {
            return new[]
            {
                new LevelConfig
                {
                    levelId = 1,
                    title = "Sky-Isle Cleanup",
                    stemDomain = "Logic",
                    description = "Navigate the grid and clean all trash tiles to restore the island.",
                    requiredBlockTypes = new[] { "ACTION_MOVE", "ACTION_TURN", "ACTION_CLEAN" },
                    cstaStandards = new[] { "1B-AP-08", "1B-AP-10" },
                    ngssPractices = new[] { "Science and Engineering Practice 5: Using Mathematics and Computational Thinking" },
                    codeCapacityLimit = 10,
                    successMetric = SuccessMetric.TRASH_CLEANED_PERCENTAGE,
                    successThreshold = 1.0f,
                    stuckRetryThreshold = 4,
                    cosmeticReward = "leaf_antenna",
                    timeOfDay = "DAY",
                    weather = "CLEAR",
                    gridWidth = 5,
                    gridHeight = 5,
                    botStart = new Vector2Int(0, 0),
                    botFacing = Direction.East,
                    hintTier1 = "Watch where your bot is facing!",
                    hintTier2 = "Grid coordinates: (column, row). Your bot starts at (1,1).",
                    hintTier3ToolType = "DRAG_MOVE_DEMO"
                },
                new LevelConfig
                {
                    levelId = 2,
                    title = "Solar Panel Path",
                    stemDomain = "Sequencing & Loops",
                    description = "Clean the dusty solar panels lined up in a row. Use a Repeat block so your program stays short.",
                    requiredBlockTypes = new[] { "ACTION_MOVE", "ACTION_CLEAN", "LOOP_REPEAT" },
                    cstaStandards = new[] { "1B-AP-10", "1B-AP-11" },
                    ngssPractices = new[] { "Science and Engineering Practice 5: Using Mathematics and Computational Thinking", "Science and Engineering Practice 6: Constructing Explanations and Designing Solutions" },
                    codeCapacityLimit = 6,
                    successMetric = SuccessMetric.PANELS_CLEANED_PERCENTAGE,
                    successThreshold = 1.0f,
                    stuckRetryThreshold = 4,
                    cosmeticReward = "solar_visor",
                    timeOfDay = "DAY",
                    weather = "CLEAR",
                    gridWidth = 7,
                    gridHeight = 3,
                    botStart = new Vector2Int(0, 1),
                    botFacing = Direction.East,
                    hintTier1 = "Do you see a pattern? Move, clean, move, clean...",
                    hintTier2 = "A Repeat block runs the blocks inside it again and again. Count the panels to pick the number.",
                    hintTier3ToolType = "LOOP_UNROLL_VISUALIZER"
                },
                new LevelConfig
                {
                    levelId = 3,
                    title = "Water Wheel Village",
                    stemDomain = "Nested Loops",
                    description = "Water every garden bed around the village square. Repeat a pattern inside another pattern to cover the whole square.",
                    requiredBlockTypes = new[] { "ACTION_MOVE", "ACTION_TURN", "ACTION_WATER", "LOOP_REPEAT" },
                    cstaStandards = new[] { "1B-AP-10", "1B-AP-11", "1B-AP-15" },
                    ngssPractices = new[] { "Science and Engineering Practice 5: Using Mathematics and Computational Thinking", "5-ESS3-1: Obtain and combine information about ways individual communities use science ideas to protect the Earth's resources and environment" },
                    codeCapacityLimit = 8,
                    successMetric = SuccessMetric.PLANTS_WATERED_PERCENTAGE,
                    successThreshold = 1.0f,
                    stuckRetryThreshold = 5,
                    cosmeticReward = "water_wheel_wheels",
                    timeOfDay = "DAY",
                    weather = "CLEAR",
                    gridWidth = 6,
                    gridHeight = 6,
                    botStart = new Vector2Int(1, 1),
                    botFacing = Direction.East,
                    hintTier1 = "The beds make a square. How many sides does a square have?",
                    hintTier2 = "Put one Repeat block inside another: the inside loop waters one side, the outside loop does all 4 sides.",
                    hintTier3ToolType = "NESTED_LOOP_TRACE"
                },
                new LevelConfig
                {
                    levelId = 4,
                    title = "Wind Farm Ridge",
                    stemDomain = "Energy Systems",
                    description = "Rotate each wind turbine to face the wind and feed the town's power grid. Reach the target energy output.",
                    requiredBlockTypes = new[] { "ACTION_MOVE", "ACTION_TURN", "ACTION_ROTATE_TURBINE", "LOOP_REPEAT" },
                    cstaStandards = new[] { "1B-AP-10", "1B-CS-02" },
                    ngssPractices = new[] { "Science and Engineering Practice 2: Developing and Using Models", "4-PS3-4: Apply scientific ideas to design, test, and refine a device that converts energy from one form to another" },
                    codeCapacityLimit = 10,
                    successMetric = SuccessMetric.ENERGY_OUTPUT_KW,
                    successThreshold = 50.0f,
                    stuckRetryThreshold = 5,
                    cosmeticReward = "wind_turbine_hat",
                    timeOfDay = "DAY",
                    weather = "WINDY",
                    gridWidth = 7,
                    gridHeight = 5,
                    botStart = new Vector2Int(0, 2),
                    botFacing = Direction.East,
                    hintTier1 = "Look at the wind arrows. Which way are the turbines pointing?",
                    hintTier2 = "A turbine makes the most energy when it faces straight into the wind. Each rotation turns it 90 degrees.",
                    hintTier3ToolType = "TURBINE_ANGLE_SLIDER"
                },
                new LevelConfig
                {
                    levelId = 5,
                    title = "Recycling Plant Sort",
                    stemDomain = "Conditionals",
                    description = "Items roll down the conveyor. Use If / Else blocks so your bot cleans recyclables and moves past everything else.",
                    requiredBlockTypes = new[] { "CONDITION_IF", "CONDITION_ELSE", "ACTION_CLEAN", "ACTION_MOVE", "LOOP_REPEAT" },
                    cstaStandards = new[] { "1B-AP-10", "1B-AP-12" },
                    ngssPractices = new[] { "Science and Engineering Practice 4: Analyzing and Interpreting Data", "5-ESS3-1: Obtain and combine information about ways individual communities use science ideas to protect the Earth's resources and environment" },
                    codeCapacityLimit = 10,
                    successMetric = SuccessMetric.ITEMS_SORTED_ACCURACY,
                    successThreshold = 0.9f,
                    stuckRetryThreshold = 5,
                    cosmeticReward = "recycled_armor",
                    timeOfDay = "DAY",
                    weather = "CLEAR",
                    gridWidth = 8,
                    gridHeight = 3,
                    botStart = new Vector2Int(0, 1),
                    botFacing = Direction.East,
                    hintTier1 = "Not every item is the same. What is different about the recyclables?",
                    hintTier2 = "If something is true, the bot does the first action. Else it does the second one.",
                    hintTier3ToolType = "DECISION_TREE_BUILDER"
                },
                new LevelConfig
                {
                    levelId = 6,
                    title = "Hydro Dam Hydraulics",
                    stemDomain = "Mathematical Modeling",
                    description = "Open the dam gates in the right order to keep the reservoir level safe while spinning the hydro generator. Balance water flow in and out.",
                    requiredBlockTypes = new[] { "LOOP_REPEAT", "CONDITION_IF", "ACTION_ROTATE_TURBINE", "ACTION_WATER", "INPUT_BATTERY_CHARGE" },
                    cstaStandards = new[] { "1B-AP-09", "1B-DA-07", "1B-AP-10" },
                    ngssPractices = new[] { "Science and Engineering Practice 2: Developing and Using Models", "Science and Engineering Practice 5: Using Mathematics and Computational Thinking", "4-ESS3-1: Obtain and combine information to describe that energy and fuels are derived from natural resources" },
                    codeCapacityLimit = 12,
                    successMetric = SuccessMetric.RESERVOIR_LEVEL_IN_RANGE,
                    successThreshold = 0.95f,
                    stuckRetryThreshold = 6,
                    cosmeticReward = "hydro_jetpack",
                    timeOfDay = "DAY",
                    weather = "RAIN",
                    gridWidth = 6,
                    gridHeight = 6,
                    botStart = new Vector2Int(0, 0),
                    botFacing = Direction.East,
                    hintTier1 = "Watch the water gauge. Is it rising faster or slower than it drains?",
                    hintTier2 = "Water in minus water out = change in level. Open a gate when the level gets too high.",
                    hintTier3ToolType = "FLOW_RATE_SIMULATOR"
                },
                new LevelConfig
                {
                    levelId = 7,
                    title = "Bridge Builder Canyon",
                    stemDomain = "Structural Engineering",
                    description = "Place bridge supports so the eco-truck can cross the canyon. The bridge must hold the load without bending too far.",
                    requiredBlockTypes = new[] { "ACTION_MOVE", "ACTION_TURN", "LOOP_REPEAT", "SUBROUTINE_CALL" },
                    cstaStandards = new[] { "1B-AP-11", "1B-AP-13", "1B-AP-15" },
                    ngssPractices = new[] { "Science and Engineering Practice 6: Constructing Explanations and Designing Solutions", "3-5-ETS1-2: Generate and compare multiple possible solutions to a problem based on how well each is likely to meet the criteria and constraints", "3-5-ETS1-3: Plan and carry out fair tests in which variables are controlled and failure points are considered" },
                    codeCapacityLimit = 12,
                    successMetric = SuccessMetric.BRIDGE_LOAD_SUPPORTED,
                    successThreshold = 1.0f,
                    stuckRetryThreshold = 6,
                    cosmeticReward = "bridge_builder_boots",
                    timeOfDay = "DAY",
                    weather = "CLEAR",
                    gridWidth = 9,
                    gridHeight = 5,
                    botStart = new Vector2Int(0, 2),
                    botFacing = Direction.East,
                    hintTier1 = "Where does the bridge sag the most when the truck drives on it?",
                    hintTier2 = "Triangles are strong shapes. Build one support pattern as a subroutine and call it for every gap.",
                    hintTier3ToolType = "LOAD_STRESS_PREVIEW"
                },
                new LevelConfig
                {
                    levelId = 8,
                    title = "Compost Garden Sensors",
                    stemDomain = "Sensors & Events",
                    description = "Program a garden helper that runs forever: read the moisture sensor and water only the dry plants to save water.",
                    requiredBlockTypes = new[] { "TRIGGER_ALWAYS_LOOP", "INPUT_MOISTURE_SENSOR", "CONDITION_IF", "ACTION_WATER", "ACTION_MOVE" },
                    cstaStandards = new[] { "1B-AP-10", "1B-CS-02", "1B-DA-06" },
                    ngssPractices = new[] { "Science and Engineering Practice 3: Planning and Carrying Out Investigations", "Science and Engineering Practice 4: Analyzing and Interpreting Data" },
                    codeCapacityLimit = 12,
                    successMetric = SuccessMetric.PLANTS_HEALTHY_AND_WATER_SAVED,
                    successThreshold = 0.9f,
                    stuckRetryThreshold = 6,
                    cosmeticReward = "compost_cape",
                    timeOfDay = "DAY",
                    weather = "SUNNY_HOT",
                    gridWidth = 6,
                    gridHeight = 6,
                    botStart = new Vector2Int(0, 0),
                    botFacing = Direction.East,
                    hintTier1 = "Some plants look droopy, others look fine. Does every plant need water?",
                    hintTier2 = "A sensor gives your bot information. If moisture is below the line, then water. 'Always' keeps checking.",
                    hintTier3ToolType = "SENSOR_READOUT_OVERLAY"
                },
                new LevelConfig
                {
                    levelId = 9,
                    title = "Night Grid Balancer",
                    stemDomain = "Systems Thinking",
                    description = "When night falls the solar panels stop. Check the battery charge and switch the town between solar, wind and stored energy so the lights never go out.",
                    requiredBlockTypes = new[] { "TRIGGER_ALWAYS_LOOP", "INPUT_BATTERY_CHARGE", "CONDITION_IF", "CONDITION_ELSE", "SUBROUTINE_CALL", "ACTION_ROTATE_TURBINE" },
                    cstaStandards = new[] { "1B-AP-09", "1B-AP-11", "1B-CS-02", "1B-DA-07" },
                    ngssPractices = new[] { "Science and Engineering Practice 2: Developing and Using Models", "4-ESS3-1: Obtain and combine information to describe that energy and fuels are derived from natural resources and their uses affect the environment" },
                    codeCapacityLimit = 14,
                    successMetric = SuccessMetric.UPTIME_PERCENTAGE,
                    successThreshold = 0.95f,
                    stuckRetryThreshold = 7,
                    cosmeticReward = "aurora_paint",
                    timeOfDay = "DAY_NIGHT_CYCLE",
                    weather = "VARIABLE_WIND",
                    gridWidth = 8,
                    gridHeight = 6,
                    botStart = new Vector2Int(0, 0),
                    botFacing = Direction.East,
                    hintTier1 = "Watch the battery meter when the sun goes down. What happens?",
                    hintTier2 = "Store extra energy in the day and use it at night. Make one subroutine for day and one for night.",
                    hintTier3ToolType = "ENERGY_FLOW_DIAGRAM"
                },
                new LevelConfig
                {
                    levelId = 10,
                    title = "Eco-City Master Mission",
                    stemDomain = "Integrated Engineering",
                    description = "Bring the whole Eco-City online: clean the streets, water the parks and power the town using everything you have learned, with as few blocks as possible.",
                    requiredBlockTypes = new[] { "TRIGGER_ALWAYS_LOOP", "INPUT_MOISTURE_SENSOR", "INPUT_BATTERY_CHARGE", "CONDITION_IF", "CONDITION_ELSE", "SUBROUTINE_CALL", "LOOP_REPEAT", "ACTION_MOVE", "ACTION_TURN", "ACTION_CLEAN", "ACTION_WATER", "ACTION_ROTATE_TURBINE" },
                    cstaStandards = new[] { "1B-AP-08", "1B-AP-10", "1B-AP-11", "1B-AP-13", "1B-AP-15" },
                    ngssPractices = new[] { "Science and Engineering Practice 1: Asking Questions and Defining Problems", "Science and Engineering Practice 6: Constructing Explanations and Designing Solutions", "3-5-ETS1-1: Define a simple design problem reflecting a need or a want that includes specified criteria for success and constraints" },
                    codeCapacityLimit = 16,
                    successMetric = SuccessMetric.CITY_SUSTAINABILITY_SCORE,
                    successThreshold = 0.85f,
                    stuckRetryThreshold = 8,
                    cosmeticReward = "golden_gear_crown",
                    timeOfDay = "DAY_NIGHT_CYCLE",
                    weather = "VARIABLE",
                    gridWidth = 10,
                    gridHeight = 8,
                    botStart = new Vector2Int(0, 0),
                    botFacing = Direction.East,
                    hintTier1 = "Break the big mission into smaller jobs. Which job is the most urgent?",
                    hintTier2 = "Reuse your best ideas: make a subroutine for each job, then call them from one Always loop.",
                    hintTier3ToolType = "DECOMPOSITION_PLANNER"
                },
            };
        }
    }
}
