using GypsyAliens.Core;
using GypsyAliens.Network;
using UnityEngine;
using UnityEngine.UI;

namespace GypsyAliens.UI
{
    /// <summary>
    /// Wires ConnectionMenu prefab buttons to <see cref="NetworkService"/>.
    /// Uses Clean Settings UI controls (legacy InputField / Button).
    /// </summary>
    public sealed class ConnectionUISystem : GameSystemBehaviour<ConnectionUISystem>
    {
        [SerializeField] GameObject _menuRoot;
        [SerializeField] InputField _roomNameInput;
        [SerializeField] Button _hostButton;
        [SerializeField] Button _joinButton;
        [SerializeField] Button _autoButton;
        [SerializeField] string _defaultRoomName = "GypsyAliens";

        protected override void Awake()
        {
            base.Awake();

            if (_roomNameInput != null && string.IsNullOrEmpty(_roomNameInput.text))
            {
                _roomNameInput.text = _defaultRoomName;
            }

            if (_hostButton != null)
            {
                _hostButton.onClick.AddListener(() => _ = StartHost());
            }

            if (_joinButton != null)
            {
                _joinButton.onClick.AddListener(() => _ = StartClient());
            }

            if (_autoButton != null)
            {
                _autoButton.onClick.AddListener(() => _ = StartAuto());
            }
        }

        protected override void OnDestroy()
        {
            if (_hostButton != null) _hostButton.onClick.RemoveAllListeners();
            if (_joinButton != null) _joinButton.onClick.RemoveAllListeners();
            if (_autoButton != null) _autoButton.onClick.RemoveAllListeners();
            base.OnDestroy();
        }

        async System.Threading.Tasks.Task StartHost()
        {
            if (!TryGetNetwork(out var network))
            {
                return;
            }

            SetMenuVisible(false);
            ShowLoadingConnecting();
            await network.StartHostAsync(GetRoomName());
        }

        async System.Threading.Tasks.Task StartClient()
        {
            if (!TryGetNetwork(out var network))
            {
                return;
            }

            SetMenuVisible(false);
            ShowLoadingConnecting();
            await network.StartClientAsync(GetRoomName());
        }

        async System.Threading.Tasks.Task StartAuto()
        {
            if (!TryGetNetwork(out var network))
            {
                return;
            }

            SetMenuVisible(false);
            ShowLoadingConnecting();
            await network.StartAutoHostOrClientAsync(GetRoomName());
        }

        static void ShowLoadingConnecting()
        {
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LoadingScreenSystem>(out var loading))
            {
                loading.ShowConnecting();
            }
        }

        string GetRoomName()
        {
            if (_roomNameInput != null && !string.IsNullOrWhiteSpace(_roomNameInput.text))
            {
                return _roomNameInput.text.Trim();
            }

            return _defaultRoomName;
        }

        bool TryGetNetwork(out NetworkService network)
        {
            network = null;
            if (SystemLocator.Instance == null || !SystemLocator.Instance.TryGet(out network))
            {
                Debug.LogError("ConnectionUISystem: NetworkService is not registered.");
                return false;
            }

            return true;
        }

        public void SetMenuVisible(bool visible)
        {
            if (_menuRoot != null)
            {
                _menuRoot.SetActive(visible);
            }
        }
    }
}
