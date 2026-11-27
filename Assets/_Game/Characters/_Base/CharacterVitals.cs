using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimal life/death state for a character — the victim side of <see cref="IKillable"/>. Auto-added
/// by <see cref="CharacterBase"/> (like PhysicsGrabber), so existing character prefabs gain it with no
/// editor setup; exposed as <see cref="CharacterBase.Vitals"/>.
///
/// On <see cref="Kill"/> it stops player control (disabling CharacterMovement also unlocks the cursor
/// via that component's OnDisable) and fires <see cref="Died"/> — <see cref="SkeletonDisassembler"/> listens and
/// scatters the bones, the local player then spectates as a living skull (<see cref="LivingSkull"/>).
/// The run is only lost when no living character remains (<see cref="AliveCount"/> == 0 →
/// <see cref="MissionManager.TriggerLoss"/>).
/// </summary>
[DisallowMultipleComponent]
public class CharacterVitals : MonoBehaviour, IKillable
{
    private static readonly List<CharacterVitals> s_all = new();

    /// <summary>Every enabled CharacterVitals in the loaded scenes (dead ones included).</summary>
    public static IReadOnlyList<CharacterVitals> All => s_all;

    /// <summary>
    /// Living characters in the scene. Phase 4 (NGO): count connected players' characters instead — today a dev
    /// dummy (TestScene's TestDummy) counts as a living player too.
    /// </summary>
    public static int AliveCount
    {
        get { int n = 0; for (int i = 0; i < s_all.Count; i++) if (!s_all[i].IsDead) n++; return n; }
    }

    public bool IsDead { get; private set; }

    /// <summary>Fired once when this character dies.</summary>
    public event Action Died;

    private CharacterBase _character;

    private void Awake() => _character = GetComponent<CharacterBase>();

    private void OnEnable()  { if (!s_all.Contains(this)) s_all.Add(this); }
    private void OnDisable() => s_all.Remove(this);

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

        // Co-op: the run is lost only when everybody is dead (dead players spectate as their skull).
        int alive = AliveCount;
        if (alive == 0)
        {
            if (MissionManager.Instance != null) MissionManager.Instance.TriggerLoss();
        }
        else
        {
            Debug.Log($"[Vitals] {alive} character(s) still alive — the run goes on.", this);
        }
    }
}
