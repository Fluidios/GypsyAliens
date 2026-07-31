using UnityEngine;

namespace GypsyAliens.Core
{
    /// <summary>
    /// Base MonoBehaviour that auto-registers itself with <see cref="SystemLocator"/>.
    /// </summary>
    public abstract class GameSystemBehaviour<TSelf> : MonoBehaviour, IGameSystem
        where TSelf : class, IGameSystem
    {
        protected virtual void Awake()
        {
            if (SystemLocator.Instance == null)
            {
                Debug.LogError($"{typeof(TSelf).Name} requires a SystemLocator in the scene.", this);
                return;
            }

            SystemLocator.Instance.Register((TSelf)(object)this);
        }

        protected virtual void OnDestroy()
        {
            if (SystemLocator.Instance != null)
            {
                SystemLocator.Instance.Unregister((TSelf)(object)this);
            }
        }
    }
}
