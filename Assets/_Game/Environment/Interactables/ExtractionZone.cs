using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ExtractionZone : MonoBehaviour
{
    [SerializeField] private float _extractionDelay = 3f;

    private readonly HashSet<CharacterBase>  _playersInZone         = new();
    private readonly HashSet<EvidencePickup> _droppedEvidenceInZone = new();

    private float _countdownTimer;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Update()
    {
        if (!MissionManager.Instance.IsActive) return;

        if (CheckWinConditions())
        {
            _countdownTimer += Time.deltaTime;
            if (_countdownTimer >= _extractionDelay)
                MissionManager.Instance.TriggerWin();
        }
        else
        {
            _countdownTimer = 0f;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        var character = other.GetComponent<CharacterBase>();
        if (character != null)
        {
            _playersInZone.Add(character);
            return;
        }

        var pickup = other.GetComponent<EvidencePickup>();
        if (pickup != null && pickup.State == EvidencePickup.EvidenceState.InWorld)
        {
            pickup.OnEnteredExtractionZone();
            _droppedEvidenceInZone.Add(pickup);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var character = other.GetComponent<CharacterBase>();
        if (character != null)
        {
            _playersInZone.Remove(character);
            return;
        }

        var pickup = other.GetComponent<EvidencePickup>();
        if (pickup != null)
        {
            pickup.OnExitedExtractionZone();
            _droppedEvidenceInZone.Remove(pickup);
        }
    }

    private bool CheckWinConditions()
    {
        var allEvidence = MissionManager.Instance.AllEvidence;
        if (allEvidence.Count == 0) return false;

        // Destroyed evidence blocks extraction — it must be replaced (e.g. Fotchi retakes photo)
        foreach (var e in allEvidence)
            if (e.State == EvidencePickup.EvidenceState.Destroyed) return false;

        // Count evidence physically in zone + evidence carried by players standing in zone
        int evidenceAtZone = _droppedEvidenceInZone.Count;
        foreach (var player in _playersInZone)
            evidenceAtZone += player.Inventory.Items.Count;

        bool allEvidencePresent = evidenceAtZone >= allEvidence.Count;

        // Majority = strictly more than half of alive players
        int aliveCount       = MissionManager.Instance.AlivePlayerCount;
        bool majorityPresent = _playersInZone.Count > aliveCount * 0.5f;

        return allEvidencePresent && majorityPresent;
    }
}
