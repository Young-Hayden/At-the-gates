using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the HUD. Unlike the earlier version this one does NOT build anything — every
/// panel, button and label lives in the scene as a real object (created by
/// Assets/Editor/BuildHUD.cs) so it can be seen in the Scene view and edited by hand.
/// This component only fills in values and wires the clicks.
/// </summary>
public class HUD : MonoBehaviour
{
    [Header("资源栏")]
    public Text goldText;
    public Text woodText;
    public Text meatText;

    [Header("建造栏")]
    public GameObject[] buildPrefabs = new GameObject[0];
    public Button[] buildButtons = new Button[0];
    public Image[] buildButtonImages = new Image[0];
    [Tooltip("建造按钮下方的两行造价读数（金 / 木）；买不起时变暗。由 Assets/Editor/PolishHUD.cs 接线。")]
    public Text[] buildCostTexts = new Text[0];

    [Header("资源栏人口格")]
    [Tooltip("资源栏第四格的人口读数（如 10/12）。为空时退回在状态行里显示人口。")]
    public Text populationText;
    [Tooltip("人口未满时的颜色。")]
    public Color populationNormalTint = new Color(1f, 0.96f, 0.86f, 1f);
    [Tooltip("人口已满时的颜色（琥珀）。")]
    public Color populationFullTint = new Color(1f, 0.72f, 0.35f, 1f);

    [Header("生产面板（选中工厂建筑时显示）")]
    public GameObject productionPanel;
    public Text productionTitle;
    public Button[] produceButtons = new Button[0];
    public Image[] produceIcons = new Image[0];
    public Text[] produceCosts = new Text[0];
    public Image[] produceBacks = new Image[0];

    [Header("兵种面板（常驻，花金钱直接下单）")]
    public GameObject unitPanel;

    /// <summary>One buyable unit. The list is generated from the factories' own product
    /// tables, so prices and icons cannot drift away from the buildings that make them.</summary>
    [System.Serializable]
    public class UnitOffer
    {
        public string label = "战士";
        public GameObject unitPrefab;
        public Sprite icon;
        public ResourceCost cost = new ResourceCost(60);
        [Tooltip("没有对应建筑时按钮上显示的提示。")]
        public string requiresHint = "需兵营";
    }

    public UnitOffer[] unitOffers = new UnitOffer[0];
    public Button[] unitButtons = new Button[0];
    public Image[] unitIcons = new Image[0];
    public Image[] unitBacks = new Image[0];
    public Text[] unitNames = new Text[0];
    public Text[] unitCosts = new Text[0];
    public RectTransform[] unitFills = new RectTransform[0];
    [Tooltip("每个兵种槽的变暗遮罩：没有工厂 / 缺资源 / 人口或兵种上限已满时显示。")]
    public Image[] unitDims = new Image[0];

    [Header("选中面板")]
    public GameObject selectionPanel;
    public Text selectionTitle;
    public Text selectionBody;
    public Button demolishButton;
    [Tooltip("攻击命令按钮：点击后下一次左键为攻击移动/集火，右键或 Esc 取消。")]
    public Button attackButton;
    [Tooltip("攻击命令待命时按钮底图的颜色。")]
    public Color attackArmedTint = new Color(1f, 0.72f, 0.45f);
    [Tooltip("维修按钮：花钱把选中的己方建筑修满。Phase 2 才把它接进场景；在此之前为空。")]
    public Button repairButton;

    [Header("科技条")]
    [Tooltip("科技条容器：选中己方已建成的指挥中心时才显示。由 Assets/Editor/BuildTechPanel.cs 搭建。")]
    public GameObject techStrip;
    [Tooltip("锻造 / 铠甲 / 采集，下标必须与 Technology.Tech 的枚举值一致。")]
    public Button[] techButtons = new Button[0];
    [Tooltip("每个科技按钮下方的费用读数，也用来显示「已研究」。")]
    public Text[] techCosts = new Text[0];
    [Tooltip("科技条的进度读数：研究进行中显示科技名与剩余秒数。")]
    public Text techProgress;
    [Tooltip("每个科技按钮的变暗遮罩：已研究 / 正在研究别的 / 买不起时显示。")]
    public Image[] techDims = new Image[0];

    [Header("状态行")]
    public Text statusText;
    [Tooltip("状态文字的深色底条；没有提示时整条隐藏，免得空条压在草地上。")]
    public Image statusBacking;

