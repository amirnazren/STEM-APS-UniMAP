using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EcoBotBuilders.Core
{
    /// <summary>
    /// Identifies the supported block categories in the visual programming system.
    /// </summary>
    public enum BlockType
    {
        Unknown = 0,
        Start,
        Trigger,
        If,
        Else,
        Repeat,
        Action,
        Sensor,
        SubroutineCall,
        End
    }

    /// <summary>
    /// Represents a single node in the block program tree.
    /// </summary>
    [Serializable]
    public class LogicBlock
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private BlockType blockType = BlockType.Unknown;
        [SerializeField] private string conditionKey;
        [SerializeField] private string conditionOperator;
        [SerializeField] private float conditionValue;
        [SerializeField] private int repeatCount = 1;
        [SerializeField] private string actionName;
        [SerializeField] private string sensorName;
        [SerializeField] private string subroutineName;
        [SerializeField] private bool booleanValue;
        [SerializeField] private List<LogicBlock> children = new List<LogicBlock>();
        [SerializeField] private List<LogicBlock> elseChildren = new List<LogicBlock>();

        /// <summary>Unique identifier for this block.</summary>
        public string Id => id;
        /// <summary>Human-readable name for this block.</summary>
        public string DisplayName => displayName;
        /// <summary>The type of block.</summary>
        public BlockType BlockType => blockType;
        /// <summary>Optional condition key used by sensors and branches.</summary>
        public string ConditionKey => conditionKey;
        /// <summary>Optional condition operator used by branches.</summary>
        public string ConditionOperator => conditionOperator;
        /// <summary>Optional numeric comparison value.</summary>
        public float ConditionValue => conditionValue;
        /// <summary>Repeat count for loop blocks.</summary>
        public int RepeatCount => repeatCount;
        /// <summary>Action identifier for action blocks.</summary>
        public string ActionName => actionName;
        /// <summary>Sensor identifier for sensor blocks.</summary>
        public string SensorName => sensorName;
        /// <summary>Subroutine identifier for subroutine call blocks.</summary>
        public string SubroutineName => subroutineName;
        /// <summary>Boolean payload for sensor/trigger blocks.</summary>
        public bool BooleanValue => booleanValue;
        /// <summary>Child blocks executed in the main branch.</summary>
        public List<LogicBlock> Children => children;
        /// <summary>Child blocks executed in the else branch.</summary>
        public List<LogicBlock> ElseChildren => elseChildren;

        /// <summary>
        /// Creates a simple action block. Used for the built-in subroutine library and for tests.
        /// </summary>
        public static LogicBlock CreateAction(string actionName, string displayName)
        {
            return new LogicBlock
            {
                id = Guid.NewGuid().ToString("N"),
                blockType = BlockType.Action,
                actionName = actionName,
                displayName = displayName
            };
        }
    }

    /// <summary>
    /// Runtime engine that validates and executes visual block programs.
    /// </summary>
    public class BlockParserEngine : MonoBehaviour
    {
        [SerializeField] private List<LogicBlock> blockProgram = new List<LogicBlock>();
        [SerializeField] private bool stepThroughMode;
        [SerializeField] private int codeCapacityLimit = 256;
        [SerializeField] private float stepDelaySeconds = 0.4f;

        private readonly Dictionary<string, List<LogicBlock>> subroutineLibrary = new Dictionary<string, List<LogicBlock>>(StringComparer.OrdinalIgnoreCase);
        private Coroutine executionCoroutine;
        private bool waitingForStepAdvance;

        /// <summary>
        /// Raised whenever a block is highlighted during execution.
        /// </summary>
        public event Action<LogicBlock> OnBlockHighlighted;

        /// <summary>
        /// Raised when <see cref="ExecuteProgram"/> finishes. The argument is false when the
        /// program failed validation and never ran, true when every root block was executed.
        /// </summary>
        public event Action<bool> OnProgramFinished;

        /// <summary>
        /// Gets or sets the current block program.
        /// </summary>
        public List<LogicBlock> BlockProgram
        {
            get => blockProgram;
            set => blockProgram = value ?? new List<LogicBlock>();
        }

        /// <summary>
        /// Gets or sets whether execution requires manual step advance.
        /// </summary>
        public bool StepThroughMode
        {
            get => stepThroughMode;
            set => stepThroughMode = value;
        }

        /// <summary>
        /// Gets the maximum number of blocks allowed in a valid program.
        /// </summary>
        public int CodeCapacityLimit
        {
            get => codeCapacityLimit;
            set => codeCapacityLimit = Mathf.Max(1, value);
        }

        /// <summary>
        /// Registers the default built-in subroutines.
        /// </summary>
        private void Awake()
        {
            RegisterDefaultSubroutines();
        }

        /// <summary>
        /// Starts executing the current block program.
        /// </summary>
        public void StartProgramExecution()
        {
            if (executionCoroutine != null)
            {
                StopCoroutine(executionCoroutine);
            }

            executionCoroutine = StartCoroutine(ExecuteProgram());
        }

        /// <summary>
        /// Advances execution by one step while in step-through mode.
        /// </summary>
        public void OnStepAdvance()
        {
            waitingForStepAdvance = false;
        }

        /// <summary>
        /// Validates the current program and returns any errors found.
        /// </summary>
        public List<string> ValidateProgram()
        {
            var errors = new List<string>();
            if (blockProgram == null)
            {
                errors.Add("Block program is null.");
                return errors;
            }

            if (blockProgram.Count > codeCapacityLimit)
            {
                errors.Add($"Program exceeds code capacity limit of {codeCapacityLimit} blocks.");
            }

            if (blockProgram.Count == 0)
            {
                errors.Add("Block program is empty.");
                return errors;
            }

            foreach (var root in blockProgram)
            {
                ValidateBlockRecursive(root, errors, "root");
            }

            return errors;
        }

        /// <summary>
        /// Executes the program by walking the block tree recursively.
        /// </summary>
        public IEnumerator ExecuteProgram()
        {
            var errors = ValidateProgram();
            if (errors.Count > 0)
            {
                Debug.LogError($"Block program validation failed:\n- {string.Join("\n- ", errors)}", this);
                OnProgramFinished?.Invoke(false);
                yield break;
            }

            foreach (var root in blockProgram)
            {
                yield return ParseBlockTree(root);
            }

            executionCoroutine = null;
            OnProgramFinished?.Invoke(true);
        }

        /// <summary>
        /// Counts every block in the current program, including nested children and else branches.
        /// </summary>
        public int CountBlocks()
        {
            return CountRecursive(blockProgram);
        }

        /// <summary>
        /// Returns true when the current program contains at least one subroutine call block.
        /// </summary>
        public bool UsesSubroutines()
        {
            return ContainsType(blockProgram, BlockType.SubroutineCall);
        }

        private static int CountRecursive(IReadOnlyList<LogicBlock> blocks)
        {
            if (blocks == null)
            {
                return 0;
            }

            var total = 0;
            foreach (var block in blocks)
            {
                if (block == null)
                {
                    continue;
                }

                total += 1 + CountRecursive(block.Children) + CountRecursive(block.ElseChildren);
            }

            return total;
        }

        private static bool ContainsType(IReadOnlyList<LogicBlock> blocks, BlockType type)
        {
            if (blocks == null)
            {
                return false;
            }

            foreach (var block in blocks)
            {
                if (block == null)
                {
                    continue;
                }

                if (block.BlockType == type || ContainsType(block.Children, type) || ContainsType(block.ElseChildren, type))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Parses and executes a block subtree recursively.
        /// </summary>
        public IEnumerator ParseBlockTree(LogicBlock root)
        {
            if (root == null)
            {
                yield break;
            }

            yield return HighlightBlock(root);

            switch (root.BlockType)
            {
                case BlockType.Start:
                case BlockType.Trigger:
                    yield return ExecuteChildren(root.Children);
                    break;
                case BlockType.If:
                    if (EvaluateCondition(root))
                    {
                        yield return ExecuteChildren(root.Children);
                    }
                    else
                    {
                        yield return ExecuteChildren(root.ElseChildren);
                    }
                    break;
                case BlockType.Repeat:
                    var repeatCount = Mathf.Max(0, root.RepeatCount);
                    for (var i = 0; i < repeatCount; i++)
                    {
                        yield return ExecuteChildren(root.Children);
                    }
                    break;
                case BlockType.SubroutineCall:
                    if (subroutineLibrary.TryGetValue(root.SubroutineName ?? string.Empty, out var subroutine))
                    {
                        yield return ExecuteChildren(subroutine);
                    }
                    else
                    {
                        Debug.LogWarning($"Unknown subroutine '{root.SubroutineName}'.", this);
                    }
                    break;
                case BlockType.Sensor:
                    _ = EvaluateSensor(root);
                    break;
                case BlockType.Action:
                    ExecuteAction(root);
                    break;
                case BlockType.End:
                case BlockType.Else:
                case BlockType.Unknown:
                default:
                    break;
            }
        }

        /// <summary>
        /// Highlights a block and enforces timing or step-through gating.
        /// </summary>
        public IEnumerator HighlightBlock(LogicBlock block)
        {
            if (block == null)
            {
                yield break;
            }

            OnBlockHighlighted?.Invoke(block);

            if (stepThroughMode)
            {
                waitingForStepAdvance = true;
                while (waitingForStepAdvance)
                {
                    yield return null;
                }
            }
            else
            {
                var delay = Mathf.Max(0f, stepDelaySeconds);
                if (delay > 0f)
                {
                    yield return new WaitForSeconds(delay);
                }
            }
        }

        /// <summary>
        /// Registers (or replaces) a named subroutine that SubroutineCall blocks can invoke.
        /// </summary>
        public void RegisterSubroutine(string name, List<LogicBlock> body)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            subroutineLibrary[name] = body ?? new List<LogicBlock>();
        }

        /// <summary>
        /// Clears level-specific subroutines and restores the built-in defaults.
        /// </summary>
        public void ResetSubroutines()
        {
            RegisterDefaultSubroutines();
        }

        /// <summary>Returns true when a subroutine with the given name is registered.</summary>
        public bool HasSubroutine(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && subroutineLibrary.ContainsKey(name);
        }

        private void RegisterDefaultSubroutines()
        {
            subroutineLibrary.Clear();
            subroutineLibrary["Manage_Energy"] = new List<LogicBlock>
            {
                CreateActionBlock("CheckBattery", "Check battery level"),
                CreateActionBlock("ConservePower", "Conserve power"),
                CreateActionBlock("ReportEnergy", "Report energy status")
            };
            subroutineLibrary["Auto_Water"] = new List<LogicBlock>
            {
                CreateActionBlock("CheckMoisture", "Check moisture"),
                CreateActionBlock("DispenseWater", "Dispense water")
            };
            subroutineLibrary["Run_Recycle_Sorter"] = new List<LogicBlock>
            {
                CreateActionBlock("ScanItem", "Scan item"),
                CreateActionBlock("SortRecyclables", "Sort recyclables")
            };
        }

        private static LogicBlock CreateActionBlock(string actionName, string displayName)
        {
            return LogicBlock.CreateAction(actionName, displayName);
        }

        private IEnumerator ExecuteChildren(IReadOnlyList<LogicBlock> children)
        {
            if (children == null)
            {
                yield break;
            }

            foreach (var child in children)
            {
                if (child == null)
                {
                    continue;
                }

                yield return ParseBlockTree(child);
            }
        }

        private bool EvaluateCondition(LogicBlock block)
        {
            if (block == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(block.ConditionKey))
            {
                return false;
            }

            var sensorValue = EvaluateSensor(block);
            return block.ConditionOperator switch
            {
                "==" => Mathf.Approximately(sensorValue, block.ConditionValue),
                "!=" => !Mathf.Approximately(sensorValue, block.ConditionValue),
                ">" => sensorValue > block.ConditionValue,
                ">=" => sensorValue >= block.ConditionValue,
                "<" => sensorValue < block.ConditionValue,
                "<=" => sensorValue <= block.ConditionValue,
                _ => sensorValue > 0.5f
            };
        }

        private float EvaluateSensor(LogicBlock block)
        {
            if (block == null)
            {
                return 0f;
            }

            return block.BooleanValue ? 1f : 0f;
        }

        private void ExecuteAction(LogicBlock block)
        {
            if (block == null)
            {
                return;
            }

            Debug.Log($"Executing action: {block.ActionName ?? block.DisplayName ?? "Unnamed Action"}", this);
        }

        private void ValidateBlockRecursive(LogicBlock block, ICollection<string> errors, string path)
        {
            if (block == null)
            {
                errors.Add($"Null block found at {path}.");
                return;
            }

            switch (block.BlockType)
            {
                case BlockType.If:
                    if (block.Children == null || block.Children.Count == 0)
                    {
                        errors.Add($"If block '{block.DisplayName ?? block.Id}' at {path} has no true-branch children.");
                    }
                    break;
                case BlockType.Repeat:
                    if (block.RepeatCount < 0)
                    {
                        errors.Add($"Repeat block '{block.DisplayName ?? block.Id}' at {path} has negative repeat count.");
                    }
                    break;
                case BlockType.SubroutineCall:
                    if (string.IsNullOrWhiteSpace(block.SubroutineName))
                    {
                        errors.Add($"Subroutine call block at {path} is missing a subroutine name.");
                    }
                    else if (!subroutineLibrary.ContainsKey(block.SubroutineName))
                    {
                        errors.Add($"Unknown subroutine '{block.SubroutineName}' at {path}.");
                    }
                    break;
                case BlockType.Sensor:
                    if (string.IsNullOrWhiteSpace(block.SensorName) && string.IsNullOrWhiteSpace(block.ConditionKey))
                    {
                        errors.Add($"Sensor block '{block.DisplayName ?? block.Id}' at {path} is missing a sensor name.");
                    }
                    break;
            }

            if (block.Children != null)
            {
                for (var i = 0; i < block.Children.Count; i++)
                {
                    ValidateBlockRecursive(block.Children[i], errors, $"{path}.children[{i}]");
                }
            }

            if (block.ElseChildren != null)
            {
                for (var i = 0; i < block.ElseChildren.Count; i++)
                {
                    ValidateBlockRecursive(block.ElseChildren[i], errors, $"{path}.elseChildren[{i}]");
                }
            }
        }
    }
}
