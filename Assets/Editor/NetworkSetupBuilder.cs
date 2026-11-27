using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// <b>Fluffterror > Network > Build Network Setup</b> — re-runnable. Creates / updates:
///  - <c>Network/Resources/NetConfig.asset</c> + the action-less <c>InputReader_Remote.asset</c> handed to remote players,
///  - <c>Network/Prefabs/Player.prefab</c> from the scene-placed <c>Character</c> of SampleScene (NetworkObject, owner-authoritative
///    NetworkTransform, NetworkPlayer) — only when the prefab does not exist yet (delete it to rebuild),
///  - SampleScene: removes the scene-placed player, adds <c>PlayerSpawn</c> point(s) where it stood and a <c>Network</c> object (NetworkBootstrap),
///  - <c>Scenes/Lobby.unity</c> (LobbyMenu + NetworkBootstrap + camera),
///  - Build Settings: Lobby, SampleScene.
/// TestScene is untouched (keeps its scene-placed character + TestDummy for the dev probes; no bootstrap = offline).
/// </summary>
public static class NetworkSetupBuilder
{
    private const string Dir          = "Assets/_Game/Network";
    private const string ResDir       = Dir + "/Resources";
    private const string PrefabDir    = Dir + "/Prefabs";
    private const string PrefabPath   = PrefabDir + "/Player.prefab";
    private const string ConfigPath   = ResDir + "/NetConfig.asset";
    private const string RemoteInput  = ResDir + "/InputReader_Remote.asset";
    private const string DummyInput   = "Assets/_Game/Testing/InputReader_Dummy.asset";
    private const string GameScene    = "Assets/Scenes/SampleScene.unity";
    private const string LobbyScene   = "Assets/Scenes/Lobby.unity";

    [MenuItem("Fluffterror/Network/Build Network Setup")]
    public static void Build()
    {
        EnsureFolder(ResDir); EnsureFolder(PrefabDir);
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        // 1. remote input
        if (AssetDatabase.LoadAssetAtPath<InputReader>(RemoteInput) == null)
        {
            if (!AssetDatabase.CopyAsset(DummyInput, RemoteInput))
            {
                var ir = ScriptableObject.CreateInstance<InputReader>();
                AssetDatabase.CreateAsset(ir, RemoteInput);
            }
        }

        // 2. game scene: player prefab + spawn point + bootstrap
        var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
        CharacterBase character = FindScenePlayer();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            if (character == null) { Debug.LogError("[NetworkSetup] No scene-placed player in SampleScene and no Player.prefab — nothing to build from."); return; }
            BuildPlayerPrefab(character);
        }

        // 3. config asset
        var config = AssetDatabase.LoadAssetAtPath<NetConfig>(ConfigPath);
        if (config == null) { config = ScriptableObject.CreateInstance<NetConfig>(); AssetDatabase.CreateAsset(config, ConfigPath); }
        config.PlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<NetworkObject>();
        config.RemoteInput = AssetDatabase.LoadAssetAtPath<InputReader>(RemoteInput);
        config.GameScene = "SampleScene"; config.LobbyScene = "Lobby";
        EditorUtility.SetDirty(config);

        // 4. SampleScene wiring
        if (character != null)
        {
            Vector3 pos = character.transform.position; Quaternion rot = character.transform.rotation;
            Object.DestroyImmediate(character.gameObject);
            AddSpawnPoints(pos, rot);
        }
        EnsureBootstrap(config, "Network");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // 5. Lobby scene
        BuildLobby(config);

        // 6. build settings
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(LobbyScene, true),
            new EditorBuildSettingsScene(GameScene, true),
        };

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[NetworkSetup] Done: NetConfig, Player.prefab, SampleScene wired (PlayerSpawn + Network), Lobby scene, Build Settings = Lobby + SampleScene.");
    }

    // ── player prefab ───────────────────────────────────────────────────────

    private static CharacterBase FindScenePlayer()
    {
        foreach (var cb in Object.FindObjectsByType<CharacterBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (cb.GetComponentInChildren<Camera>(true) != null && cb.GetComponent<TrotinMovement>() == null) return cb;
        return null;
    }

    private static void BuildPlayerPrefab(CharacterBase source)
    {
        GameObject temp = Object.Instantiate(source.gameObject);
        temp.name = "Player";
        temp.SetActive(true);

        // Scene references inside the copy would be lost in the prefab: report them.
        var lost = new List<string>();
        foreach (var mb in temp.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var so = new SerializedObject(mb);
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                var o = it.objectReferenceValue;
                if (EditorUtility.IsPersistent(o)) continue;
                var tr = (o as Component) != null ? ((Component)o).transform : (o as GameObject) != null ? ((GameObject)o).transform : null;
                if (tr != null && !tr.IsChildOf(temp.transform)) lost.Add($"{mb.GetType().Name}.{it.propertyPath} -> {o.name}");
            }
        }
        if (lost.Count > 0) Debug.LogWarning("[NetworkSetup] Scene references on the player that the prefab cannot keep:\n" + string.Join("\n", lost));

        temp.AddComponent<NetworkObject>();
        var nt = temp.AddComponent<NetworkTransform>();
        nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;   // each player owns their own movement
        nt.SyncPositionX = nt.SyncPositionY = nt.SyncPositionZ = true;
        nt.SyncRotAngleX = false; nt.SyncRotAngleY = true; nt.SyncRotAngleZ = false;   // yaw only; pitch is its own variable
        nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
        nt.Interpolate = true;
        temp.AddComponent<NetworkPlayer>();

        PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
        Object.DestroyImmediate(temp);

        // NGO derives the prefab's GlobalObjectIdHash in OnValidate; make sure it is stored.
        var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        var no = contents.GetComponent<NetworkObject>();
        EditorUtility.SetDirty(no);
        PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
        Debug.Log("[NetworkSetup] Created " + PrefabPath);
    }

    // ── scene pieces ────────────────────────────────────────────────────────

    private static void AddSpawnPoints(Vector3 pos, Quaternion rot)
    {
        foreach (var old in Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("PlayerSpawns").transform;
        for (int i = 0; i < 4; i++)
        {
            var go = new GameObject($"PlayerSpawn_{i + 1}");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(pos + rot * new Vector3((i - 1.5f) * 1.2f, 0f, 0f), rot);
            go.AddComponent<PlayerSpawnPoint>();
        }
    }

    private static void EnsureBootstrap(NetConfig config, string name)
    {
        var existing = Object.FindFirstObjectByType<NetworkBootstrap>();
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        var boot = go.GetComponent<NetworkBootstrap>() ?? go.AddComponent<NetworkBootstrap>();
        var so = new SerializedObject(boot);
        so.FindProperty("_config").objectReferenceValue = config;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildLobby(NetConfig config)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.03f, 0.05f);
        camGo.AddComponent<AudioListener>();
        var ui = new GameObject("LobbyMenu");
        ui.AddComponent<LobbyMenu>();
        EnsureBootstrap(config, "Network");
        EditorSceneManager.SaveScene(scene, LobbyScene);
    }

    private static void EnsureFolder(string path)
    {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
    }
}
