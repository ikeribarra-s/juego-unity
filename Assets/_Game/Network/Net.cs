using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Small static facade over NetworkManager so gameplay code does not touch it directly. Solo play is a host with no
/// clients, so <see cref="IsServer"/> is true there as well: write server-side code once, it works offline too.
/// </summary>
public static class Net
{
    public static NetworkManager Manager => NetworkManager.Singleton;

    /// <summary>A session (host or client) is running.</summary>
    public static bool IsOnline => Manager != null && Manager.IsListening;

    /// <summary>True on the host (including solo) and when there is no session at all (scene-placed offline tools such as TestScene).</summary>
    public static bool IsServer => !IsOnline || Manager.IsServer;

    /// <summary>True when connected as a pure client (not the host).</summary>
    public static bool IsPureClient => IsOnline && Manager.IsClient && !Manager.IsServer;

    /// <summary>The character this machine controls, or null (no session / not spawned yet).</summary>
    public static CharacterBase LocalPlayer
    {
        get
        {
            if (!IsOnline) return null;
            var po = Manager.LocalClient != null ? Manager.LocalClient.PlayerObject : null;
            return po != null ? po.GetComponent<CharacterBase>() : null;
        }
    }
}
