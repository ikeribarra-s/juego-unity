using UnityEngine;

/// <summary>
/// The salt bag as a usable item (<see cref="IUsableItem"/>): a big bag of salt (<see cref="SaltFXProfile.Capacity"/> units)
/// that the player pours by holding F (<see cref="HandItemController"/>): after a short delay a stream of pixel grains falls from
/// a point in front of the chest onto the floor where the player looks (<see cref="SaltFX"/>): puffs, ticks and
/// white piles that grow where more salt lands. Pouring uses salt up (the slot shows the percentage left); the empty bag is
/// destroyed. The mesh itself has no static salt. A bag dropped in the world keeps what is left in it.
/// </summary>
[RequireComponent(typeof(ItemPickup))]
public class SaltBagUse : MonoBehaviour, IUsableItem
{
    [SerializeField] private SaltFXProfile _fx;
    [Tooltip("Model shown in the right hand while the bag is in the pockets (no physics / scripts).")]
    [SerializeField] private GameObject _heldPrefab;

    private float _amount = -1f;   // -1 = full (resolved lazily: the pocketed bag is inactive, so Awake may never run)
    private bool _flowing;
    private int _shownPercent = -1;
    private ItemPickup _item;

    public float Amount => _amount < 0f ? Capacity : _amount;
    public float Capacity => _fx != null ? _fx.Capacity : 150f;
    public float Fill01 => Mathf.Clamp01(Amount / Mathf.Max(1f, Capacity));
    public bool IsFlowing => _flowing;

    public string UseHint => "hold to pour salt";
    public GameObject HeldPrefab => _heldPrefab;
    public bool CanUse => Amount > 0.001f;

    public void Setup(SaltFXProfile fx, GameObject heldPrefab) { _fx = fx; _heldPrefab = heldPrefab; }

    private void OnEnable() => RefreshSubtitle();

    private void RefreshSubtitle()
    {
        int pct = Mathf.CeilToInt(Fill01 * 100f);
        if (pct == _shownPercent) return;
        _shownPercent = pct;
        if (_item == null) _item = GetComponent<ItemPickup>();
        if (_item != null) _item.SetSubtitle($"{pct}%");
    }

    public bool UseTick(CharacterBase user, HandItemController hand, float dt)
    {
        if (!CanUse) return true;
        if (_fx == null || !hand.PourReady) return false;      // wait for the pour start-up delay
        if (!hand.TryGetPourTarget(out Vector3 target, out Vector3 normal)) return false;

        var fx = SaltFX.Get(_fx);
        Vector3 mouth = hand.MouthWorld;
        if (!_flowing)
        {
            _flowing = true;
            if (_fx.Shake != null && SoundManager.Instance != null) SoundManager.Instance.Play(_fx.Shake, mouth);
            fx.Burst(mouth, target);
        }

        float use = Mathf.Min(Amount, _fx.UnitsPerSecond * dt);
        _amount = Amount - use;
        fx.Pour(mouth, target, normal, dt, dt > 0f ? use / (_fx.UnitsPerSecond * dt) : 1f);
        hand.SetPourSound(_fx.PourLoop, true);
        RefreshSubtitle();
        return _amount <= 0.001f;
    }

    public void UseEnd(CharacterBase user, HandItemController hand)
    {
        _flowing = false;
        if (hand != null) hand.SetPourSound(null, false);
    }
}
