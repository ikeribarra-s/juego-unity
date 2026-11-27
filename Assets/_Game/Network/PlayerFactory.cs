using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Creates player instances for every peer. The copy is instantiated under an INACTIVE holder so nothing runs yet, a remote
/// one is configured (<see cref="CharacterBase.ConfigureAsRemote"/>: inert input, no movement, no camera rig...), and only then
/// moved to the scene root — so Awake/OnEnable of every script already see the right local/remote state (OnEnable subscribes
/// to input and locks the cursor; it must never run on a remote copy).
/// Also the <see cref="INetworkPrefabInstanceHandler"/> that NGO calls on clients when a player object spawns.
/// </summary>
public class PlayerFactory : INetworkPrefabInstanceHandler
{
    private static Transform s_holder;
    private readonly NetConfig _config;

    public PlayerFactory(NetConfig config) => _config = config;

    /// <summary>Instantiate a player for <paramref name="ownerClientId"/> (remote unless it is this machine's own client).</summary>
    public GameObject Create(ulong ownerClientId, Vector3 position, Quaternion rotation)
    {
        if (s_holder == null)
        {
            var h = new GameObject("PlayerFactory (inactive holder)") { hideFlags = HideFlags.HideInHierarchy };
            h.SetActive(false);
            Object.DontDestroyOnLoad(h);
            s_holder = h.transform;
        }

        GameObject go = Object.Instantiate(_config.PlayerPrefab.gameObject, s_holder);
        go.transform.SetPositionAndRotation(position, rotation);

        bool isLocal = NetworkManager.Singleton != null && ownerClientId == NetworkManager.Singleton.LocalClientId;
        var cb = go.GetComponent<CharacterBase>();
        if (!isLocal && cb != null) cb.ConfigureAsRemote(_config.RemoteInput);

        go.transform.SetParent(null, true);   // becomes active in the hierarchy -> Awake/OnEnable run now, fully configured
        go.name = isLocal ? $"Player {ownerClientId} (you)" : $"Player {ownerClientId}";
        return go;
    }

    // ── INetworkPrefabInstanceHandler (clients) ─────────────────────────────

    public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation) =>
        Create(ownerClientId, position, rotation).GetComponent<NetworkObject>();

    public void Destroy(NetworkObject networkObject)
    {
        if (networkObject != null) Object.Destroy(networkObject.gameObject);
    }
}
