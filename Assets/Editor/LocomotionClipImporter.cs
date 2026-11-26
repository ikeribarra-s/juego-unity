using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Fluffterror › Animation › Import Locomotion Clips: sets the Quaternius UAL single-clip FBX
/// (Assets/Art/Characters/Animations/UAL) to Humanoid with ONE shared avatar taken from Rig/UAL_Rig.fbx (T-pose + skinned mesh),
/// loops + baked root. The clip FBX are armature-only: without a mesh Unity takes whatever pose the file's nodes hold as the
/// avatar rest, so building avatars from them calibrated every clip on a mid-stride frame (rest rotations off by up to 174 deg:
/// floating, asymmetric legs, leaning chest). Never build the avatar from a clip file.
/// </summary>
public static class LocomotionClipImporter
{
    public const string Dir = "Assets/Art/Characters/Animations/UAL";
    public const float FacingOffset = 180f;   // the exported rig faces -Z
    const string Master = Dir + "/Rig/UAL_Rig.fbx";   // T-pose rig with mesh (real bind pose), built by build_ual_rig.py
    static readonly string[] Loops = { "Idle", "IdleTorch", "Walk_", "Jog_", "Sprint_", "CrouchIdle", "CrouchWalk_", "Jump_InAir" };

    [MenuItem("Fluffterror/Animation/Import Locomotion Clips")]
    public static void Run()
    {
        // Generic first: drops the stored human description so it is rebuilt from the file's rest pose.
        foreach (var p in Directory.GetFiles(Dir, "*.fbx"))
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(p.Replace('\\', '/'));
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            imp.preserveHierarchy = true;   // keep the 'Armature' root so the hierarchy matches UAL_Rig (or the copied avatar is rejected)
            imp.clipAnimations = new ModelImporterClipAnimation[0];
            imp.SaveAndReimport();
        }

        var mi = (ModelImporter)AssetImporter.GetAtPath(Master);
        mi.animationType = ModelImporterAnimationType.Generic; mi.importAnimation = false; mi.SaveAndReimport();
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.SaveAndReimport();
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(Master);
        Debug.Log($"[Loco] master avatar valid={avatar != null && avatar.isValid} human={avatar != null && avatar.isHuman}");

        foreach (var p in Directory.GetFiles(Dir, "*.fbx"))
        {
            string path = p.Replace('\\', '/');
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (path != Master)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                imp.sourceAvatar = avatar;
            }
            imp.importAnimation = true;
            imp.importCameras = false; imp.importLights = false;
            var clips = imp.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                bool loop = false;
                foreach (var l in Loops) if (c.name.StartsWith(l)) loop = true;
                c.loopTime = loop; c.loopPose = false;   // the clips already loop exactly (last key == first key)
                c.lockRootRotation = true;  c.keepOriginalOrientation = true;
                c.rotationOffset = FacingOffset;
                c.lockRootHeightY = true;   c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                clips[i] = c;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Loco] import done");
    }
}
