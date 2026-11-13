# 障碍格起点统一恢复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让位于树木或建筑占地格的单位能够通过统一规则平滑走出并继续追击、采集、玩家移动或 AI 移动，同时移除正式场景中的两个测试单位。

**Architecture:** 在 `MapGrid` 提供 `ResolveWalkableStart(Vector3Int)`，合法起点原样返回；非法起点只尝试符合高度规则的四向相邻可走格。四个命令调用点把恢复格作为 A* 起点，`UnitMover` 沿路径自然走出，不传送。先以一次性运行态探针取得失败结果，修改后用同一探针验收。

**Tech Stack:** 团结引擎 2022.3.62t5、Unity 2D Tilemap、C#、Codely Bridge MCP、既有 A* / UnitMover。

## Global Constraints

- 只处理审计事项 1；不修改金矿、肉经济、UI、动画伤害或新玩法。
- 不直接传送单位，不改变合法起点下的目的地选择和 A* 规则。
- 非法起点只尝试四向相邻出口；无出口时安全拒绝。
- 一次性探针放在 `Temp`，不作为游戏资源导入，不留在正式场景。
- 从 `SampleScene` 删除 `TEST_Lancer` 和 `TEST_Enemy`。
- 项目不是 Git 仓库；不初始化 Git，不执行提交步骤。

## 文件结构

- 修改：`Assets/Scripts/MapGrid.cs` — 唯一的起点恢复规则。
- 修改：`Assets/Scripts/CombatAI.cs` — 追击使用恢复后的起点。
- 修改：`Assets/Scripts/Gatherer.cs` — 前往资源点/卸货点使用恢复后的起点。
- 修改：`Assets/Scripts/SelectionManager.cs` — 玩家右键移动使用恢复后的起点。
- 修改：`Assets/Scripts/AIController.cs` — AI 移动使用恢复后的起点。
- 修改：`Assets/Scenes/SampleScene.scene` — 删除两个测试单位。
- 创建后删除：`Temp/BlockedStartRecoveryProbe.cs` — 修复前后共用的一次性运行态探针。

---

### Task 1: 统一恢复入口并接入四条命令路径

**Files:**
- Create: `Temp/BlockedStartRecoveryProbe.cs`
- Modify: `Assets/Scripts/MapGrid.cs`（放在 `NearestWalkable` 方法之前）
- Modify: `Assets/Scripts/CombatAI.cs:191-200`
- Modify: `Assets/Scripts/Gatherer.cs:275-288`
- Modify: `Assets/Scripts/SelectionManager.cs:276-299`
- Modify: `Assets/Scripts/AIController.cs:365-371`

**Interfaces:**
- Produces: `public Vector3Int? MapGrid.ResolveWalkableStart(Vector3Int start)`
- Consumes: 既有 `MapGrid.FindPath`、`NearestWalkableWithin`、`NearestWalkableInRegion`、`NearestWalkableTo`、`Region`、`Walkable`。
- 返回语义：当前格可走则返回当前格；否则返回一个满足高度规则的四向相邻可走格；无合法出口返回 `null`。

- [x] **Step 1: 写入修复前后共用的运行态失败探针**

创建 `Temp/BlockedStartRecoveryProbe.cs`：

