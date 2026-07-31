using Fusion;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Drives Idle/Run from rendered displacement so all peers see matching animal locomotion.
    /// </summary>
    public sealed class NpcAnimationDriver : NetworkBehaviour
    {
        static readonly int SpeedHash = Animator.StringToHash("Speed");

        [SerializeField] Animator _animator;
        [SerializeField] float _idleSpeedThreshold = 0.08f;
        [SerializeField] float _referenceRunSpeed = 2.2f;
        [SerializeField] float _minAnimSpeed = 0.75f;
        [SerializeField] float _maxAnimSpeed = 1.35f;
        [SerializeField] float _speedSmooth = 16f;

        Vector3 _lastPosition;
        bool _hasLastPosition;
        float _smoothedSpeed;
        bool _hasSpeedParam;

        public override void Spawned()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            _hasSpeedParam = false;
            if (_animator != null)
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.nameHash == SpeedHash && p.type == AnimatorControllerParameterType.Float)
                    {
                        _hasSpeedParam = true;
                        break;
                    }
                }
            }

            _lastPosition = transform.position;
            _hasLastPosition = true;
            _smoothedSpeed = 0f;
            ApplyIdle();
        }

        public override void Render()
        {
            if (_animator == null)
            {
                return;
            }

            if (!_hasLastPosition)
            {
                _lastPosition = transform.position;
                _hasLastPosition = true;
                return;
            }

            var delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            delta.y = 0f;

            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            var instantSpeed = delta.magnitude / dt;
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, instantSpeed, 1f - Mathf.Exp(-_speedSmooth * dt));

            if (_smoothedSpeed <= _idleSpeedThreshold)
            {
                ApplyIdle();
                return;
            }

            if (_hasSpeedParam)
            {
                _animator.SetFloat(SpeedHash, _smoothedSpeed);
            }

            var strideScale = _smoothedSpeed / Mathf.Max(0.01f, _referenceRunSpeed);
            _animator.speed = Mathf.Clamp(strideScale, _minAnimSpeed, _maxAnimSpeed);
        }

        void ApplyIdle()
        {
            _smoothedSpeed = 0f;
            if (_animator == null)
            {
                return;
            }

            if (_hasSpeedParam)
            {
                _animator.SetFloat(SpeedHash, 0f);
            }

            _animator.speed = 1f;
        }
    }
}
