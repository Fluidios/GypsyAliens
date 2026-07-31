using Fusion;
using UnityEngine;

namespace GypsyAliens.Network
{
    /// <summary>
    /// Drives locomotion from actual rendered displacement so Idle/Walk match movement,
    /// and scales animator playback to travelled distance (stride sync).
    /// </summary>
    public sealed class PlayerAnimationDriver : NetworkBehaviour
    {
        static readonly int SpeedHash = Animator.StringToHash("Speed");

        [SerializeField] Animator _animator;
        [SerializeField] float _idleSpeedThreshold = 0.08f;
        [SerializeField] float _referenceWalkSpeed = 1.4f;
        [SerializeField] float _minAnimSpeed = 0.7f;
        [SerializeField] float _maxAnimSpeed = 1.4f;
        [SerializeField] float _speedSmooth = 18f;

        Vector3 _lastPosition;
        bool _hasLastPosition;
        float _smoothedSpeed;

        public override void Spawned()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
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

            _animator.SetFloat(SpeedHash, _smoothedSpeed);
            var strideScale = _smoothedSpeed / Mathf.Max(0.01f, _referenceWalkSpeed);
            _animator.speed = Mathf.Clamp(strideScale, _minAnimSpeed, _maxAnimSpeed);
        }

        void ApplyIdle()
        {
            _smoothedSpeed = 0f;
            _animator.SetFloat(SpeedHash, 0f);
            _animator.speed = 1f;
        }
    }
}