```csharp
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

var output = new List<string>();
var grid = MapGrid.Instance;
if (grid == null) throw new Exception("Probe requires the open SampleScene and MapGrid.");

const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

var startCell = new Vector3Int(8, 8, 0);   // Tree2 的运行态占地格
var normalStartCell = new Vector3Int(7, 8, 0);
var goalCell = new Vector3Int(10, 5, 0);   // 与恢复格同区域，且远离测试敌人
Vector3 startWorld = grid.CellCenter(startCell);
Vector3 goalWorld = grid.CellCenter(goalCell);

foreach (var controller in UnityEngine.Object.FindObjectsByType<AIController>(FindObjectsSortMode.None))
    controller.active = false;

var lancerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Units/Lancer_Blue.prefab");
var pawnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Units/Pawn_Blue.prefab");
var redPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Units/Warrior_Red.prefab");
if (lancerPrefab == null || pawnPrefab == null || redPrefab == null)
    throw new Exception("A probe prefab is missing.");

GameObject Spawn(GameObject prefab, Vector3 at)
{
    var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
    var ai = go.GetComponent<CombatAI>();
    if (ai != null) ai.enabled = false; // Awake 已缓存字段；阻止探针结束后的自动行为
    return go;
}

void Record(string name, bool value) => output.Add(name + "=" + value);

var resolve = typeof(MapGrid).GetMethod("ResolveWalkableStart",
    BindingFlags.Instance | BindingFlags.Public);
Record("HelperPresent", resolve != null);

Vector3Int? Resolve(Vector3Int cell)
{
    if (resolve == null) return null;
    return (Vector3Int?)resolve.Invoke(grid, new object[] { cell });
}

// 追击：修复前 mover.Moving=False；修复后应立刻获得以相邻恢复格开头的路径。
{
    var actor = Spawn(lancerPrefab, startWorld);
    var target = Spawn(redPrefab, goalWorld);
    var ai = actor.GetComponent<CombatAI>();
    ai.OrderAttack(target.GetComponent<UnitActor>());
    Vector3 before = actor.transform.position;
    typeof(CombatAI).GetMethod("Chase", InstancePrivate).Invoke(ai, null);
    Record("RecoverCombat", actor.GetComponent<UnitMover>().Moving);
    Record("RecoverNoTeleport", actor.transform.position == before);
    UnityEngine.Object.DestroyImmediate(actor);
    UnityEngine.Object.DestroyImmediate(target);
}

// 平地对照：修复前后都应正常。
{
    var actor = Spawn(lancerPrefab, grid.CellCenter(normalStartCell));
    var target = Spawn(redPrefab, goalWorld);
    var ai = actor.GetComponent<CombatAI>();
    ai.OrderAttack(target.GetComponent<UnitActor>());
    typeof(CombatAI).GetMethod("Chase", InstancePrivate).Invoke(ai, null);
    Record("NormalCombat", actor.GetComponent<UnitMover>().Moving);
    UnityEngine.Object.DestroyImmediate(actor);
    UnityEngine.Object.DestroyImmediate(target);
}

// 采集寻路入口：直接验证 GoTo 的返回值和路径，不等待采集循环。
{
    var actor = Spawn(pawnPrefab, startWorld);
    var gatherer = actor.GetComponent<Gatherer>();
    bool ok = (bool)typeof(Gatherer).GetMethod("GoTo", InstancePrivate)
        .Invoke(gatherer, new object[] { goalWorld, null });
    Record("RecoverGather", ok && actor.GetComponent<UnitMover>().Moving);
    UnityEngine.Object.DestroyImmediate(actor);
}

// 玩家右键移动入口。
{
    var actor = Spawn(lancerPrefab, startWorld);
    var mover = actor.GetComponent<UnitMover>();
    var selection = UnityEngine.Object.FindFirstObjectByType<SelectionManager>();
    selection.selected.Clear();
    selection.selected.Add(mover);
    typeof(SelectionManager).GetMethod("IssueMove", InstancePrivate)
        .Invoke(selection, new object[] { goalWorld });
    Record("RecoverSelection", mover.Moving);
    selection.selected.Clear();
    UnityEngine.Object.DestroyImmediate(actor);
}

// AI 移动入口。
{
    var actor = Spawn(lancerPrefab, startWorld);
    var mover = actor.GetComponent<UnitMover>();
    typeof(AIController).GetMethod("SendTo", StaticPrivate)
        .Invoke(null, new object[] { actor.GetComponent<UnitActor>(), goalCell });
    Record("RecoverAI", mover.Moving);
    UnityEngine.Object.DestroyImmediate(actor);
}

// 合法起点保持不变；障碍起点只恢复到四向相邻可走格。
if (resolve != null)
{
    var normal = Resolve(normalStartCell);
    Record("NormalStartIdentity", normal.HasValue && normal.Value == normalStartCell);

    var recovered = Resolve(startCell);
    Record("RecoverIsAdjacent", recovered.HasValue
        && Mathf.Abs(recovered.Value.x - startCell.x) + Mathf.Abs(recovered.Value.y - startCell.y) == 1
        && grid.Walkable(recovered.Value));

    // 找一个当前完全空出的十字区域，临时封死中心和四个出口。
    Vector3Int? sealedCenter = null;
    Vector3Int[] directions =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0)
    };
    for (int x = -5; x <= 15 && !sealedCenter.HasValue; x++)
    for (int y = 0; y <= 12 && !sealedCenter.HasValue; y++)
    {
        var c = new Vector3Int(x, y, 0);
        if (!grid.Walkable(c)) continue;
        bool allOpen = true;
        foreach (var d in directions) allOpen &= grid.Walkable(c + d);
        if (allOpen) sealedCenter = c;
    }
    if (!sealedCenter.HasValue) throw new Exception("No open plus-shaped probe area found.");

    var cells = new List<Vector3Int> { sealedCenter.Value };
    foreach (var d in directions) cells.Add(sealedCenter.Value + d);
    grid.AddBuildingFootprint(cells);
    try
    {
        Record("SealedStartRefused", !Resolve(sealedCenter.Value).HasValue);
    }
    finally
    {
        grid.RemoveBuildingFootprint(cells);
    }

    // 恢复不能绕过原有的区域可达性限制。
    var recoveredStart = Resolve(startCell);
    Vector3Int? otherRegionCell = null;
    if (recoveredStart.HasValue)
    {
        int recoveredRegion = grid.Region(recoveredStart.Value);
        for (int x = -45; x <= 45 && !otherRegionCell.HasValue; x++)
        for (int y = -15; y <= 25 && !otherRegionCell.HasValue; y++)
        {
            var c = new Vector3Int(x, y, 0);
            if (grid.Walkable(c) && grid.Region(c) >= 0 && grid.Region(c) != recoveredRegion)
                otherRegionCell = c;
        }
        if (!otherRegionCell.HasValue) throw new Exception("No separate region found for the reachability check.");
        Record("UnreachableRefused", grid.FindPath(recoveredStart.Value, otherRegionCell.Value) == null);
    }
}

return string.Join("\n", output);
```

