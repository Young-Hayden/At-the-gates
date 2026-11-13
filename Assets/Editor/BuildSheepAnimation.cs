using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Text;
using System.IO;
using System.Linq;

/// <summary>
/// Builds the sheep's two clips and controller from the art that already ships with
/// the project, then installs them (plus <see cref="SheepWander"/>) on
/// HappySheep_Idle.prefab.
///
/// The sheets are already Multiple-mode (8 idle frames, 6 bouncing frames, 64 PPU),
/// so they are used as-is — re-slicing here would risk moving the pivots the scene
/// instances were placed against.
///
/// Binding-path trap, burned into this project three times: the track path is relative
/// to the object carrying the Animator. The Animator is put on the same object as the
/// SpriteRenderer, which makes the path "". It is measured from the prefab at build
/// time (<see cref="AnimationUtility.CalculateTransformPath"/>, same idea as
/// BuildUnitAnimation.SpritePathOf) and printed so a wrong path cannot pass silently.
/// </summary>
public static class BuildSheepAnimation
{
    const string ArtDir = "Assets/Art/Resources/Sheep";
    const string OutDir = "Assets/Animations/Sheep";
    const string PrefabPath = "Assets/Prefabs/Resources/HappySheep_Idle.prefab";
    const float IdleFps = 8f;    // 8 frames = 1.0 s graze loop
    const float WalkFps = 10f;   // 6 frames = 0.6 s bounce loop

    [MenuItem("Tools/动画/重建羊动画")]
    public static void Build()
    {
        var sb = new StringBuilder();
        EnsureFolder(OutDir);

        var idleSprites = Sheet(ArtDir + "/HappySheep_Idle.png");
        var walkSprites = Sheet(ArtDir + "/HappySheep_Bouncing.png");
        if (idleSprites == null || idleSprites.Length == 0 ||
            walkSprites == null || walkSprites.Length == 0)
        {
            Debug.LogError("[羊动画] 缺少羊的贴图：" + ArtDir);
            return;
        }

        string bindingPath = ComputeBindingPath();

        var idle = MakeClip(idleSprites, OutDir + "/Sheep_Idle.anim", bindingPath, IdleFps);
        var walk = MakeClip(walkSprites, OutDir + "/Sheep_Walk.anim", bindingPath, WalkFps);

        var ctrl = FreshController(OutDir + "/Sheep_Wander.controller");
        ctrl.AddParameter("Walking", AnimatorControllerParameterType.Bool);

        var sm = ctrl.layers[0].stateMachine;
        var idleState = State(sm, "Idle", idle, new Vector3(260f, 40f));
        var walkState = State(sm, "Walk", walk, new Vector3(520f, 40f));
        sm.defaultState = idleState;

        Move(idleState, walkState, AnimatorConditionMode.If, 0f, "Walking");
        Move(walkState, idleState, AnimatorConditionMode.IfNot, 0f, "Walking");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();

        sb.AppendLine("  绑定路径 = \"" + bindingPath + "\"（Animator 与 SpriteRenderer 同物体时必须为空）");
        sb.AppendLine("  Idle " + idleSprites.Length + "帧 " + idle.length.ToString("F2") + "s   Walk "
                      + walkSprites.Length + "帧 " + walk.length.ToString("F2") + "s");

        ApplyToPrefab(ctrl, sb);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[羊动画] 完成\n" + sb);
    }

    // ------------------------------------------------------------------ prefab

