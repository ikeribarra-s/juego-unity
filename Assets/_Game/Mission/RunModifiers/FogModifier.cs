using UnityEngine;

/// <summary>Fog: fog density ×2.5 in every mood and creature sight ×0.7 (fog is also the cheapest look).</summary>
[CreateAssetMenu(fileName = "RM_Fog", menuName = "Fluffterror/Living Map/Run Modifier/Fog")]
public class FogModifier : RunModifier
{
    [SerializeField, Range(1f, 5f)] private float _fogScale = 2.5f;
    [SerializeField, Range(0.1f, 1f)] private float _sightScale = 0.7f;
    [Tooltip("Mist colour the moods' (near-black) fog is pulled toward, so the fog reads as fog, not just darkness.")]
    [SerializeField] private Color _mistColor = new(0.13f, 0.14f, 0.16f);
    [SerializeField, Range(0f, 1f)] private float _mistAmount = 0.75f;

    public override void Apply(RunContext ctx)
    {
        if (ctx.Moods != null)
        {
            ctx.Moods.SetScales(_fogScale, ctx.Moods.AmbientScale);
            ctx.Moods.SetFogTint(_mistColor, _mistAmount);
        }
        if (ctx.Spawner != null) ctx.Spawner.SightRangeScale = _sightScale;
        Debug.Log($"[RunModifier] Fog: fog ×{_fogScale}, sight ×{_sightScale}.");
    }

    public override void Remove(RunContext ctx)
    {
        if (ctx.Moods != null) { ctx.Moods.SetScales(1f, ctx.Moods.AmbientScale); ctx.Moods.SetFogTint(Color.black, 0f); }
        if (ctx.Spawner != null) ctx.Spawner.SightRangeScale = 1f;
    }
}
