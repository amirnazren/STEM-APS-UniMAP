using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EcoBotBuilders.Level1.View
{
    /// <summary>
    /// Offline level-select menu. Lists every shipped level; a level is playable only when
    /// <see cref="GameProgress.IsUnlocked"/> says so, so the lock is enforced by the persistence
    /// layer, not just hidden in the UI. Builds its own Canvas/camera/input in Awake like the
    /// game scene, so the scene asset only needs this one component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelSelectScreen : MonoBehaviour
    {
        const string GameSceneName = "MainGameScene";
        RectTransform root;
        Canvas canvas;

        void Awake()
        {
            Debug.Log("[Eco-Bot] Level Select ready (offline). Unlock state read from PlayerPrefs.");
            var regular = Resources.Load<Font>("Level1/Interface");
            var bold = Resources.Load<Font>("Level1/InterfaceBold");
            if (!regular || !bold) { Debug.LogError("[Eco-Bot] Bundled UI fonts missing."); enabled = false; return; }
            IslandUI.Regular = regular; IslandUI.Bold = bold;
            EnsureCameraAndInput();
            BuildUI();
        }

        void EnsureCameraAndInput()
        {
            if (!Camera.main)
            {
                var c = new GameObject("Offline camera", typeof(Camera)); c.tag = "MainCamera";
                c.GetComponent<Camera>().orthographic = true; c.transform.position = new Vector3(0, 0, -10);
            }
            Camera.main.clearFlags = CameraClearFlags.SolidColor; Camera.main.backgroundColor = IslandUI.Ocean;
            if (!FindObjectOfType<AudioListener>()) Camera.main.gameObject.AddComponent<AudioListener>();
            if (!FindObjectOfType<EventSystem>()) new GameObject("Offline input", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        void BuildUI()
        {
            canvas = new GameObject("Level select canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.transform.SetParent(transform, false); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            var backdrop = IslandUI.Panel("Ocean backdrop", canvas.transform, 0, 0, 0, 0, IslandUI.Ocean).rectTransform;
            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one; backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            root = IslandUI.Rect("Safe design area", canvas.transform, 0, 0, 1280, 720);
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f); root.pivot = new Vector2(.5f, .5f);

            IslandUI.Art("robot", root, 40, 34, 92, 92);
            IslandUI.Label("ECO-BOT BUILDERS", root, 150, 44, 520, 26, 17, IslandUI.Hex("8DD9C2"), true);
            IslandUI.Label("Choose your level", root, 148, 72, 700, 54, 40, IslandUI.Cream, true);
            IslandUI.Label("OFFLINE ADVENTURE  /  No sign-in. Just curiosity.", root, 150, 128, 900, 26, 14, IslandUI.Hex("8AB6B6"));

            for (int id = LevelCatalog.First; id <= LevelCatalog.Last; id++) BuildCard(id);

            IslandUI.Label("More levels are on the way. Clear a level to unlock the next one!", root, 150, 648, 1000, 28, 16, IslandUI.Hex("B5DAD7"));
        }

        void BuildCard(int id)
        {
            int index = id - LevelCatalog.First;
            float x = 150 + index * 500;
            float y = 200;
            float w = 440, h = 400;
            bool unlocked = GameProgress.IsUnlocked(id);
            bool completed = GameProgress.IsCompleted(id);
            var theme = LevelTheme.For(id);

            var card = IslandUI.Panel("Level " + id + " card", root, x, y, w, h, unlocked ? IslandUI.Cream : IslandUI.Hex("27525C"));
            IslandUI.Label("LEVEL " + id, card.transform, 28, 26, w - 56, 30, 18, unlocked ? IslandUI.Hex("3B7F74") : IslandUI.Hex("8AB6B6"), true);
            IslandUI.Label(theme.Title, card.transform, 28, 56, w - 56, 52, 32, unlocked ? IslandUI.Ink : IslandUI.Cream, true);
            IslandUI.Label(theme.Concept, card.transform, 28, 116, w - 56, 30, 18, unlocked ? IslandUI.Hex("3B7F74") : IslandUI.Hex("B5DAD7"), true);

            // Panel preview art: robot for L1, dusty panel for L2 (falls back to the target sprite).
            string preview = string.IsNullOrEmpty(theme.Board.Cleaned) ? "robot" : theme.Board.Target;
            IslandUI.Art(preview, card.transform, (w - 150) / 2f, 150, 150, 150);

            if (completed)
            {
                var badge = IslandUI.Label("✓ Completed", card.transform, 28, 150, w - 56, 30, 20, IslandUI.Hex("2E8B6B"), true);
                badge.alignment = TextAnchor.UpperRight;
            }

            if (unlocked)
            {
                int captured = id;
                IslandUI.Button(completed ? "Play again ▶" : "Play ▶", card.transform, (w - 300) / 2f, h - 86, 300, 60, IslandUI.Hex("8DD9C2"), () => Play(captured), 24);
            }
            else
            {
                var locked = IslandUI.Panel("Locked", card.transform, (w - 300) / 2f, h - 86, 300, 60, IslandUI.Hex("1C3A42"));
                var label = IslandUI.Label("Locked  ·  Finish Level " + (id - 1), locked.transform, 6, 0, 288, 60, 18, IslandUI.Cream, true);
                label.alignment = TextAnchor.MiddleCenter;
            }
        }

        void Play(int id)
        {
            if (!GameProgress.IsUnlocked(id)) return;   // defensive: never load a locked level
            GameProgress.SelectedLevel = id;
            SceneManager.LoadScene(GameSceneName);
        }
    }
}
