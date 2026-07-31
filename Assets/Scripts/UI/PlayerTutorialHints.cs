using UnityEngine;
using UnityEngine.UI;

namespace GypsyAliens.UI
{
    /// <summary>
    /// One-shot bottom-screen tutorial for the local player each match.
    /// </summary>
    public sealed class PlayerTutorialHints : MonoBehaviour
    {
        enum Step
        {
            Move = 0,
            Throw = 1,
            AwaitDrag = 2,
            Drag = 3,
            Done = 4,
        }

        const string MoveText = "Кликните, чтобы двигаться к точке клика.";
        const string ThrowText =
            "Зажмите Spacebar и отпустите, чтобы бросить камень и привлечь внимание, либо оглушить цель.";
        const string DragText =
            "Зажмите Spacebar и не отпускайте, чтобы тащить. Притащите цель к зоне эвакуации.";

        [SerializeField] float _bottomMargin = 48f;
        [SerializeField] int _fontSize = 28;

        Canvas _canvas;
        Text _label;
        Step _step = Step.Move;
        bool _built;
        bool _active;

        void Awake()
        {
            EnsureUi();
            SetActive(false);
        }

        public void SetActive(bool active)
        {
            EnsureUi();
            _active = active;
            if (!_active)
            {
                if (_label != null)
                {
                    _label.gameObject.SetActive(false);
                }

                return;
            }

            if (_step == Step.Done)
            {
                if (_label != null)
                {
                    _label.gameObject.SetActive(false);
                }

                return;
            }

            ShowStep(_step);
        }

        public void NotifyMoveClicked()
        {
            if (!_active || _step != Step.Move)
            {
                return;
            }

            ShowStep(Step.Throw);
        }

        /// <summary>Hide throw hint after the first rock throw; drag hint waits for proximity.</summary>
        public void NotifyRockThrown()
        {
            if (!_active || _step != Step.Throw)
            {
                return;
            }

            ShowStep(Step.AwaitDrag);
        }

        public void NotifyNearAnimal(bool near)
        {
            if (!_active || !near)
            {
                return;
            }

            if (_step == Step.AwaitDrag || _step == Step.Throw)
            {
                ShowStep(Step.Drag);
            }
        }

        public void NotifyDragStarted()
        {
            if (!_active || _step != Step.Drag)
            {
                return;
            }

            ShowStep(Step.Done);
        }

        void ShowStep(Step step)
        {
            EnsureUi();
            _step = step;
            if (_label == null)
            {
                return;
            }

            switch (step)
            {
                case Step.Move:
                    _label.text = MoveText;
                    _label.gameObject.SetActive(true);
                    break;
                case Step.Throw:
                    _label.text = ThrowText;
                    _label.gameObject.SetActive(true);
                    break;
                case Step.Drag:
                    _label.text = DragText;
                    _label.gameObject.SetActive(true);
                    break;
                case Step.AwaitDrag:
                default:
                    _label.text = string.Empty;
                    _label.gameObject.SetActive(false);
                    break;
            }
        }

        void EnsureUi()
        {
            if (_built)
            {
                return;
            }

            _built = true;
            var root = new GameObject("TutorialHintsCanvas");
            root.transform.SetParent(transform, false);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 80;
            root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            root.AddComponent<GraphicRaycaster>();

            var textGo = new GameObject("Hint");
            textGo.transform.SetParent(root.transform, false);
            var rect = textGo.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.08f, 0f);
            rect.anchorMax = new Vector2(0.92f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, _bottomMargin);
            rect.sizeDelta = new Vector2(0f, 96f);

            _label = textGo.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_label.font == null)
            {
                _label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            _label.fontSize = _fontSize;
            _label.alignment = TextAnchor.LowerCenter;
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.color = Color.white;
            _label.raycastTarget = false;

            var outline = textGo.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }
}
