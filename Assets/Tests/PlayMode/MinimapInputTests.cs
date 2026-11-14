using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MinimapInputTests : RegressionFixture
{
    Minimap CreateMinimap(out Camera cam, out GameObject pauseGO, out GameObject panel)
    {
        cam = Selection.cam;
        var rts = cam.gameObject.AddComponent<RTSCamera>();
        rts.enabled = false;
        rts.autoMapBounds = false;
        rts.useStartPosition = false;
        rts.mapBounds = new Rect(-50f, -50f, 100f, 100f);
        cam.transform.position = new Vector3(10f, 10f, -10f);

        var go = new GameObject("Minimap under test", typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(200f, 100f);
        rt.position = Vector3.zero;
        var map = go.AddComponent<Minimap>();
        map.mainCamera = cam;
        map.rtsCamera = rts;

        panel = new GameObject("Pause panel");
        panel.SetActive(false);
        pauseGO = new GameObject("Pause owner");
        var pause = pauseGO.AddComponent<PauseMenu>();
        pause.panel = panel;
        return map;
    }

    static PointerEventData CentreClick()
    {
        return new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = Vector2.zero,
        };
    }

    [Test]
    public void Click_NormalMatch_MovesCamera()
    {
        var map = CreateMinimap(out var cam, out _, out _);
        map.OnPointerClick(CentreClick());
        Assert.AreNotEqual(new Vector3(10f, 10f, -10f), cam.transform.position);
    }

    [Test]
    public void Click_Paused_DoesNotMoveCamera()
    {
        var map = CreateMinimap(out var cam, out _, out _);
        var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
        pause.SetOpen(true);
        Vector3 before = cam.transform.position;
        map.OnPointerClick(CentreClick());
        Assert.AreEqual(before, cam.transform.position);
        pause.SetOpen(false);
    }

    [Test]
    public void Click_GameOver_DoesNotMoveCamera()
    {
        var map = CreateMinimap(out var cam, out _, out _);
        GameState.Instance.End(GameState.Outcome.Lose);
        Vector3 before = cam.transform.position;
        map.OnPointerClick(CentreClick());
        Assert.AreEqual(before, cam.transform.position);
    }
}
