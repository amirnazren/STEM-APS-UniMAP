using System;
using UnityEngine;

namespace EcoBotBuilders.Core
{
    /// <summary>
    /// Drives a full day/night simulation, including lighting, skybox color transitions,
    /// renewable power output, battery tracking, and phase-change notifications.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class DayNightController : MonoBehaviour
    {
        [Serializable]
        public enum DayPhase
        {
            Dawn,
            Day,
            Dusk,
            Night
        }

        [Header("Time")]
        [SerializeField, Min(1f)] private float dayLengthSeconds = 600f;
        [SerializeField, Range(0f, 24f)] private float startTimeHours = 8f;
        [SerializeField, Range(0f, 1f)] private float initialNormalizedBattery = 0.5f;
        [SerializeField] private bool advanceTime = true;

        [Header("Solar")]
        [SerializeField] private AnimationCurve solarOutputCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 0f);
        [SerializeField, Min(0f)] private float maxSolarOutput = 10f;

        [Header("Wind")]
        [SerializeField, Min(0f)] private float baseWindOutput = 4f;
        [SerializeField, Min(0f)] private float windVariance = 1.5f;
        [SerializeField, Min(0.1f)] private float windNoiseIntervalSeconds = 10f;
        [SerializeField] private Vector2 windNoiseMeanRange = new Vector2(0.2f, 0.8f);

        [Header("Battery")]
        [SerializeField, Min(0f)] private float batteryCapacity = 100f;
        [SerializeField, Min(0f)] private float batteryLoad = 5f;
        [SerializeField, Min(0f)] private float batteryChargeRate = 1f;
        [SerializeField, Min(0f)] private float batteryDischargeRate = 1f;

        [Header("Skybox / URP")]
        [SerializeField] private Light sunLight;
        [SerializeField] private Gradient skyGradient;
        [SerializeField] private Gradient ambientGradient;
        [SerializeField] private Color clearColor = new Color(0.45f, 0.65f, 0.95f, 1f);
        [SerializeField] private Color nightColor = new Color(0.02f, 0.03f, 0.08f, 1f);
        [SerializeField] private bool updateFogColor = true;
        [SerializeField] private bool updateSkyboxTintIfAvailable = true;

        [Header("Phase Thresholds")]
        [SerializeField, Range(0f, 1f)] private float dawnStartNormalized = 0.20f;
        [SerializeField, Range(0f, 1f)] private float dayStartNormalized = 0.30f;
        [SerializeField, Range(0f, 1f)] private float duskStartNormalized = 0.70f;
        [SerializeField, Range(0f, 1f)] private float nightStartNormalized = 0.80f;

        /// <summary>Raised whenever the current day phase changes. Intended for audio crossfades and other phase-driven systems.</summary>
        public event Action<DayPhase> OnDayPhaseChanged;

        /// <summary>Current time of day in hours [0, 24).</summary>
        public float TimeOfDayHours { get; private set; }

        /// <summary>Current normalized day time in the range [0, 1).</summary>
        public float NormalizedTimeOfDay => TimeOfDayHours / 24f;

        /// <summary>Current day phase.</summary>
        public DayPhase CurrentPhase { get; private set; } = DayPhase.Day;

        /// <summary>True when the current phase is Day.</summary>
        public bool IsDaytime => CurrentPhase == DayPhase.Day;

        /// <summary>Current solar output in power units.</summary>
        public float SolarOutput { get; private set; }

        /// <summary>Current wind output in power units.</summary>
        public float WindOutput { get; private set; }

        /// <summary>Total power balance: generation minus load.</summary>
        public float PowerBalance { get; private set; }

        /// <summary>Current battery charge in energy units.</summary>
        public float BatteryCharge { get; private set; }

        /// <summary>Battery charge normalized to [0, 1].</summary>
        public float BatteryNormalized => NormalizeBatteryCharge(BatteryCharge);

        private Material _runtimeSkyboxMaterial;
        private float _windNoiseTimer;
        private float _windNoiseValue;
        private bool _hasInitialized;

        /// <summary>
        /// Normalizes a battery charge value against the configured capacity.
        /// </summary>
        /// <param name="charge">Battery charge in energy units.</param>
        /// <returns>Charge normalized to the range [0, 1].</returns>
        public float NormalizeBatteryCharge(float charge)
        {
            if (batteryCapacity <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(charge / batteryCapacity);
        }

        /// <summary>
        /// Forces the controller to evaluate all outputs and apply visuals immediately.
        /// </summary>
        public void RefreshNow()
        {
            EvaluateState(0f, true);
        }

        private void Awake()
        {
            TimeOfDayHours = Mathf.Repeat(startTimeHours, 24f);
            BatteryCharge = Mathf.Clamp01(initialNormalizedBattery) * Mathf.Max(0f, batteryCapacity);
            InitializeWindNoise(true);
            SetupSkyboxMaterial();
            EvaluateState(0f, true);
            _hasInitialized = true;
        }

        private void OnEnable()
        {
            if (_hasInitialized)
            {
                EvaluateState(0f, true);
            }
        }

        private void Update()
        {
            if (advanceTime)
            {
                AdvanceTime(Time.deltaTime);
            }
            else
            {
                EvaluateState(Time.deltaTime, false);
            }
        }

        /// <summary>
        /// Advances simulation time by the specified delta.
        /// </summary>
        /// <param name="deltaTime">Elapsed seconds.</param>
        public void AdvanceTime(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                return;
            }

            if (dayLengthSeconds <= 0.0001f)
            {
                dayLengthSeconds = 1f;
            }

            float hoursPerSecond = 24f / dayLengthSeconds;
            TimeOfDayHours = Mathf.Repeat(TimeOfDayHours + deltaTime * hoursPerSecond, 24f);
            EvaluateState(deltaTime, true);
        }

