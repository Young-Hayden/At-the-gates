# 主界面、设置与模式场景实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不覆盖当前 MainMenu 未保存内容的前提下，加入显示/镜头设置和三模式选择场景，并验证从主菜单到现有人机对局的完整流程。

**Architecture:** `GameSettings` 负责 PlayerPrefs 与运行时应用，`MenuSettingsUI` 和 `ModeSelectMenu` 负责交互；定向 Editor 工具修改/生成场景，保证可重复运行。先做主界面这一独立子项目，场景深度修复和最终 RTS 审查分别使用后续计划。

**Tech Stack:** 团结引擎 2022.3.62t5、UGUI、C#、PlayerPrefs、Codely Bridge MCP、既有 Tiny Swords UI 素材。

## Global Constraints

- 遵循 `docs/superpowers/specs/2026-10-17-main-menu-mode-select-deep-audit-design.md`。
- 布局使用 A「卷轴指挥所」；设置只含显示＋镜头，不加音量。
- 战役和对战只锁定占位，不创建空玩法场景。
- 不全量重建当前 MainMenu；施工前按 dirty 原样备份。
- 不全量重撒地图、树木或装饰，不修改 SampleScene 玩法以配合 UI。
- 项目不是 Git 仓库，不初始化 Git；备份放项目根 `Backups`。
- 测试 PlayerPrefs 前保存原值，测试后恢复；正式 Demo 再按用户选择设置。
- 一次性探针只放 `Temp`，完成后删除。

## 文件结构

- 新建：`Assets/Scripts/GameSettings.cs` — PlayerPrefs、默认值、显示与相机应用。
- 新建：`Assets/Scripts/UI/MenuSettingsUI.cs` — 主菜单设置面板交互。
- 新建：`Assets/Scripts/UI/ModeSelectMenu.cs` — 模式与返回按钮交互。
- 新建：`Assets/Editor/EnhanceMainMenu.cs` — 定向增强当前/未来 MainMenu。
- 新建：`Assets/Editor/BuildModeSelect.cs` — 生成 ModeSelect 并维护场景列表。
- 修改：`Assets/Scripts/RTSCamera.cs` — 启动时应用相机设置。
- 修改：`Assets/Scripts/UI/MainMenu.cs` — 无需改流程代码，只改场景字段；若必须改则限制为设置初始化。
- 修改：`Assets/Editor/BuildMainMenu.cs` — 新模式场景名、规则文本和增强工具调用。
- 修改：`Assets/Scenes/MainMenu.scene`。
- 新建：`Assets/Scenes/ModeSelect.scene`。
- 新建后删除：`Temp` 下 UI/设置探针和截图。

---

### Task 1: 备份、失败基线与 GameSettings

**Files:**
- Create: `Backups/2026-10-17-menu-mode/Assets/Scenes/MainMenu.scene`
- Create: `Assets/Scripts/GameSettings.cs`
- Test: `Temp/MenuSettingsProbe.cs`

**Interfaces:**
- Produces:
  - `GameSettings.ResolutionIndex : int`
  - `GameSettings.Fullscreen : bool`
  - `GameSettings.EdgePan : bool`
  - `GameSettings.PanSpeed : float`
  - `GameSettings.ApplyDisplay() : void`
  - `GameSettings.ApplyCamera(RTSCamera camera) : void`
  - `GameSettings.ResetToDefaults() : void`

- [x] **Step 1: 备份当前 MainMenu 与场景设置**

按 dirty 原样复制 `Assets/Scenes/MainMenu.scene` 和 meta 到 `Backups/2026-10-17-menu-mode`，并记录当前 Build Settings 和 PlayerPrefs 四个键的旧值。禁止覆盖已有备份。

- [x] **Step 2: 写并运行失败探针**

预期当前：`GameSettings` 类型不存在、ModeSelect 场景不存在、MainMenu 无设置按钮/面板、MainMenu.gameScene=SampleScene。

- [x] **Step 3: 实现 GameSettings**

固定键名与默认值：

```csharp
const string ResolutionKey = "rts.resolution";
const string FullscreenKey = "rts.fullscreen";
const string EdgePanKey = "rts.edgePan";
const string PanSpeedKey = "rts.panSpeed";
// ResolutionIndex 默认 2：1280x720=0, 1600x900=1, 1920x1080=2
// Fullscreen=true, EdgePan=true, PanSpeed=18
```

`ApplyDisplay()` 在 `Application.isEditor` 时直接返回；否则按索引调用 `Screen.SetResolution(width, height, Fullscreen)`。`ApplyCamera(camera)` 仅写 `camera.edgePan` 和 `camera.panSpeed`。所有 setter 变更后立即 `PlayerPrefs.Save()`，PanSpeed 限制在 6～30。

- [x] **Step 4: 验证默认值、持久化和相机应用**

预期：默认值正确；修改四个值后 PlayerPrefs 一致；`ApplyCamera` 后 RTSCamera 字段一致；编辑器调用 `ApplyDisplay` 不改窗口。恢复测试前 PlayerPrefs。

---

### Task 2: 主菜单设置面板与规则更新

**Files:**
- Create: `Assets/Scripts/UI/MenuSettingsUI.cs`
- Create: `Assets/Editor/EnhanceMainMenu.cs`
- Modify: `Assets/Editor/BuildMainMenu.cs`
- Modify: `Assets/Scenes/MainMenu.scene`
- Modify: `Assets/Scripts/RTSCamera.cs`

