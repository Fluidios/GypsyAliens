using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.Network;
using UnityEngine;

namespace GypsyAliens.Gameplay
{
    /// <summary>
    /// Creates and maintains the evacuation zone at the player spawn point.
    /// </summary>
    public sealed class EvacuationZoneSystem : GameSystemBehaviour<EvacuationZoneSystem>
    {
        [SerializeField] float _radius = 3.2f;
        [SerializeField] float _saucerAltitude = 11f;

        EvacuationZone _zone;

        public EvacuationZone Zone => _zone;

        protected override void Awake()
        {
            base.Awake();
            BindLevel();
        }

        void Start()
        {
            BindLevel();
        }

        void Update()
        {
            if (_zone == null)
            {
                TryEnsureZone();
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

            DestroyZone();
            base.OnDestroy();
        }

        void BindLevel()
        {
            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                return;
            }

            level.GenerationStarted -= OnGenerationStarted;
            level.LevelReady -= OnLevelReady;
            level.GenerationStarted += OnGenerationStarted;
            level.LevelReady += OnLevelReady;

            if (level.IsReady)
            {
                OnLevelReady();
            }
        }

        void OnGenerationStarted()
        {
            DestroyZone();
        }

        void OnLevelReady()
        {
            TryEnsureZone();
        }

        void TryEnsureZone()
        {
            if (_zone != null)
            {
                return;
            }

            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level)
                || !level.IsReady)
            {
                return;
            }

            var go = new GameObject("EvacuationZone");
            go.transform.position = level.SpawnPosition;
            _zone = go.AddComponent<EvacuationZone>();
            _zone.Configure(_radius, _saucerAltitude);
        }

        void DestroyZone()
        {
            if (_zone != null)
            {
                Destroy(_zone.gameObject);
                _zone = null;
            }
        }
    }
}
