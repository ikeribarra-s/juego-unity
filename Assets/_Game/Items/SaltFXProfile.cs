using UnityEngine;

/// <summary>
/// Look + numbers of the pouring salt (<see cref="SaltFX"/>, <see cref="SaltBagUse"/>): pixel-art materials, sounds and tunables.
/// One asset (<c>Assets/_Game/Items/SaltFX_Profile.asset</c>), built by Fluffterror > Items > Setup Inventory Items.
/// </summary>
[CreateAssetMenu(menuName = "Fluffterror/Items/Salt FX Profile", fileName = "SaltFX_Profile")]
public class SaltFXProfile : ScriptableObject
{
    [Header("Materials (point-filtered pixel sheets, Assets/_Game/Rendering/VFX/Salt)")]
    [Tooltip("Floor piles: URP Lit, alpha-clipped, SaltPile_Sheet (4x2 variants of 16 px).")]
    public Material Pile;
    [Tooltip("Falling grains: URP Particles/Unlit, SaltGrain_Sheet (4x1 of 8 px, random frame).")]
    public Material Grain;
    [Tooltip("Dust puff where the salt lands: URP Particles/Unlit, SaltPuff_Sheet (4x4 of 16 px, whole sheet over life).")]
    public Material Puff;

    [Header("Sounds")]
    [Tooltip("Looped while pouring (SFX_Items_SaltPour).")]
    public SoundDefinition PourLoop;
    [Tooltip("Cloth rustle + grains when the bag tilts and the flow starts.")]
    public SoundDefinition Shake;
    [Tooltip("Tiny granular tick where the salt hits the floor.")]
    public SoundDefinition Land;

    [Header("Bag")]
    [Tooltip("Salt units in a full bag. A lot: with Units Per Second 9 this is ~17 s of pouring.")]
    public float Capacity = 150f;
    [Tooltip("Units poured per second while F is held.")]
    public float UnitsPerSecond = 9f;

    [Header("Stream (particles)")]
    public float GrainsPerSecond = 120f;
    [Tooltip("World size (m) of a falling grain sprite.")]
    public Vector2 GrainSize = new(0.05f, 0.085f);
    [Tooltip("Sideways scatter of the flow at the mouth and on the floor (m).")]
    public float MouthSpread = 0.016f;
    public float FloorSpread = 0.09f;
    [Range(0f, 1f)] public float GrainBrightness = 0.85f;

    [Header("Landing")]
    [Tooltip("Landings per second (each: puff + floor pile + tick).")]
    public float ImpactsPerSecond = 14f;
    public Vector2 PuffSize = new(0.30f, 0.46f);
    public float PuffLife = 0.55f;
    public int StartBurst = 16;

    [Header("Floor piles")]
    public Vector2 PileSize = new(0.22f, 0.36f);
    [Tooltip("A landing this close to an existing pile makes it grow instead of adding another.")]
    public float MergeRadius = 0.14f;
    public float GrowStep = 0.03f;
    public float MaxPileSize = 0.7f;
    [Tooltip("Oldest piles are recycled beyond this many (all bags together).")]
    public int MaxPiles = 600;
}
