using UnityEngine;

/// <summary>
/// Stable identity inherited by every instance of a saveable prefab. The id is the
/// prefab GUID, so renames and the AI's *_AI naming convention cannot break saves.
/// </summary>
public class SaveIdentity : MonoBehaviour
{
    [SerializeField] string prefabId;

    public string PrefabId
    {
        get => prefabId;
        set => prefabId = value;
    }

#if UNITY_EDITOR
    /// <summary>Editor-only helper used by the catalog builder.</summary>
    public void EditorSetPrefabId(string id) { prefabId = id; }
#endif
}
