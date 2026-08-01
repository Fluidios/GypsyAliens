using System.Collections.Generic;
using GypsyAliens.Core;
using GypsyAliens.Network;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GypsyAliens.UI
{
    /// <summary>
    /// Wires ConnectionMenu prefab buttons to <see cref="NetworkService"/>.
    /// Uses Clean Settings UI controls (legacy InputField / Button / Dropdown).
    /// </summary>
    public sealed class ConnectionUISystem : GameSystemBehaviour<ConnectionUISystem>
    {
        const string RegionPrefsKey = "GypsyAliens.PhotonRegion";

        [SerializeField] GameObject _menuRoot;
        [SerializeField] InputField _roomNameInput;
        [SerializeField] Dropdown _regionDropdown;
        [SerializeField] Button _hostButton;
        [SerializeField] Button _joinButton;
        [SerializeField] Button _autoButton;
        [SerializeField] Button _settingsButton;
        [SerializeField] string _defaultRoomName = "GypsyAliens";

        // Public Photon Cloud regions available for typical Fusion apps.
        // Note: "ru" is NOT available on the public Photon Cloud for this AppId
        // (InvalidRegion / "Region ru is not available").
        static readonly string[] RegionCodes =
        {
            "", // Best region
            "eu",
            "us",
            "usw",
            "asia",
            "jp",
            "in",
            "sa",
            "au",
            "cae",
            "kr",
            "zae",
            "uae",
        };

        static readonly string[] RegionLabels =
        {
            "Best Region (auto)",
            "Europe (eu)",
            "USA East (us)",
            "USA West (usw)",
            "Asia (asia)",
            "Japan (jp)",
            "India (in)",
            "South America (sa)",
            "Australia (au)",
            "Canada East (cae)",
            "South Korea (kr)",
            "South Africa (zae)",
            "UAE (uae)",
        };

        protected override void Awake()
        {
            base.Awake();

            if (_roomNameInput != null && string.IsNullOrEmpty(_roomNameInput.text))
            {
                _roomNameInput.text = _defaultRoomName;
            }

            if (_regionDropdown == null && _menuRoot != null)
            {
                _regionDropdown = _menuRoot.GetComponentInChildren<Dropdown>(true);
            }

            PopulateRegionDropdown();

            EnsureSettingsButton();
            ConfigureButton(_hostButton, () => _ = StartHost());
            ConfigureButton(_joinButton, () => _ = StartClient());
            ConfigureButton(_autoButton, () => _ = StartAuto());
            ConfigureButton(_settingsButton, OpenHubSettings);
        }

        protected override void OnDestroy()
        {
            if (_hostButton != null) _hostButton.onClick.RemoveAllListeners();
            if (_joinButton != null) _joinButton.onClick.RemoveAllListeners();
            if (_autoButton != null) _autoButton.onClick.RemoveAllListeners();
            if (_settingsButton != null) _settingsButton.onClick.RemoveAllListeners();
            if (_regionDropdown != null) _regionDropdown.onValueChanged.RemoveAllListeners();
            base.OnDestroy();
        }

        void OpenHubSettings()
        {
            ClearUiSelection();
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<PauseMenuSystem>(out var pause))
            {
                pause.OpenHubSettings();
            }
        }

        void EnsureSettingsButton()
        {
            if (_settingsButton != null || _menuRoot == null)
            {
                return;
            }

            var existing = _menuRoot.transform.Find("SettingsButton");
            if (existing != null)
            {
                _settingsButton = existing.GetComponent<Button>();
                if (_settingsButton != null)
                {
                    return;
                }
            }

            var go = new GameObject("SettingsButton");
            go.transform.SetParent(_menuRoot.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-28f, -28f);
            rt.sizeDelta = new Vector2(64f, 64f);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.12f, 0.16f, 0.2f, 0.92f);

            _settingsButton = go.AddComponent<Button>();
            _settingsButton.targetGraphic = image;
            var nav = _settingsButton.navigation;
            nav.mode = Navigation.Mode.None;
            _settingsButton.navigation = nav;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null)
            {
                label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            label.text = "⚙";
            label.fontSize = 36;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
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
            await network.StartHostAsync(GetRoomName(), GetSelectedRegion());
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
            await network.StartClientAsync(GetRoomName(), GetSelectedRegion());
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
            await network.StartAutoHostOrClientAsync(GetRoomName(), GetSelectedRegion());
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

        string GetSelectedRegion()
        {
            if (_regionDropdown == null
                || _regionDropdown.value < 0
                || _regionDropdown.value >= RegionCodes.Length)
            {
                return "eu";
            }

            return RegionCodes[_regionDropdown.value];
        }

        void PopulateRegionDropdown()
        {
            if (_regionDropdown == null)
            {
                Debug.LogWarning("ConnectionUISystem: RegionDropdown is missing on the ConnectionMenu prefab.");
                return;
            }

            _regionDropdown.ClearOptions();
            var options = new List<Dropdown.OptionData>(RegionLabels.Length);
            for (var i = 0; i < RegionLabels.Length; i++)
            {
                options.Add(new Dropdown.OptionData(RegionLabels[i]));
            }

            _regionDropdown.AddOptions(options);

            var saved = PlayerPrefs.GetString(RegionPrefsKey, "eu");
            // Migrate removed / unavailable regions (e.g. public Cloud has no "ru").
            if (saved == "ru")
            {
                saved = "eu";
                PlayerPrefs.SetString(RegionPrefsKey, saved);
                PlayerPrefs.Save();
            }

            var index = 1; // Europe default
            for (var i = 0; i < RegionCodes.Length; i++)
            {
                if (RegionCodes[i] == saved)
                {
                    index = i;
                    break;
                }
            }

            _regionDropdown.value = index;
            _regionDropdown.RefreshShownValue();
            _regionDropdown.onValueChanged.RemoveListener(OnRegionChanged);
            _regionDropdown.onValueChanged.AddListener(OnRegionChanged);
        }

        void OnRegionChanged(int index)
        {
            if (index < 0 || index >= RegionCodes.Length)
            {
                return;
            }

            PlayerPrefs.SetString(RegionPrefsKey, RegionCodes[index]);
            PlayerPrefs.Save();
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
