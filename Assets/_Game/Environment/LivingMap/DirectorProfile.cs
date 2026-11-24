using UnityEngine;

/// <summary>
/// Tuning for the <see cref="TensionDirector"/> (Living Map §5.1). One asset per "flavour" of run.
/// Tension T ∈ [0,1]: CALM ──(T ≥ BuildUpAt)──► BUILD-UP ──(T ≥ SpikeAt or creature Chase)──► SPIKE
/// ──(SpikeDuration)──► RELIEF (forced decay) ──► CALM.
/// </summary>
[CreateAssetMenu(fileName = "DP_New", menuName = "Fluffterror/Living Map/Director Profile")]
public class DirectorProfile : ScriptableObject
{
    [Header("Tension")]
    [Tooltip("T gained per second while Calm (the slow creep).")]
    public float CalmRise = 0.01f;
    [Tooltip("T gained per second during Build-up (it keeps climbing toward the spike).")]
    public float BuildUpRise = 0.012f;
    [Tooltip("Random extra rise per tick (so the rhythm is felt but not predicted).")]
    public float RiseJitter = 0.006f;
    [Range(0f, 1f)] public float BuildUpAt = 0.35f;
    [Range(0f, 1f)] public float SpikeAt = 0.8f;
    [Tooltip("Spike length (s) before relief, random in range (doc: 20–40 s after the spike starts relief).")]
    public Vector2 SpikeDuration = new(8f, 14f);
    [Tooltip("Relief length (s): T decays to ReliefFloor, no scares except a calming event.")]
    public Vector2 ReliefDuration = new(20f, 35f);
    [Range(0f, 1f)] public float ReliefFloor = 0.1f;
    [Tooltip("Comfort: T lost per second while every living player is within GroupRadius (only with 2+ players).")]
    public float GroupComfort = 0.02f;
    public float GroupRadius = 8f;

    [Header("Inputs")]
    public float CreatureInvestigate = 0.15f;
    public float CreaturePoint = 0.3f;
    [Tooltip("Permanent floor added per running generator.")]
    public float GeneratorFloor = 0.1f;
    [Tooltip("Permanent floor once the extraction opens.")]
    public float ExtractionFloor = 0.3f;

    [Header("Events")]
    [Tooltip("Events per minute in Calm / Build-up / Spike / Relief.")]
    public Vector4 EventsPerMinute = new(2f, 5f, 0f, 1f);
    [Tooltip("Never two director events closer than this (s).")]
    public float MinEventGap = 6f;
    [Tooltip("Seconds after the level starts before the first event (let the run card read).")]
    public float StartGrace = 12f;
    public AmbientEventDefinition[] Events;
    [Tooltip("Fired once when entering Spike (the sting).")]
    public AmbientEventDefinition SpikeEntryEvent;

    [Header("Moods (LightingMoodController)")]
    public LightingMood CalmMood = LightingMood.Normal;
    public LightingMood BuildUpMood = LightingMood.Tense;
    public LightingMood SpikeMood = LightingMood.Chase;
    public LightingMood ReliefMood = LightingMood.Normal;
    [Tooltip("Calm/Relief mood once the extraction is open.")]
    public LightingMood UnlockedMood = LightingMood.Extraction;
    [Tooltip("Mood blend time per phase change (s). Build-up creeps in slowly; spike snaps.")]
    public float BuildUpBlend = 6f;
    public float SpikeBlend = 0.6f;
    public float CalmBlend = 4f;

    [Header("Tick")]
    [Tooltip("Director update rate (Hz). Cheap — no Find* per tick.")]
    public float TickRate = 2f;
}
