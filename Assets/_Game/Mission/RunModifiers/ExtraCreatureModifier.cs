using UnityEngine;

/// <summary>
/// Extra creature: one more hunter. Until Chucho (Creature_01.md) exists this just raises
/// <see cref="EnemySpawner.EnemyCount"/>, so the extra one is whatever the spawner spawns.
/// </summary>
[CreateAssetMenu(fileName = "RM_ExtraCreature", menuName = "Fluffterror/Living Map/Run Modifier/Extra Creature")]
public class ExtraCreatureModifier : RunModifier
{
    [SerializeField, Min(1)] private int _extra = 1;

    private int _applied;

    public override void Apply(RunContext ctx)
    {
        if (ctx.Spawner == null) { Debug.LogWarning("[RunModifier] Extra creature: no EnemySpawner in the scene."); return; }
        ctx.Spawner.EnemyCount += _extra;
        _applied = _extra;
        Debug.Log($"[RunModifier] Extra creature: spawner count → {ctx.Spawner.EnemyCount}.");
    }

    public override void Remove(RunContext ctx)
    {
        if (ctx.Spawner != null) ctx.Spawner.EnemyCount -= _applied;
        _applied = 0;
    }
}
