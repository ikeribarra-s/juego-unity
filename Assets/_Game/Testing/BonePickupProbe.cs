using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Dev probe for bone pick-up targeting: kills the TestDummy, aims the local player at a bone from several lateral offsets
/// (circular aim-assist zone), with and without a wall in the way, then fills the inventory to check the slot count.
/// <c>BonePickupProbe.Run()</c> in Play mode, read <c>BonePickupProbe.Report</c> when <c>Done</c>.
/// </summary>
public class BonePickupProbe : MonoBehaviour
{
    public static string Report = "";
    public static bool Done;
    readonly StringBuilder _sb = new();

    public static void Run()
    {
        Done = false; Report = "";
        new GameObject("BonePickupProbe").AddComponent<BonePickupProbe>();
    }

    void Update()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    IEnumerator Start()
    {
        var player = CharacterBase.FindLocal();
        var cc = player.Controller;
        var inter = player.Interaction;
        var targetField = typeof(InteractionSystem).GetField("_currentTarget", BindingFlags.NonPublic | BindingFlags.Instance);
        var pitchField = typeof(CharacterMovement).GetField("_cameraPitch", BindingFlags.NonPublic | BindingFlags.Instance);
        Camera cam = Camera.main;

        string Target() => targetField.GetValue(inter) is Component c && c != null ? c.name : "none";

        _sb.AppendLine($"slots: MaxSlots={player.Inventory.MaxSlots} stats={player.Stats.InventorySlots}");

        // 1. Kill the dummy, let the bones settle.
        var dummy = GameObject.Find("TestDummy");
        dummy.GetComponent<CharacterVitals>().Kill(null);
        float t = 0; while (t < 4f) { t += Time.unscaledDeltaTime; yield return null; }

        var bones = new System.Collections.Generic.List<BonePickup>(FindObjectsByType<BonePickup>(FindObjectsSortMode.None));
        _sb.AppendLine($"bone pickups spawned: {bones.Count}");
        if (bones.Count < 4) { _sb.AppendLine("not enough bones"); Finish(); yield break; }

        // Pick the bone that is most isolated (nearest other bone farthest away) so a neighbour does not win the test.
        BonePickup bone = null; float bestIso = -1f;
        foreach (var b in bones)
        {
            if (b.GetComponentInChildren<Collider>().bounds.center.y > 0.35f) continue;   // only bones lying on the floor
            float nearest = float.MaxValue;
            foreach (var o in bones) if (o != b) nearest = Mathf.Min(nearest, Vector3.Distance(b.transform.position, o.transform.position));
            if (nearest > bestIso) { bestIso = nearest; bone = b; }
        }
        Vector3 B = bone.GetComponentInChildren<Collider>().bounds.center;
        _sb.AppendLine($"test bone {bone.name} at {B:F2}, nearest other bone {bestIso:F2} m");
        float floorY = B.y;   // the bone's own height (it lies on the floor)

        Vector3 dirToPlayer = player.transform.position - B; dirToPlayer.y = 0; dirToPlayer.Normalize();
        float[] offsets = { 0f, 0.2f, 0.35f, 0.42f, 0.6f, 0.9f };
        foreach (float off in offsets)
        {
            yield return Aim(player, cc, pitchField, cam, B, floorY, dirToPlayer, off, 1.7f);
            string rayInfo = Physics.Raycast(cam.transform.position, cam.transform.forward, out var rh, 10f, ~0, QueryTriggerInteraction.Ignore) ? $"{rh.collider.name} @ {rh.distance:F2} m, point {rh.point:F2}" : "nothing";
            _sb.AppendLine($"  offset {off:F2} m from the bone -> target: {Target()}  (prompt: {inter.CurrentPrompt}) | camRay -> {rayInfo} | eye {cam.transform.position:F2} pitch {cam.transform.eulerAngles.x:F1} | interaction enabled={inter.enabled}, bone active={bone.gameObject.activeInHierarchy}, bone dist to wanted floor pt {Vector3.Distance(B, rayOrigin(off, B, floorY, dirToPlayer)):F2}");
        }

        // 2. A wall between the eye and the bone must block the assist.
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        yield return Aim(player, cc, pitchField, cam, B, floorY, dirToPlayer, 0.2f, 1.7f);
        Vector3 eye = cam.transform.position;
        wall.transform.position = eye + (B - eye) * 0.45f;
        wall.transform.rotation = Quaternion.LookRotation(new Vector3(B.x - eye.x, 0, B.z - eye.z));
        wall.transform.localScale = new Vector3(3f, 6f, 0.1f);
        yield return null; yield return null; yield return null;
        _sb.AppendLine($"  offset 0.20 m with a wall in between -> target: {Target()}");
        Destroy(wall);

        // 3. Slots: pick up bones through Interact until full.
        int picked = 0;
        foreach (var b in bones)
        {
            if (b == null || !b.gameObject.activeSelf) continue;
            b.Interact(player);
            if (!b.gameObject.activeSelf) picked++;
            if (picked >= 4) break;
        }
        _sb.AppendLine($"slots: picked up {player.Inventory.Items.Count} of {player.Inventory.MaxSlots} (4th Interact refused = {picked == player.Inventory.MaxSlots}), IsFull={player.Inventory.IsFull}");
        Finish();
    }

    static Vector3 rayOrigin(float lateral, Vector3 B, float floorY, Vector3 dirToPlayer) { Vector3 right = Vector3.Cross(Vector3.up, -dirToPlayer); return new Vector3(B.x, floorY, B.z) + right * lateral; }

    IEnumerator Aim(CharacterBase player, CharacterController cc, FieldInfo pitchField, Camera cam, Vector3 B, float floorY, Vector3 dirToPlayer, float lateral, float distance)
    {
        Vector3 right = Vector3.Cross(Vector3.up, -dirToPlayer);          // lateral axis across the view
        Vector3 T = new Vector3(B.x, floorY, B.z) + right * lateral;      // floor point under the crosshair
        Vector3 pos = new Vector3(T.x, floorY, T.z) + dirToPlayer * distance;
        cc.enabled = false; player.transform.position = pos; cc.enabled = true;
        player.transform.rotation = Quaternion.LookRotation(-dirToPlayer);
        for (int i = 0; i < 3; i++) yield return null;
        // Pitch so the camera ray passes through T.
        yield return null;
        Vector3 eye = cam.transform.position;
        float pitch = Mathf.Atan2(eye.y - T.y, distance) * Mathf.Rad2Deg;
        pitchField.SetValue(player.Movement, pitch);
        player.CameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);   // ApplyLook only writes it while there is look input
        for (int i = 0; i < 6; i++) yield return null;
    }

    void Finish() { Report = _sb.ToString(); Done = true; }
}
