using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single-slot full-match save service. Captures long-lived state, validates every
/// prefab reference through <see cref="SavePrefabCatalog"/>, and rebuilds a scene
/// after the normal Unity startup pass so existing components initialise as usual.
/// </summary>
public static class SaveGameService
{
    public const int Version = 2;

    /// <summary>Oldest file this build can still read. Version 1 predates the technology
    /// mask; it loads with an empty mask and no other schema difference.</summary>
    public const int OldestReadableVersion = 1;
    const string CatalogResource = "SavePrefabCatalog";

    public static string SavePath => Path.Combine(
        Application.persistentDataPath, "AtTheGates", "save_slot_1.json");

    public static bool HasSave => File.Exists(SavePath);

    static SavePrefabCatalog catalog;
    static SaveGameData pendingLoad;
    static string pendingError;

    static SavePrefabCatalog Catalog
    {
        get
        {
            if (catalog == null) catalog = Resources.Load<SavePrefabCatalog>(CatalogResource);
            return catalog;
        }
    }

    static string TempPath => SavePath + ".tmp";

    public static bool HasValidSave()
    {
        return TryRead(out _, out _);
    }

    public static bool Save(out string error)
    {
        var data = CaptureSnapshot(out error);
        if (data == null) return false;
        return SaveToPath(data, SavePath, out error);
    }