    [Header("结算")]
    public GameObject gameOverPanel;
    public Text gameOverText;
    public Button restartButton;
    [Tooltip("结算面板的「回主菜单」按钮。")]
    public Button backToMenuButton;
    public string mainMenuScene = "MainMenu";

    [Header("配色")]
    public Color normalTint = Color.white;
    public Color disabledTint = new Color(1f, 0.45f, 0.45f, 1f);
    [Tooltip("建造栏按钮未解锁时的底色。比纯红柔和，和雕花木框更搭。")]
    public Color buildLockedTint = new Color(0.55f, 0.50f, 0.46f, 1f);
    [Tooltip("缺建筑 / 队列满 时按钮上的提示字颜色。")]
    public Color warnTint = new Color(1f, 0.72f, 0.4f);
    [Tooltip("买不起时造价读数的颜色。")]
    public Color costDimTint = new Color(0.74f, 0.70f, 0.62f, 1f);
    [Tooltip("肉低于 50 时资源栏肉读数的颜色（琥珀）。")]
    public Color meatLowTint = new Color(1f, 0.72f, 0.35f, 1f);
    [Tooltip("肉为 0 时资源栏肉读数的颜色（红）。")]
    public Color meatEmptyTint = new Color(1f, 0.35f, 0.3f, 1f);

    static readonly Color UnitCostNormal = new Color(1f, 0.96f, 0.86f, 1f);

    /// <summary>Meat at or above this reads normally; below it turns amber, and zero
    /// turns red. Upkeep is the reason the number can sit near zero for long.</summary>
    const int MeatLowThreshold = 50;

    Color meatNormalTint;
    bool meatNormalCaptured;

    ProductionBuilding shownFactory;
    readonly List<UnitMover> liveUnits = new List<UnitMover>();

    /// <summary>Scene-authored colour of each production cost label, captured the
    /// first time a warning temporarily paints it so the price can be restored
    /// without adding another serialized field or rebuilding the panel.</summary>
    readonly Dictionary<Text, Color> costTextColors = new Dictionary<Text, Color>();

    // Lazy-cached: three Refresh* passes asked for this every frame. Unity's fake-null
    // means a destroyed manager re-finds itself on the next access for free.
    SelectionManager smCache;
    SelectionManager SM
    {
        get
        {
            if (smCache == null) smCache = FindFirstObjectByType<SelectionManager>();
            return smCache;
        }
    }

    void Start()
    {
        for (int i = 0; i < buildButtons.Length; i++)
        {
            if (buildButtons[i] == null) continue;
            int index = i;
            buildButtons[i].onClick.RemoveAllListeners();
            buildButtons[i].onClick.AddListener(() => OnBuildClicked(index));
        }
        for (int i = 0; i < produceButtons.Length; i++)
        {
            if (produceButtons[i] == null) continue;
            int index = i;
            produceButtons[i].onClick.RemoveAllListeners();
            produceButtons[i].onClick.AddListener(() => OnProduceClicked(index));
        }
        for (int i = 0; i < unitButtons.Length; i++)
        {
            if (unitButtons[i] == null) continue;
            int index = i;
            unitButtons[i].onClick.RemoveAllListeners();
            unitButtons[i].onClick.AddListener(() => OnUnitOfferClicked(index));
        }
        if (restartButton != null)
        {
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(() =>
            {
                if (GameState.Instance != null) GameState.Instance.RestartLevel();
            });
        }
        if (backToMenuButton != null)
        {
            backToMenuButton.onClick.RemoveAllListeners();
            backToMenuButton.onClick.AddListener(() =>
            {
                // The match is already decided — there is no progress left to save,
                // so no confirmation dialog: straight to the menu via the loading screen.
                Time.timeScale = 1f;
                SaveGameService.CancelPendingLoad();
                LoadingScreen.Load(mainMenuScene);
            });
        }
        if (demolishButton != null)
        {
            var sm = FindFirstObjectByType<SelectionManager>();
            if (sm != null) demolishButton.onClick.AddListener(() =>
            {
                if (PauseMenu.IsAnyOpen) return;
                sm.DemolishSelected();
            });
        }
        if (repairButton != null)
        {
            // Remove-then-add with a named handler keeps this idempotent: Start can run
            // again after a rebuild, and stacking the listener would charge one click twice.
            repairButton.onClick.RemoveListener(OnRepairClicked);
            repairButton.onClick.AddListener(OnRepairClicked);
        }
        for (int i = 0; i < techButtons.Length; i++)
        {
            if (techButtons[i] == null) continue;
            int index = i;
            techButtons[i].onClick.RemoveAllListeners();
            techButtons[i].onClick.AddListener(() => OnTechClicked(index));
        }
        if (attackButton != null)
        {
            var sm = FindFirstObjectByType<SelectionManager>();
            if (sm != null) attackButton.onClick.AddListener(() =>
            {
                if (PauseMenu.IsAnyOpen) return;
                // toggle: a second click on the button cancels the armed order
                if (sm.AttackArmed) sm.DisarmAttack();
                else sm.ArmAttack();
            });
        }
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        if (Economy.Instance != null) Economy.Instance.Changed += RefreshResources;
        if (GameState.Instance != null) GameState.Instance.GameEnded += OnGameEnded;

        RefreshResources();
        RefreshAll();
    }

