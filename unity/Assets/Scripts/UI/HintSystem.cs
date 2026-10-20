using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using EcoBotBuilders.Network;

namespace EcoBotBuilders.UI
{
    /// <summary>
    /// Hint presentation tiers used to escalate guidance quality as the player retries a task.
    /// </summary>
    public enum HintTier
    {
        /// <summary>Light nudge pointing the player toward the goal.</summary>
        Gentle = 0,

        /// <summary>More explicit hint focused on the next useful action.</summary>
        Directed = 1,

        /// <summary>Very explicit guidance that removes ambiguity.</summary>
        Explicit = 2,

        /// <summary>Final rescue hint that can outline a step-by-step solution.</summary>
        Rescue = 3
    }

    /// <summary>
    /// Data container for a single hint entry.
    /// </summary>
    [Serializable]
    public class HintData
    {
        [SerializeField]
        private int level;

        [SerializeField]
        private HintTier tier;

        [SerializeField]
        [TextArea(2, 5)]
        private string hintText;

        [SerializeField]
        [TextArea(1, 3)]
        private string pedagogicalTheme;

        /// <summary>Gets or sets the level this hint belongs to.</summary>
        public int Level
        {
            get => level;
            set => level = value;
        }

        /// <summary>Gets or sets the escalation tier for this hint.</summary>
        public HintTier Tier
        {
            get => tier;
            set => tier = value;
        }

        /// <summary>Gets or sets the user-facing hint text.</summary>
        public string HintText
        {
            get => hintText;
            set => hintText = value;
        }

        /// <summary>Gets or sets the pedagogical theme that frames the hint.</summary>
        public string PedagogicalTheme
        {
            get => pedagogicalTheme;
            set => pedagogicalTheme = value;
        }

        /// <summary>Creates an empty hint data record.</summary>
        public HintData()
        {
        }

        /// <summary>Creates a hint data record with all fields initialized.</summary>
        public HintData(int level, HintTier tier, string hintText, string pedagogicalTheme)
        {
            this.level = level;
            this.tier = tier;
            this.hintText = hintText;
            this.pedagogicalTheme = pedagogicalTheme;
        }
    }

    /// <summary>
    /// Controls the in-game hint panel and escalates hints as the player retries a level.
    /// </summary>
    public class HintSystem : MonoBehaviour
    {
        [Serializable]
        private class LevelHintSet
        {
            [SerializeField] private int level;
            [SerializeField] private string pedagogicalTheme;
            [SerializeField] private List<HintData> hints = new List<HintData>(4);

            public int Level => level;
            public string PedagogicalTheme => pedagogicalTheme;
            public IReadOnlyList<HintData> Hints => hints;

            public LevelHintSet(int level, string pedagogicalTheme, params string[] hintTexts)
            {
                this.level = level;
                this.pedagogicalTheme = pedagogicalTheme;
                hints = new List<HintData>(Mathf.Max(4, hintTexts?.Length ?? 0));
                if (hintTexts == null)
                {
                    return;
                }

                var tiers = (HintTier[])Enum.GetValues(typeof(HintTier));
                for (int i = 0; i < hintTexts.Length; i++)
                {
                    hints.Add(new HintData(level, tiers[Mathf.Clamp(i, 0, tiers.Length - 1)], hintTexts[i], pedagogicalTheme));
                }
            }
        }

        private static readonly HintTier[] TierOrder =
        {
            HintTier.Gentle,
            HintTier.Directed,
            HintTier.Explicit,
            HintTier.Rescue
        };

        [Header("UI References")]
        [SerializeField] private CanvasGroup hintCanvasGroup;
        [SerializeField] private Text hintTextLabel;
        [SerializeField] private Text hintThemeLabel;
        [SerializeField] private Animator hintAnimator;

        [Header("Animation")]
        [SerializeField] private float fadeDuration = 0.2f;
        [SerializeField] private float revealDelay = 0.02f;
        [SerializeField] private bool useUnscaledTime = true;