    public static bool SaveToPath(SaveGameData data, string path, out string error)
    {
        error = Validate(data);
        if (!string.IsNullOrEmpty(error)) return false;
        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            // Never delete the only valid save before the replacement is in place.
            // File.Replace is atomic on the same volume and retains the previous file.
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
            error = null;
            return true;
        }
        catch (Exception e)
        {
            error = "写入存档失败：" + e.Message;
            return false;
        }
    }

    public static bool TryRead(out SaveGameData data, out string error)
    {
        return TryReadFromPath(SavePath, out data, out error);
    }

    public static bool TryReadFromPath(string path, out SaveGameData data, out string error)
    {
        data = null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            error = "没有找到存档。";
            return false;
        }
        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            data = JsonUtility.FromJson<SaveGameData>(json);
            error = Validate(data);
            if (!string.IsNullOrEmpty(error))
            {
                data = null;
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            data = null;
            error = "存档损坏或无法解析：" + e.Message;
            return false;
        }
    }

    public static void RequestLoad(SaveGameData data)
    {
        pendingLoad = data;
        pendingError = data == null ? "没有可读取的存档。" : null;
    }

    public static bool ConsumePendingLoad(out SaveGameData data, out string error)
    {
        data = pendingLoad;
        error = pendingError;
        pendingLoad = null;
        pendingError = null;
        return data != null;
    }

    /// <summary>Drop a queued load that will never happen — leaving the match for the
    /// main menu must not let a stale request ambush the next SampleScene start.</summary>
    public static void CancelPendingLoad()
    {
        pendingLoad = null;
        pendingError = null;
    }

    public static SaveGameData CaptureSnapshot(out string error)
    {
        error = null;
        if (GameState.Instance == null)
        {
            error = "当前场景没有对局状态，无法保存。";
            return null;
        }
        if (GameState.Instance.IsOver)
        {
            error = "对局已经结束，不能保存。";
            return null;
        }
        if (Economy.Instance == null)
        {
            error = "当前场景没有经济系统，无法保存。";
            return null;
        }
        if (Catalog == null)
        {
            error = "缺少 SavePrefabCatalog（Assets/Resources）。";
            return null;
        }

        var data = new SaveGameData
        {
            version = Version,
            savedAtUtc = DateTime.UtcNow.ToString("o"),
            sceneName = SceneManager.GetActiveScene().name,
            economy = new EconomySave
            {
                gold = Economy.Instance.gold,
                wood = Economy.Instance.wood,
                meat = Economy.Instance.meat,
            },
            gameState = GameState.Instance.CaptureSaveState(),
        };

        for (int i = 0; i < AIController.All.Count; i++)
        {
            var ai = AIController.All[i];
            if (ai != null) data.ai.Add(ai.CaptureSaveState());
        }

        var buildings = UnityEngine.Object.FindObjectsByType<Building>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < buildings.Length; i++)
        {
            var b = buildings[i];
            if (b == null || b.isGhost || !b.gameObject.scene.IsValid()) continue;
            string id = IdentityOf(b.transform, out error);
            if (id == null) return null;

            var record = new SaveObjectRecord
            {
                prefabId = id,
                objectName = b.name,
                position = b.transform.position,
                rotation = b.transform.rotation,
                localScale = b.transform.localScale,
                building = b.CaptureSaveState(),
            };

            var production = b.GetComponent<ProductionBuilding>();
            if (production != null)
            {
                record.hasProduction = true;
                record.production = production.CaptureSaveState();
            }

            var nodes = b.GetComponentsInChildren<ResourceNode>(true);
            for (int n = 0; n < nodes.Length; n++)
                if (nodes[n] != null) record.resources.Add(nodes[n].CaptureSaveState());

            var tower = b.GetComponent<TowerGarrison>();
            if (tower != null && tower.HasArcher && tower.Archer != null)
            {
                string archerId = IdentityOf(tower.Archer.transform, out error);
                if (archerId == null) return null;
                record.hasGarrison = true;
                record.garrison = new SaveGarrisonRecord
                {
                    prefabId = archerId,
                    actor = tower.Archer.CaptureSaveState(),
                };
            }
            var wanderingSheep = b.GetComponent<SheepWander>();
            if (wanderingSheep != null && wanderingSheep.IsWalking)
                record.position = wanderingSheep.StableSavePosition;
            data.buildings.Add(record);
        }

        var units = UnityEngine.Object.FindObjectsByType<UnitActor>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < units.Length; i++)
        {
            var actor = units[i];
            if (actor == null || !actor.enabled || !actor.IsAlive ||
                !actor.gameObject.activeInHierarchy || !actor.gameObject.scene.IsValid()) continue;
            string id = IdentityOf(actor.transform, out error);
            if (id == null) return null;

            var record = new SaveUnitRecord
            {
                prefabId = id,
                objectName = actor.name,
                position = actor.transform.position,
                rotation = actor.transform.rotation,
                localScale = actor.transform.localScale,
                actor = actor.CaptureSaveState(),
            };
            var gatherer = actor.GetComponent<Gatherer>();
            if (gatherer != null)
            {
                record.hasGatherer = true;
                record.gatherer = gatherer.CaptureSaveState();
            }
            data.units.Add(record);
        }

        var seenBushRoots = new HashSet<int>();
        for (int i = 0; i < ClearableBush.All.Count; i++)
        {
            var bush = ClearableBush.All[i];
            if (bush == null) continue;
            var root = bush.Root;
            if (!seenBushRoots.Add(root.GetInstanceID())) continue;
            string id = IdentityOf(root.transform, out error);
            if (id == null) return null;
            data.bushes.Add(new SaveBushRecord
            {
                prefabId = id,
                objectName = root.name,
                position = root.transform.position,
                rotation = root.transform.rotation,
                localScale = root.transform.localScale,
            });
        }

        var rts = UnityEngine.Object.FindFirstObjectByType<RTSCamera>();
        var cam = rts != null ? rts.GetComponent<Camera>() : Camera.main;
        if (cam != null)
        {
            data.camera.valid = true;
            data.camera.position = cam.transform.position;
            data.camera.orthographicSize = cam.orthographicSize;
        }

        error = Validate(data);
        return string.IsNullOrEmpty(error) ? data : null;
    }

    static string IdentityOf(Transform transform, out string error)
    {
        error = null;
        var identity = transform.GetComponentInParent<SaveIdentity>();
        if (identity == null || string.IsNullOrEmpty(identity.PrefabId))
        {
            error = "物体 \"" + transform.name + "\" 没有 SaveIdentity，无法保存。请运行 Tools/Save/重建预制体目录。";
            return null;
        }
        return identity.PrefabId;
    }

    static string Validate(SaveGameData data)
    {
        if (data == null) return "存档内容为空。";
        if (data.version < OldestReadableVersion || data.version > Version)
            return "存档版本不兼容：存档为 " + data.version + "，游戏需要 " +
                   OldestReadableVersion + "~" + Version + "。";
        if (data.gameState == null) return "存档缺少对局状态。";
        if (data.gameState.result != GameState.Outcome.Playing)
            return "该存档来自已经结束的对局，无法继续。";
        if (Catalog == null) return "缺少 SavePrefabCatalog（Assets/Resources）。";
        if (data.buildings == null) return "存档缺少建筑列表。";
        if (data.units == null) return "存档缺少单位列表。";
        if (data.ai == null) return "存档缺少 AI 列表。";
        if (data.bushes == null) return "存档缺少草丛列表。";

        if (data.buildings != null)
            foreach (var r in data.buildings)
            {
                string error = ValidatePrefab(r != null ? r.prefabId : null);
                if (error != null) return error;
                if (r.hasGarrison && r.garrison != null)
                {
                    error = ValidatePrefab(r.garrison.prefabId);
                    if (error != null) return error;
                }
            }
        if (data.units != null)
            foreach (var r in data.units)
            {
                string error = ValidatePrefab(r != null ? r.prefabId : null);
                if (error != null) return error;
            }
        if (data.bushes != null)
            foreach (var r in data.bushes)
            {
                string error = ValidatePrefab(r != null ? r.prefabId : null);
                if (error != null) return error;
            }
        return null;
    }

    static string ValidatePrefab(string id)
    {
        if (string.IsNullOrEmpty(id)) return "存档引用了空的预制体 id。";
        return Catalog.TryGetPrefab(id, out _) ? null : "存档引用的预制体不在当前目录中：" + id;
    }

    /// <summary>Rebuild the saved match in the current scene. The caller should already
    /// have reloaded SampleScene so all managers start from their normal defaults.</summary>
    public static IEnumerator RestoreSnapshot(SaveGameData data, Action<bool, string> completed)
    {
        string error = Validate(data);
        if (!string.IsNullOrEmpty(error))
        {
            completed?.Invoke(false, error);
            yield break;
        }

        float oldTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        try
        {
        // Every early-out and exception below must still restore the match clock.
        // The technology mask has to be live before any unit is restored: a unit saved
        // at its Armour-buffed maximum would otherwise be clamped back to its base
        // maximum and lose health. A version-1 save carries an empty mask.
        GameState.RestoreTechnology(data.gameState);

        var destroyed = new HashSet<int>();
        foreach (var u in UnityEngine.Object.FindObjectsByType<UnitActor>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (u != null && u.gameObject.scene.IsValid()) DestroyOnce(u.gameObject, destroyed);
        foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b != null && b.gameObject.scene.IsValid()) DestroyOnce(b.gameObject, destroyed);
        for (int i = 0; i < ClearableBush.All.Count; i++)
            if (ClearableBush.All[i] != null) DestroyOnce(ClearableBush.All[i].Root, destroyed);

        yield return null;      // deferred Destroy completes at frame end

        var buildingViews = new List<Building>();
        for (int i = 0; i < data.buildings.Count; i++)
        {
            var r = data.buildings[i];
            if (!Catalog.TryGetPrefab(r.prefabId, out var prefab)) continue;
            var go = UnityEngine.Object.Instantiate(prefab, r.position, r.rotation);
            go.name = string.IsNullOrEmpty(r.objectName) ? prefab.name : r.objectName;
            go.transform.localScale = r.localScale;
            var building = go.GetComponent<Building>();
            if (building != null) building.Recompute();
            buildingViews.Add(building);
        }

        var unitViews = new List<UnitActor>();
        for (int i = 0; i < data.units.Count; i++)
        {
            var r = data.units[i];
            if (!Catalog.TryGetPrefab(r.prefabId, out var prefab)) continue;
            var go = UnityEngine.Object.Instantiate(prefab, r.position, r.rotation);
            go.name = string.IsNullOrEmpty(r.objectName) ? prefab.name : r.objectName;
            go.transform.localScale = r.localScale;
            unitViews.Add(go.GetComponent<UnitActor>());
        }

        // Bushes must go back under the "Garnish" root: GarnishRules.IsBushCell only
        // scans that root's children, so a root-level restore would let buildings be
        // placed on top of uncleared bushes.
        var garnishRoot = GameObject.Find("Garnish");
        for (int i = 0; i < data.bushes.Count; i++)
        {
            var r = data.bushes[i];
            if (!Catalog.TryGetPrefab(r.prefabId, out var prefab)) continue;
            var go = UnityEngine.Object.Instantiate(prefab, r.position, r.rotation);
            go.name = string.IsNullOrEmpty(r.objectName) ? prefab.name : r.objectName;
            go.transform.localScale = r.localScale;
            if (garnishRoot != null) go.transform.SetParent(garnishRoot.transform, true);
        }
        GarnishRules.Invalidate();

        yield return null;      // Start(): health bars, construction setup, AI clocks

        for (int i = 0; i < buildingViews.Count && i < data.buildings.Count; i++)
        {
            var b = buildingViews[i];
            var r = data.buildings[i];
            if (b == null) continue;
            b.RestoreSaveState(r.building);
            if (r.hasProduction)
            {
                var pb = b.GetComponent<ProductionBuilding>();
                if (pb != null) pb.RestoreSaveState(r.production);
            }
            var nodes = b.GetComponentsInChildren<ResourceNode>(true);
            for (int n = 0; n < nodes.Length && n < r.resources.Count; n++)
                nodes[n].RestoreSaveState(r.resources[n]);
        }

        for (int i = 0; i < unitViews.Count && i < data.units.Count; i++)
        {
            var actor = unitViews[i];
            var r = data.units[i];
            if (actor == null) continue;
            actor.RestoreSaveState(r.actor);
            if (r.hasGatherer)
            {
                var gatherer = actor.GetComponent<Gatherer>();
                if (gatherer != null) gatherer.RestoreSaveState(r.gatherer);
            }
        }

        for (int i = 0; i < data.ai.Count; i++)
        {
            var saved = data.ai[i];
            bool applied = false;
            for (int a = 0; a < AIController.All.Count; a++)
                if (AIController.All[a] != null && AIController.All[a].team == saved.team)
                {
                    AIController.All[a].RestoreSaveState(saved);
                    applied = true;
                    break;
                }
            if (!applied) Debug.LogWarning("[Save] No AI controller found for team " + saved.team + ".");
        }

        if (GameState.Instance != null) GameState.Instance.RestoreSaveState(data.gameState);
        if (Economy.Instance != null && data.economy != null)
        {
            Economy.Instance.gold = data.economy.gold;
            Economy.Instance.wood = data.economy.wood;
            Economy.Instance.meat = data.economy.meat;
            Economy.Instance.RaiseChanged();   // fields were written behind the event's back
        }

        if (data.camera != null && data.camera.valid)
        {
            var rts = UnityEngine.Object.FindFirstObjectByType<RTSCamera>();
            var cam = rts != null ? rts.GetComponent<Camera>() : Camera.main;
            if (cam != null)
            {
                cam.transform.position = data.camera.position;
                cam.orthographicSize = data.camera.orthographicSize;
            }
        }

        for (int i = 0; i < buildingViews.Count && i < data.buildings.Count; i++)
        {
            var r = data.buildings[i];
            var b = buildingViews[i];
            if (b == null || !r.hasGarrison || r.garrison == null) continue;
            if (!Catalog.TryGetPrefab(r.garrison.prefabId, out var archerPrefab))
            {
                error = "驻塔弓箭手预制体缺失。";
                break;
            }
            var go = UnityEngine.Object.Instantiate(
                archerPrefab, b.transform.position + Vector3.down, Quaternion.identity);
            go.name = "Garrisoned Archer";
            var actor = go.GetComponent<UnitActor>();
            if (actor == null)
            {
                error = "驻塔弓箭手预制体没有 UnitActor。";
                break;
            }
            actor.RestoreSaveState(r.garrison.actor);
            var tower = b.GetComponent<TowerGarrison>();
            if (tower == null || !tower.RestoreGarrison(actor))
            {
                UnityEngine.Object.Destroy(go);
                error = "无法恢复 \"" + b.name + "\" 的驻塔弓箭手。";
                break;
            }
        }

        var selection = UnityEngine.Object.FindFirstObjectByType<SelectionManager>();
        if (selection != null) selection.selected.Clear();
        }
        finally
        {
            Time.timeScale = oldTimeScale;
        }

        completed?.Invoke(string.IsNullOrEmpty(error), error);
    }

    static void DestroyOnce(GameObject go, HashSet<int> seen)
    {
        if (go == null || !seen.Add(go.GetInstanceID())) return;
        UnityEngine.Object.Destroy(go);
    }
}
