using System;
using System.Collections.Generic;
using UnityEngine;

namespace EcoBotBuilders.Physics
{
    /// <summary>
    /// Represents a hydraulic connection point in a simplified pressure network.
    /// </summary>
    [Serializable]
    public sealed class HydraulicNode
    {
        [SerializeField]
        private string nodeId;

        [SerializeField]
        private Vector3 localPosition;

        [SerializeField]
        private float basePressure;

        [SerializeField]
        private float currentPressure;

        [SerializeField]
        private bool isPressureSource;

        [SerializeField]
        private bool isPressureSink;

        [SerializeField]
        private float compliance = 1f;

        /// <summary>
        /// Creates a new hydraulic node.
        /// </summary>
        public HydraulicNode(string id, Vector3 position, float pressure = 0f, bool pressureSource = false, bool pressureSink = false, float nodeCompliance = 1f)
        {
            nodeId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
            localPosition = position;
            basePressure = pressure;
            currentPressure = pressure;
            isPressureSource = pressureSource;
            isPressureSink = pressureSink;
            compliance = Mathf.Max(0.0001f, nodeCompliance);
        }

        /// <summary>
        /// Gets the stable node identifier.
        /// </summary>
        public string NodeId => nodeId;

        /// <summary>
        /// Gets or sets the local position of the node.
        /// </summary>
        public Vector3 LocalPosition
        {
            get => localPosition;
            set => localPosition = value;
        }

        /// <summary>
        /// Gets the pressure currently stored in the node.
        /// </summary>
        public float CurrentPressure => currentPressure;

        /// <summary>
        /// Gets or sets the resting pressure used during network balancing.
        /// </summary>
        public float BasePressure
        {
            get => basePressure;
            set => basePressure = value;
        }

        /// <summary>
        /// Gets whether the node acts as a fixed pressure source.
        /// </summary>
        public bool IsPressureSource => isPressureSource;

        /// <summary>
        /// Gets whether the node acts as a pressure sink.
        /// </summary>
        public bool IsPressureSink => isPressureSink;

        /// <summary>
        /// Gets or sets the compliance factor used during pressure equalization.
        /// </summary>
        public float Compliance
        {
            get => compliance;
            set => compliance = Mathf.Max(0.0001f, value);
        }

        /// <summary>
        /// Applies a pressure adjustment to the node.
        /// </summary>
        public void ApplyPressureDelta(float delta)
        {
            currentPressure += delta;
        }

        /// <summary>
        /// Sets the current pressure directly.
        /// </summary>
        public void SetPressure(float pressure)
        {
            currentPressure = pressure;
        }

        /// <summary>
        /// Resets current pressure to the configured base pressure.
        /// </summary>
        public void ResetToBasePressure()
        {
            currentPressure = basePressure;
        }
    }

    /// <summary>
    /// Represents a structural segment within a bridge network.
    /// </summary>
    [Serializable]
    public sealed class BridgeSegment
    {
        [SerializeField]
        private string segmentId;

        [SerializeField]
        private HydraulicNode startNode;

        [SerializeField]
        private HydraulicNode endNode;

        [SerializeField]
        private float length = 1f;

        [SerializeField]
        private float crossSectionArea = 1f;

        [SerializeField]
        private float materialStrength = 100f;

        [SerializeField]
        private float damping = 0.15f;

        [SerializeField]
        private float deadLoad;

        [SerializeField]
        private float liveLoad;

        [SerializeField]
        private bool failed;

        /// <summary>
        /// Creates a new bridge segment.
        /// </summary>
        public BridgeSegment(string id, HydraulicNode start, HydraulicNode end, float area = 1f, float strength = 100f)
        {
            segmentId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
            startNode = start;
            endNode = end;
            crossSectionArea = Mathf.Max(0.0001f, area);
            materialStrength = Mathf.Max(0.0001f, strength);
            length = Mathf.Max(0.0001f, Vector3.Distance(start?.LocalPosition ?? Vector3.zero, end?.LocalPosition ?? Vector3.right));
        }

        /// <summary>
        /// Gets the segment identifier.
        /// </summary>
        public string SegmentId => segmentId;

        /// <summary>
        /// Gets the start node.
        /// </summary>
        public HydraulicNode StartNode => startNode;

        /// <summary>
        /// Gets the end node.
        /// </summary>
        public HydraulicNode EndNode => endNode;

        /// <summary>
        /// Gets or sets the segment length.
        /// </summary>
        public float Length
        {
            get => length;
            set => length = Mathf.Max(0.0001f, value);
        }

        /// <summary>
        /// Gets or sets the structural cross-section area.
        /// </summary>
        public float CrossSectionArea
        {
            get => crossSectionArea;
            set => crossSectionArea = Mathf.Max(0.0001f, value);
        }

