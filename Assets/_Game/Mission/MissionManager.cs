using System;
using System.Collections.Generic;
using UnityEngine;

public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [SerializeField] private MissionConfig _config;
    [Tooltip("Temporary — replaced by NetworkManager.ConnectedClients.Count in Phase 4")]
    [SerializeField] private int _playerCount = 1;

    [Header("Generators")]
    [Tooltip("If true, extraction stays locked until every PowerGenerator in the level is Running.")]
    [SerializeField] private bool _requireGenerators = true;

    public event Action            MissionWon;
    public event Action            MissionLost;
    public event Action<EvidencePickup> EvidenceDestroyed;
    /// <summary>Fired when the last generator starts running (extraction unlocks).</summary>
    public event Action            GeneratorsCompleted;

    public int   AlivePlayerCount => _playerCount;
    public float TimeRemaining    { get; private set; }
    public bool  IsActive         { get; private set; }

    public IReadOnlyList<EvidencePickup> AllEvidence => _allEvidence;
    private readonly List<EvidencePickup> _allEvidence = new();

    // ── Generator objective ──────────────────────────────────────────────────
    public bool RequiresGenerators    => _requireGenerators;
    public int  TotalGeneratorCount   => PowerGenerator.All.Count;
    public int  RunningGeneratorCount
    {
        get { int n = 0; foreach (var g in PowerGenerator.All) if (g.IsRunning) n++; return n; }
    }
    /// <summary>True when the generator objective doesn't block extraction:
    /// not required, no generators in the level, or every generator is Running.</summary>
    public bool GeneratorsComplete =>
        !_requireGenerators || RunningGeneratorCount >= TotalGeneratorCount;

    private bool _generatorsWereComplete;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()  => PowerGenerator.AnyStateChanged += OnGeneratorStateChanged;
    private void OnDisable() => PowerGenerator.AnyStateChanged -= OnGeneratorStateChanged;

    private void OnGeneratorStateChanged(PowerGenerator _)
    {
        if (!_requireGenerators) return;

        bool complete = GeneratorsComplete;
        if (complete && !_generatorsWereComplete)
        {
            Debug.Log($"[Mission] All {TotalGeneratorCount} generators running — extraction unlocked.");
            GeneratorsCompleted?.Invoke();
        }
        else if (!complete && _generatorsWereComplete)
        {
            // Only possible when a generator has a finite run duration.
            Debug.Log("[Mission] A generator shut down — extraction locked again.");
        }
        _generatorsWereComplete = complete;
    }

    private void Start()
    {
        RegisterAllSceneEvidence();
        TimeRemaining = _config.MissionDuration;
        IsActive      = true;

        int required = _config.GetRequiredEvidenceCount(_playerCount);
        if (_allEvidence.Count < required)
            Debug.LogWarning($"[Mission] Scene has {_allEvidence.Count} items but config requires {required} for {_playerCount} player(s). Add more EvidencePickup objects.");

        _generatorsWereComplete = GeneratorsComplete;
        Debug.Log($"[Mission] '{_config.MissionName}' started — {_allEvidence.Count} evidence items, " +
                  $"{TotalGeneratorCount} generators{(_requireGenerators ? " (required)" : "")}, {_config.MissionDuration}s timer.");
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

    public void TriggerWin()  => EndMission(won: true);

    /// <summary>End the run as a loss (e.g. a player was killed by the creature).</summary>
    public void TriggerLoss() => EndMission(won: false);

    private void EndMission(bool won)
    {
        if (!IsActive) return;
        IsActive = false;

        if (won)
        {
            Debug.Log("[Mission] WIN — generators running and all evidence extracted.");
            MissionWon?.Invoke();
        }
        else
        {
            Debug.Log("[Mission] LOSE — time expired.");
            MissionLost?.Invoke();
        }
    }
}
