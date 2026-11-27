using UnityEngine;

/// <summary>The pieces a dead skeleton falls apart into (<see cref="SkeletonDisassembler"/>).</summary>
public enum BoneGroup
{
    Skull,
    Ribcage,
    Pelvis,
    LeftUpperArm, LeftForearm, LeftHand,
    RightUpperArm, RightForearm, RightHand,
    LeftThigh, LeftShin, LeftFoot,
    RightThigh, RightShin, RightFoot,
}

public static class BoneGroups
{
    public const int Count = 15;

    public static string DisplayName(BoneGroup g) => g switch
    {
        BoneGroup.Skull         => "Skull",
        BoneGroup.Ribcage       => "Ribcage",
        BoneGroup.Pelvis        => "Pelvis",
        BoneGroup.LeftUpperArm  => "Left Humerus",
        BoneGroup.LeftForearm   => "Left Forearm",
        BoneGroup.LeftHand      => "Left Hand",
        BoneGroup.RightUpperArm => "Right Humerus",
        BoneGroup.RightForearm  => "Right Forearm",
        BoneGroup.RightHand     => "Right Hand",
        BoneGroup.LeftThigh     => "Left Femur",
        BoneGroup.LeftShin      => "Left Shin",
        BoneGroup.LeftFoot      => "Left Foot",
        BoneGroup.RightThigh    => "Right Femur",
        BoneGroup.RightShin     => "Right Shin",
        BoneGroup.RightFoot     => "Right Foot",
        _                       => g.ToString(),
    };

    /// <summary>Where a group's triangles go when it is too small to be its own piece (hand → forearm → humerus → ribcage …).</summary>
    public static BoneGroup Parent(BoneGroup g) => g switch
    {
        BoneGroup.LeftHand      => BoneGroup.LeftForearm,
        BoneGroup.LeftForearm   => BoneGroup.LeftUpperArm,
        BoneGroup.LeftUpperArm  => BoneGroup.Ribcage,
        BoneGroup.RightHand     => BoneGroup.RightForearm,
        BoneGroup.RightForearm  => BoneGroup.RightUpperArm,
        BoneGroup.RightUpperArm => BoneGroup.Ribcage,
        BoneGroup.LeftFoot      => BoneGroup.LeftShin,
        BoneGroup.LeftShin      => BoneGroup.LeftThigh,
        BoneGroup.LeftThigh     => BoneGroup.Pelvis,
        BoneGroup.RightFoot     => BoneGroup.RightShin,
        BoneGroup.RightShin     => BoneGroup.RightThigh,
        BoneGroup.RightThigh    => BoneGroup.Pelvis,
        BoneGroup.Skull         => BoneGroup.Ribcage,
        BoneGroup.Ribcage       => BoneGroup.Pelvis,
        _                       => BoneGroup.Pelvis,
    };

    /// <summary>Humanoid bone → piece. Neck, spine, chest and clavicles are the ribcage; fingers the hand, toes the foot.</summary>
    public static BoneGroup FromHumanBone(HumanBodyBones b)
    {
        int i = (int)b;
        if (i >= (int)HumanBodyBones.LeftThumbProximal  && i <= (int)HumanBodyBones.LeftLittleDistal)  return BoneGroup.LeftHand;
        if (i >= (int)HumanBodyBones.RightThumbProximal && i <= (int)HumanBodyBones.RightLittleDistal) return BoneGroup.RightHand;
        return b switch
        {
            HumanBodyBones.Head or HumanBodyBones.Jaw or HumanBodyBones.LeftEye or HumanBodyBones.RightEye => BoneGroup.Skull,
            HumanBodyBones.Hips          => BoneGroup.Pelvis,
            HumanBodyBones.LeftUpperArm  => BoneGroup.LeftUpperArm,
            HumanBodyBones.LeftLowerArm  => BoneGroup.LeftForearm,
            HumanBodyBones.LeftHand      => BoneGroup.LeftHand,
            HumanBodyBones.RightUpperArm => BoneGroup.RightUpperArm,
            HumanBodyBones.RightLowerArm => BoneGroup.RightForearm,
            HumanBodyBones.RightHand     => BoneGroup.RightHand,
            HumanBodyBones.LeftUpperLeg  => BoneGroup.LeftThigh,
            HumanBodyBones.LeftLowerLeg  => BoneGroup.LeftShin,
            HumanBodyBones.LeftFoot or HumanBodyBones.LeftToes   => BoneGroup.LeftFoot,
            HumanBodyBones.RightUpperLeg => BoneGroup.RightThigh,
            HumanBodyBones.RightLowerLeg => BoneGroup.RightShin,
            HumanBodyBones.RightFoot or HumanBodyBones.RightToes => BoneGroup.RightFoot,
            _                            => BoneGroup.Ribcage,   // Spine, Chest, UpperChest, Neck, shoulders
        };
    }
}