        /// <summary>
        /// Gets or sets the maximum stress capacity of the segment material.
        /// </summary>
        public float MaterialStrength
        {
            get => materialStrength;
            set => materialStrength = Mathf.Max(0.0001f, value);
        }

        /// <summary>
        /// Gets or sets the segment damping used by the simplified dynamic model.
        /// </summary>
        public float Damping
        {
            get => damping;
            set => damping = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Gets or sets the segment dead load.
        /// </summary>
        public float DeadLoad
        {
            get => deadLoad;
            set => deadLoad = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Gets or sets the segment live load.
        /// </summary>
        public float LiveLoad
        {
            get => liveLoad;
            set => liveLoad = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Gets whether the segment has failed.
        /// </summary>
        public bool Failed => failed;

        /// <summary>
        /// Computes the axial stress induced by the current load state.
        /// </summary>
        public float ComputeStress(float additionalLoad = 0f)
        {
            float pressureDelta = Mathf.Abs((startNode?.CurrentPressure ?? 0f) - (endNode?.CurrentPressure ?? 0f));
            float totalLoad = deadLoad + liveLoad + Mathf.Max(0f, additionalLoad);
            float axialForce = totalLoad + pressureDelta * Length;
            return axialForce / Mathf.Max(0.0001f, CrossSectionArea);
        }

        /// <summary>
        /// Computes a normalized stability value between 0 and 1.
        /// </summary>
        public float ComputeStability(float additionalLoad = 0f)
        {
            float stressRatio = ComputeStress(additionalLoad) / Mathf.Max(0.0001f, MaterialStrength);
            return Mathf.Clamp01(1f - stressRatio);
        }

        /// <summary>
        /// Marks the segment as failed.
        /// </summary>
        public void Fail()
        {
            failed = true;
        }

        /// <summary>
        /// Repairs the segment.
        /// </summary>
        public void Repair()
        {
            failed = false;
        }
    }

    /// <summary>
    /// Simulates simplified hydraulic and structural bridge behavior.
    /// </summary>
    public class PhysicsSimulator : MonoBehaviour
    {
        [Header("Hydraulics")]
        [SerializeField]
        private float pressureEqualizationRate = 0.5f;

        [SerializeField]
        private float pressureLeakRate = 0.01f;

        [SerializeField]
        private float pressureTolerance = 0.05f;

        [Header("Structure")]
        [SerializeField]
        private float overloadFailureThreshold = 1.2f;

        [SerializeField]
        private float stabilityPassThreshold = 0.65f;

        [SerializeField]
        private float weightCapacityBias = 100f;

        [SerializeField]
        private List<HydraulicNode> hydraulicNodes = new List<HydraulicNode>();

        [SerializeField]
        private List<BridgeSegment> bridgeSegments = new List<BridgeSegment>();

        /// <summary>
        /// Optional hook that fires when a segment fails.
        /// </summary>
        public event Action<BridgeSegment> SegmentFailed;

        /// <summary>
        /// Adds a hydraulic node to the simulator.
        /// </summary>
        public void RegisterNode(HydraulicNode node)
        {
            if (node == null)
            {
                return;
            }

            if (!hydraulicNodes.Contains(node))
            {
                hydraulicNodes.Add(node);
            }
        }

        /// <summary>
        /// Adds a bridge segment to the simulator.
        /// </summary>
        public void RegisterSegment(BridgeSegment segment)
        {
            if (segment == null)
            {
                return;
            }

            if (!bridgeSegments.Contains(segment))
            {
                bridgeSegments.Add(segment);
            }
        }

        /// <summary>
        /// Returns the pressure stored at the node with the given id.
        /// </summary>
        public float GetNodePressure(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return 0f;
            }

            HydraulicNode node = hydraulicNodes.Find(n => n != null && string.Equals(n.NodeId, nodeId, StringComparison.Ordinal));
            return node?.CurrentPressure ?? 0f;
        }

        /// <summary>
        /// Checks whether the current pressure network is approximately balanced.
        /// </summary>
        public bool IsNetworkBalanced()
        {
            if (hydraulicNodes.Count == 0)
            {
                return true;
            }

            float total = 0f;
            float sumSquares = 0f;
            int count = 0;

            for (int i = 0; i < hydraulicNodes.Count; i++)
            {
                HydraulicNode node = hydraulicNodes[i];
                if (node == null)
                {
                    continue;
                }

                count++;
                total += node.CurrentPressure;
                sumSquares += node.CurrentPressure * node.CurrentPressure;
            }

            if (count == 0)
            {
                return true;
            }

            float mean = total / count;
            float variance = Mathf.Max(0f, sumSquares / count - mean * mean);
            return variance <= pressureTolerance * pressureTolerance;
        }

        /// <summary>
        /// Performs one simulation step for pressures and structure.
        /// </summary>
        public void SimulateStep(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            DistributePressure(deltaTime);
            EvaluateBridgeIntegrity();
        }

        /// <summary>
        /// Distributes pressure through the registered hydraulic network.
        /// </summary>
        public void DistributePressure(float deltaTime)
        {
            if (hydraulicNodes.Count == 0)
            {
                return;
            }

            float averagePressure = 0f;
            int validCount = 0;

            for (int i = 0; i < hydraulicNodes.Count; i++)
            {
                HydraulicNode node = hydraulicNodes[i];
                if (node == null)
                {
                    continue;
                }

                averagePressure += node.CurrentPressure;
                validCount++;
            }

            if (validCount == 0)
            {
                return;
            }

            averagePressure /= validCount;

            for (int i = 0; i < hydraulicNodes.Count; i++)
            {
                HydraulicNode node = hydraulicNodes[i];
                if (node == null)
                {
                    continue;
                }

                if (node.IsPressureSource)
                {
                    node.SetPressure(node.BasePressure);
                    continue;
                }

                float target = Mathf.Lerp(node.CurrentPressure, averagePressure, pressureEqualizationRate * deltaTime / node.Compliance);
                if (node.IsPressureSink)
                {
                    target = Mathf.Min(target, node.BasePressure);
                }

                target = Mathf.Lerp(target, node.BasePressure, pressureLeakRate * deltaTime);
                node.SetPressure(target);
            }
        }

        /// <summary>
        /// Computes stress for a specific bridge segment.
        /// </summary>
        public float GetBridgeStress(BridgeSegment segment, float additionalLoad = 0f)
        {
            return segment == null ? 0f : segment.ComputeStress(additionalLoad);
        }

        /// <summary>
        /// Checks whether a bridge segment is structurally stable.
        /// </summary>
        public bool IsBridgeStable(BridgeSegment segment, float additionalLoad = 0f)
        {
            if (segment == null || segment.Failed)
            {
                return false;
            }

            return segment.ComputeStress(additionalLoad) <= segment.MaterialStrength * overloadFailureThreshold &&
                   segment.ComputeStability(additionalLoad) >= stabilityPassThreshold;
        }

        /// <summary>
        /// Returns true when at least one bridge segment is registered and every registered
        /// segment is stable under <paramref name="additionalLoad"/>.
        /// </summary>
        public bool AreAllBridgesStable(float additionalLoad = 0f)
        {
            int checkedSegments = 0;
            for (int i = 0; i < bridgeSegments.Count; i++)
            {
                BridgeSegment segment = bridgeSegments[i];
                if (segment == null)
                {
                    continue;
                }

                checkedSegments++;
                if (!IsBridgeStable(segment, additionalLoad))
                {
                    return false;
                }
            }

            return checkedSegments > 0;
        }

        /// <summary>
        /// Returns a score representing how much weight the bridge can support.
        /// </summary>
        public float GetWeightCapacityScore()
        {
            if (bridgeSegments.Count == 0)
            {
                return 0f;
            }

            float totalScore = 0f;
            int validSegments = 0;

            for (int i = 0; i < bridgeSegments.Count; i++)
            {
                BridgeSegment segment = bridgeSegments[i];
                if (segment == null)
                {
                    continue;
                }

                validSegments++;
                float safetyMargin = Mathf.Max(0f, segment.MaterialStrength - segment.ComputeStress());
                float geometryFactor = Mathf.Sqrt(Mathf.Max(0.0001f, segment.CrossSectionArea)) / Mathf.Max(0.0001f, segment.Length);
                totalScore += (safetyMargin * geometryFactor) + weightCapacityBias;
            }

            return validSegments == 0 ? 0f : totalScore / validSegments;
        }

        private void EvaluateBridgeIntegrity()
        {
            for (int i = 0; i < bridgeSegments.Count; i++)
            {
                BridgeSegment segment = bridgeSegments[i];
                if (segment == null || segment.Failed)
                {
                    continue;
                }

                float stress = segment.ComputeStress();
                if (stress > segment.MaterialStrength * overloadFailureThreshold)
                {
                    TriggerFailure(segment);
                }
            }
        }

        private void TriggerFailure(BridgeSegment segment)
        {
            if (segment == null)
            {
                return;
            }

            segment.Fail();
            OnBridgeSegmentFailed(segment);
            SegmentFailed?.Invoke(segment);
        }

        /// <summary>
        /// Failure animation hook invoked when a segment exceeds its safe stress limit.
        /// </summary>
        /// <param name="segment">The failed bridge segment.</param>
        protected virtual void OnBridgeSegmentFailed(BridgeSegment segment)
        {
            // Intentionally empty: override in derived classes or hook via event.
        }

        private void Reset()
        {
            hydraulicNodes = new List<HydraulicNode>();
            bridgeSegments = new List<BridgeSegment>();
        }
    }
}
