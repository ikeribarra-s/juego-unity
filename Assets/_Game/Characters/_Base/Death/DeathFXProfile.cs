using UnityEngine;

/// <summary>
/// Shared tunables + sounds of the death FX (Docs/Design/DeathFX.md section 3). One asset
/// (<c>Death/DeathFX_Profile.asset</c>) referenced by <see cref="DeathFX"/> (and optionally per <see cref="SkeletonDisassembler"/>).
/// </summary>
[CreateAssetMenu(menuName = "Fluffterror/Death/Death FX Profile", fileName = "DeathFX_Profile")]
public class DeathFXProfile : ScriptableObject
{
    [Header("Sounds")]
    public SoundDefinition Collapse;     // 3D at the pelvis, heard through walls up to 35 m
    public SoundDefinition Sting;        // 2D, local player only
    public SoundDefinition SkullRoll;    // 3D
    public SoundDefinition SkullYip;     // 3D, SkullRoll + YipDelay
    public SoundDefinition SkullReady;   // 2D, local player only
    [Tooltip("Optional 3D loop on the skull. OFF by default (a dead team of 8 would stack 7 hums); no clip is assigned yet.")]
    public SoundDefinition SkullHum;
    public bool SkullHumEnabled = false;
    public SoundDefinition BonePickup;   // 3D at the bone

    [Header("Timing")]
    public float StingDelay = 0.15f;
    public float YipDelay = 0.06f;

    [Header("Budget / culling")]
    [Tooltip("Bursts (dust + chips) allowed per second; a skull roll puff counts 0.5. Over budget: VFX dropped, sounds never.")]
    public float MaxBurstsPerSecond = 4f;
    public float VfxCullDistance = 30f;
    public float WispCullDistance = 25f;
    [Range(0f, 1f)] public float RemoteCountScale = 0.7f;

    [Header("Dead look (local player; MILD variant, DeathFX.md section 12.2)")]
    public float DeadBlendTime = 0.8f;
    public float ReviveTime = 0.6f;
    public float Saturation = -35f;
    public float PostExposure = -0.3f;
    [Range(0f, 1f)] public float Vignette = 0.5f;
    [Range(0f, 1f)] public float Bloom = 0.2f;
    [Range(0f, 1f)] public float Chromatic = 0.22f;
    [Range(0f, 1f)] public float Grain = 0.5f;
    [Tooltip("Kick at the moment of death.")]
    public float KickGrain = 0.6f;
    public float KickGrainTime = 0.30f;
    public float KickChromatic = 0.55f;
    public float KickChromaticTime = 0.25f;

    [Header("Skull wisp / eye light")]
    public float WispRate = 5f;
    public int WispRollBurst = 4;
    public float WispFadeIn = 0.5f;
    public Color EyeColor = new(0.498f, 0.839f, 0.761f, 1f);   // #7FD6C2
    public float EyeIntensity = 0.8f;
    public float EyeRange = 1.6f;
    public Vector3 EyeLocalPosition = new(0f, 0.02f, 0.08f);
    public float EyePulseMultiplier = 2f;
    public float EyePulseHold = 0.15f;
    public float EyePulseEase = 0.3f;
}