        [Header("Hint Content")]
        [SerializeField] private List<HintData> hintDatabase = new List<HintData>();

        private readonly Dictionary<int, Dictionary<HintTier, HintData>> hintLookup = new Dictionary<int, Dictionary<HintTier, HintData>>();
        private Coroutine activeHintRoutine;
        private string currentDisplayedHint;
        private HintData currentHintData;
        private HintData hintHiddenByPause;
        private NetworkManager subscribedNetwork;
        private bool classPaused;

        /// <summary>Gets the current hint text displayed by the system.</summary>
        public string CurrentDisplayedHint => currentDisplayedHint;

        /// <summary>True while the teacher has paused the class ("Eyes Up"); hints stay hidden until resume.</summary>
        public bool IsClassPaused => classPaused;

        private void OnEnable()
        {
            TrySubscribeToNetwork();
        }

        private void Start()
        {
            // NetworkManager.Instance is assigned in its Awake, which may run after our OnEnable.
            TrySubscribeToNetwork();
        }

        private void OnDisable()
        {
            UnsubscribeFromNetwork();
        }

        private void TrySubscribeToNetwork()
        {
            NetworkManager network = NetworkManager.Instance;
            if (network == null || network == subscribedNetwork)
            {
                return;
            }

            UnsubscribeFromNetwork();
            subscribedNetwork = network;
            subscribedNetwork.OnClassPaused += HandleClassPaused;
            subscribedNetwork.OnClassResumed += HandleClassResumed;
        }

        private void UnsubscribeFromNetwork()
        {
            if (subscribedNetwork == null)
            {
                return;
            }

            subscribedNetwork.OnClassPaused -= HandleClassPaused;
            subscribedNetwork.OnClassResumed -= HandleClassResumed;
            subscribedNetwork = null;
        }

        private void HandleClassPaused(RoomSignalData _)
        {
            if (classPaused)
            {
                return;
            }

            classPaused = true;
            hintHiddenByPause = currentHintData;
            HideHintInternal();
        }

        private void HandleClassResumed(RoomSignalData _)
        {
            if (!classPaused)
            {
                return;
            }

            classPaused = false;
            HintData restore = hintHiddenByPause;
            hintHiddenByPause = null;
            if (restore != null)
            {
                ShowHint(restore);
            }
        }

        private void Awake()
        {
            BuildDefaultDatabaseIfNeeded();
            RebuildLookup();
            EnsureCanvasGroupDefaults();
        }

        /// <summary>
        /// Shows a hint for the given level and retry count, escalating by retry attempts.
        /// </summary>
        /// <param name="level">The gameplay level being attempted.</param>
        /// <param name="retryCount">The number of retries so far.</param>
        public void ShowHint(int level, int retryCount)
        {
            if (!TryGetHintForRetry(level, retryCount, out var hint))
            {
                HideHint();
                return;
            }

            ShowHint(hint);
        }

        /// <summary>
        /// Shows a specific hint entry immediately.
        /// </summary>
        /// <param name="hintData">The hint data to present.</param>
        public void ShowHint(HintData hintData)
        {
            if (hintData == null)
            {
                HideHint();
                return;
            }

            if (classPaused)
            {
                // Remember the hint and reveal it once the teacher resumes the class.
                hintHiddenByPause = hintData;
                return;
            }

            currentHintData = hintData;
            if (activeHintRoutine != null)
            {
                StopCoroutine(activeHintRoutine);
                activeHintRoutine = null;
            }

            activeHintRoutine = StartCoroutine(AnimateHintDisplayCoroutine(hintData));
        }

        /// <summary>
        /// Hides the hint panel and clears the displayed text.
        /// </summary>
        public void HideHint()
        {
            hintHiddenByPause = null;
            HideHintInternal();
        }