- [x] **Step 2: 在修改生产代码前运行探针并确认失败**

1. 打开 `Assets/Scenes/SampleScene.scene`，确认编辑器处于停止状态。
2. 通过 `unity_editor action=play` 进入 Play Mode。
3. 通过 `execute_csharp_script execution_mode=play script_path=Temp/BlockedStartRecoveryProbe.cs` 运行。
4. 通过 `unity_editor action=stop` 停止。

预期关键结果：

```text
HelperPresent=False
RecoverCombat=False
NormalCombat=True
RecoverGather=False
RecoverSelection=False
RecoverAI=False
```

`NormalCombat=True` 证明探针没有把整个追击系统误判为损坏；其余四项是本次需要修复的失败。

- [x] **Step 3: 在 MapGrid 增加唯一恢复入口**

在 `MapGrid` 的 `NearestWalkable` 方法之前加入：

```csharp
/// <summary>
/// Resolves a pathfinding start for a unit that may have ended up inside a
/// building footprint. A blocked unit walks to the returned neighbouring cell
/// as the first path waypoint; it is never teleported.
///
/// Only cardinal neighbours are considered, using the same level/ramp rule as
/// normal movement. A sealed unit therefore fails safely instead of escaping
/// across water or to an unrelated landmass.
/// </summary>
public Vector3Int? ResolveWalkableStart(Vector3Int start)
{
    EnsureScanned();
    if (WalkableRaw(start)) return start;

    for (int i = 0; i < 4; i++)
    {
        var n = start + Dirs[i];
        if (!WalkableRaw(n)) continue;
        int fromLevel = LevelRaw(start);
        int toLevel = LevelRaw(n);
        if (fromLevel != toLevel && !rampCells.Contains(start) && !rampCells.Contains(n))
            continue;
        return n;
    }
    return null;
}
```

- [x] **Step 4: 接入 CombatAI.Chase**

把 `CombatAI.cs` 中从 `var start = ...` 到 `mover.SetPath` 的段落替换为：

```csharp
var start = grid.ResolveWalkableStart(grid.WorldToCell(transform.position));
if (!start.HasValue) return;
int region = grid.Region(start.Value);
// The destination must be within the attack range of the target — not merely
// anywhere in its neighbourhood. Without that cap, a unit already close to the
// target "arrives" at its own cell and never actually closes in.
var dest = grid.NearestWalkableWithin(start.Value, goal, 3, region, Mathf.Max(unit.attackRange, 1.2f));
if (!dest.HasValue) return;
var path = grid.FindPath(start.Value, dest.Value);
if (path != null) mover.SetPath(path);
```

- [x] **Step 5: 接入 Gatherer.GoTo**

把 `Gatherer.cs` 中起点及路径段落替换为：

```csharp
var start = grid.ResolveWalkableStart(grid.WorldToCell(transform.position));
if (!start.HasValue) return false;

int region = grid.Region(start.Value);
// Nearest-walkable-to-the-unit used to hand back the unit's own cell whenever it
// was already within radius of the node — so the worker "arrived" without moving
// and harvested from three tiles away. The destination has to be adjacent.
var dest = grid.NearestWalkableWithin(start.Value, grid.WorldToCell(worldPos), 4, region, 1.6f);
if (!dest.HasValue) return false;

var path = grid.FindPath(start.Value, dest.Value);
if (path == null) return false;

mover.SetPath(path, onArrive);
return true;
```

- [x] **Step 6: 接入 SelectionManager.IssueMove**

把选中单位循环中的起点和寻路段落替换为：

