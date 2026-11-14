# 《兵临城下》固定 2560×1440 与攻击范围显示设计

日期：2026-11-14  
工程：`D:\Unity\projects\At the Gates`  
基线：`main@3e5659a` 加当前未提交的审计修复与地图精修。  
状态：设计已获用户批准；待书面规格复审。

## 1. 目标

1. 游戏运行时固定为 2560×1440 全屏；Canvas 参考保持 1920×1080，由既有 UI 自动放大铺满（用户实图裁定）。
2. 箭塔实际防御射程从 4.5 提高到 5.5。
3. 选中的每个单位和箭塔显示近似英雄联盟风格的攻击范围圈。
4. 完整自检后提交本地 Git，并同步、推送 GitHub 公开仓库。

## 2. 固定显示

### 2.1 运行时

`GameSettings` 成为唯一显示定义：

- `FixedWidth = 2560`
- `FixedHeight = 1440`
- 固定 `FullScreenMode.FullScreenWindow`
- `ApplyDisplay()` 在独立播放器中调用 `Screen.SetResolution(2560, 1440, FullScreenMode.FullScreenWindow)`
- 删除旧的 1280×720 / 1600×900 / 1920×1080 `ResolutionIndex` 和可变 `Fullscreen` 逻辑
- 旧的 PlayerPrefs 键可以保留在用户机器上，但运行时不再读取

`ProjectSettings` 的 `defaultScreenWidth/Height` 改为 2560/1440，保持 `fullscreenMode: 1`、`resizableWindow: 0`。

### 2.2 Canvas

运行时固定为 2560×1440，但 CanvasScaler 参考分辨率**保持 1920×1080**，让既有界面按 1.333 倍整体放大、维持旧版构图并铺满画面。

用户已查看两张 2560 实图并裁定：不把现有固定尺寸 UI 重排成 2560 设计坐标。`SampleScene`、`MainMenu`、`ModeSelect`、`LoadingScreen.prefab` 的 Canvas 参考值保持 `1920×1080`，不得改动。

编辑器 HUD/菜单/加载界面构建器的参考常量也保持 `1920×1080`，防止未来重建改变现有观感；本次不运行重建器。

### 2.3 设置面板

`MenuSettingsUI` 在运行时把分辨率区域收敛为单个固定项：

- 第一分辨率按钮显示 `2560×1440`，保持选中色但不可点击。
- 其它两个分辨率按钮隐藏。
- 全屏按钮不可点击，标签显示 `全屏：固定 2560×1440`。
- 边缘平移、平移速度、恢复默认继续可用；“恢复默认”不再重置分辨率。

不修改 MainMenu 场景中的 UI 对象布局，只在代码中固定控件状态。

## 3. 箭塔防御范围

`TowerGarrison` 增加：

- `public float defenseRange = 5.5f;`
- `public float ActiveRange => HasArcher ? range : defenseRange;`

驻塔时 `range = Mathf.Max(defenseRange, archer.attackRange)`；当前弓手 4.5，因此塔射程为 5.5。未来弓手射程若高于 5.5，塔自动采用更高值。

空塔完成建造后：选中显示 5.5 范围圈，但没有弓手时不开火。半建塔不显示范围圈。

HUD 选中箭塔时在详情中显示 `防御射程 5.5`。

不修改 Tower_Blue/Tower_Red 预制体；新字段在没有序列化覆盖时使用代码默认 5.5。

## 4. 攻击范围圈

### 4.1 新组件

新增 `Assets/Scripts/AttackRangeIndicator.cs`（MonoBehaviour）：

- 持有 LineRenderer，96 段闭合圆。
- 共享 `Sprites/Default` 材质，不新增资源文件。
- 线宽约 0.08 世界单位、半透明、队色：蓝方青蓝、红方红、其他金色。
- `Show(Vector3 center, float radius, Color color, int sortingOrder)` 更新位置、半径、颜色与排序。
- `Hide()` 关闭渲染。
- `Radius`、`IsVisible` 暴露给测试。
- 世界空间位于地面；排序在单位脚下层，不遮挡单位头像/血条。
- 无 Collider、无 RaycastTarget，不参与选择、寻路或战斗。

### 4.2 选择管理

`SelectionManager` 维护可复用的 `AttackRangeIndicator` 池：

