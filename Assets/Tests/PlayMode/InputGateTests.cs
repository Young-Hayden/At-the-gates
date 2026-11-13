using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Regression coverage for routing left-button gestures through the world input gate.
/// </summary>
public class InputGateTests : RegressionFixture
{
    [UnityTest]
    public IEnumerator LeftPress_StartedOnUI_NeverStartsBoxSelection()
    {
        UnitMover mover = CreateSelectableInsideDragRectangle();
        yield return null;

        DriveDragSequence(true);

        Assert.IsEmpty(Selection.selected,
            "A left press that began on HUD must not select a unit after dragging into the world.");
        Assert.IsFalse(Selection.selected.Contains(mover));
    }

    [UnityTest]
    public IEnumerator LeftPress_StartedInWorld_BoxSelectsUnitAfterDrag()
    {
        UnitMover mover = CreateSelectableInsideDragRectangle();
        yield return null;

        DriveDragSequence(false);

        Assert.IsTrue(Selection.selected.Contains(mover),
            "The world-origin positive control must select the unit inside the drag rectangle.");
        Assert.AreEqual(1, Selection.selected.Count);
    }

    [Test]
    public void Camera_UIAndModalInput_DoNotMoveOrZoom()
    {
        var go = CreateCameraRig(out var cam, out var rts);
        try
        {
            Vector3 before = go.transform.position;
            float zoom = cam.orthographicSize;

            Vector3 after = HandleCameraPointer(rts, before, new Vector2(100f, 100f),
                new Vector2(0f, 2f), true, false, true, false);
            Assert.AreEqual(before, after, "HUD pointer input must not move the camera.");
            Assert.AreEqual(zoom, cam.orthographicSize, "HUD wheel input must not zoom the camera.");

            after = HandleCameraPointer(rts, before, new Vector2(100f, 100f),
                new Vector2(0f, 2f), true, false, false, true);
            Assert.AreEqual(before, after, "Modal pointer input must not move the camera.");
            Assert.AreEqual(zoom, cam.orthographicSize, "Modal wheel input must not zoom the camera.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Camera_WorldScroll_StillZooms()
    {
        var go = CreateCameraRig(out var cam, out var rts);
        try
        {
            float zoom = cam.orthographicSize;
            HandleCameraPointer(rts, go.transform.position, new Vector2(100f, 100f),
                new Vector2(0f, 2f), false, false, false, false);

            Assert.Less(cam.orthographicSize, zoom,
                "The ordinary world-scroll positive control must still zoom.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Camera_MiddleDownOnUI_DoesNotStartDrag()
    {
        var go = CreateCameraRig(out var cam, out var rts);
        try
        {
            Vector3 before = go.transform.position;
            HandleCameraPointer(rts, before, new Vector2(100f, 100f),
                Vector2.zero, true, false, true, false);
            Vector3 after = HandleCameraPointer(rts, before, new Vector2(160f, 140f),
                Vector2.zero, false, false, false, false);

            Assert.AreEqual(before, after,
                "A middle press that began on HUD must not become a world drag later.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Camera_WorldStartedDrag_ContinuesOverUIUntilRelease()
    {
        var go = CreateCameraRig(out var cam, out var rts);
        try
        {
            Vector3 before = go.transform.position;
            HandleCameraPointer(rts, before, new Vector2(100f, 100f),
                Vector2.zero, true, false, false, false);
            Vector3 dragged = HandleCameraPointer(rts, before, new Vector2(160f, 140f),
                Vector2.zero, false, false, true, false);
            Assert.AreNotEqual(before, dragged,
                "Entering HUD must not cancel a drag that started in the world.");

            Vector3 released = HandleCameraPointer(rts, dragged, new Vector2(180f, 160f),
                Vector2.zero, false, true, true, false);
            Vector3 afterRelease = HandleCameraPointer(rts, released, new Vector2(220f, 200f),
                Vector2.zero, false, false, false, false);
            Assert.AreEqual(released, afterRelease,
                "Releasing over HUD must still end the world-started drag.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Camera_ModalOpen_CancelsExistingDrag()
    {
        var go = CreateCameraRig(out var cam, out var rts);
        try
        {
            Vector3 before = go.transform.position;
            HandleCameraPointer(rts, before, new Vector2(100f, 100f),
                Vector2.zero, true, false, false, false);
            Vector3 modal = HandleCameraPointer(rts, before, new Vector2(160f, 140f),
                new Vector2(0f, 2f), false, false, false, true);
            Assert.AreEqual(before, modal);

            Vector3 afterModal = HandleCameraPointer(rts, modal, new Vector2(220f, 200f),
                Vector2.zero, false, false, false, false);
            Assert.AreEqual(modal, afterModal,
                "A modal opening must cancel the old drag rather than resume it afterwards.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    GameObject CreateCameraRig(out Camera cam, out RTSCamera rts)
    {
        var go = new GameObject("Camera gate", typeof(Camera), typeof(RTSCamera));
        cam = go.GetComponent<Camera>();
        rts = go.GetComponent<RTSCamera>();
        cam.orthographic = true;
        cam.orthographicSize = 10f;
        go.transform.position = new Vector3(0f, 0f, -10f);
        rts.autoMapBounds = false;
        rts.useStartPosition = false;
        rts.mapBounds = new Rect(-100f, -100f, 200f, 200f);
        rts.minZoom = 1f;
        rts.maxZoom = 40f;
        rts.zoomSpeed = 3f;
        rts.middleDragPan = true;
        rts.enabled = false;
        return go;
    }

    Vector3 HandleCameraPointer(RTSCamera rts, Vector3 position, Vector2 screen,
        Vector2 scroll, bool middleDown, bool middleUp, bool overUI, bool modalOpen)
    {
        var handler = typeof(RTSCamera).GetMethod("HandlePointerInput",
            BindingFlags.Instance | BindingFlags.Public, null,
            new[] { typeof(Vector3), typeof(Vector2), typeof(Vector2),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool) },
            null);
        Assert.IsNotNull(handler,
            "RTSCamera must expose the parameterized pointer production path.");
        return (Vector3)handler.Invoke(rts,
            new object[] { position, screen, scroll, middleDown, middleUp, overUI, modalOpen });
    }

    UnitMover CreateSelectableInsideDragRectangle()
    {
        PaintRect(-4, -4, 12, 12);
        var mover = new GameObject("Selectable").AddComponent<UnitMover>();
        Vector3 world = Selection.cam.ScreenToWorldPoint(new Vector3(60f, 60f, 10f));
        world.z = 0f;
        mover.transform.position = world;
        var actor = mover.gameObject.AddComponent<UnitActor>();
        actor.team = Team.Blue;
        return mover;
    }

    void DriveDragSequence(bool pressStartedOnUI)
    {
        Vector2 pressScreen = new Vector2(20f, 20f);
        Vector2 releaseScreen = new Vector2(100f, 100f);
        Vector3 pressWorld = Selection.cam.ScreenToWorldPoint(pressScreen);
        Vector3 releaseWorld = Selection.cam.ScreenToWorldPoint(releaseScreen);

        HandleLeftMouseInput(pressWorld, pressScreen, true, true, false, pressStartedOnUI);
        HandleLeftMouseInput(releaseWorld, releaseScreen, false, true, false, false);
        HandleLeftMouseInput(releaseWorld, releaseScreen, false, false, true, false);
    }

    void HandleLeftMouseInput(Vector3 world, Vector2 screen,
        bool down, bool held, bool up, bool overUI)
    {
        var handler = typeof(SelectionManager).GetMethod("HandleLeftMouseInput",
            BindingFlags.Instance | BindingFlags.Public, null,
            new[] { typeof(Vector3), typeof(Vector2), typeof(bool), typeof(bool), typeof(bool), typeof(bool) },
            null);
        Assert.IsNotNull(handler,
            "SelectionManager must expose the parameterized left-mouse production path.");
        handler.Invoke(Selection, new object[] { world, screen, down, held, up, overUI });
    }
}