    void OnDestroy()
    {
        if (Economy.Instance != null) Economy.Instance.Changed -= RefreshResources;
        if (GameState.Instance != null) GameState.Instance.GameEnded -= OnGameEnded;
    }

    void Update() { RefreshAll(); }

    // ------------------------------------------------------------------ refresh

    void RefreshAll()
    {
        RefreshBuildButtons();
        RefreshProduction();
        RefreshUnitPanel();
        RefreshSelection();
        RefreshPopulation();
        RefreshStatus();
    }

    void RefreshResources()
    {
        if (Economy.Instance == null) return;
        if (goldText != null) goldText.text = Economy.Instance.gold.ToString();
        if (woodText != null) woodText.text = Economy.Instance.wood.ToString();
        if (meatText != null)
        {
            // Remember the scene-authored colour before a fuel warning repaints it.
            if (!meatNormalCaptured)
            {
                meatNormalTint = meatText.color;
                meatNormalCaptured = true;
            }
            int meat = Economy.Instance.meat;
            meatText.text = meat.ToString();
            meatText.color = meat <= 0 ? meatEmptyTint
                           : meat < MeatLowThreshold ? meatLowTint
                           : meatNormalTint;
        }
    }

    void RefreshBuildButtons()
    {
        var eco = Economy.Instance;
        RememberCostColors();
        for (int i = 0; i < buildButtons.Length; i++)
        {
            if (buildButtons[i] == null) continue;
            var prefab = i < buildPrefabs.Length ? buildPrefabs[i] : null;
            var b = prefab != null ? prefab.GetComponent<Building>() : null;
            bool ok = b == null || eco == null || eco.CanAfford(b.cost);
            // one command centre per side — grey the button out once it exists
            if (ok && b != null && b.isCommandCenter && HasCommandCentre()) ok = false;
            // everything else unlocks only after that command centre is finished
            if (ok && b != null && !b.isCommandCenter && !Building.HasCompletedCommandCentre(PlayerTeam)) ok = false;
            if (buildButtons[i].interactable != ok) buildButtons[i].interactable = ok;
            if (i < buildButtonImages.Length && buildButtonImages[i] != null)
            {
                var want = ok ? normalTint : buildLockedTint;
                if (buildButtonImages[i].color != want) buildButtonImages[i].color = want;
            }
            if (i < buildCostTexts.Length && buildCostTexts[i] != null)
            {
                var label = buildCostTexts[i];
                var want = ok ? CostTextColor(label) : costDimTint;
                if (label.color != want) label.color = want;
            }
        }
    }

    /// <summary>Does the player own a living, completed command centre? Ghosts and
    /// half-built bases are excluded.</summary>
    static bool HasCommandCentre()
    {
        var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        return Building.HasLivingCommandCentre(player);
    }

    // ------------------------------------------------------------- production

