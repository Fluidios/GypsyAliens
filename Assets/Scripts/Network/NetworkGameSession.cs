using Fusion;
using GypsyAliens.Core;
using GypsyAliens.Level;
using UnityEngine;

namespace GypsyAliens.Network
{
    /// <summary>
    /// Networked session state. Host owns the level seed; all peers generate locally.
    /// GameplayReady becomes true after host has spawned level NPCs.
    /// </summary>
    public sealed class NetworkGameSession : NetworkBehaviour
    {
        [Networked, OnChangedRender(nameof(OnLevelSeedChanged))]
        public int LevelSeed { get; set; }

        [Networked, OnChangedRender(nameof(OnGameplayReadyChanged))]
        public NetworkBool GameplayReady { get; set; }

        bool _generatedForSeed;

        public static NetworkGameSession Instance { get; private set; }

        public event System.Action GameplayReadyChanged;

        public override void Spawned()
        {
            Instance = this;

            if (HasStateAuthority)
            {
                GameplayReady = false;
                if (LevelSeed == 0)
                {
                    LevelSeed = UnityEngine.Random.Range(1, int.MaxValue);
                }
            }

            TryGenerate();
            NotifyGameplayReadyChanged();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void MarkGameplayReady()
        {
            if (!HasStateAuthority)
            {
                return;
            }

            GameplayReady = true;
        }

        void OnLevelSeedChanged()
        {
            _generatedForSeed = false;
            if (HasStateAuthority)
            {
                GameplayReady = false;
            }

            TryGenerate();
        }

        void OnGameplayReadyChanged()
        {
            NotifyGameplayReadyChanged();
        }

        void NotifyGameplayReadyChanged()
        {
            GameplayReadyChanged?.Invoke();
        }

        void TryGenerate()
        {
            if (LevelSeed == 0 || _generatedForSeed)
            {
                return;
            }

            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                Debug.LogWarning("NetworkGameSession: LevelGenerationSystem not available yet.");
                return;
            }

            level.BeginGenerate(LevelSeed);
            _generatedForSeed = true;
        }
    }
}
