using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Phase A Task 7 — the Repair command button exists only in SampleScene, and until
/// this file nothing in the suite referenced <see cref="HUD.repairButton"/>. The HUD
/// rebuild tool (Tools/重建 HUD) recreates the whole canvas from scratch, so a run of
/// it silently deletes the button, leaves HUD.repairButton null, and the repair
/// feature disappears from the game while every other test stays green.
///
/// This test loads the shipped scene and fails loudly when that button is missing,
/// mis-wired, duplicated, moved onto the Demolish slot, visible before anything is
/// selected, left without a click handler, or left blank.
/// </summary>
public class RepairButtonSceneTests : RegressionFixture
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";
    const string RepairName = "Repair";
    const string DemolishName = "Demolish";
    const string LabelName = "Label";
    const string RepairCaption = "维修";

    // Phase B Task 5 technology strip.
    const string TechStripName = "TechStrip";
    const string HouseBluePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";
    const string CastleBluePath = "Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab";
    static readonly string[] TechButtonNames = { "Tech_Forge", "Tech_Armour", "Tech_Gather" };
    static readonly string[] TechCaptions = { "锻造", "铠甲", "采集" };
    static readonly string[] TechCosts = { "250金100木", "250金100木", "200金" };
    static readonly string[] ExistingChildNames = { "Title", "Body", "Demolish", "Attack", "Repair" };

    const float Tolerance = 0.01f;
    static readonly Vector2 ExpectedPosition = new Vector2(14f, 10f);
    static readonly Vector2 ExpectedSize = new Vector2(84f, 34f);

    [UnityTest]
    public IEnumerator SelectionPanel_HasWiredHiddenRepairButton()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null; // the scene is loaded and HUD.Start has run

        var hud = Object.FindFirstObjectByType<HUD>();
        Assert.IsNotNull(hud,
            "SampleScene contains no HUD; the HUD canvas may have been wiped or failed to rebuild.");

        var selectionPanel = hud.selectionPanel;
        Assert.IsNotNull(selectionPanel,
            "HUD.selectionPanel is not wired in SampleScene, so the Repair button cannot exist.");
        var panel = selectionPanel.GetComponent<RectTransform>();
        Assert.IsNotNull(panel, "HUD.selectionPanel has no RectTransform.");

        // Gather first, assert after: a wiped HUD then reports every missing piece in
        // one run instead of stopping at the first null.
        Button wiredButton = hud.repairButton;
        RectTransform repair = panel.Find(RepairName) as RectTransform;
        RectTransform demolish = panel.Find(DemolishName) as RectTransform;

        int repairNamedChildren = 0;
        for (int i = 0; i < panel.childCount; i++)
            if (panel.GetChild(i).name == RepairName) repairNamedChildren++;

        Button button = repair != null ? repair.GetComponent<Button>() : null;
        Image image = repair != null ? repair.GetComponent<Image>() : null;
        Transform labelTransform = repair != null ? repair.Find(LabelName) : null;
        Text label = labelTransform != null ? labelTransform.GetComponent<Text>() : null;

        string wiredName = wiredButton != null ? wiredButton.name : "<not wired>";
        Transform wiredParent = wiredButton != null ? wiredButton.transform.parent : null;

        Vector2 position = repair != null
            ? repair.anchoredPosition
            : new Vector2(float.NaN, float.NaN);
        Vector2 size = repair != null
            ? repair.sizeDelta
            : new Vector2(float.NaN, float.NaN);

        // Panel-space rects from the transforms themselves, so anchors/pivots cannot
        // flatter the collision check. Inactive RectTransforms still lay out normally.
        Rect repairRect = repair != null
            ? PanelRect(repair, panel)
            : Rect.MinMaxRect(float.NaN, float.NaN, float.NaN, float.NaN);
        Rect demolishRect = demolish != null
            ? PanelRect(demolish, panel)
            : Rect.MinMaxRect(float.NaN, float.NaN, float.NaN, float.NaN);

        int listenerCount = RuntimeListenerCount(button != null ? button.onClick : null);

        // Unity's bundled NUnit predates Assert.Multiple, so collect each group's
        // assertion failure by hand and report them together. This keeps a single
        // rebuild-induced break from hiding the other six checks behind the first.
        var failures = new List<string>();
        void Check(string label, TestDelegate assertion)
        {
            try { assertion(); }
            catch (AssertionException e) { failures.Add(label + ": " + e.Message); }
        }

        // 1. The core regression guard: the button must exist and be wired.
        Check("1. Repair button exists", () => Assert.IsNotNull(wiredButton,
            "HUD.repairButton is null: the Repair button is missing from SampleScene. "
            + "This is what a HUD rebuild (Tools/重建 HUD wipes the canvas and recreates it) "
            + "leaves behind, silently removing the repair feature from the game."));

        // 2. The wired reference must be the Repair object under SelectionPanel.
        Check("2. Repair button is the panel's Repair child", () =>
        {
            Assert.AreEqual(RepairName, wiredName,
                "HUD.repairButton must point at an object named 'Repair', but it points at '" + wiredName + "'.");
            Assert.AreSame(selectionPanel.transform, wiredParent,
                "HUD.repairButton must be the Repair child of SelectionPanel, not an object elsewhere in the hierarchy.");
        });

        // 3. No duplicate left behind by a second run of the repair-button builder.
        Check("3. Exactly one Repair child", () => Assert.AreEqual(1, repairNamedChildren,
            "SelectionPanel must have exactly one child named 'Repair', but it has " + repairNamedChildren
            + "; a re-run of the repair-button builder duplicates instead of rewiring."));

        // 4. Geometry, and the free slot left of Demolish.
        Check("4. Geometry and Demolish clearance", () =>
        {
            Assert.IsNotNull(repair, "SelectionPanel has no child named 'Repair', so its geometry cannot be checked.");
            Assert.AreEqual(ExpectedPosition.x, position.x, Tolerance,
                "Repair anchoredPosition.x must be " + ExpectedPosition.x + " (bottom row, left slot).");
            Assert.AreEqual(ExpectedPosition.y, position.y, Tolerance,
                "Repair anchoredPosition.y must be " + ExpectedPosition.y + ".");
            Assert.AreEqual(ExpectedSize.x, size.x, Tolerance,
                "Repair sizeDelta.x must be " + ExpectedSize.x + ".");
            Assert.AreEqual(ExpectedSize.y, size.y, Tolerance,
                "Repair sizeDelta.y must be " + ExpectedSize.y + ".");
            Assert.IsNotNull(demolish,
                "SelectionPanel has no 'Demolish' child, so the Repair slot cannot be proven clear of it.");
            Assert.LessOrEqual(repairRect.xMax, demolishRect.xMin,
                "The Repair slot [" + repairRect.xMin.ToString("0.##") + ".." + repairRect.xMax.ToString("0.##")
                + "] overlaps the Demolish slot [" + demolishRect.xMin.ToString("0.##") + ".."
                + demolishRect.xMax.ToString("0.##") + "] (measured from the RectTransforms in panel space).");
        });

        // 5. Hidden until something repairable is selected.
        Check("5. Hidden on load", () =>
        {
            Assert.IsNotNull(repair, "SelectionPanel has no child named 'Repair' to check for visibility.");
            Assert.IsFalse(repair != null && repair.gameObject.activeSelf,
                "The Repair button must be saved inactive and only shown by HUD.RefreshSelection when a "
                + "repairable building is selected; it is active on scene load.");
        });

        // 6. Interactive: enabled Button, an Image with a sprite, and a click handler.
        Check("6. Interactive wiring", () =>
        {
            Assert.IsNotNull(button, "The Repair object has no Button component, so it cannot be clicked.");
            Assert.IsTrue(button != null && button.enabled,
                "The Repair Button component is disabled, so the button can never respond.");
            Assert.IsNotNull(image,
                "The Repair object has no Image, so it would render blank and would not be a click target.");
            Assert.IsNotNull(image != null ? image.sprite : null,
                "The Repair Image has no sprite; the button would be invisible and unclickable.");
            Assert.GreaterOrEqual(listenerCount, 1,
                "The Repair button has no runtime onClick listener after HUD.Start, so clicking it does nothing. "
                + "HUD.Start must add the selection-manager repair handler.");
        });

        // 7. Half-built guard: the caption must be there.
        Check("7. Label caption", () =>
        {
            Assert.IsNotNull(label, "Repair has no child named 'Label', so the button would render without a caption.");
            Assert.AreEqual(RepairCaption, label != null ? label.text : null,
                "The Repair button's Label must read '" + RepairCaption + "'.");
        });

        if (failures.Count > 0)
            Assert.Fail(failures.Count + " Repair-button scene check(s) failed:\n- "
                + string.Join("\n- ", failures));
    }

    /// <summary>
    /// Phase B Task 5 — the technology strip lives in the same panel. The HUD rebuild
    /// tool wipes the canvas, so the strip can vanish the same way the Repair button can;
    /// the geometry is the part that cannot be seen (PlayMode screenshots are blocked in
    /// this project), so the rects are measured from the RectTransforms themselves.
    /// </summary>
    [UnityTest]
    public IEnumerator SelectionPanel_HasHiddenWiredTechStrip()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        var hud = Object.FindFirstObjectByType<HUD>();
        Assert.IsNotNull(hud, "SampleScene contains no HUD.");
        var selectionPanel = hud.selectionPanel;
        Assert.IsNotNull(selectionPanel, "HUD.selectionPanel is not wired in SampleScene.");
        var panel = selectionPanel.GetComponent<RectTransform>();
        Assert.IsNotNull(panel, "HUD.selectionPanel has no RectTransform.");

        RectTransform strip = panel.Find(TechStripName) as RectTransform;
        int stripCount = 0;
        for (int i = 0; i < panel.childCount; i++)
            if (panel.GetChild(i).name == TechStripName) stripCount++;

        var wiredButtons = hud.techButtons;
        var wiredCosts = hud.techCosts;
        Text progress = hud.techProgress;

        var stripButtons = new RectTransform[TechButtonNames.Length];
        var stripLabels = new Text[TechButtonNames.Length];
        var stripCosts = new Text[TechButtonNames.Length];
        var stripRects = new Rect[TechButtonNames.Length];
        for (int i = 0; i < TechButtonNames.Length; i++)
        {
            stripButtons[i] = strip != null ? strip.Find(TechButtonNames[i]) as RectTransform : null;
            if (stripButtons[i] != null)
            {
                stripRects[i] = PanelSpace(stripButtons[i], panel);
                var label = stripButtons[i].Find("Label");
                var cost = stripButtons[i].Find("Cost");
                stripLabels[i] = label != null ? label.GetComponent<Text>() : null;
                stripCosts[i] = cost != null ? cost.GetComponent<Text>() : null;
            }
        }
        var progressRt = strip != null ? strip.Find("Progress") as RectTransform : null;

        Rect panelRect = new Rect(0f, 0f, panel.rect.width, panel.rect.height);
        Rect stripRect = strip != null ? PanelSpace(strip, panel) : new Rect(float.NaN, float.NaN, 0f, 0f);
        Rect progressRect = progressRt != null ? PanelSpace(progressRt, panel) : new Rect(float.NaN, float.NaN, 0f, 0f);

        var existing = new Rect[ExistingChildNames.Length];
        for (int i = 0; i < ExistingChildNames.Length; i++)
        {
            var rt = panel.Find(ExistingChildNames[i]) as RectTransform;
            existing[i] = rt != null ? PanelSpace(rt, panel) : new Rect(float.NaN, float.NaN, 0f, 0f);
        }

        bool stripHiddenOnLoad = strip != null && !strip.gameObject.activeSelf;

        var failures = new List<string>();
        void Check(string label, TestDelegate assertion)
        {
            try { assertion(); }
            catch (AssertionException e) { failures.Add(label + ": " + e.Message); }
        }

        // 1. Exactly one strip, wired from the HUD.
        Check("1. One wired strip", () =>
        {
            Assert.IsNotNull(strip, "SelectionPanel has no child named '" + TechStripName
                + "'. Run Tools/UI/添加科技条 — a HUD rebuild (Tools/重建 HUD) removes it.");
            Assert.AreEqual(1, stripCount, "SelectionPanel must have exactly one '" + TechStripName
                + "' child, but has " + stripCount + "; a re-run of the strip builder duplicates instead of rewiring.");
            Assert.IsNotNull(hud.techStrip, "HUD.techStrip is null.");
            Assert.AreSame(strip != null ? strip.gameObject : null, hud.techStrip,
                "HUD.techStrip must point at the TechStrip child of SelectionPanel.");
        });

        // 2. Three buttons, one per technology, wired in enum order.
        Check("2. Three wired buttons", () =>
        {
            Assert.IsNotNull(wiredButtons, "HUD.techButtons is null.");
            Assert.AreEqual(TechButtonNames.Length, wiredButtons.Length,
                "HUD.techButtons must hold exactly " + TechButtonNames.Length + " buttons.");
            for (int i = 0; i < TechButtonNames.Length; i++)
            {
                Assert.IsNotNull(stripButtons[i], "The strip has no '" + TechButtonNames[i] + "' child.");
                Assert.AreSame(strip, stripButtons[i].parent,
                    TechButtonNames[i] + " must be a direct child of " + TechStripName + ".");
                Assert.AreSame(stripButtons[i].GetComponent<Button>(), wiredButtons[i],
                    "HUD.techButtons[" + i + "] must be the " + TechButtonNames[i] + " Button.");
                Assert.IsNotNull(stripButtons[i].GetComponent<Button>(), TechButtonNames[i] + " has no Button.");
                Assert.IsNotNull(stripButtons[i].GetComponent<Image>()?.sprite,
                    TechButtonNames[i] + " has no sprite and would be invisible.");
                Assert.IsNotNull(wiredCosts, "HUD.techCosts is null.");
                Assert.AreEqual(TechButtonNames.Length, wiredCosts.Length, "HUD.techCosts must hold three labels.");
                Assert.AreSame(stripCosts[i], wiredCosts[i], "HUD.techCosts[" + i + "] is not the " + TechButtonNames[i] + " cost label.");
            }
            Assert.IsNotNull(progressRt, "The strip has no 'Progress' label.");
            Assert.AreSame(progressRt.GetComponent<Text>(), progress, "HUD.techProgress is not the strip's Progress label.");
        });

        // 3. Captions and prices read from the scene.
        Check("3. Captions and costs", () =>
        {
            for (int i = 0; i < TechButtonNames.Length; i++)
            {
                Assert.AreEqual(TechCaptions[i], stripLabels[i] != null ? stripLabels[i].text : null,
                    TechButtonNames[i] + " must be captioned " + TechCaptions[i] + ".");
                Assert.AreEqual(TechCosts[i], stripCosts[i] != null ? stripCosts[i].text : null,
                    TechButtonNames[i] + " must show its cost " + TechCosts[i] + ".");
            }
        });

        // 4. Hidden until a friendly finished command centre is selected.
        Check("4. Hidden on load", () =>
        {
            Assert.IsTrue(stripHiddenOnLoad,
                "The tech strip must be saved inactive and shown only while a friendly completed "
                + "command centre is selected; it is active on scene load.");
        });

        // 5. Geometry: every new rect inside the panel and clear of all five existing children.
        Check("5. Geometry", () =>
        {
            Assert.IsNotNull(strip, "No strip to measure.");
            for (int i = 0; i < ExistingChildNames.Length; i++)
                Assert.IsNotNull(panel.Find(ExistingChildNames[i]),
                    "SelectionPanel has no '" + ExistingChildNames[i] + "' child to test the strip against.");
            var newRects = new List<KeyValuePair<string, Rect>>
            {
                new KeyValuePair<string, Rect>(TechStripName, stripRect),
                new KeyValuePair<string, Rect>("Progress", progressRect)
            };
            for (int i = 0; i < TechButtonNames.Length; i++)
                newRects.Add(new KeyValuePair<string, Rect>(TechButtonNames[i], stripRects[i]));

            foreach (var pair in newRects)
            {
                Assert.IsTrue(panelRect.Contains(pair.Value.min) && panelRect.Contains(pair.Value.max),
                    pair.Key + " [" + R(pair.Value) + "] is not inside the panel [" + R(panelRect) + "].");
                for (int i = 0; i < ExistingChildNames.Length; i++)
                    Assert.IsFalse(pair.Value.Overlaps(existing[i]),
                        pair.Key + " [" + R(pair.Value) + "] intersects the existing "
                        + ExistingChildNames[i] + " [" + R(existing[i]) + "].");
            }
        });

        // 6. Behaviour: only a friendly command centre shows it, and a click starts research.
        var housePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HouseBluePath);
        var castlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CastleBluePath);
        Assert.IsNotNull(housePrefab, "Missing " + HouseBluePath);
        Assert.IsNotNull(castlePrefab, "Missing " + CastleBluePath);
        var selection = Object.FindFirstObjectByType<SelectionManager>();
        Assert.IsNotNull(selection, "SampleScene has no SelectionManager.");

        var house = Object.Instantiate(housePrefab);
        selection.selectedBuilding = house.GetComponent<Building>();
        yield return null;
        bool stripShownForHouse = strip != null && strip.gameObject.activeSelf;
        bool stripShownForHouseOk = !stripShownForHouse;
        Object.Destroy(house);
        selection.selectedBuilding = null;
        yield return null;

        var castle = Object.Instantiate(castlePrefab);
        selection.selectedBuilding = castle.GetComponent<Building>();
        yield return null;
        bool stripShownForCastle = strip != null && strip.gameObject.activeSelf;
        var economy = Economy.Instance;
        int goldBefore = economy != null ? economy.gold : -1;
        int woodBefore = economy != null ? economy.wood : -1;
        bool clickableBeforeClick = wiredButtons != null && wiredButtons.Length == 3 && wiredButtons[0].interactable;
        if (clickableBeforeClick) wiredButtons[0].onClick.Invoke();
        yield return null;
        string progressText = progress != null ? progress.text : null;
        int goldAfter = economy != null ? economy.gold : -1;
        int woodAfter = economy != null ? economy.wood : -1;
        bool otherDisabledWhileRunning = wiredButtons != null && wiredButtons.Length == 3
            && !wiredButtons[1].interactable && !wiredButtons[2].interactable;
        var activeTech = Technology.ActiveTechOf(GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue);
        bool researchStarted = activeTech.HasValue && activeTech.Value == Technology.Tech.Forge;

        Check("6. Shown only for a command centre", () => Assert.IsTrue(stripShownForHouseOk,
            "The strip must stay hidden while a friendly non-command-centre building is selected; "
            + "it was shown for a friendly House."));
        Check("7. Shown for a friendly command centre", () => Assert.IsTrue(stripShownForCastle,
            "Selecting a friendly completed command centre must show the strip."));
        Check("8. Clicking starts the technology", () =>
        {
            Assert.IsTrue(clickableBeforeClick, "The 锻造 button should be affordable and clickable at game start.");
            Assert.IsTrue(researchStarted, "Clicking 锻造 must start Forge research.");
            Assert.AreEqual(goldBefore - 250, goldAfter, "锻造 must charge 250 gold.");
            Assert.AreEqual(woodBefore - 100, woodAfter, "锻造 must charge 100 wood.");
        });
        Check("9. Running research disables the others", () =>
        {
            Assert.IsTrue(otherDisabledWhileRunning,
                "While 锻造 is being researched the other two tech buttons must be disabled.");
            Assert.IsNotNull(progressText, "The strip has no progress label.");
            StringAssert.Contains("锻造研究中", progressText ?? "",
                "The progress label must name the running technology and its remaining time.");
        });

        if (failures.Count > 0)
            Assert.Fail(failures.Count + " tech-strip scene check(s) failed:\n- "
                + string.Join("\n- ", failures));
    }

    /// <summary>
    /// Phase B Task 5 layout guard. The technology strip needs a 266 px selection panel, and
    /// growing the panel once pushed its bottom edge 64 px over the always-visible
    /// MinimapPanel, hiding the strip's progress readout behind it. This pins the corrected
    /// layout: the two panels must not overlap, must keep a real gap, and both must sit inside
    /// the canvas. A future growth of the selection panel fails here instead of shipping a
    /// hidden readout.
    /// </summary>
    [UnityTest]
    public IEnumerator SelectionPanel_DoesNotOverlapMinimap_AndBothFitCanvas()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        var hud = Object.FindFirstObjectByType<HUD>();
        Assert.IsNotNull(hud, "SampleScene contains no HUD.");
        var canvas = hud.GetComponentInParent<Canvas>();
        Assert.IsNotNull(canvas, "HUD is not under a Canvas.");
        var canvasRt = canvas.GetComponent<RectTransform>();
        Assert.IsNotNull(canvasRt, "The HUD Canvas has no RectTransform.");

        Assert.IsNotNull(hud.selectionPanel, "HUD.selectionPanel is not wired in SampleScene.");
        var panelRt = hud.selectionPanel.GetComponent<RectTransform>();
        var minimapRt = canvasRt.Find("Root/MinimapPanel") as RectTransform;

        Assert.IsNotNull(panelRt, "SelectionPanel has no RectTransform.");
        Assert.IsNotNull(minimapRt,
            "The HUD Canvas has no Root/MinimapPanel; the layout guard cannot run.");

        Rect canvasRect = canvasRt.rect;
        Rect panelRect = CanvasSpace(panelRt, canvasRt);
        Rect minimapRect = CanvasSpace(minimapRt, canvasRt);

        Assert.IsFalse(panelRect.Overlaps(minimapRect),
            "SelectionPanel [" + R(panelRect) + "] overlaps MinimapPanel [" + R(minimapRect)
            + "]. The strip justified growing the panel to 266 px; growing it again must not push "
            + "it over the minimap — move the minimap or shorten the panel instead.");

        Assert.GreaterOrEqual(panelRect.yMin - minimapRect.yMax, 16f,
            "SelectionPanel [" + R(panelRect) + "] must keep at least 16 px of canvas space above "
            + "MinimapPanel [" + R(minimapRect) + "], but the gap is "
            + (panelRect.yMin - minimapRect.yMax).ToString("0.##") + " px.");

        var rects = new[]
        {
            new KeyValuePair<string, Rect>("SelectionPanel", panelRect),
            new KeyValuePair<string, Rect>("MinimapPanel", minimapRect)
        };
        foreach (var pair in rects)
        {
            Assert.IsTrue(canvasRect.Contains(pair.Value.min) && canvasRect.Contains(pair.Value.max),
                pair.Key + " [" + R(pair.Value) + "] is not fully inside the canvas ["
                + R(canvasRect) + "]; every panel must keep a positive margin on every side.");
        }

        yield return null;
    }

    /// <summary>Child rect in the canvas' own local space, from world corners.</summary>
    static Rect CanvasSpace(RectTransform child, RectTransform canvas)
    {
        var corners = new Vector3[4];
        child.GetWorldCorners(corners);
        Vector2 min = canvas.InverseTransformPoint(corners[0]);
        Vector2 max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = canvas.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>Child rect in panel space with the origin at the panel's bottom-left, the
    /// frame the task's measurements use (Demolish 104..208, Attack 214..310, Repair 14..98).</summary>
    static Rect PanelSpace(RectTransform child, RectTransform panel)
    {
        Rect local = PanelRect(child, panel);
        return new Rect(local.x + panel.rect.width, local.y + panel.rect.height,
                        local.width, local.height);
    }

    static string R(Rect r) =>
        r.xMin.ToString("0.##") + ".." + r.xMax.ToString("0.##") + " x "
        + r.yMin.ToString("0.##") + ".." + r.yMax.ToString("0.##");

    /// <summary>Child rect expressed in the panel's local space, computed from world

    /// corners so anchors and pivots cannot skew the intersection test.</summary>
    static Rect PanelRect(RectTransform child, RectTransform panel)
    {
        var corners = new Vector3[4];
        child.GetWorldCorners(corners);
        Vector2 min = panel.InverseTransformPoint(corners[0]);
        Vector2 max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = panel.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>
    /// onClick listeners added at runtime are invisible to the public API
    /// (GetPersistentEventCount only counts inspector-wired calls), so read the
    /// engine's own InvokableCallList. Verified against this Unity version: a fresh
    /// Button with two AddListener calls reports a runtime count of 2.
    /// </summary>
    static int RuntimeListenerCount(UnityEventBase unityEvent)
    {
        if (unityEvent == null) return 0;
        FieldInfo callsField = typeof(UnityEventBase).GetField(
            "m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
        object calls = callsField != null ? callsField.GetValue(unityEvent) : null;
        FieldInfo runtimeField = calls != null
            ? calls.GetType().GetField("m_RuntimeCalls", BindingFlags.Instance | BindingFlags.NonPublic)
            : null;
        var runtimeCalls = runtimeField != null ? runtimeField.GetValue(calls) as IList : null;
        return runtimeCalls != null ? runtimeCalls.Count : 0;
    }
}