```csharp
var start = grid.ResolveWalkableStart(grid.WorldToCell(u.transform.position));
if (!start.HasValue) { refused++; continue; }

// Snap to a tile that is reachable *from this unit*, not merely nearby —
// clicking an island or across water no longer silently does nothing.
// Cells already claimed by an earlier unit in this order are skipped,
// so a group never stacks two units on one tile.
int region = grid.Region(start.Value);
var off = SpiralOffset(i);
var want = clicked + new Vector3Int(off.x, off.y, 0);
var dest = grid.NearestWalkableInRegion(want, 4, region, claimed);
if (!dest.HasValue) { refused++; continue; }

var path = grid.FindPath(start.Value, dest.Value);
if (path != null)
{
    // a plain move cancels any previous attack order
    var ai = u.GetComponent<CombatAI>();
    if (ai != null) ai.ClearOrder();
    u.SetPath(path);
    claimed.Add(dest.Value);
    moved++;
}
else refused++;
```

- [x] **Step 7: 接入 AIController.SendTo**

把 `SendTo` 的起点和寻路段落替换为：

```csharp
var start = grid.ResolveWalkableStart(grid.WorldToCell(u.transform.position));
if (!start.HasValue) return;
int region = grid.Region(start.Value);
var dest = grid.NearestWalkableTo(start.Value, goalCell, 5, region);
if (!dest.HasValue) return;
var path = grid.FindPath(start.Value, dest.Value);
if (path != null) mover.SetPath(path);
```

- [x] **Step 8: 编译并用同一探针验证修复**

1. 调用 `unity_editor action=request_compile`，再调用 `wait_for_compile`。
2. 预期编译无错误；若有错误，只修正本次改动造成的编译问题后重跑。
3. 进入 Play Mode，再次执行 `Temp/BlockedStartRecoveryProbe.cs`，随后停止。

预期结果：

```text
HelperPresent=True
RecoverCombat=True
RecoverNoTeleport=True
NormalCombat=True
RecoverGather=True
RecoverSelection=True
RecoverAI=True
NormalStartIdentity=True
RecoverIsAdjacent=True
SealedStartRefused=True
UnreachableRefused=True
```

同时确认探针没有修改并保存场景；Play Mode 停止后场景中的临时单位自动消失。

---

### Task 2: 删除正式场景测试单位并做最终复验

**Files:**
- Modify: `Assets/Scenes/SampleScene.scene`
- Delete after use: `Temp/BlockedStartRecoveryProbe.cs`

**Interfaces:**
- Consumes: Task 1 的 `MapGrid.ResolveWalkableStart(Vector3Int)` 和四条已接入路径。
- Produces: 不含两个 `TEST_` 单位的正式 `SampleScene`；无临时探针残留。

- [x] **Step 1: 删除两个测试单位并保存场景**

在停止状态执行以下编辑器脚本：

```csharp
using UnityEngine;
using UnityEditor.SceneManagement;

var scene = EditorSceneManager.GetActiveScene();
if (scene.path != "Assets/Scenes/SampleScene.scene")
    throw new System.Exception("Open SampleScene before cleanup.");

foreach (var name in new[] { "TEST_Lancer", "TEST_Enemy" })
{
    var go = GameObject.Find(name);
    if (go != null) Object.DestroyImmediate(go);
}
EditorSceneManager.MarkSceneDirty(scene);
EditorSceneManager.SaveScene(scene);
return "TEST units removed; scene saved.";
```

- [x] **Step 2: 在清理后的正式场景重跑完整探针**

再次进入 Play Mode，运行同一个 `Temp/BlockedStartRecoveryProbe.cs`，随后停止。

预期仍全部为：

```text
RecoverCombat=True
RecoverNoTeleport=True
NormalCombat=True
RecoverGather=True
RecoverSelection=True
RecoverAI=True
NormalStartIdentity=True
RecoverIsAdjacent=True
SealedStartRefused=True
UnreachableRefused=True
```

- [x] **Step 3: 检查控制台和场景残留**

1. 读取编辑器控制台，预期没有本次探针或恢复代码产生的异常。
2. 确认 `GameObject.Find("TEST_Lancer")` 和 `GameObject.Find("TEST_Enemy")` 都返回 `null`。
3. 确认编辑器已停止，`SampleScene` 在保存后为非 dirty。
4. 删除一次性文件 `Temp/BlockedStartRecoveryProbe.cs`，不删除 Unity 正在使用的其他 `Temp` 内容。

- [x] **Step 4: 核对最终差异范围**

最终只允许以下内容发生变化：

```text
Assets/Scripts/MapGrid.cs
Assets/Scripts/CombatAI.cs
Assets/Scripts/Gatherer.cs
Assets/Scripts/SelectionManager.cs
Assets/Scripts/AIController.cs
Assets/Scenes/SampleScene.scene
docs/superpowers/specs/2026-10-16-blocked-start-recovery-design.md
docs/superpowers/plans/2026-10-16-blocked-start-recovery.md
```

若出现其他游戏代码、预制体、UI 或经济配置变化，撤销该额外变化后再报告完成。
