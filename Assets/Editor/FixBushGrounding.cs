using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Grounds the four existing bush prefabs and removes duplicate scene bushes.
/// Does not regenerate or otherwise rearrange the garnish layer.</summary>
public static class FixBushGrounding
{
    [MenuItem("Tools/第二阶段/修复草丛接地与重复")]
    public static void Apply()
    {
        foreach (var name in new[] { "Bush1", "Bush2", "Bush3", "Bush4" })
            FixPrefab(name);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int removed = DeduplicateScene();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[草丛修复] 四种 prefab 已贴地并设 offset=-8；删除同格重复 " + removed);
    }

    static void FixPrefab(string name)
    {
        string path = "Assets/Prefabs/Garnish/" + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var sr = root.GetComponentInChildren<SpriteRenderer>(true);
            var ys = root.GetComponentInChildren<YSort>(true);
            if (sr == null || sr.sprite == null || ys == null)
                throw new System.InvalidOperationException(name + " 缺 SpriteRenderer/Sprite/YSort");

            float lift = -sr.sprite.bounds.min.y * sr.transform.localScale.y;
            sr.transform.localPosition = new Vector3(0f, lift, 0f);
            ys.baseOrder = 1000 + Mathf.RoundToInt(lift * 10f);
            ys.ordersPerUnit = 10f;
            ys.offset = -8;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static int DeduplicateScene()
    {
        var root = GameObject.Find("Garnish");
        if (root == null) return 0;
        var seen = new HashSet<Vector3>();
        int removed = 0;
        foreach (var child in root.transform.Cast<Transform>().ToArray())
        {
            if (!child.name.StartsWith("Bush")) continue;
            if (seen.Add(child.position)) continue;
            Object.DestroyImmediate(child.gameObject);
            removed++;
        }
        return removed;
    }
}
