# Attack Ring Ground-Layer Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将攻击范围圈从 LineRenderer 改为与绿色选择圈同排序的 SpriteRenderer 地面圆环。

**Architecture:** `AttackRangeIndicator` 生成并缓存白色环形 Sprite，通过 SpriteRenderer 着色和 Y-sort；SelectionManager 的池与行为不变。

**Tech Stack:** 团结引擎 2022.3.62t5、C#、SpriteRenderer、NUnit PlayMode、Codely Bridge。

## Global Constraints

- 与绿色选择圈使用相同 Sorting Layer 和排序 `YSort.OrderFor(center.y) - 1`。
- 不使用 LineRenderer；不添加 Collider/RaycastTarget。
- 保持 `Create/Show/Hide/Radius/IsVisible` 接口、范围数值和对象池行为。
- 环形纹理按半径缓存，世界线宽约 0.06–0.07，Point 过滤。
- 完整自检后仅报告；本轮没有新的提交/推送授权。

---

### Task 1: SpriteRenderer 地面攻击圈

**Files:**
- Modify: `Assets/Scripts/AttackRangeIndicator.cs`
- Modify: `Assets/Tests/PlayMode/AttackRangeTests.cs`

**Interfaces:**
- Preserves: `AttackRangeIndicator.Create(Transform)`, `Show(Vector3,float,Color,int)`, `Hide()`, `Radius`, `IsVisible`.
- Produces: 组件对象上存在 `SpriteRenderer`，无 `LineRenderer`。

- [ ] **Step 1: 写失败测试**

在 `AttackRangeTests` 加入：

```csharp
[UnityTest]
public IEnumerator RangeRing_UsesGroundSpriteRenderer_WithSelectionRingSorting()
{
    PaintRect(-6, -6, 14, 14);
    var go = new GameObject("Ground ring unit");
    go.transform.position = new Vector3(1.25f, 2.75f, 0f);
    go.AddComponent<UnitMover>();
    var actor = go.AddComponent<UnitActor>();
    actor.attackRange = 3.2f;

    Selection.selected.Clear();
    Selection.selectedBuilding = null;
    Selection.selected.Add(go.GetComponent<UnitMover>());
    InvokeUpdateRings();
    yield return null;

    var ring = Object.FindFirstObjectByType<AttackRangeIndicator>();
    Assert.IsNotNull(ring);
    Assert.IsNull(ring.GetComponent<LineRenderer>(),
        "The attack range ring must no longer be a world-space LineRenderer.");
    var ringRenderer = ring.GetComponent<SpriteRenderer>();
    Assert.IsNotNull(ringRenderer,
        "The attack range ring must use SpriteRenderer like the green selection ring.");
    Assert.AreEqual(YSort.OrderFor(go.transform.position.y) - 1,
        ringRenderer.sortingOrder, "The attack ring must use the selection-ring ground order.");

    var selectionRing = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
        .FirstOrDefault(r => r.gameObject.name == "SelectionRing");
    Assert.IsNotNull(selectionRing, "The green selection ring control must exist.");
    Assert.AreEqual(selectionRing.sortingLayerID, ringRenderer.sortingLayerID,
        "Attack and selection rings must share a sorting layer.");
    Assert.AreEqual(selectionRing.sortingOrder, ringRenderer.sortingOrder,
        "Attack and selection rings must render in the same ground order.");
}
```

- [ ] **Step 2: 运行测试确认 RED**

```csharp
RegressionTestMenu.RunPlayModeTest("AttackRangeTests.RangeRing_UsesGroundSpriteRenderer_WithSelectionRingSorting");
```

预期：当前组件有 LineRenderer、没有 SpriteRenderer，测试失败。

- [ ] **Step 3: 替换为缓存环形 Sprite**

`AttackRangeIndicator` 改为：

```csharp
const int TextureSize = 256;
const float OuterRadiusPixels = 120f;
const float WorldLineWidth = 0.065f;

static readonly Dictionary<int, Sprite> ringCache = new Dictionary<int, Sprite>();
SpriteRenderer ringRenderer;

void Awake()
{
    var collider2D = GetComponent<Collider2D>();
    if (collider2D != null) collider2D.enabled = false;
    var collider3D = GetComponent<Collider>();
    if (collider3D != null) collider3D.enabled = false;
    ringRenderer = gameObject.AddComponent<SpriteRenderer>();
    ringRenderer.enabled = false;
}

public void Show(Vector3 center, float radius, Color tint, int sortingOrder)
{
    transform.position = center;
    transform.localScale = Vector3.one * radius;
    ringRenderer.sprite = SpriteForRadius(radius);
    ringRenderer.color = tint;
    ringRenderer.sortingOrder = sortingOrder;
    ringRenderer.enabled = true;
    Radius = radius;
}

public void Hide() => ringRenderer.enabled = false;
public bool IsVisible => ringRenderer != null && ringRenderer.enabled;
```

`SpriteForRadius(radius)`：

- key = `Mathf.RoundToInt(radius * 100f)`；
- `pixelsPerWorld = OuterRadiusPixels / radius`；
- `thickness = Mathf.Max(1.5f, WorldLineWidth * pixelsPerWorld)`；
- 创建 256×256 RGBA32、Point、Clamp Texture；
- 像素中心到圆心距离在 `[OuterRadiusPixels - thickness, OuterRadiusPixels]` 为白色，否则透明；
- `Sprite.Create(tex, new Rect(0,0,256,256), new Vector2(.5f,.5f), OuterRadiusPixels)`；
- 设置 `HideFlags.DontSave`，缓存并返回。

`Create` 仍然只创建对象和组件，public 接口不变。

- [ ] **Step 4: 聚焦 GREEN**

运行新测试与既有：

```csharp
RegressionTestMenu.RunPlayModeTest("AttackRangeTests.RangeRing_UsesGroundSpriteRenderer_WithSelectionRingSorting");
RegressionTestMenu.RunPlayModeTest("AttackRangeTests.SelectedUnits_ShowOneRingEach_AndUpdateOnDeselect");
RegressionTestMenu.RunPlayModeTest("AttackRangeTests.SelectedFinishedTower_ShowsFivePointFive_ButHalfBuiltTowerDoesNot");
```

- [ ] **Step 5: 2560 视觉复验**

在已提交的 2560 运行配置下选中僧侣/工人/弓手，截取 `Logs/attack-rings-ground-fix-2560.png`。逐张 Read 确认蓝色攻击圈与绿色选择圈同在地面层，脚部和树木能遮挡线条。必要时结合上一张用户截图对比。

- [ ] **Step 6: 完整回归**

在 2560×1440 跑完整 PlayMode（预计 297 项），归档日志。检查编译、Console、ProjectDoctor、真实存档和场景指纹。只报告结果，不提交、不推送。
