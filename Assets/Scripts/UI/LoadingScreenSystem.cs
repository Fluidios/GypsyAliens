using GypsyAliens.Core;
using GypsyAliens.Level;
using UnityEngine;
using UnityEngine.UI;

namespace GypsyAliens.UI
{
    /// <summary>
    /// Fullscreen loading overlay shown while connecting / generating the level.
    /// </summary>
    public sealed class LoadingScreenSystem : GameSystemBehaviour<LoadingScreenSystem>
    {
        [SerializeField] GameObject _root;
        [SerializeField] Text _statusText;
        [SerializeField] Image _progressFill;
        [SerializeField] float _fakeProgressSpeed = 0.35f;
        [SerializeField] string _connectingMessage = "Connecting...";
        [SerializeField] string _generatingMessage = "Generating location...";

        bool _visible;
        float _displayProgress;

        protected override void Awake()
        {
            base.Awake();
            HideImmediate();

            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                BindLevel(level);
            }
        }

        void Start()
        {
            // Level system may register after this Awake depending on hierarchy order.
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                BindLevel(level);
            }
        }

        protected override void OnDestroy()
        {
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                level.GenerationStarted -= OnGenerationStarted;
                level.LevelReady -= OnLevelReady;
            }

            base.OnDestroy();
        }

        void BindLevel(LevelGenerationSystem level)
        {
            level.GenerationStarted -= OnGenerationStarted;
            level.LevelReady -= OnLevelReady;
            level.GenerationStarted += OnGenerationStarted;
            level.LevelReady += OnLevelReady;
        }

        void Update()
        {
            if (!_visible || _progressFill == null)
            {
                return;
            }

            _displayProgress = Mathf.MoveTowards(_displayProgress, 0.92f, _fakeProgressSpeed * Time.deltaTime);
            _progressFill.fillAmount = _displayProgress;
        }

        public void ShowConnecting() => Show(_connectingMessage);

        public void ShowGenerating() => Show(_generatingMessage);

        public void Show(string message)
        {
            _visible = true;
            _displayProgress = 0.05f;

            if (_root != null)
            {
                _root.SetActive(true);
            }

            if (_statusText != null && !string.IsNullOrEmpty(message))
            {
                _statusText.text = message;
            }

            if (_progressFill != null)
            {
                _progressFill.fillAmount = _displayProgress;
            }
        }

        public void Hide()
        {
            if (_progressFill != null)
            {
                _progressFill.fillAmount = 1f;
            }

            HideImmediate();
        }

        void HideImmediate()
        {
            _visible = false;
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        void OnGenerationStarted() => ShowGenerating();

        void OnLevelReady() => Hide();
    }
}
