using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.Network;
using UnityEngine;
using UnityEngine.UI;

namespace GypsyAliens.UI
{
    /// <summary>
    /// Fullscreen loading overlay shown while connecting / generating / spawning NPCs.
    /// </summary>
    public sealed class LoadingScreenSystem : GameSystemBehaviour<LoadingScreenSystem>
    {
        [SerializeField] GameObject _root;
        [SerializeField] Text _statusText;
        [SerializeField] Image _progressFill;
        [SerializeField] float _fakeProgressSpeed = 0.35f;
        [SerializeField] string _connectingMessage = "Connecting...";
        [SerializeField] string _generatingMessage = "Generating location...";
        [SerializeField] string _preparingMessage = "Preparing location...";

        bool _visible;
        float _displayProgress;
        bool _boundSession;
        NetworkGameSession _session;

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
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                BindLevel(level);
            }
        }

        void Update()
        {
            TryBindSession();

            if (!_visible || _progressFill == null)
            {
                return;
            }

            _displayProgress = Mathf.MoveTowards(_displayProgress, 0.92f, _fakeProgressSpeed * Time.deltaTime);
            _progressFill.fillAmount = _displayProgress;
        }

        protected override void OnDestroy()
        {
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                level.GenerationStarted -= OnGenerationStarted;
                level.LevelReady -= OnLevelReady;
            }

            UnbindSession();
            base.OnDestroy();
        }

        void BindLevel(LevelGenerationSystem level)
        {
            level.GenerationStarted -= OnGenerationStarted;
            level.LevelReady -= OnLevelReady;
            level.GenerationStarted += OnGenerationStarted;
            level.LevelReady += OnLevelReady;
        }

        void TryBindSession()
        {
            if (_boundSession && _session != null)
            {
                return;
            }

            var session = NetworkGameSession.Instance;
            if (session == null)
            {
                return;
            }

            UnbindSession();
            _session = session;
            _session.GameplayReadyChanged += OnGameplayReadyChanged;
            _boundSession = true;

            if (_session.GameplayReady)
            {
                Hide();
            }
        }

        void UnbindSession()
        {
            if (_session != null)
            {
                _session.GameplayReadyChanged -= OnGameplayReadyChanged;
            }

            _session = null;
            _boundSession = false;
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

        void OnLevelReady()
        {
            // Keep overlay up until host marks GameplayReady (NPCs spawned).
            Show(_preparingMessage);
            TryBindSession();
            if (_session != null && _session.GameplayReady)
            {
                Hide();
            }
        }

        void OnGameplayReadyChanged()
        {
            if (_session == null || !_session.GameplayReady)
            {
                return;
            }

            // Don't hide until local generation finished.
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level)
                && !level.IsReady)
            {
                return;
            }

            Hide();
        }
    }
}