**Interfaces:**
- Consumes: Task 1 的 GameSettings API、现有 MainMenu 和 Tiny Swords 图片。
- Produces: `MenuSettingsUI.Open()/Close()/SetResolution(int)/ToggleFullscreen()/ToggleEdgePan()/SetPanSpeed(float)/ResetToDefaults()`。

- [x] **Step 1: 编写 MenuSettingsUI**

组件放在 Menu Canvas 上，公开引用：openButton、panel、resolutionButtons[3]、fullscreenButton/Label、edgePanButton/Label、panSpeedSlider、panSpeedValue、resetButton、closeButton。

`Awake` 绑定所有按钮/Slider、调用 `GameSettings.ApplyDisplay()`、刷新文字并关闭面板。`Open` 显示并刷新，`Close` 隐藏。每个操作都更新 GameSettings 和 UI；Fullscreen/EdgePan 文本必须为“开/关”，Slider 数值显示整数。恢复默认时同步 Slider.value，避免文字与实际值不一致。

- [x] **Step 2: 编写定向 EnhanceMainMenu**

`EnhanceMainMenu.Apply(Scene scene)` 必须幂等：

1. 找到该场景的 Menu Canvas，设置 `MainMenu.gameScene="ModeSelect"`。
2. 更新 Rules/Body 为民房产工人、固定矿、H 驻守、小地图说明。
3. 在 Board 右上加入齿轮 openButton。
4. 在 Menu Canvas 下、Fade 之前创建/更新 SettingsPanel，使用 `Banner_Vertical`。
5. 面板包含三种分辨率按钮、全屏/边缘平移开关、速度 Slider、数值、恢复默认和返回；所有 MenuSettingsUI 引用逐项接齐。
6. Fade 始终为最后一个子节点，不重复创建已有对象。

当前场景调用后保存 MainMenu；`BuildMainMenu.Build` 在其 SaveScene 前调用同一 Apply，保证未来重建不丢增强。

- [x] **Step 3: 更新 BuildMainMenu 与 RTSCamera**

BuildMainMenu 保留 `GameScene="SampleScene"`，新增 `ModeScene="ModeSelect"`，将 `menu.gameScene` 设为 ModeScene，场景列表按 MainMenu、ModeSelect、SampleScene 排列，规则文本同步更新。

`RTSCamera.Awake` 在测量地图前调用 `GameSettings.ApplyCamera(this)`，不修改原有平移/缩放/钳制算法。

- [x] **Step 4: 编译并做静态验收**

预期：MainMenu 仍是原 Board/Rules/Menu/Troops 结构，新增且仅新增齿轮和 SettingsPanel；规则无“造金矿/城堡产工人”；MainMenu.gameScene=ModeSelect；Fade 最后；所有 UI 引用非空；连续 Apply 两次对象数不变。

---

### Task 3: 三模式场景与完整 UI 流程

**Files:**
- Create: `Assets/Scripts/UI/ModeSelectMenu.cs`
- Create: `Assets/Editor/BuildModeSelect.cs`
- Create: `Assets/Scenes/ModeSelect.scene`
- Test: `Temp/ModeFlowProbe.cs` 和三种分辨率截图

**Interfaces:**
- Produces: `ModeSelectMenu` 的 humanButton/campaignButton/versusButton/backButton，以及 `gameScene="SampleScene"`、`mainMenuScene="MainMenu"`。

- [x] **Step 1: 编写 ModeSelectMenu**

`Awake` 调用 `GameSettings.ApplyDisplay()`；`Start` 只给人机与返回按钮绑定场景加载。战役/对战按钮在场景中 `interactable=false`，显示锁和“后期开放”，不得绑定任何加载动作。

- [x] **Step 2: 生成 A 布局 ModeSelect**

场景包含 Menu Camera、Menu Canvas、EventSystem、卷轴背景、标题和返回按钮。三张卡牌横向排列：

- 蓝色人机：当前完整 RTS 对局，可点击。
- 黄色战役：锁定，后期开放。
- 红色对战：锁定，后期开放。

保存后关闭 additive 场景，并确保 Build Settings 顺序为 MainMenu、ModeSelect、SampleScene。重复运行不得产生重复场景或 Build Settings 项。

- [x] **Step 3: 运行完整流程**

从 MainMenu 开始：

1. 打开/关闭设置，验证背景按钮不穿透。
2. 修改分辨率索引、全屏、边缘平移、镜头速度并重新加载 MainMenu，验证保存。
3. 开始游戏进入 ModeSelect；战役/对战不可点击；返回可回 MainMenu。
4. 人机进入 SampleScene；RTSCamera 的 edgePan/panSpeed 与设置一致。
5. 恢复测试前 PlayerPrefs。

- [x] **Step 4: 三种尺寸布局与清理验收**

在 1920×1080、1600×900、1280×720 分别截取 MainMenu、设置面板和 ModeSelect，检查标题、规则、按钮、三卡牌、锁文本均无裁切或互相遮挡。退出 Play 后场景无临时对象，编译与 Console 无本次错误。

---

### Task 4: 独立审查与进入场景深度审计

**Files:**
- Create: `Backups/2026-10-17-menu-mode/verification.txt`

**Interfaces:**
- Produces: 主界面子项目验证记录，供后续场景审计使用。

- [x] **Step 1: 独立只读审查**

检查场景流程、PlayerPrefs 生命周期、重复 Apply、UI 输入隔离和编辑器/正式运行差异。Critical/Important 必须修复并重跑 Task 3。

- [x] **Step 2: 写验证记录并冻结本阶段范围**

记录所有实际输出、截图、审查结果和修改文件。此计划完成后，另行根据真实扫描结果编写“场景铺设与碰撞遮挡修复”计划，不把未知问题预写成修复项。
