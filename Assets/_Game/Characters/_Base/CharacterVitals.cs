using System;
using UnityEngine;

/// <summary>
/// Minimal life/death state for a character — the victim side of <see cref="IKillable"/>. Auto-added
/// by <see cref="CharacterBase"/> (like PhysicsGrabber), so existing character prefabs gain it with no
/// editor setup; exposed as <see cref="CharacterBase.Vitals"/>.
///
/// On <see cref="Kill"/> it stops player control (disabling CharacterMovement also unlocks the cursor
/// via that component's OnDisable) and, single-player, ends the run as a loss through
/// <see cref="MissionManager.TriggerLoss"/>. The <see cref="Died"/> event is the hook for a death
/// screen / spectator cam / co-op "downed" state later (Phase 4) — change the body of Kill to swap
/// the game-over behaviour for a revive flow.
/// </summary>
[DisallowMultipleComponent]
public class CharacterVitals : MonoBehaviour, IKillable
{
    public bool IsDead { get; private set; }

    /// <summary>Fired once when this character dies.</summary>
    public event Action Died;

    private CharacterBase _character;

    private void Awake() => _character = GetComponent<CharacterBase>();

    public void Kill(GameObject source)
    {
        if (IsDead) return;
        IsDead = true;

        // Hand control off: disabling movement also restores the cursor (CharacterMovement.OnDisable).
        if (_character != null)
        {
            if (_character.Movement != null)    _character.Movement.enabled    = false;
            if (_character.Interaction != null) _character.Interaction.enabled = false;
        }

        Debug.Log($"[Vitals] '{name}' was killed by '{(source != null ? source.name : "unknown")}'.", this);
        Died?.Invoke();

        // Single-player: death = mission lost. Co-op downed/revive is Phase 4.
        if (MissionManager.Instance != null) MissionManager.Instance.TriggerLoss();
    }
}