    void RefreshProduction()
    {
        var sm = SM;
        var building = sm != null ? sm.selectedBuilding : null;
        var factory = building != null ? building.GetComponent<ProductionBuilding>() : null;

        if (factory != shownFactory)
        {
            shownFactory = factory;
            if (productionPanel != null) productionPanel.SetActive(factory != null);
        }
        if (factory == null) return;

        if (productionTitle != null)
        {
            if (building.IsUnderConstruction)
            {
                productionTitle.text = BuildingTitle(building) + "   建造中 "
                    + Mathf.RoundToInt(building.ConstructionProgress * 100f) + "%";
            }
            else
            {
                string q = factory.Queue.Count > 0
                    ? "   (" + factory.Queue.Count + " 个排队中 " + Mathf.RoundToInt(factory.Progress * 100f) + "%)"
                    : "";
                productionTitle.text = BuildingTitle(building) + q;
            }
        }

        var eco = Economy.Instance;
        RememberCostColors();
        for (int i = 0; i < produceButtons.Length; i++)
        {
            bool has = i < factory.products.Length && factory.products[i].unitPrefab != null;
            if (produceButtons[i] != null) produceButtons[i].gameObject.SetActive(has);
            if (!has) continue;

            // The icon was never filled in before, which left every production button
            // blank. Prefer the hand-cropped portrait; a unit's own sprite is a 3x3 unit
            // frame with a 1 unit character in it and reads as a speck at 64px.
            if (i < produceIcons.Length && produceIcons[i] != null)
            {
                var want = factory.products[i].icon;
                if (want == null)
                {
                    var usr = factory.products[i].unitPrefab.GetComponentInChildren<SpriteRenderer>();
                    want = usr != null ? usr.sprite : null;
                }
                if (produceIcons[i].sprite != want) produceIcons[i].sprite = want;
                produceIcons[i].enabled = want != null;
            }

            bool ok = factory.CanQueue(i, true, out string reason);
            if (produceButtons[i] != null) produceButtons[i].interactable = ok;
            if (i < produceBacks.Length && produceBacks[i] != null)
            {
                var want = ok ? normalTint : disabledTint;
                if (produceBacks[i].color != want) produceBacks[i].color = want;
            }
            if (i < produceCosts.Length && produceCosts[i] != null)
            {
                var costText = produceCosts[i];
                if (string.IsNullOrEmpty(reason))
                {
                    costText.text = factory.products[i].cost.ToString();
                    costText.color = CostTextColor(costText);
                }
                else
                {
                    // e.g. "战士已达上限" — the price cannot buy past the army cap.
                    costText.text = reason;
                    costText.color = warnTint;
                }
            }
        }
    }

    /// <summary>Captures every price label's scene-authored colour before a refusal
    /// warning can overwrite it. Called once per panel refresh, ahead of the loop
    /// that may paint a warning.</summary>
    void RememberCostColors()
    {
        for (int i = 0; i < produceCosts.Length; i++)
        {
            var text = produceCosts[i];
            if (text != null && !costTextColors.ContainsKey(text))
                costTextColors[text] = text.color;
        }
        for (int i = 0; i < buildCostTexts.Length; i++)
        {
            var text = buildCostTexts[i];
            if (text != null && !costTextColors.ContainsKey(text))
                costTextColors[text] = text.color;
        }
    }

    /// <summary>The price label's normal colour, remembered before a refusal
    /// warning overwrites it.</summary>
    Color CostTextColor(Text text)
    {
        if (!costTextColors.TryGetValue(text, out var color))
        {
            color = text.color;
            costTextColors[text] = color;
        }
        return color;
    }

    void OnBuildClicked(int index)
    {
        if (PauseMenu.IsAnyOpen) return;      // pause freezes time, not clicks — don't spend while frozen
        if (BuildingPlacer.Instance == null) return;
        if (index < 0 || index >= buildPrefabs.Length || buildPrefabs[index] == null) return;
        BuildingPlacer.Instance.Begin(buildPrefabs[index]);
    }

    void OnProduceClicked(int index)
    {
        if (PauseMenu.IsAnyOpen) return;
        if (shownFactory == null) return;
        shownFactory.TryQueue(index);
    }

    void OnRepairClicked()
    {
        if (PauseMenu.IsAnyOpen) return;
        var sm = SM;
        if (sm != null) sm.RepairSelected();
    }

    // ------------------------------------------------------------------ tech

    static readonly string[] TechNames = { "锻造", "铠甲", "采集" };
    static readonly Color TechCostNormal = new Color(1f, 0.97f, 0.85f);

    /// <summary>Repaints the strip for the selected command centre: an affordable,
    /// unresearched line stays clickable while nothing else runs, and the shared
    /// progress label carries the running research's remaining seconds.</summary>
    void RefreshTechPanel()
    {
        var eco = Economy.Instance;
        var team = PlayerTeam;
        bool busy = Technology.ActiveTechOf(team).HasValue;
        for (int i = 0; i < techButtons.Length; i++)
        {
            var button = techButtons[i];
            if (button == null) continue;
            var tech = (Technology.Tech)i;
            bool researched = Technology.Has(team, tech);
            var cost = Technology.CostOf(tech);
            bool afford = eco == null || eco.CanAfford(cost);
            bool usable = !researched && !busy && afford;
            if (button.interactable != usable) button.interactable = usable;

            if (i < techDims.Length && techDims[i] != null)
            {
                if (techDims[i].enabled != !usable) techDims[i].enabled = !usable;
            }

            if (i < techCosts.Length && techCosts[i] != null)
            {
                var label = techCosts[i];
                string want = researched ? "已研究" : FormatCost(cost);
                if (label.text != want) label.text = want;
                var tint = researched ? warnTint : afford ? TechCostNormal : disabledTint;
                if (label.color != tint) label.color = tint;
            }
        }

        if (techProgress != null)
        {
            var active = Technology.ActiveTechOf(team);
            string want = active.HasValue
                ? TechNames[(int)active.Value] + "研究中　剩余 "
                    + Mathf.Max(0, Mathf.CeilToInt(Technology.RemainingOf(team))) + " 秒"
                : "";
            if (techProgress.text != want) techProgress.text = want;
        }
    }

