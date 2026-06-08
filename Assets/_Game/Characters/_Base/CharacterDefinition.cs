using UnityEngine;

[CreateAssetMenu(fileName = "CharacterDefinition", menuName = "Fluffterror/Character Definition")]
public class CharacterDefinition : ScriptableObject
{
    public enum MovementClass { Base, Trotin }

    [SerializeField] private string         _displayName = "Unnamed";
    [SerializeField] private MovementClass  _movement;
    [SerializeField] private CharacterStats _stats;

    public string         DisplayName => _displayName;
    public MovementClass  Movement    => _movement;
    public CharacterStats Stats       => _stats;
}
