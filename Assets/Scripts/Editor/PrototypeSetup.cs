#if UNITY_EDITOR
using System.IO;
using System.IO;
using Fusion;
using Fusion.Editor;
using GypsyAliens.Cameras;
using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.Network;
using GypsyAliens.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace GypsyAliens.EditorTools
{
    public static class PrototypeSetup
    {
        const string PrefabNetworkFolder = "Assets/Prefabs/Network";
        const string PrefabUiFolder = "Assets/Prefabs/UI";
        const string ArtAnimatorsFolder = "Assets/Art/Animators";
        const string DataFolder = "Assets/Data";
        const string ScenePath = "Assets/Scenes/Game.unity";

        const string FloorPath = "Assets/Synty/PolygonPrototype/Prefabs/Buildings/Simple/SM_Buildings_Floor_1x1_01.prefab";
        const string WallPath = "Assets/Synty/PolygonPrototype/Prefabs/Buildings/Simple/SM_Buildings_Wall_1x3_01.prefab";
        const string DoorWallPath = "Assets/Synty/PolygonPrototype/Prefabs/Buildings/Simple/SM_Buildings_WallDoor_2x3_01.prefab";
        const string DummyPath = "Assets/Kevin Iglesias/Human Animations/Unity Demo Scenes/Human Basic Motions/Prefabs/Human_BasicMotionsDummy_M.prefab";
        const string IdleClipPath = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Idles/HumanM@Idle01.fbx";
        const string WalkClipPath = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx";

        [MenuItem("GypsyAliens/Setup Prototype Scene")]
        public static void Setup()
        {
            EnsureFolders();

            var tileSet = CreateOrUpdateTileSet();
            var animator = CreatePlayerAnimator();
            CreateRunnerPrefab();
            CreateSessionPrefab();
            CreatePlayerPrefab(animator);
            var connectionMenuPrefab = CreateConnectionMenuPrefab();

            var runnerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabNetworkFolder + "/NetworkRunner.prefab")
                .GetComponent<NetworkRunner>();
            var sessionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabNetworkFolder + "/NetworkGameSession.prefab")
                .GetComponent<NetworkObject>();
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabNetworkFolder + "/NetworkPlayer.prefab")
                .GetComponent<NetworkObject>();

            CreateGameScene(tileSet, runnerPrefab, sessionPrefab, playerPrefab, connectionMenuPrefab);

            NetworkProjectConfigUtilities.RebuildPrefabTable();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("GypsyAliens prototype setup complete.");
        }

        [MenuItem("GypsyAliens/Rebuild Connection Menu UI")]
        public static void RebuildConnectionMenuUi()
        {
            EnsureFolders();
            var menuPrefab = CreateConnectionMenuPrefab();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var existing = GameObject.Find("ConnectionMenu");
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            var menuInstance = (GameObject)PrefabUtility.InstantiatePrefab(menuPrefab);
            menuInstance.name = "ConnectionMenu";

            var ui = Object.FindFirstObjectByType<ConnectionUISystem>();
            if (ui != null)
            {
                WireConnectionUi(ui, menuInstance);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Connection menu rebuilt from Clean Settings UI.");
        }

        [MenuItem("GypsyAliens/Rebuild Fusion Prefab Table")]
        public static void RebuildFusionPrefabs()
        {
            NetworkProjectConfigUtilities.RebuildPrefabTable();
            Debug.Log("Fusion prefab table rebuilt.");
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabNetworkFolder);
            EnsureFolder(PrefabUiFolder);
            EnsureFolder("Assets/Art");
            EnsureFolder(ArtAnimatorsFolder);
            EnsureFolder(DataFolder);
            EnsureFolder("Assets/Scenes");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, name);
        }

        static BuildingTileSet CreateOrUpdateTileSet()
        {
            const string path = DataFolder + "/BuildingTileSet.asset";
            var asset = AssetDatabase.LoadAssetAtPath<BuildingTileSet>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<BuildingTileSet>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var so = new SerializedObject(asset);
            so.FindProperty("_floorPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(FloorPath);
            so.FindProperty("_wallPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(WallPath);
            so.FindProperty("_doorWallPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(DoorWallPath);
            so.FindProperty("_cellSize").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static RuntimeAnimatorController CreatePlayerAnimator()
        {
            const string path = ArtAnimatorsFolder + "/PlayerLocomotion.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var idleClip = LoadFirstClip(IdleClipPath);
            var walkClip = LoadFirstClip(WalkClipPath);

            var root = controller.layers[0].stateMachine;
            var idle = root.AddState("Idle", new Vector3(300, 0, 0));
            idle.motion = idleClip;
            var walk = root.AddState("Walk", new Vector3(300, 80, 0));
            walk.motion = walkClip;
            root.defaultState = idle;

            var toWalk = idle.AddTransition(walk);
            toWalk.hasExitTime = false;
            toWalk.duration = 0.15f;
            toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

            var toIdle = walk.AddTransition(idle);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.15f;
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

            EditorUtility.SetDirty(controller);
            return controller;
        }

        static AnimationClip LoadFirstClip(string fbxPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (var asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }

            Debug.LogError($"Could not load AnimationClip from {fbxPath}");
            return null;
        }

        static NetworkRunner CreateRunnerPrefab()
        {
            const string path = PrefabNetworkFolder + "/NetworkRunner.prefab";
            var go = new GameObject("NetworkRunner");
            var runner = go.AddComponent<NetworkRunner>();
            go.AddComponent<NetworkSceneManagerDefault>();
            go.AddComponent<PlayerSpawner>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<NetworkRunner>();
        }

        static NetworkObject CreateSessionPrefab()
        {
            const string path = PrefabNetworkFolder + "/NetworkGameSession.prefab";
            var go = new GameObject("NetworkGameSession");
            var netObj = go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkGameSession>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<NetworkObject>();
        }

        static NetworkObject CreatePlayerPrefab(RuntimeAnimatorController animator)
        {
            const string path = PrefabNetworkFolder + "/NetworkPlayer.prefab";
            var go = new GameObject("NetworkPlayer");
            go.AddComponent<NetworkObject>();
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            go.AddComponent<NetworkCharacterController>();
            go.AddComponent<NetworkPlayerController>();
            go.AddComponent<PlayerAnimationDriver>();

            var dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPath);
            if (dummyPrefab != null)
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab);
                visual.name = "Visual";
                visual.transform.SetParent(go.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;

                var anim = visual.GetComponentInChildren<Animator>();
                if (anim == null)
                {
                    anim = visual.AddComponent<Animator>();
                }

                anim.runtimeAnimatorController = animator;
                anim.applyRootMotion = false;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<NetworkObject>();
        }

        static void CreateGameScene(
            BuildingTileSet tileSet,
            NetworkRunner runnerPrefab,
            NetworkObject sessionPrefab,
            NetworkObject playerPrefab,
            GameObject connectionMenuPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Object.FindFirstObjectByType<UnityEngine.Camera>();
            if (camera == null)
            {
                var camGo = new GameObject("Main Camera");
                camera = camGo.AddComponent<UnityEngine.Camera>();
                camGo.tag = "MainCamera";
            }

            camera.orthographic = true;
            camera.orthographicSize = 11f;
            camera.transform.position = new Vector3(0f, 0f, 0f) + Quaternion.Euler(50f, 45f, 0f) * (Vector3.back * 28f);
            camera.transform.rotation = Quaternion.Euler(50f, 45f, 0f);
            var follow = camera.gameObject.GetComponent<IsometricCameraFollow>();
            if (follow == null)
            {
                follow = camera.gameObject.AddComponent<IsometricCameraFollow>();
            }

            var levelRoot = new GameObject("LevelRoot").transform;

            var systems = new GameObject("Systems");
            systems.AddComponent<SystemLocator>();

            var networkGo = new GameObject("NetworkService");
            networkGo.transform.SetParent(systems.transform);
            var network = networkGo.AddComponent<NetworkService>();
            var networkSo = new SerializedObject(network);
            networkSo.FindProperty("_runnerPrefab").objectReferenceValue = runnerPrefab;
            networkSo.FindProperty("_sessionPrefab").objectReferenceValue = sessionPrefab;
            networkSo.FindProperty("_playerPrefab").objectReferenceValue = playerPrefab;
            networkSo.ApplyModifiedPropertiesWithoutUndo();

            var levelGo = new GameObject("LevelGenerationSystem");
            levelGo.transform.SetParent(systems.transform);
            var level = levelGo.AddComponent<LevelGenerationSystem>();
            levelGo.AddComponent<ProceduralBuildingGenerator>();
            var levelSo = new SerializedObject(level);
            levelSo.FindProperty("_tileSet").objectReferenceValue = tileSet;
            levelSo.FindProperty("_levelRoot").objectReferenceValue = levelRoot;
            levelSo.FindProperty("_generator").objectReferenceValue = levelGo.GetComponent<ProceduralBuildingGenerator>();
            levelSo.ApplyModifiedPropertiesWithoutUndo();

            var genSo = new SerializedObject(levelGo.GetComponent<ProceduralBuildingGenerator>());
            genSo.FindProperty("_tileSet").objectReferenceValue = tileSet;
            genSo.FindProperty("_levelRoot").objectReferenceValue = levelRoot;
            genSo.ApplyModifiedPropertiesWithoutUndo();

            var cameraGo = new GameObject("CameraSystem");
            cameraGo.transform.SetParent(systems.transform);
            var cameraSystem = cameraGo.AddComponent<CameraSystem>();
            var camSo = new SerializedObject(cameraSystem);
            camSo.FindProperty("_follow").objectReferenceValue = follow;
            camSo.ApplyModifiedPropertiesWithoutUndo();

            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            var menuInstance = (GameObject)PrefabUtility.InstantiatePrefab(connectionMenuPrefab);
            menuInstance.name = "ConnectionMenu";

            var uiGo = new GameObject("ConnectionUISystem");
            uiGo.transform.SetParent(systems.transform);
            var ui = uiGo.AddComponent<ConnectionUISystem>();
            WireConnectionUi(ui, menuInstance);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
        }

        static GameObject CreateConnectionMenuPrefab()
        {
            const string path = PrefabUiFolder + "/ConnectionMenu.prefab";
            const string cleanRoot = "Assets/Clean Settings UI/";

            var squareButtonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cleanRoot + "Prefabs/SquareButton.prefab");
            var inputFieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cleanRoot + "Prefabs/InputField.prefab");
            var headerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cleanRoot + "Prefabs/Header - Dark.prefab");
            var subHeaderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cleanRoot + "Prefabs/Sub header - Dark.prefab");
            var descriptionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cleanRoot + "Prefabs/Description text - Dark.prefab");
            var containerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(cleanRoot + "Images/container.png");
            var backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(cleanRoot + "Images/backgrounds/background1.png");

            var root = new GameObject("ConnectionMenu", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();

            // Fullscreen backdrop from Clean Settings UI.
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(root.transform, false);
            var backdropRt = backdrop.GetComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;
            var backdropImage = backdrop.GetComponent<Image>();
            backdropImage.sprite = backgroundSprite;
            backdropImage.type = Image.Type.Simple;
            backdropImage.preserveAspect = false;
            backdropImage.color = Color.white;

            // Center panel using container sprite.
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(520, 420);
            panelRt.anchoredPosition = Vector2.zero;
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = containerSprite;
            panelImage.type = Image.Type.Sliced;
            panelImage.color = Color.white;

            var header = InstantiateUiPrefab(headerPrefab, panel.transform, "Title");
            SetRect(header, new Vector2(0f, 150f), new Vector2(360f, 40f));
            SetUiText(header, "GypsyAliens", TextAnchor.MiddleCenter);

            var subtitle = InstantiateUiPrefab(subHeaderPrefab, panel.transform, "Subtitle");
            SetRect(subtitle, new Vector2(0f, 105f), new Vector2(360f, 28f));
            SetUiText(subtitle, "Multiplayer Session", TextAnchor.MiddleCenter);

            var description = InstantiateUiPrefab(descriptionPrefab, panel.transform, "Description");
            SetRect(description, new Vector2(0f, 70f), new Vector2(400f, 28f));
            SetUiText(description, "Enter a room name, then host or join.", TextAnchor.MiddleCenter);

            var roomInput = InstantiateUiPrefab(inputFieldPrefab, panel.transform, "RoomName");
            SetRect(roomInput, new Vector2(0f, 10f), new Vector2(360f, 48f));
            ConfigureRoomInput(roomInput, "GypsyAliens");

            var hostBtn = InstantiateUiPrefab(squareButtonPrefab, panel.transform, "HostButton");
            SetRect(hostBtn, new Vector2(-140f, -90f), new Vector2(160f, 56f));
            SetUiText(hostBtn, "Host", TextAnchor.MiddleCenter);

            var joinBtn = InstantiateUiPrefab(squareButtonPrefab, panel.transform, "JoinButton");
            SetRect(joinBtn, new Vector2(140f, -90f), new Vector2(160f, 56f));
            SetUiText(joinBtn, "Join", TextAnchor.MiddleCenter);

            var autoBtn = InstantiateUiPrefab(squareButtonPrefab, panel.transform, "AutoButton");
            SetRect(autoBtn, new Vector2(0f, -170f), new Vector2(200f, 56f));
            SetUiText(autoBtn, "Auto Host/Join", TextAnchor.MiddleCenter);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject InstantiateUiPrefab(GameObject source, Transform parent, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            return instance;
        }

        static void SetRect(GameObject go, Vector2 anchoredPosition, Vector2 size)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
        }

        static void SetUiText(GameObject root, string value, TextAnchor alignment)
        {
            var texts = root.GetComponentsInChildren<Text>(true);
            if (texts.Length == 0)
            {
                return;
            }

            // Prefer a child named Text, otherwise the root/first Text.
            Text target = null;
            foreach (var text in texts)
            {
                if (text.gameObject.name == "Text")
                {
                    target = text;
                    break;
                }
            }

            target ??= texts[0];
            target.text = value;
            target.alignment = alignment;
        }

        static void ConfigureRoomInput(GameObject roomInput, string defaultValue)
        {
            var label = roomInput.transform.Find("Label");
            if (label != null)
            {
                var labelText = label.GetComponent<Text>();
                if (labelText != null)
                {
                    labelText.text = "Room Name";
                }
            }

            var input = roomInput.GetComponent<InputField>();
            if (input != null)
            {
                input.text = defaultValue;
                input.contentType = InputField.ContentType.Alphanumeric;
            }
        }

        static void WireConnectionUi(ConnectionUISystem ui, GameObject menuRoot)
        {
            var so = new SerializedObject(ui);
            so.FindProperty("_menuRoot").objectReferenceValue = menuRoot;

            var input = menuRoot.GetComponentInChildren<InputField>(true);
            so.FindProperty("_roomNameInput").objectReferenceValue = input;

            foreach (var button in menuRoot.GetComponentsInChildren<Button>(true))
            {
                switch (button.gameObject.name)
                {
                    case "HostButton":
                        so.FindProperty("_hostButton").objectReferenceValue = button;
                        break;
                    case "JoinButton":
                        so.FindProperty("_joinButton").objectReferenceValue = button;
                        break;
                    case "AutoButton":
                        so.FindProperty("_autoButton").objectReferenceValue = button;
                        break;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
            {
                if (s.path == scenePath)
                {
                    return;
                }
            }

            var list = new EditorBuildSettingsScene[scenes.Length + 1];
            for (var i = 0; i < scenes.Length; i++)
            {
                list[i] = scenes[i];
            }

            list[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = list;
        }
    }
}
#endif
