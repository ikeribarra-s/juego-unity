using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Blackout: a share of the wall torches start burnt out (relightable with a carried torch), the ambient
/// light drops and the director starts a little tenser. Dead torches also lower the real-time light count.
/// </summary>
[CreateAssetMenu(fileName = "RM_Blackout", menuName = "Fluffterror/Living Map/Run Modifier/Blackout")]
public class BlackoutModifier : RunModifier
{
    [SerializeField, Range(0f, 1f)] private float _torchesOut = 0.5f;
    [Tooltip("Never put out a torch this close to the player start (one readable light to grab).")]
    [SerializeField] private float _keepLitNearStart = 10f;
    [Tooltip("Multiplier on ambient light (LightingMoodController.AmbientScale).")]
    [SerializeField, Range(0f, 1f)] private float _ambientScale = 0.45f;
    [Tooltip("Permanent tension floor for the director.")]
    [SerializeField, Range(0f, 0.5f)] private float _tensionFloor = 0.08f;

    public override void Apply(RunContext ctx)
    {
        var torches = new List<TorchStateController>(TorchStateController.All);
        // Stable order so the same seed kills the same torches.
        torches.Sort((a, b) => a.transform.position.sqrMagnitude.CompareTo(b.transform.position.sqrMagnitude));
        Vector3 start = ctx.Layout != null ? ctx.Layout.PlayerStart : Vector3.zero;
        int killed = 0;
        foreach (var t in torches)
        {
            if (t == null || t.IsCarried) continue;
            if (ctx.Layout != null && (t.transform.position - start).sqrMagnitude < _keepLitNearStart * _keepLitNearStart) continue;
            if (ctx.Rng.NextDouble() < _torchesOut) { t.Extinguish(-1f, silent: true); killed++; }
        }
        if (ctx.Moods != null) ctx.Moods.SetScales(ctx.Moods.FogScale, _ambientScale);
        if (ctx.Director != null) ctx.Director.SetModifierFloor(_tensionFloor);
        Debug.Log($"[RunModifier] Blackout: {killed}/{torches.Count} torches out, ambient ×{_ambientScale}.");
    }

    public override void Remove(RunContext ctx)
    {
        if (ctx.Moods != null) ctx.Moods.SetScales(ctx.Moods.FogScale, 1f);
    }
}
