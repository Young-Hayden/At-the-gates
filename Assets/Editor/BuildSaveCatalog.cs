using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds stable prefab ids and rebuilds the runtime catalog used by the save system.
/// Run after adding a new saveable prefab.
/// </summary>
public static class BuildSaveCatalog
{
    const string PrefabRoot = "Assets/Prefabs";
    const string ResourceDirectory = "Assets/Resources";
    const string CatalogPath = ResourceDirectory + "/SavePrefabCatalog.asset";

    [MenuItem("Tools/Save/重建预制体目录")]
    public static void Rebuild()
    {
        Directory.CreateDirectory(ResourceDirectory);

        var entries = new List<SavePrefabCatalog.Entry>();
        var touched = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || !IsSaveable(prefab)) continue;

            var identity = prefab.GetComponent<SaveIdentity>();
            if (identity == null) identity = prefab.AddComponent<SaveIdentity>();
            identity.EditorSetPrefabId(guid);
            EditorUtility.SetDirty(identity);
            PrefabUtility.SavePrefabAsset(prefab);
            touched.Add(path);

            entries.Add(new SavePrefabCatalog.Entry { id = guid, prefab = prefab });
        }

        var catalog = AssetDatabase.LoadAssetAtPath<SavePrefabCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<SavePrefabCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.entries = entries;
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[Save] Prefabs=" + entries.Count + ", catalog=" + CatalogPath +
                  "\n" + string.Join("\n", touched));
    }

    static bool IsSaveable(GameObject prefab)
    {
        return prefab.GetComponentInChildren<UnitActor>(true) != null ||
               prefab.GetComponentInChildren<Building>(true) != null ||
               prefab.GetComponentInChildren<ResourceNode>(true) != null ||
               prefab.GetComponentInChildren<ClearableBush>(true) != null;
    }
}
