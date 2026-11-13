using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime lookup from stable prefab GUIDs to the actual prefab assets. Lives under
/// Assets/Resources so a player build can rebuild saved objects without AssetDatabase.
/// </summary>
[CreateAssetMenu(fileName = "SavePrefabCatalog", menuName = "At the Gates/Save Prefab Catalog")]
public class SavePrefabCatalog : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string id;
        public GameObject prefab;
    }

    public List<Entry> entries = new List<Entry>();

    Dictionary<string, GameObject> lookup;

    public bool TryGetPrefab(string id, out GameObject prefab)
    {
        EnsureLookup();
        if (!string.IsNullOrEmpty(id) && lookup.TryGetValue(id, out prefab)) return true;
        prefab = null;
        return false;
    }

    void OnEnable() { lookup = null; }
    void OnValidate() { lookup = null; }

    void EnsureLookup()
    {
        if (lookup != null) return;
        lookup = new Dictionary<string, GameObject>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null || string.IsNullOrEmpty(e.id) || e.prefab == null) continue;
            lookup[e.id] = e.prefab;
        }
    }
}
