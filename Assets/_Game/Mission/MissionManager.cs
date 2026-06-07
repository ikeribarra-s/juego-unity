using System;
using System.Collections.Generic;
using UnityEngine;

public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [SerializeField] private MissionConfig _config;
    [Tooltip("Temporary — replaced by NetworkManager.ConnectedClients.Count in Phase 4")]
    [SerializeField] private int _playerCount = 1;

    public event Action            MissionWon;
    public event Action            MissionLost;
    public event Action<EvidencePickup> EvidenceDestroyed;

    public int   AlivePlayerCount => _playerCount;
    public float TimeRemaining    { get; private set; }
    public bool  IsActive         { get; private set; }

    public IReadOnlyList<EvidencePickup> AllEvidence => _allEvidence;
    private readonly List<EvidencePickup> _allEvidence = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        RegisterAllSceneEvidence();
        TimeRemaining = _config.MissionDuration;
        IsActive      = true;

        int required = _config.GetRequiredEvidenceCount(_playerCount);
        if (_allEvidence.Count < required)
            Debug.LogWarning($"[Mission] Scene has {_allEvidence.Count} items but config requires {required} for {_playerCount} player(s). Add more EvidencePickup objects.");

        Debug.Log($"[Mission] '{_config.MissionName}' started — {_allEvidence.Count} evidence items, {_config.MissionDuration}s timer.");
    }

    private void Update()
    {
        if (!IsActive) return;

        TimeRemaining -= Time.deltaTime;
        if (TimeRemaining <= 0f)
        {
            TimeRemaining = 0f;
            EndMission(won: false);
        }
    }

    private void RegisterAllSceneEvidence()
    {
        _allEvidence.Clear();
        _allEvidence.AddRange(FindObjectsByType<EvidencePickup>(FindObjectsSortMode.None));
    }

    public void NotifyEvidenceDestroyed(EvidencePickup item)
    {
        EvidenceDestroyed?.Invoke(item);
        Debug.Log($"[Mission] Evidence destroyed: {item.Definition.DisplayName}. Must be replaced.");
    }

    public void RegisterNewEvidence(EvidencePickup item)
    {
        // Called when Fotchi creates a new photo at runtime
        _allEvidence.Add(item);
    }

    public void TriggerWin() => EndMission(won: true);

    private void EndMission(bool won)
    {
        if (!IsActive) return;
        IsActive = false;

        if (won)
        {
            Debug.Log("[Mission] WIN — all evidence extracted.");
            MissionWon?.Invoke();
        }
        else
        {
            Debug.Log("[Mission] LOSE — time expired.");
            MissionLost?.Invoke();
        }
    }
}
