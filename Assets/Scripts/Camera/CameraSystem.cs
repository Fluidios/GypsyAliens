using UnityEngine;

namespace GypsyAliens.Cameras
{
    /// <summary>
    /// Provides isometric camera follow target binding for the local player.
    /// </summary>
    public sealed class CameraSystem : Core.GameSystemBehaviour<CameraSystem>
    {
        [SerializeField] IsometricCameraFollow _follow;

        protected override void Awake()
        {
            base.Awake();
            if (_follow == null)
            {
                _follow = GetComponentInChildren<IsometricCameraFollow>(true);
            }
        }

        public void SetFollowTarget(Transform target)
        {
            if (_follow == null)
            {
                Debug.LogWarning("CameraSystem: IsometricCameraFollow is not assigned.", this);
                return;
            }

            _follow.SetTarget(target);
        }
    }
}
