using System.Collections.Generic;
using UnityEngine;

/// <summary>Marker for where players appear. The server hands them out in order (wrapping around) as clients join.</summary>
[AddComponentMenu("Fluffterror/Network/Player Spawn Point")]
public class PlayerSpawnPoint : MonoBehaviour
{
    private static readonly List<PlayerSpawnPoint> s_all = new();
    public static IReadOnlyList<PlayerSpawnPoint> All => s_all;

    private void OnEnable()  { if (!s_all.Contains(this)) s_all.Add(this); }
    private void OnDisable() => s_all.Remove(this);

    /// <summary>Spawn pose for the n-th player: points in order, spread sideways when there are fewer points than players.</summary>
    public static void GetPose(int index, out Vector3 pos, out Quaternion rot)
    {
        if (s_all.Count == 0) { pos = Vector3.zero; rot = Quaternion.identity; return; }
        var p = s_all[index % s_all.Count].transform;
        int lap = index / s_all.Count;
        pos = p.position + p.right * (lap * 1.2f);
        rot = p.rotation;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.45f);
        Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, transform.forward * 0.9f);
    }
}
