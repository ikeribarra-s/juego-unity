#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class WallsLayerSetup
{
    [MenuItem("Fluffterror/Setup/Add Walls Layer")]
    public static void AddWallsLayer()
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == "Walls")
            {
                Debug.Log($"Walls layer already exists at index {i}.");
                return;
            }
        }

        // User layers start at index 8
        for (int i = 8; i < layers.arraySize; i++)
        {
            var slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = "Walls";
                tagManager.ApplyModifiedProperties();
                Debug.Log($"Walls layer created at index {i}.");
                return;
            }
        }

        Debug.LogError("No empty layer slot found (8–31 are all occupied).");
    }
}
#endif
