using UnityEngine;

[CreateAssetMenu(fileName = "MissionConfig", menuName = "Fluffterror/Mission Config")]
public class MissionConfig : ScriptableObject
{
    [Header("Identity")]
    public string MissionName = "Mission 1";
    public int    LevelIndex  = 1;

    [Header("Evidence scaling")]
    [Tooltip("Multiplied by LevelIndex for the base required count")]
    public int BaseEvidenceCount = 2;
    [Tooltip("Added per player in the session")]
    public int PlayerMultiplier  = 1;

    [Header("Timing")]
    public float MissionDuration = 300f;

    // (BaseEvidenceCount * LevelIndex) + (playerCount * PlayerMultiplier)
    public int GetRequiredEvidenceCount(int playerCount)
        => (BaseEvidenceCount * LevelIndex) + (playerCount * PlayerMultiplier);
}
