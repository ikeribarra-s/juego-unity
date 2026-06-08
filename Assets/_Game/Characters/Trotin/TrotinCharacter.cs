using UnityEngine;

[RequireComponent(typeof(TrotinMovement))]
public class TrotinCharacter : CharacterBase
{
    public TrotinMovement TrotinMovement { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        TrotinMovement = GetComponent<TrotinMovement>();
    }
}
