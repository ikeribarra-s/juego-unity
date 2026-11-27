using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networking settings, loaded from <c>Resources/NetConfig</c> by <see cref="NetworkBootstrap"/>. Built by
/// <b>Fluffterror > Network > Build Network Setup</b> (<c>NetworkSetupBuilder</c>).
/// </summary>
[CreateAssetMenu(menuName = "Fluffterror/Network/Net Config", fileName = "NetConfig")]
public class NetConfig : ScriptableObject
{
    [Header("Prefabs")]
    [Tooltip("The player (NetworkObject on the root). Spawned by the server for every connected client; instantiated through " +
             "PlayerFactory on every peer so remote copies are configured BEFORE Awake/OnEnable.")]
    public NetworkObject PlayerPrefab;
    [Tooltip("Action-less InputReader handed to remote players: their input subscriptions (movement, grabber, torch...) never fire.")]
    public InputReader RemoteInput;

    [Header("Scenes")]
    public string LobbyScene = "Lobby";
    [Tooltip("Scene the host loads for the run; players are spawned once it is loaded.")]
    public string GameScene = "SampleScene";

    [Header("Session")]
    [Range(1, 8)] public int MaxPlayers = 8;
    public ushort Port = 7777;
    [Tooltip("Network ticks per second (NetworkTransform / NetworkVariable send rate).")]
    public uint TickRate = 30;

    public static NetConfig Load() => Resources.Load<NetConfig>("NetConfig");
}
