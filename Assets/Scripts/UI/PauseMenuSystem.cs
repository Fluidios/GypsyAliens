using GypsyAliens.Audio;
using GypsyAliens.Core;
using GypsyAliens.Network;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GypsyAliens.UI
{
    /// <summary>
    /// Local overlay: match pause (Escape) or hub settings (gear). Does not pause simulation.
    /// </summary>
    public sealed class PauseMenuSystem : GameSystemBehaviour<PauseMenuSystem>
    {
        [SerializeField] int _sortingOrder = 120;

        Canvas _canvas;
        GameObject _root;
        Text _title;
        Text _hint;
        Text _volumeLabel;
        Slider _volumeSlider;
        Button _resumeButton;
        Button _exitButton;
        bool _open;
        bool _hubMode;

        public bool IsOpen => _open;

        protected override void Awake()
        {
            base.Awake();
            GameAudioSettings.ApplySavedOrDefault();
            EnsureUi();
            SetOpen(false);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            if (_open)
            {
                // Escape always closes; hub mode never opens via Escape.
                SetOpen(false);
                return;
            }

            if (!IsInActiveMatch())
            {
                return;
            }

            // Don't fight the victory overlay.
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<MissionProgressUISystem>(out var mission)
                && mission.IsVictoryVisible)
            {
                return;
            }

            OpenMatchPause();
        }

        static bool IsInActiveMatch()
        {
            return NetworkGameSession.Instance != null
                && NetworkGameSession.Instance.Object != null
                && NetworkGameSession.Instance.Object.IsValid
                && NetworkGameSession.Instance.GameplayReady;
        }

        /// <summary>Match pause: Resume + volume + Exit to Hub.</summary>
        public void OpenMatchPause()
        {
            ApplyMode(hubMode: false);
            SetOpen(true);
        }

        /// <summary>Hub settings from the gear button: volume + Continue only.</summary>
        public void OpenHubSettings()
        {
            ApplyMode(hubMode: true);
            SetOpen(true);
        }

        public void SetOpen(bool open)
        {
            EnsureUi();
            _open = open;
            if (_root != null)
            {
                _root.SetActive(open);
            }

            if (open && _volumeSlider != null)
            {
                _volumeSlider.SetValueWithoutNotify(GameAudioSettings.MasterVolume);
                RefreshVolumeLabel(GameAudioSettings.MasterVolume);
            }
        }

        public void Resume()
        {
            SetOpen(false);
        }

        public void ExitToHub()
        {
            _ = ExitToHubAsync();
        }

        async System.Threading.Tasks.Task ExitToHubAsync()
        {
            SetOpen(false);

            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<MissionProgressUISystem>(out var mission))
            {
                mission.HideAllOverlays();
            }

            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<NetworkService>(out var network))
            {
                await network.ShutdownAsync();
            }

            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<ConnectionUISystem>(out var connection))
            {
                connection.SetMenuVisible(true);
            }
        }

        void ApplyMode(bool hubMode)
        {
            EnsureUi();
            _hubMode = hubMode;

            if (_title != null)
            {
                _title.text = hubMode ? "Settings" : "Paused";
            }

            if (_hint != null)
            {
                _hint.text = hubMode
                    ? "Adjust game volume."
                    : "The match keeps running for other players.";
            }

            if (_resumeButton != null)
            {
                var label = _resumeButton.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = hubMode ? "Continue" : "Resume";
                }
            }

            if (_exitButton != null)
            {
                _exitButton.gameObject.SetActive(!hubMode);
            }

            if (_resumeButton != null)
            {
                var resumeRt = _resumeButton.GetComponent<RectTransform>();
                if (resumeRt != null)
                {
                    resumeRt.anchoredPosition = hubMode
                        ? new Vector2(0f, -90f)
                        : new Vector2(0f, -70f);
                }
            }

            // Match needs room for Exit; hub is shorter.
            var card = _root != null ? _root.transform.Find("Card") : null;
            if (card is RectTransform cardRt)
            {
                cardRt.sizeDelta = hubMode
                    ? new Vector2(420f, 300f)
                    : new Vector2(420f, 360f);
            }
        }

        void EnsureUi()
        {
            if (_canvas != null && _root != null)
            {
                return;
            }

            var canvasGo = new GameObject("PauseMenuCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = _sortingOrder;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("PauseRoot");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRt = _root.AddComponent<RectTransform>();
            StretchFull(rootRt);

            var dim = CreateImage(_root.transform, "Dim", new Color(0.02f, 0.04f, 0.06f, 0.72f));
            StretchFull(dim.rectTransform);

            var card = CreateImage(_root.transform, "Card", new Color(0.08f, 0.1f, 0.12f, 0.94f));
            var cardRt = card.rectTransform;
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(420f, 360f);

            _title = CreateLabel(_root.transform, "Title", "Paused", 42, FontStyle.Bold, new Vector2(0f, 120f), new Vector2(380f, 56f));
            _hint = CreateLabel(
                _root.transform,
                "Hint",
                "The match keeps running for other players.",
                18,
                FontStyle.Normal,
                new Vector2(0f, 72f),
                new Vector2(380f, 40f));

            _volumeLabel = CreateLabel(
                _root.transform,
                "VolumeLabel",
                "Volume 50%",
                20,
                FontStyle.Normal,
                new Vector2(0f, 28f),
                new Vector2(380f, 28f));

            _volumeSlider = CreateSlider(_root.transform, "VolumeSlider", new Vector2(0f, -10f), new Vector2(300f, 28f));
            _volumeSlider.minValue = 0f;
            _volumeSlider.maxValue = 1f;
            _volumeSlider.wholeNumbers = false;
            _volumeSlider.SetValueWithoutNotify(GameAudioSettings.MasterVolume);
            _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            RefreshVolumeLabel(GameAudioSettings.MasterVolume);

            _resumeButton = CreateButton(_root.transform, "ResumeButton", new Vector2(0f, -70f), "Resume");
            _resumeButton.onClick.AddListener(Resume);

            _exitButton = CreateButton(_root.transform, "ExitButton", new Vector2(0f, -140f), "Exit to Hub");
            _exitButton.onClick.AddListener(ExitToHub);
        }

        void OnVolumeChanged(float value)
        {
            GameAudioSettings.SetMasterVolume(value);
            RefreshVolumeLabel(value);
        }

        void RefreshVolumeLabel(float value)
        {
            if (_volumeLabel != null)
            {
                _volumeLabel.text = $"Volume {Mathf.RoundToInt(value * 100f)}%";
            }
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        static Text CreateLabel(
            Transform parent,
            string name,
            string text,
            int size,
            FontStyle style,
            Vector2 anchoredPos,
            Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            var label = go.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null)
            {
                label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        static Button CreateButton(Transform parent, string name, Vector2 anchoredPos, string caption)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(280f, 56f);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.18f, 0.45f, 0.55f, 1f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            StretchFull(labelRt);
            var label = labelGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null)
            {
                label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            label.text = caption;
            label.fontSize = 26;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            return button;
        }

        static Slider CreateSlider(Transform parent, string name, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            var slider = go.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;

            var bg = CreateImage(go.transform, "Background", new Color(0.15f, 0.18f, 0.2f, 1f));
            StretchFull(bg.rectTransform);
            bg.raycastTarget = true;

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.25f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.75f);
            fillAreaRt.offsetMin = new Vector2(6f, 0f);
            fillAreaRt.offsetMax = new Vector2(-6f, 0f);

            var fill = CreateImage(fillArea.transform, "Fill", new Color(0.25f, 0.7f, 0.75f, 1f));
            StretchFull(fill.rectTransform);
            fill.raycastTarget = false;

            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRt = handleArea.AddComponent<RectTransform>();
            StretchFull(handleAreaRt);
            handleAreaRt.offsetMin = new Vector2(10f, 0f);
            handleAreaRt.offsetMax = new Vector2(-10f, 0f);

            var handle = CreateImage(handleArea.transform, "Handle", new Color(0.9f, 0.95f, 1f, 1f));
            handle.rectTransform.sizeDelta = new Vector2(22f, 22f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }
    }
}