        private void HideHintInternal()
        {
            currentHintData = null;
            if (activeHintRoutine != null)
            {
                StopCoroutine(activeHintRoutine);
                activeHintRoutine = null;
            }

            currentDisplayedHint = string.Empty;
            if (hintTextLabel != null)
            {
                hintTextLabel.text = string.Empty;
            }

            if (hintThemeLabel != null)
            {
                hintThemeLabel.text = string.Empty;
            }

            if (hintCanvasGroup != null)
            {
                hintCanvasGroup.alpha = 0f;
                hintCanvasGroup.interactable = false;
                hintCanvasGroup.blocksRaycasts = false;
            }
        }

        private void EnsureCanvasGroupDefaults()
        {
            if (hintCanvasGroup == null)
            {
                hintCanvasGroup = GetComponentInChildren<CanvasGroup>(true);
            }

            if (hintCanvasGroup != null)
            {
                hintCanvasGroup.alpha = 0f;
                hintCanvasGroup.interactable = false;
                hintCanvasGroup.blocksRaycasts = false;
            }
        }

        private void BuildDefaultDatabaseIfNeeded()
        {
            if (hintDatabase == null)
            {
                hintDatabase = new List<HintData>();
            }

            if (hintDatabase.Count >= 40)
            {
                return;
            }

            hintDatabase.Clear();
            AddLevelHints(1, "Orientation and observation",
                "Watch where your bot is facing!",
                "Grid coordinates: (column, row). Your bot starts at (1,1).",
                "Try placing one foundational piece before adding extras.",
                "Start with the simplest valid configuration, then refine it.");
            AddLevelHints(2, "Cause and effect",
                "Do you see a pattern? Move, clean, move, clean...",
                "A Repeat block runs the blocks inside it again and again. Count the panels to pick the number.",
                "The correct choice should improve the system, not just decorate it.",
                "Use the action that directly solves the bottleneck first.");
            AddLevelHints(3, "Pattern recognition",
                "The beds make a square. How many sides does a square have?",
                "Put one Repeat block inside another: the inside loop waters one side, the outside loop does all 4 sides.",
                "Match the sequence rather than guessing a new pattern.",
                "Follow the repeating rule exactly to complete the build.");
            AddLevelHints(4, "Planning ahead",
                "Look at the wind arrows. Which way are the turbines pointing?",
                "A turbine makes the most energy when it faces straight into the wind. Each rotation turns it 90 degrees.",
                "Avoid blocking the path you will need later.",
                "Plan the full route before committing to the first placement.");
            AddLevelHints(5, "Resource awareness",
                "Not every item is the same. What is different about the recyclables?",
                "If something is true, the bot does the first action. Else it does the second one.",
                "Spend only what is necessary to keep options open.",
                "Optimise by reserving scarce resources for the critical step.");
            AddLevelHints(6, "Error detection",
                "Watch the water gauge. Is it rising faster or slower than it drains?",
                "Water in minus water out = change in level. Open a gate when the level gets too high.",
                "Remove the piece that is causing the incorrect behavior.",
                "Fix the conflicting element first, then rebuild the sequence.");
            AddLevelHints(7, "Systems thinking",
                "Where does the bridge sag the most when the truck drives on it?",
                "Triangles are strong shapes. Build one support pattern as a subroutine and call it for every gap.",
                "Balance the connected parts so they work together.",
                "Adjust the linked components until the whole chain stabilizes.");
            AddLevelHints(8, "Experimentation",
                "Some plants look droopy, others look fine. Does every plant need water?",
                "A sensor gives your bot information. If moisture is below the line, then water. 'Always' keeps checking.",
                "Use evidence from the result to choose your next move.",
                "Systematically test each possibility until the pattern becomes clear.");
            AddLevelHints(9, "Transfer and reuse",
                "Watch the battery meter when the sun goes down. What happens?",
                "Store extra energy in the day and use it at night. Make one subroutine for day and one for night.",
                "Identify what stayed the same and what changed.",
                "Apply the proven method and adjust the details for this layout.");
            AddLevelHints(10, "Mastery and reflection",
                "Break the big mission into smaller jobs. Which job is the most urgent?",
                "Reuse your best ideas: make a subroutine for each job, then call them from one Always loop.",
                "Everything you need is already in the scene; combine it carefully.",
                "Execute the full solution cleanly from start to finish.");
        }

