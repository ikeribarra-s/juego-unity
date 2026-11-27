using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The NetworkBehaviour on the player prefab root. Owner-authoritative movement is carried by the NetworkTransform next to it
/// (AuthorityMode = Owner); this component registers the player and, in later milestones, replicates the small bits of state the
/// remote copies need (look pitch, crouch / sprint flags, vitals ...).
/// </summary>
[DisallowMultipleComponent]
public class NetworkPlayer : NetworkBehaviour
{
    private static readonly List<NetworkPlayer> s_all = new();
    public static IReadOnlyList<NetworkPlayer> All => s_all;

    /// <summary>The player this machine controls.</summary>
    public static NetworkPlayer Local { get; private set; }

    public CharacterBase Character { get; private set; }
    public bool IsLocalPlayer => IsOwner;

    public override void OnNetworkSpawn()
    {
        Character = GetComponent<CharacterBase>();
        if (!s_all.Contains(this)) s_all.Add(this);
        if (IsOwner) Local = this;
        name = IsOwner ? $"Player {OwnerClientId} (you)" : $"Player {OwnerClientId}";
        Debug.Log($"[NetworkPlayer] spawned {name} (owner={OwnerClientId}, server={IsServer}, local={IsOwner}) at {transform.position:F1}", this);
    }

    public override void OnNetworkDespawn()
    {
        s_all.Remove(this);
        if (Local == this) Local = null;
    }
}
