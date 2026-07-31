using Fusion;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Drives Idle/Run from rendered displacement so all peers see matching animal locomotion.
    /// Fully freezes the animator while the NPC is being dragged.
    /// </summary>
    public sealed class NpcAnimationDriver : NetworkBehaviour
    {
        static readonly int SpeedHash = Animator.StringToHash("Speed");

        [SerializeField] Animator _animator;
        [SerializeField] float _idleSpeedThreshold = 0.05f;
        [SerializeField] float _referenceRunSpeed = 2.6f;
        [SerializeField] float _minAnimSpeed = 0.45f;
        [SerializeField] float _maxAnimSpeed = 1.35f;
        [SerializeField] float _speedSmooth = 16f;

        NetworkFearfulNpc _npc;
        Vector3 _lastPosition;
        bool _hasLastPosition;
        float _smoothedSpeed;
        bool _hasSpeedParam;
        bool _wasFrozen;

        public override void Spawned()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            _npc = GetComponent<NetworkFearfulNpc>();
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
            _wasFrozen = false;
            ApplyIdle();
        }

        public override void Render()
        {
            if (_animator == null)
            {
                return;
            }

            if (_npc != null
                && _npc.Object != null
                && _npc.Object.IsValid
                && (_npc.IsDragged || _npc.IsExtracting))
            {
                FreezeAnimation();
                _lastPosition = transform.position;
                _hasLastPosition = true;
                return;
            }

            if (_wasFrozen)
            {
                _wasFrozen = false;
                _lastPosition = transform.position;
                _smoothedSpeed = 0f;
                ApplyIdle();
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

        void FreezeAnimation()
        {
            _smoothedSpeed = 0f;
            if (_hasSpeedParam)
            {
                _animator.SetFloat(SpeedHash, 0f);
            }

            // Hold the current pose — no idle/run while dragged.
            _animator.speed = 0f;
            _wasFrozen = true;
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
