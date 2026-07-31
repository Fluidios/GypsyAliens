using Fusion;
using GypsyAliens.Core;
using GypsyAliens.Level;
using UnityEngine;

namespace GypsyAliens.Network
{
    /// <summary>
    /// Networked session state. Host owns the level seed; all peers generate locally.
    /// </summary>
    public sealed class NetworkGameSession : NetworkBehaviour
    {
        [Networked, OnChangedRender(nameof(OnLevelSeedChanged))]
        public int LevelSeed { get; set; }

        bool _generatedForSeed;

        public override void Spawned()
        {
            if (HasStateAuthority && LevelSeed == 0)
            {
                LevelSeed = UnityEngine.Random.Range(1, int.MaxValue);
            }

            TryGenerate();
        }

        void OnLevelSeedChanged()
        {
            _generatedForSeed = false;
            TryGenerate();
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
