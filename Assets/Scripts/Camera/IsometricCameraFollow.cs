using UnityEngine;

namespace GypsyAliens.Cameras
{
    /// <summary>
    /// Orthographic tactical camera (Shadow Tactics style): looks at a focus point
    /// with pitch/yaw, keeps the follow target near screen center, and only recenters
    /// when the target leaves a world-space safe zone.
    /// </summary>
    public sealed class IsometricCameraFollow : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField] Transform _target;
        [SerializeField] float _pitch = 50f;
        [SerializeField] float _yaw = 45f;
        [SerializeField] float _distance = 28f;
        [SerializeField] float _orthographicSize = 11f;

        [Header("Follow / Safe Zone")]
        [Tooltip("World-space radius around the focus point. Camera stays still while the target is inside.")]
        [SerializeField] float _safeZoneRadius = 2.5f;
        [SerializeField] float _followSpeed = 8f;
        [SerializeField] float _snapSpeed = 20f;

        UnityEngine.Camera _camera;
        Vector3 _focusPoint;
        bool _hasFocus;
        bool _snapNext;

        public Transform Target => _target;

        void Awake()
        {
            _camera = GetComponent<UnityEngine.Camera>();
            ApplyCameraSetup();
        }

        void OnValidate()
        {
            if (_camera == null)
            {
                _camera = GetComponent<UnityEngine.Camera>();
            }

            ApplyCameraSetup();
        }

        void ApplyCameraSetup()
        {
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            if (_camera != null)
            {
                _camera.orthographic = true;
                _camera.orthographicSize = _orthographicSize;
            }
        }

        public void SetTarget(Transform target)
        {
            _target = target;
            if (_target == null)
            {
                _hasFocus = false;
                return;
            }

            _focusPoint = _target.position;
            _hasFocus = true;
            _snapNext = true;
            ApplyPose(_focusPoint, instant: true);
        }

        void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            if (!_hasFocus)
            {
                _focusPoint = _target.position;
                _hasFocus = true;
                _snapNext = true;
            }

            UpdateFocusPoint();
            ApplyPose(_focusPoint, instant: _snapNext);
            _snapNext = false;
        }

        void UpdateFocusPoint()
        {
            var targetPos = _target.position;
            var delta = targetPos - _focusPoint;
            delta.y = 0f;

            var distance = delta.magnitude;
            if (distance <= _safeZoneRadius)
            {
                return;
            }

            // Keep the target on the edge of the safe zone (soft framing buffer).
            var overflow = distance - _safeZoneRadius;
            _focusPoint += delta.normalized * overflow;
            _focusPoint.y = targetPos.y;
        }

        void ApplyPose(Vector3 focus, bool instant)
        {
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.rotation = rotation;

            // Offset is opposite of look direction so the focus stays centered in view.
            var desiredPosition = focus + rotation * (Vector3.back * _distance);
            var speed = instant ? _snapSpeed : _followSpeed;
            if (instant || speed <= 0f)
            {
                transform.position = desiredPosition;
            }
            else
            {
                transform.position = Vector3.Lerp(
                    transform.position,
                    desiredPosition,
                    1f - Mathf.Exp(-speed * Time.deltaTime));
            }

            if (_camera != null)
            {
                _camera.orthographicSize = _orthographicSize;
            }
        }
    }
}
