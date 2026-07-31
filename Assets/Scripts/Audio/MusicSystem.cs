using System.Collections;
using GypsyAliens.Core;
using GypsyAliens.Network;
using UnityEngine;

namespace GypsyAliens.Audio
{
    /// <summary>
    /// Menu loop + in-match playlist with random crossfades between tracks.
    /// </summary>
    public sealed class MusicSystem : GameSystemBehaviour<MusicSystem>
    {
        [SerializeField] AudioClip _menuMusic;
        [SerializeField] AudioClip[] _gameplayTracks;
        [SerializeField] float _menuVolume = 0.45f;
        [SerializeField] float _gameplayVolume = 0.4f;
        [SerializeField] float _crossfadeSeconds = 2.5f;
        [SerializeField] float _menuFadeSeconds = 1.2f;

        AudioSource _sourceA;
        AudioSource _sourceB;
        AudioSource _active;
        AudioSource _idle;
        bool _gameplayMode;
        int _lastGameplayIndex = -1;
        Coroutine _fadeRoutine;
        NetworkGameSession _boundSession;

        protected override void Awake()
        {
            base.Awake();
            EnsureSources();
            PlayMenuImmediate();
        }

        void Update()
        {
            TryBindSession();

            if (!_gameplayMode || _fadeRoutine != null || _active == null)
            {
                return;
            }

            // Start crossfade near the end of the current track.
            if (_active.clip != null
                && _active.isPlaying
                && _active.clip.length > _crossfadeSeconds + 0.5f
                && _active.time >= _active.clip.length - _crossfadeSeconds)
            {
                CrossfadeToNextGameplayTrack();
            }
            else if (_active.clip != null && !_active.isPlaying)
            {
                CrossfadeToNextGameplayTrack();
            }
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
                _boundSession.GameplayReadyChanged += OnGameplayReadyChanged;
                if (_boundSession.GameplayReady)
                {
                    EnterGameplay();
                }
            }
        }

        void UnbindSession()
        {
            if (_boundSession != null)
            {
                _boundSession.GameplayReadyChanged -= OnGameplayReadyChanged;
                _boundSession = null;
            }
        }

        void OnGameplayReadyChanged()
        {
            if (_boundSession != null && _boundSession.GameplayReady)
            {
                EnterGameplay();
            }
        }

        public void EnterMenu()
        {
            _gameplayMode = false;
            if (_menuMusic == null)
            {
                StopAll();
                return;
            }

            FadeToClip(_menuMusic, _menuVolume, _menuFadeSeconds, loop: true);
        }

        public void EnterGameplay()
        {
            if (_gameplayTracks == null || _gameplayTracks.Length == 0)
            {
                return;
            }

            _gameplayMode = true;
            var clip = PickRandomGameplayClip();
            if (clip == null)
            {
                return;
            }

            FadeToClip(clip, _gameplayVolume, _crossfadeSeconds, loop: false);
        }

        void CrossfadeToNextGameplayTrack()
        {
            if (!_gameplayMode || _gameplayTracks == null || _gameplayTracks.Length == 0)
            {
                return;
            }

            var clip = PickRandomGameplayClip();
            if (clip == null)
            {
                return;
            }

            FadeToClip(clip, _gameplayVolume, _crossfadeSeconds, loop: false);
        }

        AudioClip PickRandomGameplayClip()
        {
            var valid = 0;
            for (var i = 0; i < _gameplayTracks.Length; i++)
            {
                if (_gameplayTracks[i] != null)
                {
                    valid++;
                }
            }

            if (valid == 0)
            {
                return null;
            }

            if (valid == 1)
            {
                for (var i = 0; i < _gameplayTracks.Length; i++)
                {
                    if (_gameplayTracks[i] != null)
                    {
                        _lastGameplayIndex = i;
                        return _gameplayTracks[i];
                    }
                }
            }

            // Prefer a different track than the last one.
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var index = Random.Range(0, _gameplayTracks.Length);
                if (_gameplayTracks[index] == null || index == _lastGameplayIndex)
                {
                    continue;
                }

                _lastGameplayIndex = index;
                return _gameplayTracks[index];
            }

            for (var i = 0; i < _gameplayTracks.Length; i++)
            {
                if (_gameplayTracks[i] != null)
                {
                    _lastGameplayIndex = i;
                    return _gameplayTracks[i];
                }
            }

            return null;
        }

        void PlayMenuImmediate()
        {
            EnsureSources();
            if (_menuMusic == null)
            {
                return;
            }

            _gameplayMode = false;
            _active.clip = _menuMusic;
            _active.loop = true;
            _active.volume = _menuVolume;
            _active.Play();
            _idle.Stop();
            _idle.volume = 0f;
        }

        void FadeToClip(AudioClip clip, float targetVolume, float duration, bool loop)
        {
            if (clip == null)
            {
                return;
            }

            EnsureSources();
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(CrossfadeRoutine(clip, targetVolume, duration, loop));
        }

        IEnumerator CrossfadeRoutine(AudioClip nextClip, float targetVolume, float duration, bool loop)
        {
            var from = _active;
            var to = _idle;

            to.clip = nextClip;
            to.loop = loop;
            to.volume = 0f;
            to.time = 0f;
            to.Play();

            var startFrom = from.isPlaying ? from.volume : 0f;
            var t = 0f;
            duration = Mathf.Max(0.05f, duration);
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / duration);
                // Smoothstep for a softer blend.
                u = u * u * (3f - 2f * u);
                from.volume = Mathf.Lerp(startFrom, 0f, u);
                to.volume = Mathf.Lerp(0f, targetVolume, u);
                yield return null;
            }

            from.Stop();
            from.volume = 0f;
            to.volume = targetVolume;

            _active = to;
            _idle = from;
            _fadeRoutine = null;
        }

        void StopAll()
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (_sourceA != null)
            {
                _sourceA.Stop();
                _sourceA.volume = 0f;
            }

            if (_sourceB != null)
            {
                _sourceB.Stop();
                _sourceB.volume = 0f;
            }
        }

        void EnsureSources()
        {
            if (_sourceA != null && _sourceB != null)
            {
                if (_active == null)
                {
                    _active = _sourceA;
                    _idle = _sourceB;
                }

                return;
            }

            _sourceA = CreateSource("MusicA");
            _sourceB = CreateSource("MusicB");
            _active = _sourceA;
            _idle = _sourceB;
        }

        AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.volume = 0f;
            return src;
        }
    }
}
