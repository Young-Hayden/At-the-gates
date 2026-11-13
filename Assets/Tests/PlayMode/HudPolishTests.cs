using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Presentation Task P2 — the HUD refinement shipped by <see cref="PolishHUD"/>.
///
/// These tests load the shipped scene, so they only pass once the tool has been applied and
/// the scene saved. They exist because two of the binding acceptance rules are invisible to
/// every other test in the suite:
///
///  * every button label is centred on both axes and never overflows its button
///    (the game-over Restart label shipped 7 px below centre before the tool fixed it), and
///  * the new readouts — the resource cells, the population cell, the two-line build costs,
///    the 1-6 hotkey badges, the dim overlays and the minimap frame — are actually wired, not
///    merely authored in the scene.
///
/// A rerun of <c>Tools/重建 HUD</c> wipes all of this silently; that is what these assertions
/// are here to catch.
/// </summary>
public class HudPolishTests : RegressionFixture
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";
    const string LabelName = "Label";
    const float Tolerance = 0.5f;

    static IEnumerator LoadScene()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;                      // scene loaded, HUD.Start has run
    }

    static HUD FindHud()
    {
        var hud = Object.FindFirstObjectByType<HUD>();
        Assert.IsNotNull(hud, "SampleScene contains no HUD; the canvas may have been wiped.");
        Assert.IsNotNull(hud.GetComponentInParent<Canvas>(), "The HUD is not under a Canvas.");
        return hud;
    }

    /// <summary>The user's rule, verbatim: centred on both axes and never wider or taller
    /// than the button it sits on.</summary>
    [UnityTest]
    public IEnumerator EveryButtonLabel_IsCentredOnBothAxes_AndFitsItsButton()
    {
        yield return LoadScene();
        Canvas.ForceUpdateCanvases();
        var hud = FindHud();
        var canvas = hud.GetComponentInParent<Canvas>();

        int checkedLabels = 0;
        var buttons = canvas.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            var label = button.transform.Find(LabelName) as RectTransform;
            if (label == null) continue;
            var text = label.GetComponent<Text>();
            if (text == null) continue;
            checkedLabels++;

            Assert.AreEqual(TextAnchor.MiddleCenter, text.alignment,
                "Button label \"" + text.text + "\" on " + button.name + " must be centred; it is "
                + text.alignment + ".");

            var buttonRect = button.GetComponent<RectTransform>();
            Assert.IsNotNull(buttonRect, "Button " + button.name + " has no RectTransform.");

            // The brief's rule is about the BUTTON, not the label's own rect: measuring the
            // label against itself would pass even if the label grew with the button.
            Assert.LessOrEqual(text.preferredWidth, buttonRect.rect.width + Tolerance,
                "Button label \"" + text.text + "\" on " + button.name + " is wider than its button: "
                + text.preferredWidth.ToString("F1") + " > " + buttonRect.rect.width.ToString("F1") + ".");

            Assert.LessOrEqual(text.preferredHeight, buttonRect.rect.height + Tolerance,
                "Button label \"" + text.text + "\" on " + button.name + " is taller than its button: "
                + text.preferredHeight.ToString("F1") + " > " + buttonRect.rect.height.ToString("F1") + ".");

            // The technology buttons deliberately split their face into a name band and a price
            // band, so their label is centred inside its band rather than on the whole button.
            // A split face is a label that does NOT span the full height — testing the anchors
            // against each other instead would be true for every stretched label and silently
            // exempt them all from the centring check below.
            bool splitFace = label.anchorMin.y > 0.01f || label.anchorMax.y < 0.99f;
            if (splitFace)
            {
                var cost = button.transform.Find("Cost") as RectTransform;
                Assert.IsNotNull(cost, "Split-face button " + button.name + " has no Cost band.");
                AssertInside(WorldRect(label), WorldRect(buttonRect), button.name + " label");
                AssertInside(WorldRect(cost), WorldRect(buttonRect), button.name + " price");
                Assert.IsFalse(WorldRect(label).Overlaps(WorldRect(cost)),
                    "The label and the price of " + button.name + " overlap; the split face needs two bands.");
                continue;
            }

            // Icon-and-caption buttons (attack, repair) put the caption in the space to the right
            // of the icon, so centring it on the whole button would push the text over the icon.
            // They are exempt from the centre check, but must still keep both pieces inside the
            // button and clear of each other.
            var icon = button.transform.Find("Icon") as RectTransform;
            if (icon != null)
            {
                AssertInside(WorldRect(label), WorldRect(buttonRect), button.name + " label");
                AssertInside(WorldRect(icon), WorldRect(buttonRect), button.name + " icon");
                Assert.IsFalse(WorldRect(label).Overlaps(WorldRect(icon)),
                    "The label and the icon of " + button.name + " overlap.");
                continue;
            }

            Vector2 offset = CentreOffset(label, buttonRect);
            Assert.LessOrEqual(offset.magnitude, 1f,
                "Button label \"" + text.text + "\" on " + button.name + " is off centre by "
                + offset.ToString("F1") + " px.");
        }

        Assert.GreaterOrEqual(checkedLabels, 16,
            "Only " + checkedLabels + " labelled buttons were found; the HUD is missing its buttons "
            + "(the shipped scene carries 16: demolish/attack/repair, three technology buttons, two "
            + "game-over buttons, five pause-menu buttons and three quit-confirm buttons).");
        yield return null;
    }

    /// <summary>Population moved out of the status line into the resource bar's fourth cell.</summary>
    [UnityTest]
    public IEnumerator PopulationCell_IsWiredAndReplacesTheStatusSuffix()
    {
        yield return LoadScene();
        var hud = FindHud();

        Assert.IsNotNull(hud.populationText,
            "HUD.populationText is not wired; the fourth resource cell is missing.");
        Assert.IsNotNull(hud.statusBacking,
            "HUD.statusBacking is not wired; the status line has no backing strip.");

        var team = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        string expected = Population.Used(team) + "/" + Population.Capacity(team);
        Assert.AreEqual(expected, hud.populationText.text,
            "The population cell must read the side's current usage over its capacity.");

        string status = hud.statusText != null ? hud.statusText.text : "";
        StringAssert.DoesNotContain("人口", status,
            "The population readout must live in the resource bar, not the status line. Status: " + status);
        yield return null;
    }

    /// <summary>
    /// The status line keeps a fallback for a HUD whose fourth cell is not wired (that is how
    /// tests and un-polished scenes still work). Asserting only "the status line has no 人口"
    /// would pass on an empty string whether or not that branch exists, so this drives the
    /// fallback directly: unwire the cell, and the status line must pick the readout up.
    /// </summary>
    [UnityTest]
    public IEnumerator StatusLine_FallsBackToPopulation_WhenTheCellIsUnwired()
    {
        yield return LoadScene();
        var hud = FindHud();
        Assert.IsNotNull(hud.statusText, "The HUD has no status line.");
        Assert.IsNotNull(hud.populationText, "Control: the population cell is wired.");

        var cell = hud.populationText;
        hud.populationText = null;
        yield return null;

        StringAssert.Contains("人口", hud.statusText.text,
            "With no population cell the status line must carry the readout; it reads \""
            + hud.statusText.text + "\".");

        hud.populationText = cell;
        yield return null;
    }

    /// <summary>Two-line gold/wood costs and the 1-6 hotkey badges on the build bar.</summary>
    [UnityTest]
    public IEnumerator BuildBar_HasTwoLineCostsAndNumberedHotkeyBadges()
    {
        yield return LoadScene();
        var hud = FindHud();

        Assert.AreEqual(hud.buildButtons.Length, hud.buildCostTexts.Length,
            "HUD.buildCostTexts must have one entry per build button.");

        for (int i = 0; i < hud.buildButtons.Length; i++)
        {
            var button = hud.buildButtons[i];
            Assert.IsNotNull(button, "Build button " + i + " is missing.");

            var cost = hud.buildCostTexts[i];
            Assert.IsNotNull(cost, "Build button " + i + " has no cost label wired.");
            StringAssert.Contains("金", cost.text, "Build cost " + i + " must name its gold price.");
            StringAssert.Contains("木", cost.text, "Build cost " + i + " must name its wood price.");
            StringAssert.Contains("\n", cost.text, "Build cost " + i + " must use two lines.");

            var badge = button.transform.Find("Hotkey/Text");
            Assert.IsNotNull(badge, "Build button " + i + " has no hotkey badge.");
            var badgeText = badge.GetComponent<Text>();
            Assert.IsNotNull(badgeText, "The hotkey badge on build button " + i + " has no text.");
            Assert.AreEqual((i + 1).ToString(), badgeText.text,
                "The hotkey badge on build button " + i + " must show " + (i + 1) + ".");
        }
        yield return null;
    }

    /// <summary>Unit slots and tech buttons carry a dim overlay that tracks availability.
    ///
    /// The opening state alone is not enough evidence: at match start nothing is built, so every
    /// slot is unavailable and every overlay is on — which is exactly what a deleted HUD branch
    /// would look like too. This drives one slot into the available state and demands its overlay
    /// switches off, so deleting <c>unitDims[i].enabled = !ok</c> turns it red.</summary>
    [UnityTest]
    public IEnumerator UnitSlotsAndTechButtons_HaveDimOverlaysWired()
    {
        yield return LoadScene();
        var hud = FindHud();

        Assert.AreEqual(hud.unitButtons.Length, hud.unitDims.Length,
            "HUD.unitDims must have one entry per unit slot.");
        for (int i = 0; i < hud.unitDims.Length; i++)
        {
            Assert.IsNotNull(hud.unitDims[i], "Unit slot " + i + " has no dim overlay.");
            if (!hud.unitButtons[i].gameObject.activeSelf) continue;
            Assert.AreEqual(!hud.unitButtons[i].interactable, hud.unitDims[i].enabled,
                "Unit slot " + i + " must dim exactly when its button is unavailable.");
        }

        Assert.AreEqual(hud.techButtons.Length, hud.techDims.Length,
            "HUD.techDims must have one entry per technology button.");
        for (int i = 0; i < hud.techDims.Length; i++)
        {
            Assert.IsNotNull(hud.techDims[i], "Technology button " + i + " has no dim overlay.");
            Assert.IsNotNull(hud.techButtons[i].transform.Find("Dim"),
                "Technology button " + i + " is missing its Dim child.");
        }

        // ---- available state: prove the overlay follows the button ----
        int warrior = -1;
        for (int i = 0; i < hud.unitOffers.Length; i++)
        {
            var offer = hud.unitOffers[i];
            if (offer != null && offer.unitPrefab != null && offer.unitPrefab.name.Contains("Warrior"))
            {
                warrior = i;
                break;
            }
        }
        Assert.GreaterOrEqual(warrior, 0, "The shop must offer a warrior for this control.");

        var barracksPrefab = LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab");
        // A world position, not Grid.CellCenter: loading SampleScene destroys the fixture's
        // regression-scene MapGrid, and this test only needs the building to exist and be alive.
        var barracks = Object.Instantiate(barracksPrefab,
            new Vector3(2.5f, 8.5f, 0f), Quaternion.identity);
        var building = barracks.GetComponent<Building>();
        Assert.IsNotNull(building, "Barracks_Blue must carry a Building.");
        building.team = Team.Blue;
        building.Recompute();
        Economy.gold = 2000;
        UnitLimits.Invalidate();
        Population.Invalidate();

        // The shop re-scans factories on a 0.3 s timer, so give it more than one frame.
        float until = Time.time + 0.5f;
        while (Time.time < until) yield return null;

        Assert.IsTrue(hud.unitButtons[warrior].interactable,
            "Control: a finished barracks plus money must make the warrior buyable. Shown: "
            + (hud.unitCosts.Length > warrior && hud.unitCosts[warrior] != null
                ? hud.unitCosts[warrior].text : "?"));
        Assert.IsFalse(hud.unitDims[warrior].enabled,
            "An available slot must not be dimmed; a deleted enable/disable branch would leave it dark.");
        yield return null;
    }

    /// <summary>Minimap: a dark backing behind the map and a 4 px view rectangle.</summary>
    [UnityTest]
    public IEnumerator Minimap_HasBackingAndFourPixelViewRectangle()
    {
        yield return LoadScene();
        var hud = FindHud();
        var canvasRt = hud.GetComponentInParent<Canvas>().GetComponent<RectTransform>();
        var minimap = canvasRt.Find("Root/MinimapPanel") as RectTransform;
        Assert.IsNotNull(minimap, "The HUD Canvas has no Root/MinimapPanel.");

        var backing = minimap.Find("Backing") as RectTransform;
        Assert.IsNotNull(backing, "The minimap has no dark backing.");
        var image = backing.GetComponent<Image>();
        Assert.IsNotNull(image, "The minimap backing has no Image.");
        Assert.Greater(image.color.a, 0.2f,
            "The minimap backing must actually darken the map; its alpha is " + image.color.a + ".");

        var viewport = minimap.Find("View/Viewport");
        Assert.IsNotNull(viewport, "The minimap view rectangle moved; expected View/Viewport.");
        foreach (var pair in new[]
        {
            new KeyValuePair<string, float>("Top", 4f),
            new KeyValuePair<string, float>("Bottom", 4f),
            new KeyValuePair<string, float>("Left", 4f),
            new KeyValuePair<string, float>("Right", 4f),
        })
        {
            var edge = viewport.Find(pair.Key) as RectTransform;
            Assert.IsNotNull(edge, "The minimap view rectangle is missing its " + pair.Key + " edge.");
            float thickness = pair.Key == "Top" || pair.Key == "Bottom"
                ? edge.sizeDelta.y : edge.sizeDelta.x;
            Assert.AreEqual(pair.Value, thickness, 0.01f,
                "The minimap " + pair.Key + " edge must be " + pair.Value + " px thick.");
        }
        yield return null;
    }

    // ------------------------------------------------------------------ helpers

    static Vector2 CentreOffset(RectTransform label, RectTransform button)
    {
        Vector3 labelCentre = WorldCentre(label);
        Vector3 buttonCentre = button.TransformPoint(button.rect.center);
        return (Vector2)button.InverseTransformVector(labelCentre - buttonCentre);
    }

    static Vector2 WorldCentre(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return (corners[0] + corners[2]) * 0.5f;
    }

    static Rect WorldRect(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = corners[0], max = corners[0];
        for (int i = 1; i < corners.Length; i++)
        {
            min = Vector2.Min(min, corners[i]);
            max = Vector2.Max(max, corners[i]);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    static void AssertInside(Rect inner, Rect outer, string what)
    {
        Assert.IsTrue(inner.xMin >= outer.xMin - 0.5f && inner.xMax <= outer.xMax + 0.5f
                   && inner.yMin >= outer.yMin - 0.5f && inner.yMax <= outer.yMax + 0.5f,
            what + " leaves its button: " + inner + " vs " + outer);
    }
}
