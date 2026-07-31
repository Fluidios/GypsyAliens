using GypsyAliens.Core;
using GypsyAliens.Network;
using UnityEngine;
using UnityEngine.UI;

namespace GypsyAliens.UI
{
    /// <summary>
    /// HUD for animal extraction progress, plus fullscreen victory overlay
    /// (restart mission / leave lobby).
    /// </summary>
    public sealed class MissionProgressUISystem : GameSystemBehaviour<MissionProgressUISystem>
    {
        [SerializeField] Canvas _canvas;
        [SerializeField] Text _progressText;
        [SerializeField] Text _statusText;
        [SerializeField] Image _panel;

        GameObject _victoryRoot;
        Text _victoryTitle;
        Text _victorySubtitle;
        Button _restartButton;
        Button _exitButton;
        Text _restartLabel;

        NetworkGameSession _boundSession;
        bool _wasLevelCompleted;

        public bool IsVictoryVisible => _victoryRoot != null && _victoryRoot.activeSelf;

        public void HideAllOverlays()
        {
            SetVictoryVisible(false);
            SetHudVisible(false);
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureUi();
            SetHudVisible(false);
            SetVictoryVisible(false);
        }

        void Update()
        {
            TryBindSession();
            Refresh();
        }

        protected override void OnDestroy()
        {
            UnbindSession();
            base.OnDestroy();
        }

        void TryBindSession()
        {
            var session = NetworkGameSession.Instance;
            if (session == _boundSession)
            {
                return;
            }

            UnbindSession();
            _boundSession = session;
            if (_boundSession != null)
            {
                _boundSession.MissionChanged += Refresh;
                _boundSession.GameplayReadyChanged += Refresh;
            }
        }

        void UnbindSession()
        {
            if (_boundSession != null)
            {
                _boundSession.MissionChanged -= Refresh;
                _boundSession.GameplayReadyChanged -= Refresh;
                _boundSession = null;
            }
        }

        void Refresh()
        {
            var session = NetworkGameSession.Instance;
            if (session == null || !session.GameplayReady)
            {
                SetHudVisible(false);
                if (session == null || !session.LevelCompleted)
                {
                    SetVictoryVisible(false);
                }

                return;
            }

            EnsureUi();

            if (session.LevelCompleted)
            {
                SetHudVisible(false);
                SetVictoryVisible(true);
                UpdateVictoryButtons(session);
                _wasLevelCompleted = true;
                return;
            }

            if (_wasLevelCompleted)
            {
                _wasLevelCompleted = false;
                SetVictoryVisible(false);
            }

            SetHudVisible(true);

            var required = session.AnimalsRequired;
            var extracted = session.AnimalsExtracted;
            if (_progressText != null)
            {
                _progressText.text = $"Animals abducted: {extracted}/{required}";
            }

            if (_statusText == null)
            {
                return;
            }

            if (!session.AnimalsObjectiveComplete)
            {
                _statusText.text = "Drag animals into the green evacuation zone.";
                _statusText.color = new Color(0.95f, 0.95f, 0.85f, 1f);
                return;
            }

            _statusText.text = "All animals secured — every player must enter the evacuation zone.";
            _statusText.color = new Color(1f, 0.9f, 0.35f, 1f);
        }

        void UpdateVictoryButtons(NetworkGameSession session)
        {
            var isHost = session.HasStateAuthority;
            if (_restartButton != null)
            {
                _restartButton.interactable = isHost;
            }

            if (_restartLabel != null)
            {
                _restartLabel.text = isHost ? "Restart Mission" : "Waiting for host...";
            }

            if (_victorySubtitle != null)
            {
                _victorySubtitle.text = isHost
                    ? "All agents evacuated. Restart the mission or leave the lobby."
                    : "All agents evacuated. Wait for the host to restart, or leave the lobby.";
            }
        }

        void OnRestartClicked()
        {
            var session = NetworkGameSession.Instance;
            if (session == null || !session.HasStateAuthority)
            {
                return;
            }

            session.RequestRestart();
            SetVictoryVisible(false);
        }

        void OnExitClicked()
        {
            _ = ExitLobbyAsync();
        }

