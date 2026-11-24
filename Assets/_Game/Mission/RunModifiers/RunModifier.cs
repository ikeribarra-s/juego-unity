using UnityEngine;

/// <summary>Everything a <see cref="RunModifier"/> may touch, gathered once by <see cref="RunSetup"/>.</summary>
public class RunContext
{
    public int Seed;
    public System.Random Rng;                 // seeded from the run seed — modifiers stay deterministic per seed
    public DungeonLayout Layout;
    public LightingMoodController Moods;
    public TensionDirector Director;
    public EnemySpawner Spawner;
}

/// <summary>
/// Living Map R1 — a run mutator ("this run is different"). One per run, picked from the seed by
/// <see cref="RunSetup"/> and announced on the run card. Each is a small asset that tweaks existing
/// tunables in <see cref="Apply"/>; subclasses override only what they need.
/// </summary>
public abstract class RunModifier : ScriptableObject
{
    [SerializeField] private string _displayName = "Modifier";
    [TextArea] [SerializeField] private string _description = "";
    [SerializeField, Min(0f)] private float _weight = 1f;
    [SerializeField] private Color _cardColor = new(0.91f, 0.89f, 0.82f);

    public string DisplayName => _displayName;
    public string Description => _description;
    public float Weight => _weight;
    public Color CardColor => _cardColor;

    /// <summary>Called once at run start (after the level exists, before creatures spawn).</summary>
    public abstract void Apply(RunContext ctx);

    /// <summary>Undo anything that would outlive the run (scene objects reset anyway on reload).</summary>
    public virtual void Remove(RunContext ctx) { }
}
