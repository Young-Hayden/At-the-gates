using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-way, idempotent migration for the second-stage economy rules:
/// houses alone produce workers, and gold mines are fixed scene resources.
/// Deliberately edits only the active Blue/Red prefabs and the current HUD;
/// it never rebuilds the whole HUD.
/// </summary>
public static class SecondStageEconomy
{
    const string GoldPrefabPath = "Assets/Prefabs/Resources/GoldMine_Active.prefab";

    [MenuItem("Tools/第二阶段/应用民房工人与固定金矿")]
    public static void Apply()
    {
        foreach (var colour in new[] { "Blue", "Red" })
            MoveWorkerProduction(colour);

        RemoveGoldFromSceneBuilders();
        EnsureRedMine();

        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[第二阶段经济] 民房工人和固定金矿已应用");
    }

    static void MoveWorkerProduction(string colour)
    {
        string housePath = "Assets/Prefabs/Buildings/Knights/House_" + colour + ".prefab";
        string castlePath = "Assets/Prefabs/Buildings/Knights/Castle_" + colour + ".prefab";
        var house = PrefabUtility.LoadPrefabContents(housePath);
        var castle = PrefabUtility.LoadPrefabContents(castlePath);
        try
        {
            var houseProduction = house.GetComponent<ProductionBuilding>();
            var castleProduction = castle.GetComponent<ProductionBuilding>();
            var sourceProduction = castleProduction != null ? castleProduction : houseProduction;
            if (sourceProduction == null)
                throw new System.InvalidOperationException(colour + ": Castle/House 都没有可迁移的生产配置");

            string pawnName = "Pawn_" + colour;
            var source = sourceProduction.products.FirstOrDefault(p =>
                p.unitPrefab != null && p.unitPrefab.name == pawnName);
            if (source == null)
                throw new System.InvalidOperationException(colour + ": 找不到 " + pawnName + " 产品");

            // Copy values before changing either component. Asset references remain valid;
            // the managed Product instance is not shared between prefabs.
            var product = new ProductionBuilding.Product
            {
                label = source.label,
                unitPrefab = source.unitPrefab,
                icon = source.icon,
                cost = source.cost,
                buildSeconds = source.buildSeconds,
            };
            int queueLimit = sourceProduction.queueLimit;

            if (houseProduction == null) houseProduction = house.AddComponent<ProductionBuilding>();
            houseProduction.products = new[] { product };
            houseProduction.queueLimit = queueLimit;

            if (castleProduction != null) Object.DestroyImmediate(castleProduction);

            PrefabUtility.SaveAsPrefabAsset(house, housePath);
            PrefabUtility.SaveAsPrefabAsset(castle, castlePath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(house);
            PrefabUtility.UnloadPrefabContents(castle);
        }
    }

    static void RemoveGoldFromSceneBuilders()
    {
        var hud = Object.FindFirstObjectByType<HUD>(FindObjectsInactive.Include);
        if (hud != null)
        {
            for (int i = hud.buildPrefabs.Length - 1; i >= 0; i--)
            {
                var prefab = hud.buildPrefabs[i];
                if (prefab == null || prefab.name != "GoldMine_Active") continue;
                if (i < hud.buildButtons.Length && hud.buildButtons[i] != null)
                    Object.DestroyImmediate(hud.buildButtons[i].gameObject);
                hud.buildPrefabs = RemoveAt(hud.buildPrefabs, i);
                hud.buildButtons = RemoveAt(hud.buildButtons, i);
                hud.buildButtonImages = RemoveAt(hud.buildButtonImages, i);
            }

            var buildBar = GameObject.Find("BuildBar");
            if (buildBar != null)
            {
                var rt = buildBar.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(6 * 74 + 20, rt.sizeDelta.y);
            }

            foreach (var offer in hud.unitOffers)
                if (offer != null && offer.unitPrefab != null && offer.unitPrefab.name == "Pawn_Blue")
                    offer.requiresHint = "需民房";
        }

        var placer = Object.FindFirstObjectByType<BuildingPlacer>(FindObjectsInactive.Include);
        if (placer != null)
            placer.quickBuild = placer.quickBuild.Where(p => p != null && p.name != "GoldMine_Active").ToArray();

        foreach (var ai in Object.FindObjectsByType<AIController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            ai.buildOrder = ai.buildOrder.Where(p => p != null && p.name != "GoldMine_Active").ToArray();
    }

    static T[] RemoveAt<T>(T[] source, int index)
    {
        var list = new List<T>(source);
        if (index >= 0 && index < list.Count) list.RemoveAt(index);
        return list.ToArray();
    }

    static void EnsureRedMine()
    {
        var target = new Vector3(35.5f, 6f, 0f);
        bool exists = Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Any(n => n.kind == ResourceKind.Gold && (n.transform.position - target).sqrMagnitude < 0.0001f);
        if (exists) return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GoldPrefabPath);
        if (prefab == null) throw new System.InvalidOperationException("找不到 " + GoldPrefabPath);
        var mine = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        mine.name = "Mine_Gold_Red";
        mine.transform.position = target;
        var building = mine.GetComponent<Building>();
        if (building != null) building.team = Team.Neutral;
    }
}
