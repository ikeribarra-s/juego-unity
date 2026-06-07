using UnityEngine;

public class DebugHUD : MonoBehaviour
{
    [SerializeField] private CharacterBase    _character;
    [SerializeField] private MissionManager   _mission;
    [SerializeField] private InteractionSystem _interaction;

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 350, 500));
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.Label("=== DEBUG HUD ===");

        // Interaction
        GUILayout.Space(4);
        GUILayout.Label("[INTERACTION]");
        string prompt = _interaction != null ? _interaction.CurrentPrompt : "—";
        GUILayout.Label($"  Looking at: {prompt ?? "nothing"}");

        // Inventory
        GUILayout.Space(4);
        GUILayout.Label("[INVENTORY]");
        if (_character != null)
        {
            var inv = _character.Inventory;
            GUILayout.Label($"  Slots: {inv.Items.Count} / {inv.MaxSlots}");
            foreach (var item in inv.Items)
                GUILayout.Label($"    • {item.Definition.DisplayName} (val: {item.Definition.FinalValue})");
        }

        // Mission
        GUILayout.Space(4);
        GUILayout.Label("[MISSION]");
        if (_mission != null)
        {
            GUILayout.Label($"  Active: {_mission.IsActive}");
            GUILayout.Label($"  Time left: {_mission.TimeRemaining:F0}s");
            GUILayout.Label($"  Evidence in scene: {_mission.AllEvidence.Count}");

            int inWorld = 0, carried = 0, atZone = 0, destroyed = 0;
            foreach (var e in _mission.AllEvidence)
            {
                switch (e.State)
                {
                    case EvidencePickup.EvidenceState.InWorld:          inWorld++;   break;
                    case EvidencePickup.EvidenceState.Carried:          carried++;   break;
                    case EvidencePickup.EvidenceState.AtExtractionZone: atZone++;    break;
                    case EvidencePickup.EvidenceState.Destroyed:        destroyed++; break;
                }
            }
            GUILayout.Label($"  In world:    {inWorld}");
            GUILayout.Label($"  Carried:     {carried}");
            GUILayout.Label($"  At zone:     {atZone}");
            GUILayout.Label($"  Destroyed:   {destroyed}");
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