        private void AddLevelHints(int level, string theme, string gentle, string directed, string explicitHint, string rescue)
        {
            hintDatabase.Add(new HintData(level, HintTier.Gentle, gentle, theme));
            hintDatabase.Add(new HintData(level, HintTier.Directed, directed, theme));
            hintDatabase.Add(new HintData(level, HintTier.Explicit, explicitHint, theme));
            hintDatabase.Add(new HintData(level, HintTier.Rescue, rescue, theme));
        }

        private void RebuildLookup()
        {
            hintLookup.Clear();
            if (hintDatabase == null)
            {
                return;
            }

            foreach (var hint in hintDatabase)
            {
                if (hint == null)
                {
                    continue;
                }

                if (!hintLookup.TryGetValue(hint.Level, out var tierMap))
                {
                    tierMap = new Dictionary<HintTier, HintData>();
                    hintLookup.Add(hint.Level, tierMap);
                }

                tierMap[hint.Tier] = hint;
            }
        }

        private bool TryGetHintForRetry(int level, int retryCount, out HintData hint)
        {
            hint = null;
            if (!hintLookup.TryGetValue(level, out var tierMap) || tierMap == null)
            {
                return false;
            }

            var tierIndex = Mathf.Clamp(retryCount, 0, TierOrder.Length - 1);
            for (int i = tierIndex; i < TierOrder.Length; i++)
            {
                if (tierMap.TryGetValue(TierOrder[i], out hint) && hint != null)
                {
                    return true;
                }
            }

            foreach (var tier in TierOrder)
            {
                if (tierMap.TryGetValue(tier, out hint) && hint != null)
                {
                    return true;
                }
            }

            return false;
        }

        private System.Collections.IEnumerator AnimateHintDisplayCoroutine(HintData hintData)
        {
            if (hintData == null)
            {
                yield break;
            }

            currentDisplayedHint = hintData.HintText ?? string.Empty;
            if (hintTextLabel != null)
            {
                hintTextLabel.text = string.Empty;
            }

            if (hintThemeLabel != null)
            {
                hintThemeLabel.text = hintData.PedagogicalTheme ?? string.Empty;
            }

            SetCanvasVisible(true);
            yield return FadeCanvasGroup(0f, 1f, fadeDuration);

            if (hintAnimator != null)
            {
                hintAnimator.ResetTrigger("HideHint");
                hintAnimator.SetTrigger("ShowHint");
            }

            if (hintTextLabel == null)
            {
                activeHintRoutine = null;
                yield break;
            }

            var fullText = currentDisplayedHint;
            var builder = new StringBuilder(fullText.Length);
            for (int i = 0; i < fullText.Length; i++)
            {
                builder.Append(fullText[i]);
                hintTextLabel.text = builder.ToString();
                yield return Wait(revealDelay);
            }

            activeHintRoutine = null;
        }

        private System.Collections.IEnumerator FadeCanvasGroup(float from, float to, float duration)
        {
            if (hintCanvasGroup == null)
            {
                yield break;
            }

            if (duration <= 0f)
            {
                hintCanvasGroup.alpha = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                hintCanvasGroup.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }

            hintCanvasGroup.alpha = to;
        }

        private System.Collections.IEnumerator Wait(float seconds)
        {
            if (seconds <= 0f)
            {
                yield return null;
                yield break;
            }

            if (useUnscaledTime)
            {
                float elapsed = 0f;
                while (elapsed < seconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            else
            {
                yield return new WaitForSeconds(seconds);
            }
        }

        private void SetCanvasVisible(bool visible)
        {
            if (hintCanvasGroup == null)
            {
                return;
            }

            hintCanvasGroup.interactable = visible;
            hintCanvasGroup.blocksRaycasts = visible;
        }

#if DOTWEEN
        // DOTween integration is optional. The project compiles cleanly even when the package is absent.
#endif
    }
}
