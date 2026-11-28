using UnityEngine;

public class InventoryHUD : MonoBehaviour
{
    [SerializeField] private CharacterBase _character;

    private const float SlotSize    = 96f;
    private const float SlotPadding = 8f;
    private const float BottomMargin = 20f;
    private const float IconSize    = 64f;   // icons are rendered at 64 px (Tools: Icon Studio), shown 1:1 so the pixels stay crisp

    private GUIStyle _slotStyle;
    private GUIStyle _emptySlotStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _smallLabelStyle;

    private void OnGUI()
    {
        if (_character == null) _character = CharacterBase.FindLocal();   // networked: the player spawns after the scene loads
        if (_character == null) return;

        InitStyles();

        var inv      = _character.Inventory;
        var torch    = _character.TorchCarrier != null ? _character.TorchCarrier.Carried : null;
        int maxSlots = Mathf.Max(inv.MaxSlots, inv.Items.Count + (torch != null ? 1 : 0));
        int used     = inv.Items.Count + (torch != null ? 1 : 0);

        float totalWidth = maxSlots * SlotSize + (maxSlots - 1) * SlotPadding;
        float startX     = (Screen.width - totalWidth) * 0.5f;
        float startY     = Screen.height - SlotSize - BottomMargin;

        for (int i = 0; i < maxSlots; i++)
        {
            float x = startX + i * (SlotSize + SlotPadding);
            var rect = new Rect(x, startY, SlotSize, SlotSize);

            bool hasItem  = i < inv.Items.Count;
            bool isTorch  = !hasItem && torch != null && i == inv.Items.Count;

            if (isTorch)
            {
                // The carried torch occupies the first free slot (it lives on TorchCarrier, not InventorySystem).
                GUI.Box(rect, "", _slotStyle);
                var nameRect = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 24);
                GUI.Label(nameRect, torch.DisplayName, _labelStyle);
                var hintRect = new Rect(rect.x + 4, rect.yMax - 22, rect.width - 8, 18);
                GUI.Label(hintRect, "G to throw", _labelStyle);
            }
            else if (hasItem)
            {
                var item = inv.Items[i];
                GUI.Box(rect, "", _slotStyle);

                var icon = item.Icon;
                if (icon != null && icon.texture != null)
                {
                    // Icon on top, name + subtitle underneath (single-line, clipped).
                    var iconRect = new Rect(rect.center.x - IconSize * 0.5f, rect.y + 4, IconSize, IconSize);
                    GUI.DrawTexture(iconRect, icon.texture, ScaleMode.ScaleToFit, true);
                    GUI.Label(new Rect(rect.x + 2, rect.y + IconSize + 5, rect.width - 4, 13), item.DisplayName, _smallLabelStyle);
                    if (!string.IsNullOrEmpty(item.Subtitle))
                        GUI.Label(new Rect(rect.x + 2, rect.yMax - 14, rect.width - 4, 13), item.Subtitle, _smallLabelStyle);
                }
                else
                {
                    // No icon: name at the top (wraps), subtitle at the bottom (bone owner "Base's", evidence "10 pts")
                    var nameRect = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 24);
                    GUI.Label(nameRect, item.DisplayName, _labelStyle);
                    var valueRect = new Rect(rect.x + 4, rect.yMax - 22, rect.width - 8, 18);
                    GUI.Label(valueRect, item.Subtitle, _labelStyle);
                }
            }
            else
            {
                GUI.Box(rect, "", _emptySlotStyle);
            }

            // Slot number
            var indexRect = new Rect(rect.xMax - 18, rect.y + 2, 16, 16);
            GUI.Label(indexRect, (i + 1).ToString(), _labelStyle);
        }

        // Slot count label above the slots
        var countRect = new Rect(startX, startY - 22, totalWidth, 20);
        string hint = inv.Items.Count > 0 ? "   [X] drop last" : "";
        var usable = inv.FindUsable(out _);
        if (usable != null) hint += $"   [F] {usable.UseHint}";
        GUI.Label(countRect, $"Inventory  {used} / {maxSlots}{hint}", _labelStyle);
    }

    private void InitStyles()
    {
        if (_slotStyle != null) return;

        _slotStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = MakeTex(1, 1, new Color(0.15f, 0.15f, 0.15f, 0.85f)) }
        };

        _emptySlotStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = MakeTex(1, 1, new Color(0.05f, 0.05f, 0.05f, 0.5f)) }
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            wordWrap  = true,
            normal    = { textColor = Color.white }
        };

        _smallLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 10,
            wordWrap  = false,
            clipping  = TextClipping.Clip,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(0, 0, 0, 0),
            normal    = { textColor = Color.white }
        };
    }

    private static Texture2D MakeTex(int width, int height, Color col)
    {
        var tex = new Texture2D(width, height);
        tex.SetPixel(0, 0, col);
        tex.Apply();
        return tex;
    }
}
