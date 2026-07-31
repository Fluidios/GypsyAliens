using GypsyAliens.Core;
using GypsyAliens.Network;
using UnityEngine;
using UnityEngine.EventSystems;
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

            ConfigureButton(_hostButton, () => _ = StartHost());
            ConfigureButton(_joinButton, () => _ = StartClient());
            ConfigureButton(_autoButton, () => _ = StartAuto());
        }

        protected override void OnDestroy()
        {
            if (_hostButton != null) _hostButton.onClick.RemoveAllListeners();
            if (_joinButton != null) _joinButton.onClick.RemoveAllListeners();
            if (_autoButton != null) _autoButton.onClick.RemoveAllListeners();
            base.OnDestroy();
        }

        static void ConfigureButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            // SquareButton maps Selected→Highlighted (blue). Disable selection navigation
            // so Host cannot stay visually "stuck" after click / menu reopen.
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            button.onClick.AddListener(action);
        }

        async System.Threading.Tasks.Task StartHost()
        {
            if (!TryGetNetwork(out var network))
            {
                return;
            }

            ClearUiSelection();
            ForceButtonsNormal();
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

            ClearUiSelection();
            ForceButtonsNormal();
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

            ClearUiSelection();
            ForceButtonsNormal();
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
            ClearUiSelection();

            if (_menuRoot != null)
            {
                _menuRoot.SetActive(visible);
            }

            if (visible)
            {
                // Defer one frame so Animator is active before we force Normal.
                StartCoroutine(ResetButtonsNextFrame());
                if (SystemLocator.Instance != null
                    && SystemLocator.Instance.TryGet<GypsyAliens.Audio.MusicSystem>(out var music))
                {
                    music.EnterMenu();
                }
            }
        }

        System.Collections.IEnumerator ResetButtonsNextFrame()
        {
            yield return null;
            ClearUiSelection();
            ForceButtonsNormal();
        }

        void ForceButtonsNormal()
        {
            ForceButtonNormal(_hostButton);
            ForceButtonNormal(_joinButton);
            ForceButtonNormal(_autoButton);
        }

        static void ClearUiSelection()
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        static void ForceButtonNormal(Button button)
        {
            if (button == null)
            {
                return;
            }

            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;

            button.OnDeselect(null);
            button.OnPointerExit(null);

            var animator = button.GetComponent<Animator>();
            if (animator != null)
            {
                // Hard reset — triggers alone are not enough once Selected remaps to Highlighted.
                if (animator.isActiveAndEnabled)
                {
                    animator.Rebind();
                    animator.Update(0f);
                    animator.Play("Normal", 0, 0f);
                    animator.Update(0f);
                }
            }

            // Bounce interactable to clear pressed/highlighted sprites on some UI setups.
            var was = button.interactable;
            button.interactable = false;
            button.interactable = was;
        }
    }
}