        private void EvaluateState(float deltaTime, bool allowWindTimer)
        {
            DayPhase phase = DeterminePhase(TimeOfDayHours / 24f);
            if (phase != CurrentPhase)
            {
                CurrentPhase = phase;
                OnDayPhaseChanged?.Invoke(CurrentPhase);
            }

            UpdateOutputs(deltaTime, allowWindTimer);
            UpdateBattery(deltaTime);
            UpdateVisuals();
        }

        private void UpdateOutputs(float deltaTime, bool allowWindTimer)
        {
            float normalizedDayTime = TimeOfDayHours / 24f;
            float solarT = Mathf.Clamp01(solarOutputCurve != null ? solarOutputCurve.Evaluate(normalizedDayTime) : 0f);
            SolarOutput = maxSolarOutput * solarT;

            if (allowWindTimer)
            {
                _windNoiseTimer += deltaTime;
                if (_windNoiseTimer >= windNoiseIntervalSeconds)
                {
                    _windNoiseTimer = 0f;
                    InitializeWindNoise(false);
                }
            }

            WindOutput = Mathf.Max(0f, baseWindOutput + ((_windNoiseValue - 0.5f) * 2f * windVariance));
            PowerBalance = SolarOutput + WindOutput - batteryLoad;
        }

        private void UpdateBattery(float deltaTime)
        {
            if (batteryCapacity <= 0f)
            {
                BatteryCharge = 0f;
                return;
            }

            float rate = PowerBalance >= 0f ? batteryChargeRate : batteryDischargeRate;
            float delta = PowerBalance * rate * deltaTime;
            BatteryCharge = Mathf.Clamp(BatteryCharge + delta, 0f, batteryCapacity);
        }

        private void UpdateVisuals()
        {
            float normalizedTime = TimeOfDayHours / 24f;
            float skyBlend = ComputeSkyBlend(normalizedTime);
            Color targetColor = Color.Lerp(nightColor, clearColor, skyBlend);

            if (sunLight != null)
            {
                sunLight.color = targetColor;
                sunLight.intensity = Mathf.Lerp(0.05f, 1.25f, skyBlend);
                sunLight.transform.rotation = Quaternion.Euler((normalizedTime * 360f) - 90f, 170f, 0f);
            }

            RenderSettings.ambientLight = ambientGradient != null ? ambientGradient.Evaluate(skyBlend) : targetColor;
            if (updateFogColor)
            {
                RenderSettings.fogColor = targetColor;
            }

            if (updateSkyboxTintIfAvailable)
            {
                ApplySkyboxTint(skyBlend, targetColor);
            }
        }

        private void ApplySkyboxTint(float skyBlend, Color targetColor)
        {
            if (_runtimeSkyboxMaterial == null)
            {
                return;
            }

            if (_runtimeSkyboxMaterial.HasProperty("_Tint"))
            {
                _runtimeSkyboxMaterial.SetColor("_Tint", Color.Lerp(nightColor, targetColor, skyBlend));
            }
            else if (_runtimeSkyboxMaterial.HasProperty("_Color"))
            {
                _runtimeSkyboxMaterial.SetColor("_Color", Color.Lerp(nightColor, targetColor, skyBlend));
            }
        }

        private void SetupSkyboxMaterial()
        {
            if (RenderSettings.skybox == null)
            {
                return;
            }

            _runtimeSkyboxMaterial = Application.isPlaying
                ? Instantiate(RenderSettings.skybox)
                : RenderSettings.skybox;

            if (Application.isPlaying)
            {
                RenderSettings.skybox = _runtimeSkyboxMaterial;
            }
        }

        private void InitializeWindNoise(bool resetTimer)
        {
            _windNoiseValue = Mathf.Lerp(windNoiseMeanRange.x, windNoiseMeanRange.y, UnityEngine.Random.value);
            if (resetTimer)
            {
                _windNoiseTimer = 0f;
            }
        }

        private DayPhase DeterminePhase(float normalizedTime)
        {
            if (normalizedTime < dawnStartNormalized || normalizedTime >= nightStartNormalized)
            {
                return DayPhase.Night;
            }

            if (normalizedTime < dayStartNormalized)
            {
                return DayPhase.Dawn;
            }

            if (normalizedTime < duskStartNormalized)
            {
                return DayPhase.Day;
            }

            return DayPhase.Dusk;
        }

        private float ComputeSkyBlend(float normalizedTime)
        {
            float dawnWeight = SmoothRange(normalizedTime, dawnStartNormalized, dayStartNormalized);
            float dayWeight = SmoothRange(normalizedTime, dayStartNormalized, duskStartNormalized);
            float duskWeight = 1f - SmoothRange(normalizedTime, duskStartNormalized, nightStartNormalized);
            float nightWeight = (normalizedTime < dawnStartNormalized || normalizedTime >= nightStartNormalized) ? 0f : 1f;

            float blend = Mathf.Max(dawnWeight, dayWeight, duskWeight, nightWeight);
            return Mathf.Clamp01(blend);
        }

        private static float SmoothRange(float value, float min, float max)
        {
            if (max <= min)
            {
                return value >= max ? 1f : 0f;
            }

            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(min, max, value));
        }
    }
}
