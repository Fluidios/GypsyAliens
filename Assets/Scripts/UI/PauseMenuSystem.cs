using GypsyAliens.Core;
using GypsyAliens.Network;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GypsyAliens.UI
{
    /// <summary>
    /// Local Escape overlay: Resume or leave to hub. Does not pause simulation time (multiplayer).
    /// </summary>
    public sealed class PauseMenuSystem : GameSystemBehaviour<PauseMenuSystem>
    {
        [SerializeField] int _sortingOrder = 120;

        Canvas _canvas;
        GameObject _root;
        bool _open;

        public bool IsOpen => _open;

        protected override void Awake()
        {
            base.Awake();
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

            // Only while in an active match, not on the connection hub.
            var inMatch = NetworkGameSession.Instance != null
                && NetworkGameSession.Instance.Object != null
                && NetworkGameSession.Instance.Object.IsValid
                && NetworkGameSession.Instance.GameplayReady;

            if (!inMatch)
            {
                if (_open)
                {
                    SetOpen(false);
                }

                return;
            }

            // Don't fight the victory overlay.
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<MissionProgressUISystem>(out var mission)
                && mission.IsVictoryVisible)
            {
                return;
            }

            SetOpen(!_open);
        }

        public void SetOpen(bool open)
        {
            EnsureUi();
            _open = open;
            if (_root != null)
            {
                _root.SetActive(open);
            }

            // Never freeze the simulation — multiplayer keeps running for everyone.
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
            cardRt.sizeDelta = new Vector2(420f, 280f);

            CreateLabel(_root.transform, "Title", "Paused", 42, FontStyle.Bold, new Vector2(0f, 70f), new Vector2(380f, 56f));
            CreateLabel(
                _root.transform,
                "Hint",
                "The match keeps running for other players.",
                18,
                FontStyle.Normal,
                new Vector2(0f, 28f),
                new Vector2(380f, 40f));

            var resume = CreateButton(_root.transform, "ResumeButton", new Vector2(0f, -30f), "Resume");
            resume.onClick.AddListener(Resume);

            var exit = CreateButton(_root.transform, "ExitButton", new Vector2(0f, -110f), "Exit to Hub");
            exit.onClick.AddListener(ExitToHub);
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
    }
}
