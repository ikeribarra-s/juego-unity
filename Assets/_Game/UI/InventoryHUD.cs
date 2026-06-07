using UnityEngine;

public class InventoryHUD : MonoBehaviour
{
    [SerializeField] private CharacterBase _character;

    private const float SlotSize    = 80f;
    private const float SlotPadding = 8f;
    private const float BottomMargin = 20f;

    private GUIStyle _slotStyle;
    private GUIStyle _emptySlotStyle;
    private GUIStyle _labelStyle;

    private void OnGUI()
    {
        if (_character == null) return;

        InitStyles();

        var inv      = _character.Inventory;
        int maxSlots = inv.MaxSlots;

        float totalWidth = maxSlots * SlotSize + (maxSlots - 1) * SlotPadding;
        float startX     = (Screen.width - totalWidth) * 0.5f;
        float startY     = Screen.height - SlotSize - BottomMargin;

        for (int i = 0; i < maxSlots; i++)
        {
            float x = startX + i * (SlotSize + SlotPadding);
            var rect = new Rect(x, startY, SlotSize, SlotSize);

            bool hasItem = i < inv.Items.Count;

            if (hasItem)
            {
                var item = inv.Items[i];
                GUI.Box(rect, "", _slotStyle);

                // Item name — wrap if long
                var nameRect = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 24);
                GUI.Label(nameRect, item.Definition.DisplayName, _labelStyle);

                // Value at bottom of slot
                var valueRect = new Rect(rect.x + 4, rect.yMax - 22, rect.width - 8, 18);
                GUI.Label(valueRect, $"{item.Definition.FinalValue:F0} pts", _labelStyle);
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
        GUI.Label(countRect, $"Inventory  {inv.Items.Count} / {maxSlots}", _labelStyle);
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
    }

    private static Texture2D MakeTex(int width, int height, Color col)
    {
        var tex = new Texture2D(width, height);
        tex.SetPixel(0, 0, col);
        tex.Apply();
        return tex;
    }
}