        async System.Threading.Tasks.Task ExitLobbyAsync()
        {
            SetVictoryVisible(false);
            SetHudVisible(false);

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

        void SetHudVisible(bool visible)
        {
            if (_panel != null)
            {
                _panel.gameObject.SetActive(visible);
            }
        }

        void SetVictoryVisible(bool visible)
        {
            if (_victoryRoot != null)
            {
                _victoryRoot.SetActive(visible);
            }
        }

        void EnsureUi()
        {
            if (_canvas != null && _progressText != null && _statusText != null && _victoryRoot != null)
            {
                return;
            }

            if (_canvas == null)
            {
                var root = new GameObject("MissionProgressHUD");
                root.transform.SetParent(transform, false);

                _canvas = root.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 50;
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                root.AddComponent<GraphicRaycaster>();
            }

            if (_panel == null)
            {
                var panelGo = new GameObject("Panel");
                panelGo.transform.SetParent(_canvas.transform, false);
                _panel = panelGo.AddComponent<Image>();
                _panel.color = new Color(0.05f, 0.08f, 0.1f, 0.72f);
                var panelRt = _panel.rectTransform;
                panelRt.anchorMin = new Vector2(0f, 1f);
                panelRt.anchorMax = new Vector2(0f, 1f);
                panelRt.pivot = new Vector2(0f, 1f);
                panelRt.anchoredPosition = new Vector2(24f, -24f);
                panelRt.sizeDelta = new Vector2(520f, 96f);

                _progressText = CreateText(panelGo.transform, "Progress", 28, FontStyle.Bold, new Vector2(16f, -12f), new Vector2(488f, 36f));
                _statusText = CreateText(panelGo.transform, "Status", 20, FontStyle.Normal, new Vector2(16f, -50f), new Vector2(488f, 36f));
            }

            if (_victoryRoot == null)
            {
                BuildVictoryOverlay(_canvas.transform);
            }
        }

        void BuildVictoryOverlay(Transform parent)
        {
            _victoryRoot = new GameObject("VictoryOverlay");
            _victoryRoot.transform.SetParent(parent, false);

            var dim = _victoryRoot.AddComponent<Image>();
            dim.color = new Color(0.02f, 0.05f, 0.07f, 0.82f);
            var dimRt = dim.rectTransform;
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;

            var card = new GameObject("Card");
            card.transform.SetParent(_victoryRoot.transform, false);
            var cardImage = card.AddComponent<Image>();
            cardImage.color = new Color(0.08f, 0.12f, 0.14f, 0.95f);
            var cardRt = cardImage.rectTransform;
            cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(560f, 320f);
            cardRt.anchoredPosition = Vector2.zero;

            _victoryTitle = CreateText(card.transform, "Title", 42, FontStyle.Bold, new Vector2(0f, 100f), new Vector2(500f, 56f));
            _victoryTitle.alignment = TextAnchor.MiddleCenter;
            _victoryTitle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _victoryTitle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _victoryTitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _victoryTitle.text = "Mission Complete";
            _victoryTitle.color = new Color(0.55f, 1f, 0.7f, 1f);

            _victorySubtitle = CreateText(card.transform, "Subtitle", 22, FontStyle.Normal, new Vector2(0f, 40f), new Vector2(500f, 70f));
            _victorySubtitle.alignment = TextAnchor.MiddleCenter;
            _victorySubtitle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _victorySubtitle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _victorySubtitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _victorySubtitle.color = new Color(0.9f, 0.92f, 0.88f, 1f);

            _restartButton = CreateButton(card.transform, "RestartButton", new Vector2(0f, -40f), out _restartLabel);
            _restartLabel.text = "Restart Mission";
            _restartButton.onClick.AddListener(OnRestartClicked);

            _exitButton = CreateButton(card.transform, "ExitButton", new Vector2(0f, -110f), out var exitLabel);
            exitLabel.text = "Exit Lobby";
            _exitButton.onClick.AddListener(OnExitClicked);

            _victoryRoot.SetActive(false);
        }

        static Button CreateButton(Transform parent, string name, Vector2 anchoredPos, out Text label)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.18f, 0.55f, 0.4f, 1f);
            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.25f, 0.7f, 0.5f, 1f);
            colors.pressedColor = new Color(0.12f, 0.4f, 0.3f, 1f);
            colors.disabledColor = new Color(0.25f, 0.25f, 0.25f, 0.7f);
            button.colors = colors;

            var rt = image.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(320f, 52f);

            label = CreateText(go.transform, "Label", 24, FontStyle.Bold, Vector2.zero, new Vector2(300f, 44f));
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.raycastTarget = false;
            return button;
        }

        static Text CreateText(Transform parent, string name, int size, FontStyle style, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null)
            {
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return text;
        }
    }
}
