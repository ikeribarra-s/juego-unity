using UnityEngine;

/// <summary>
/// CH_SP_TrapCorridor's <c>VOL_Trap</c> (spike pit trigger): a player who falls in dies via
/// <see cref="CharacterVitals"/>. Added by the generator on chunks that have a VOL_Trap.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TrapKillVolume : MonoBehaviour
{
    private void Reset() => GetComponent<Collider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        var cb = other.GetComponentInParent<CharacterBase>();
        if (cb == null || cb.Vitals == null || cb.Vitals.IsDead) return;
        cb.Vitals.Kill(gameObject);
    }
}
