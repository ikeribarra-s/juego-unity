using UnityEngine;

/// <summary>
/// Marker: <see cref="DynamicLightBudget"/> never gives this light shadows (it still culls it by distance).
/// For readable fill / cue lights that must light their prop evenly — landmark lights (Living Map S6), and
/// later the creature's optional chase glow (Creature_01.md: "no shadows ever").
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
[AddComponentMenu("Fluffterror/Rendering/Shadowless Light")]
public class ShadowlessLight : MonoBehaviour
{
    private void OnValidate()
    {
        if (TryGetComponent(out Light l)) l.shadows = LightShadows.None;
    }
}