    void OnTechClicked(int index)
    {
        if (PauseMenu.IsAnyOpen) return;
        if (index < 0 || index >= techButtons.Length) return;
        Technology.BeginResearch(PlayerTeam, (Technology.Tech)index, out _);
    }

    /// <summary>Cost text without the space <see cref="ResourceCost.ToString"/> inserts,
    /// so a 92px button has room for "250金100木".</summary>
    static string FormatCost(ResourceCost cost)
    {
        if (cost.IsZero) return "免费";
        string text = "";
        if (cost.gold > 0) text += cost.gold + "金";
        if (cost.wood > 0) text += cost.wood + "木";
        if (cost.meat > 0) text += cost.meat + "肉";
        return text;
    }

    // ------------------------------------------------------------- unit shop

    readonly List<ProductionBuilding> factories = new List<ProductionBuilding>();
    float factoryTimer;

    static Team PlayerTeam => GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;

    /// <summary>My finished factories. Rebuilt on a timer rather than per frame — the shop
    /// asks about every unit it lists, and each question would otherwise be a scene scan.
    ///
    /// The ghost is skipped by hand: it is a full copy of the prefab, so it carries a
    /// working ProductionBuilding component and the player's own team. Scanning by
    /// component rather than through <see cref="Building.All"/> would otherwise treat the
    /// placement preview as a finished barracks and unlock everything the moment the
    /// player picks something to build.</summary>
    void RebuildFactories()
    {
        factories.Clear();
        var player = PlayerTeam;
        foreach (var pb in Object.FindObjectsByType<ProductionBuilding>(FindObjectsSortMode.None))
        {
            if (pb == null) continue;
            var b = pb.GetComponent<Building>();
            if (b == null || b.isGhost || b.team != player || !b.IsAlive || b.IsUnderConstruction) continue;
            factories.Add(pb);
        }
    }