- 每个选中 `UnitActor` 显示一个圈，半径使用其 `attackRange`。工人和僧侣也显示，尊重“每个兵种”。
- 选中的已完成 `TowerGarrison` 建筑显示 `ActiveRange` 圈；空塔按 5.5。
- 多选时逐单位显示。
- 单位移动时圈跟随；取消选择、死亡、驻塔后离场、被摧毁或半建塔完成前隐藏。
- 每帧只更新位置和必要属性，不创建新 GameObject；池按最大同时选中数增长一次。

## 5. 测试设计

### 5.1 固定显示

- 纯常量：固定宽高 2560×1440、全屏模式。
- PlayMode：SampleScene、MainMenu、ModeSelect、LoadingScreen 预制体的 CanvasScaler 参考分辨率保持 1920×1080；运行输出为 2560×1440。
- PlayerSettings/ProjectSettings：默认宽高 2560×1440、窗口不可调整、全屏。
- MainMenu 运行时：只保留一个禁用的 2560×1440 分辨率按钮，其它隐藏；全屏标签为固定文本。
- 2560×1440 下 UI 面板仍在画布内，现有面板不相交测试全部通过。

### 5.2 箭塔

- `TowerGarrison.defenseRange == 5.5`，空塔 `ActiveRange == 5.5`。
- 弓手 4.5 驻塔后塔 `ActiveRange == 5.5`。
- 敌人距离约 5.0 时塔会开火；约 6.0 时不开火。
- 现有 4.5 内命中、建造中不射击、建造完成射击等回归全部保持。

### 5.3 范围圈

- 选中一个攻击范围 3.2 的单位，只出现一个圈，Radius=3.2，位置跟随单位。
- 改变 `attackRange` 后圈半径更新。
- 多选两个单位产生两个圈；取消一个后对应圈隐藏/回收。
- 选中空塔产生半径 5.5 的圈。
- 半建塔不显示圈。
- LineRenderer 无 Collider，不拦截射线。
- 范围圈对象数量不随帧数增长（对象池）。

### 5.4 视觉与分辨率

- Game View 实际设为 2560×1440；截取主菜单设置面板、对局 HUD、选中弓手/战士/工人/僧侣和选中箭塔的图。
- 每张图逐张 Read，确认圈可见、未被地形完全遮住、不遮字；HUD 与所有面板在 2560 画布内。
- 至少执行一次独立性能采样，确认多选范围圈不会造成明显每帧分配。

## 6. 场景与资产风控

| 改动 | 收益 | 风险 | 缓解 | 回滚 | 不可验证项 |
|---|---|---|---|---|---|
| CanvasScaler 参考保持 1920×1080 | 既有 UI 原比例放大并铺满 2560 画面 | 低：不再修改任何场景/预制体 | 测试钉住1920参考；2560截图 | 无场景改动可回滚 | 其他 DPI/显示器 |
| ProjectSettings 默认 2560×1440 全屏 | 独立播放器按目标启动 | 低：显示器不支持时可能被系统缩放 | 记录运行分辨率 | 恢复 ProjectSettings 备份 | 用户实际显示器 |
| MainMenu 设置控件运行时固定 | 消除可变分辨率 | 低：旧按钮占位 | 保留单项、隐藏多余按钮 | 代码回滚 | 主观美观 |
| TowerGarrison 射程 5.5 | 防御覆盖 +22% | 中：平衡变化 | 5.0/6.0 边界测试 | 恢复字段 | 长局平衡 |
| 范围圈 LineRenderer | 选中反馈清晰 | 中：排序/性能/遮挡 | 对象池、无碰撞、截图、性能探针 | 删除组件并恢复选择管理 | 真人手感 |

保存场景前为 MainMenu、ModeSelect、SampleScene 建立字节备份；Prefab 备份 LoadingScreen。每个场景仅允许 CanvasScaler 参考分辨率一行变化，其他对象级差异必须为零。

## 7. 自检与发布

1. 先写失败测试，确认旧行为 RED，再做最小实现。
2. 编辑器脚本或代码改动后 `request_compile`，PlayMode 串行执行，不并行套件。
3. 聚焦测试通过后跑完整 286+ 新增用例套件；归档日志。
4. 2560×1440 实图逐张检查；ProjectDoctor、Console、场景/存档指纹全部核对。
5. 生成变更摘要，取得用户对外发布确认。
6. 本地 D 盘仓库提交；按既有 `sync_from_source.py` 流程同步桌面发布副本，提交并推送 GitHub `Young-Hayden/At-the-gates`。
7. 推送后用 GitHub API 核对远程 SHA 与本地一致；网络重置时使用 `127.0.0.1:7897` 代理重试，禁止 force push。
