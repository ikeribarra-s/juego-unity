using UnityEngine;

[CreateAssetMenu(fileName = "EvidenceItem", menuName = "Fluffterror/Evidence Item")]
public class EvidenceItem : ScriptableObject
{
    [Header("Identity")]
    public string DisplayName;
    public EvidenceType Type;
    public Sprite Icon;

    [Header("Value")]
    public float BaseValue = 10f;
    [Range(0.5f, 2f)]
    public float QualityMultiplier = 1f;

    [Header("Properties")]
    public bool IsBreakable;

    public float FinalValue => BaseValue * QualityMultiplier;
}
