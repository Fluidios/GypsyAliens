using Fusion;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Drives hostile locomotion + aim/shoot animator parameters from movement and combat flags.
    /// </summary>
    public sealed class HostileAnimationDriver : NetworkBehaviour
    {
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int AimingHash = Animator.StringToHash("Aiming");
        static readonly int ShootHash = Animator.StringToHash("Shoot");
        static readonly int SearchingHash = Animator.StringToHash("Searching");

        [SerializeField] Animator _animator;
        [SerializeField] float _idleSpeedThreshold = 0.08f;
        [SerializeField] float _referenceRunSpeed = 3.2f;
        [SerializeField] float _minAnimSpeed = 0.55f;
        [SerializeField] float _maxAnimSpeed = 1.25f;
        [SerializeField] float _speedSmooth = 14f;

        Vector3 _lastPosition;
        bool _hasLastPosition;
        float _smoothedSpeed;
        bool _hasSpeed;
        bool _hasAiming;
        bool _hasShoot;
        bool _hasSearching;
        bool _aiming;
        bool _shooting;
        bool _wasShooting;

        public override void Spawned()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            CacheParameters();
            _lastPosition = transform.position;
            _hasLastPosition = true;
            _smoothedSpeed = 0f;
        }

        public void SetCombat(bool aiming, bool shooting)
        {
            _aiming = aiming;
            _shooting = shooting;
        }

        public void SetSearching(bool searching)
        {
            if (_animator != null && _hasSearching)
            {
                _animator.SetBool(SearchingHash, searching);
            }
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
            var instant = delta.magnitude / dt;
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, instant, 1f - Mathf.Exp(-_speedSmooth * dt));

            if (_hasSpeed)
            {
                _animator.SetFloat(SpeedHash, _aiming || _shooting ? 0f : _smoothedSpeed);
            }

            if (_hasAiming)
            {
                _animator.SetBool(AimingHash, _aiming || _shooting);
            }

            if (_hasShoot)
            {
                if (_shooting && !_wasShooting)
                {
                    _animator.SetTrigger(ShootHash);
                }

                _wasShooting = _shooting;
            }

            if (_aiming || _shooting || _smoothedSpeed <= _idleSpeedThreshold)
            {
                _animator.speed = 1f;
                return;
            }

            var stride = _smoothedSpeed / Mathf.Max(0.01f, _referenceRunSpeed);
            _animator.speed = Mathf.Clamp(stride, _minAnimSpeed, _maxAnimSpeed);
        }

        void CacheParameters()
        {
            _hasSpeed = false;
            _hasAiming = false;
            _hasShoot = false;
            _hasSearching = false;
            if (_animator == null)
            {
                return;
            }

            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == SpeedHash && p.type == AnimatorControllerParameterType.Float)
                {
                    _hasSpeed = true;
                }
                else if (p.nameHash == AimingHash && p.type == AnimatorControllerParameterType.Bool)
                {
                    _hasAiming = true;
                }
                else if (p.nameHash == ShootHash && p.type == AnimatorControllerParameterType.Trigger)
                {
                    _hasShoot = true;
                }
                else if (p.nameHash == SearchingHash && p.type == AnimatorControllerParameterType.Bool)
                {
                    _hasSearching = true;
                }
            }
        }
    }
}