    /// <summary>The path a clip binds relative to the Animator. Measured from the
    /// prefab; when the Animator is not there yet we measure against the object it
    /// will be added to (the SpriteRenderer's), which must also yield "".</summary>
    static string ComputeBindingPath()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return "";
        var sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
        if (sr == null) return "";
        var anim = prefab.GetComponentInChildren<Animator>(true);
        Transform host = anim != null ? anim.transform : sr.transform;
        return AnimationUtility.CalculateTransformPath(sr.transform, host);
    }

    static void ApplyToPrefab(AnimatorController ctrl, StringBuilder sb)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) { sb.AppendLine("  读不到 " + PrefabPath); return; }
        try
        {
            var sr = root.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null) { sb.AppendLine("  羊预制体没有 SpriteRenderer"); return; }

            // Same object as the SpriteRenderer — this is what keeps the path "".
            var animator = sr.GetComponent<Animator>();
            if (animator == null) animator = sr.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;

            if (root.GetComponent<SheepWander>() == null)
                root.AddComponent<SheepWander>();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // Read the result back off disk instead of trusting the write.
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var savedAnimator = saved != null ? saved.GetComponentInChildren<Animator>(true) : null;
        var savedRenderer = saved != null ? saved.GetComponentInChildren<SpriteRenderer>(true) : null;
        var savedWander = saved != null ? saved.GetComponent<SheepWander>() : null;

        bool controllerOk = savedAnimator != null && savedAnimator.runtimeAnimatorController == ctrl;
        bool sameObject = savedAnimator != null && savedRenderer != null &&
                          savedAnimator.gameObject == savedRenderer.gameObject;
        string realisedPath = (savedAnimator != null && savedRenderer != null)
            ? AnimationUtility.CalculateTransformPath(savedRenderer.transform, savedAnimator.transform)
            : "<missing>";

        sb.AppendLine("  控制器 " + (controllerOk ? "✓" : "**没写进预制体**")
                      + "   SheepWander " + (savedWander != null ? "✓" : "**缺失**")
                      + "   Animator≡SpriteRenderer " + (sameObject ? "✓" : "**不同物体**")
                      + "   实际路径 \"" + realisedPath + "\"");
    }

    // ------------------------------------------------------------------ assets

    static AnimationClip MakeClip(Sprite[] sprites, string outPath, string spritePath, float fps)
    {
        var clip = new AnimationClip { frameRate = fps };
        var binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = spritePath,
            propertyName = "m_Sprite",
        };
        var keys = new ObjectReferenceKeyframe[sprites.Length];
        for (int i = 0; i < sprites.Length; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = sprites[i] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.DeleteAsset(outPath);
        AssetDatabase.CreateAsset(clip, outPath);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
    }

    /// <summary>In-place wipe, never delete-and-recreate: a new GUID would leave the
    /// prefab pointing at a controller that reads back as null.</summary>
    static AnimatorController FreshController(string path)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (ctrl == null) return AnimatorController.CreateAnimatorControllerAtPath(path);

        var sm = ctrl.layers[0].stateMachine;
        foreach (var t in sm.anyStateTransitions.ToArray()) sm.RemoveAnyStateTransition(t);
        foreach (var t in sm.entryTransitions.ToArray()) sm.RemoveEntryTransition(t);
        foreach (var cs in sm.states.ToArray()) sm.RemoveState(cs.state);
        foreach (var sub in sm.stateMachines.ToArray()) sm.RemoveStateMachine(sub.stateMachine);
        while (ctrl.parameters.Length > 0) ctrl.RemoveParameter(ctrl.parameters[0]);
        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }

    static void Move(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold, string param)
    {
        var t = from.AddTransition(to, false);
        t.hasExitTime = false;
        t.duration = 0f;
        t.AddCondition(mode, threshold, param);
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, AnimationClip clip, Vector3 pos)
    {
        var st = sm.AddState(name, pos);
        st.motion = clip;
        st.writeDefaultValues = true;
        return st;
    }

    static Sprite[] Sheet(string png)
    {
        if (!File.Exists(Path.GetFullPath(png))) return null;
        var all = AssetDatabase.LoadAllAssetsAtPath(png).OfType<Sprite>().ToArray();
        return all.Length == 0 ? null : all.OrderBy(TrailingIndex).ToArray();
    }

    static int TrailingIndex(Sprite s)
    {
        int i = s.name.LastIndexOf('_');
        return i >= 0 && int.TryParse(s.name.Substring(i + 1), out int v) ? v : 0;
    }

    static void EnsureFolder(string path)
    {
        if (Directory.Exists(Path.GetFullPath(path))) return;
        Directory.CreateDirectory(Path.GetFullPath(path));
        AssetDatabase.Refresh();
    }
}