    /// <summary>
    /// The always-on buy panel. Unlike the production panel it does not need a building
    /// selected: it lists every unit the player could ever order, greys out the ones whose
    /// factory does not exist yet, and routes the click to the nearest factory that can
    /// actually take the job.
    /// </summary>
    void RefreshUnitPanel()
    {
        if (unitPanel == null || unitOffers == null) return;

        factoryTimer -= Time.deltaTime;
        if (factoryTimer <= 0f) { factoryTimer = 0.3f; RebuildFactories(); }

        var eco = Economy.Instance;
        for (int i = 0; i < unitButtons.Length; i++)
        {
            if (unitButtons[i] == null) continue;

            bool has = i < unitOffers.Length && unitOffers[i] != null && unitOffers[i].unitPrefab != null;
            if (unitButtons[i].gameObject.activeSelf != has) unitButtons[i].gameObject.SetActive(has);
            if (!has) continue;

            var offer = unitOffers[i];
            var free = NearestFactory(offer.unitPrefab);
            bool owned = FreeFactoryCount(offer.unitPrefab) > 0;
            bool afford = eco == null || eco.CanAfford(offer.cost);
            bool capOk = UnitLimits.CanProduce(PlayerTeam, offer.unitPrefab, out string capReason);
            bool popOk = Population.CanFit(PlayerTeam, offer.unitPrefab, out string popReason);
            // Mirrors ProductionBuilding.CanQueue, where upkeep is the last refusal. Without
            // it a starving side sees a lit, clickable button whose click silently does
            // nothing — the factory refuses one layer below.
            bool fed = !Upkeep.IsStarving(PlayerTeam);
            bool ok = owned && afford && free != null && capOk && popOk && fed;

            if (i < unitIcons.Length && unitIcons[i] != null && unitIcons[i].sprite != offer.icon)
                unitIcons[i].sprite = offer.icon;
            if (i < unitNames.Length && unitNames[i] != null)
                unitNames[i].text = offer.label + QueueTag(offer.unitPrefab);

            if (i < unitCosts.Length && unitCosts[i] != null)
            {
                if (!owned) { unitCosts[i].text = offer.requiresHint; unitCosts[i].color = warnTint; }
                else if (!capOk) { unitCosts[i].text = capReason; unitCosts[i].color = warnTint; }
                else if (!popOk) { unitCosts[i].text = popReason; unitCosts[i].color = warnTint; }
                // Same order as ProductionBuilding.CanQueue: the queue is checked before
                // upkeep, so a starving side with every queue full reads "队列已满" first and
                // only flips to "食物不足" once a slot frees up.
                else if (free == null) { unitCosts[i].text = "队列已满"; unitCosts[i].color = warnTint; }
                else if (!fed) { unitCosts[i].text = "食物不足"; unitCosts[i].color = warnTint; }
                else { unitCosts[i].text = offer.cost.ToString(); unitCosts[i].color = UnitCostNormal; }
            }
            if (unitButtons[i].interactable != ok) unitButtons[i].interactable = ok;

            // A wired dim overlay is the shipping look; the red backing is the fallback for
            // HUDs built by tests (and for a scene that has not been polished yet).
            bool dimWired = i < unitDims.Length && unitDims[i] != null;
            if (dimWired)
            {
                if (unitDims[i].enabled != !ok) unitDims[i].enabled = !ok;
            }
            if (i < unitBacks.Length && unitBacks[i] != null)
            {
                var want = dimWired || ok ? normalTint : disabledTint;
                if (unitBacks[i].color != want) unitBacks[i].color = want;
            }

            if (i < unitFills.Length && unitFills[i] != null)
            {
                float p = ProgressFor(offer.unitPrefab);
                var rt = unitFills[i];
                bool show = p >= 0f;
                if (rt.gameObject.activeSelf != show) rt.gameObject.SetActive(show);
                if (show)
                {
                    float full = 66f;
                    if (rt.parent is RectTransform prt) full = Mathf.Max(10f, prt.rect.width - 12f);
                    rt.sizeDelta = new Vector2(full * Mathf.Clamp01(p), 6f);
                }
            }
        }
    }

    void OnUnitOfferClicked(int index)
    {
        if (PauseMenu.IsAnyOpen) return;
        if (index < 0 || index >= unitOffers.Length) return;
        var offer = unitOffers[index];
        if (offer == null || offer.unitPrefab == null) return;

        var target = NearestFactory(offer.unitPrefab);
        if (target == null) return;                       // no barracks, or it is full
        int i = target.IndexOfProduct(offer.unitPrefab);
        if (i >= 0) target.TryQueue(i);
    }

    int FreeFactoryCount(GameObject unit)
    {
        int n = 0;
        for (int i = 0; i < factories.Count; i++)
            if (factories[i].IndexOfProduct(unit) >= 0) n++;
        return n;
    }

