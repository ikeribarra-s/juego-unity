using UnityEngine;

/// <summary>
/// Single source of truth for the torch throw / pick-up timing, shared by the clip builder
/// (<c>Assets/Editor/TorchHoldAnimationBuilder.cs</c> keys the poses at these times), <see cref="TorchArmAnimator"/>
/// (fires the release / pick-up end on these times) and <see cref="TorchCarrier"/> (busy window + fallback timers).
/// Change a value here and re-run <b>Fluffterror > Animation > Build Torch Hold</b>.
/// </summary>
public static class TorchActionTimings
{
    // Throw (seconds from the G press). Calm, underhand-ish lob: a small wind-up beside the head, eased.
    public const float ThrowWindUp      = 0.20f;  // arm drawn up/back beside the head
    public const float ThrowWindUpHold  = 0.28f;  // ... a little further (anticipation)
    public const float ThrowRelease     = 0.38f;  // the torch leaves the hand
    public const float ThrowReleasePose = 0.40f;  // arm forward-up
    public const float ThrowFollow      = 0.52f;  // soft follow-through, forward-down
    public const float ThrowBlendOut    = 0.56f;  // arm layer starts fading back to the locomotion arm
    public const float ThrowDuration    = 0.75f;

    // Pick-up (seconds from the E press). The torch does not move: the hand reaches, the fist closes, the arm comes back
    // to the hold pose and only then the torch pops into the hand (TorchCarrier.CompletePickup).
    public const float PickupReach      = 0.17f;  // hand open at the torch
    public const float PickupGrab       = 0.19f;  // fingers close
    public const float PickupDuration   = 0.36f;  // back in the hold pose: the torch pops into the hand

    // Animator states on the LeftArm_Torch layer.
    public const string HoldState   = "TorchHold";
    public const string ThrowState  = "TorchThrow";
    public const string PickupState = "TorchPickup";

    public static readonly int HoldHash   = Animator.StringToHash(HoldState);
    public static readonly int ThrowHash  = Animator.StringToHash(ThrowState);
    public static readonly int PickupHash = Animator.StringToHash(PickupState);
}
