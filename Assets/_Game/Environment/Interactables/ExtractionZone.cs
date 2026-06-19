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
            _playersInZone.Add(character);
    }

    private void OnTriggerStay(Collider other)
    {
        // Stay (not Enter) so beam-carried items register when the player
        // releases them while they are already inside the zone.
        var pickup = other.GetComponent<EvidencePickup>();
        if (pickup == null || pickup.State != EvidencePickup.EvidenceState.InWorld) return;

        pickup.OnEnteredExtractionZone();
        _droppedEvidenceInZone.Add(pickup);
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

        // Items grabbed back out of the zone (or destroyed) no longer count.
        // Beam-carried items must be RELEASED inside the zone to count, R.E.P.O.-style.
        _droppedEvidenceInZone.RemoveWhere(p => p.State != EvidencePickup.EvidenceState.AtExtractionZone);

        bool allEvidencePresent = _droppedEvidenceInZone.Count >= allEvidence.Count;

        // Majority = strictly more than half of alive players
        int aliveCount       = MissionManager.Instance.AlivePlayerCount;
        bool majorityPresent = _playersInZone.Count > aliveCount * 0.5f;

        return allEvidencePresent && majorityPresent;
    }
}