    /// <summary>Factory that can take the order right now and is closest to what the player
    /// is looking at, so a new unit walks out of the base nearest the camera.</summary>
    ProductionBuilding NearestFactory(GameObject unit)
    {
        var cam = Camera.main;
        Vector3 from = cam != null ? cam.transform.position : Vector3.zero;
        ProductionBuilding best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < factories.Count; i++)
        {
            var pb = factories[i];
            if (pb.IndexOfProduct(unit) < 0) continue;
            if (pb.Queue.Count >= pb.queueLimit) continue;
            float d = (pb.transform.position - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = pb; }
        }
        return best;
    }

    /// <summary>0..1 while this unit is the one being built somewhere, -1 when idle.</summary>
    float ProgressFor(GameObject unit)
    {
        for (int i = 0; i < factories.Count; i++)
        {
            float p = factories[i].ProgressOf(unit);
            if (p >= 0f) return p;
        }
        return -1f;
    }

    string QueueTag(GameObject unit)
    {
        int n = 0;
        for (int i = 0; i < factories.Count; i++)
        {
            var q = factories[i].Queue;
            for (int k = 0; k < q.Count; k++)
                if (factories[i].products[q[k]].unitPrefab == unit) n++;
        }
        return n > 0 ? " ×" + n : "";
    }

    // -------------------------------------------------------------- selection

    /// <summary>Player-facing unit name. Prefab/object names stay unchanged for saves
    /// and role accounting; only the selection title is localised.</summary>
    static string UnitTitle(UnitActor actor)
    {
        switch (UnitLimits.RoleOf(actor.gameObject))
        {
            case UnitRole.Worker: return "工人";
            case UnitRole.Warrior: return "战士";
            case UnitRole.Lancer: return "枪兵";
            case UnitRole.Archer: return "弓手";
            case UnitRole.Monk: return "僧侣";
            default: return actor.DisplayName;
        }
    }

    static string BuildingTitle(Building building)
    {
        string n = building.name;
        if (n.Contains("Castle")) return "指挥中心";
        if (n.Contains("House")) return "民房";
        if (n.Contains("Barracks")) return "兵营";
        if (n.Contains("Archery")) return "靶场";
        if (n.Contains("Monastery")) return "修道院";
        if (n.Contains("Tower")) return "箭塔";
        if (n.Contains("GoldMine")) return "金矿";
        if (n.Contains("Sheep")) return "羊";
        if (n.Contains("Tree")) return "树木";
        return n;
    }

    void RefreshSelection()
    {
        var sm = SM;
        if (sm == null) return;

        string title = null, body = null;
        var live = liveUnits;

        // A unit that died this frame is still in the list until SelectionManager
        // prunes it, and a corpse delayed by corpseSeconds is still a non-null Unity
        // object — so filter both destroyed components and dead actors.
        live.Clear();
        for (int i = 0; i < sm.selected.Count; i++)
        {
            var u = sm.selected[i];
            if (u == null) continue;                  // Unity's == catches destroyed objects
            var a = u.GetComponent<UnitActor>();
            if (a != null && (!a.IsAlive || !a.enabled)) continue; // dead or garrisoned
            live.Add(u);
        }

        var b = sm.selectedBuilding;
        if (b != null && !b.IsAlive) b = null;         // wrecked buildings stop being selectable

        if (b != null)
        {
            title = BuildingTitle(b);
            body = "生命 " + b.health + " / " + b.maxHealth
                 + (b.IsUnderConstruction
                        ? "\n★ 建造中 " + Mathf.RoundToInt(b.ConstructionProgress * 100f) + "%"
                        : "")
                 + "\n占地 " + b.Width + "×" + b.Depth + " 格"
                 + (b.isDropoff ? "\n卸货点" : "")
                 + (b.isCommandCenter ? "\n指挥中心 —— 被摧毁即失败" : "");

            // A finished tower also reports the range it really covers: its own 5.5
            // minimum, or the garrisoned archer's longer reach.
            var tower = b.GetComponent<TowerGarrison>();
            if (tower != null && !b.IsUnderConstruction)
                body += "\n防御射程 " + tower.ActiveRange.ToString("0.#");

            // Gated on the button: until Phase 2 wires repairButton this is null, so the
            // shipped panel reads exactly as before. The line is shown for a friendly,
            // finished, non-neutral building — the same set where the button appears.
            if (repairButton != null
                && b.team == PlayerTeam && b.team != Team.Neutral && !b.IsUnderConstruction)
            {
                body += b.MissingHealth > 0
                    ? "\n维修：金" + b.RepairCost.gold
                        + " 木" + b.RepairCost.wood
                        + " 肉" + b.RepairCost.meat
                    : "\n维修：已满血";
            }
        }
        else if (live.Count == 1)
        {
            var u = live[0];
            var a = u.GetComponent<UnitActor>();
            var g = u.GetComponent<Gatherer>();
            title = a != null ? UnitTitle(a) : u.name;
            body = (a != null ? "生命 " + a.health + " / " + a.EffectiveMaxHealth
                    + (a.isCombatant
                        ? "\n攻击 " + a.EffectiveAttackDamage + "　射程 " + a.attackRange
                        : "")
                    : "")
                 + (g != null ? "\n" + g.Status + (g.Carrying > 0 ? "（背 " + g.Carrying + "）" : "") : "");
        }
        else if (live.Count > 1)
        {
            title = "已选中 " + live.Count + " 个单位";
            int hp = 0, max = 0;
            for (int i = 0; i < live.Count; i++)
            {
                var a = live[i].GetComponent<UnitActor>();
                if (a == null) continue;
                hp += a.health; max += a.EffectiveMaxHealth;
            }
            body = "总生命 " + hp + " / " + max;
        }

        if (demolishButton != null)
            demolishButton.gameObject.SetActive(b != null && b.CanDemolish && b.team == PlayerTeam);

        // Repair button: visible only for a friendly, finished, non-neutral building; enabled
        // only when the repair is actually possible and affordable. Null in Phase 1.
        if (repairButton != null)
        {
            bool showRepair = b != null
                && b.team == PlayerTeam && b.team != Team.Neutral && !b.IsUnderConstruction;
            if (repairButton.gameObject.activeSelf != showRepair)
                repairButton.gameObject.SetActive(showRepair);
            if (showRepair)
            {
                var eco = Economy.Instance;
                bool usable = b.CanRepair && eco != null && eco.CanAfford(b.RepairCost);
                if (repairButton.interactable != usable) repairButton.interactable = usable;
            }
        }

        // The attack command exists only while something selected can fight. While
        // armed the button glows so the mode is visible without watching the cursor.
        if (attackButton != null)
        {
            bool hasCombat = false;
            for (int i = 0; i < live.Count; i++)
            {
                var a = live[i].GetComponent<UnitActor>();
                if (a != null && a.isCombatant) { hasCombat = true; break; }
            }
            if (attackButton.gameObject.activeSelf != hasCombat)
                attackButton.gameObject.SetActive(hasCombat);
            bool armed = sm != null && sm.AttackArmed;
            var bg = attackButton.targetGraphic;
            if (bg != null)
            {
                var want = armed ? attackArmedTint : normalTint;
                if (bg.color != want) bg.color = want;
            }
        }

        // Technology strip: only while the selection is a friendly, finished command
        // centre — the same place the research actually happens. It lives inside the
        // selection panel, so it disappears with the panel for anything else.
        if (techStrip != null)
        {
            bool showTech = b != null
                && b.team == PlayerTeam && b.team != Team.Neutral
                && b.isCommandCenter && !b.IsUnderConstruction;
            if (techStrip.activeSelf != showTech) techStrip.SetActive(showTech);
            if (showTech) RefreshTechPanel();
        }

        bool show = title != null;
        if (selectionPanel != null && selectionPanel.activeSelf != show) selectionPanel.SetActive(show);
        if (!show) return;
        if (selectionTitle != null) selectionTitle.text = title;
        if (selectionBody != null) selectionBody.text = body;
    }

    void RefreshStatus()
    {
        if (statusText == null) return;
        string msg = "";
        var sm = SM;
        if (GameState.Instance != null && GameState.Instance.IsOver)
            msg = "";
        else if (sm != null && sm.AttackArmed)
            msg = "选择攻击目标　　右键 / Esc 取消";
        else if (BuildingPlacer.Instance != null && BuildingPlacer.Instance.IsPlacing)
            msg = string.IsNullOrEmpty(BuildingPlacer.Instance.LastRefusalReason)
                ? "左键放置　　右键 / Esc 取消"
                : BuildingPlacer.Instance.LastRefusalReason;

        // Population lives in the resource bar's fourth cell once PolishHUD has wired it;
        // before that (and in tests that build a HUD by hand) it falls back to the status
        // line, which is where it shipped from Phase B until now.
        var team = PlayerTeam;
        if (populationText == null)
        {
            string pop = "人口 " + Population.Used(team) + "/" + Population.Capacity(team);
            if (Upkeep.IsStarving(team)) pop += "　食物不足";
            msg = string.IsNullOrEmpty(msg) ? pop : msg + "　　" + pop;
        }
        else if (Upkeep.IsStarving(team))
        {
            msg = string.IsNullOrEmpty(msg) ? "食物不足" : msg + "　　食物不足";
        }

        if (statusText.text != msg) statusText.text = msg;
        if (statusBacking != null)
        {
            bool show = !string.IsNullOrEmpty(msg);
            if (statusBacking.enabled != show) statusBacking.enabled = show;
        }
    }

    /// <summary>The resource bar's population cell: amber when the side is at capacity,
    /// red while upkeep is starving the army.</summary>
    void RefreshPopulation()
    {
        if (populationText == null) return;
        var team = PlayerTeam;
        int used = Population.Used(team);
        int capacity = Population.Capacity(team);
        string want = used + "/" + capacity;
        if (populationText.text != want) populationText.text = want;

        var tint = Upkeep.IsStarving(team) ? meatEmptyTint
                 : used >= capacity ? populationFullTint
                 : populationNormalTint;
        if (populationText.color != tint) populationText.color = tint;
    }

    // --------------------------------------------------------------- game over

    void OnGameEnded(GameState.Outcome outcome)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (gameOverText == null) return;
        gameOverText.text = outcome == GameState.Outcome.Win ? "胜利！" : "失败";
        gameOverText.color = outcome == GameState.Outcome.Win
            ? new Color(0.55f, 1f, 0.55f) : new Color(1f, 0.45f, 0.45f);
    }
}
