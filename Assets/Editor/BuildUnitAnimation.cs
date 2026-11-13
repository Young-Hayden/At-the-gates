using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Rebuilds every combat unit's clips and animator.
///
/// Why this exists: the first pass at these clips bound the sprite track to path "",
/// i.e. the root object — but the SpriteRenderer sits on a child called "Sprite". A clip
/// whose binding matches nothing plays happily and changes nothing, so Warrior, Archer,
/// Lancer and Monk were all frozen sprites. Attack looked "missing" simply because that
/// is when you notice.
///
/// The binding path is now derived from the prefab at build time
/// (<see cref="AnimationUtility.CalculateTransformPath"/>), so it cannot drift again.
///
/// Controllers get Any-State transitions into their action states: a swing has to
/// interrupt Run as well as Idle, and there is no "Run -> Attack" edge to forget.
/// </summary>
public static class BuildUnitAnimation
{
    const float Fps = 12f;

    // Lancer attacks are per-direction; index order must match CombatAI.DirIndex.
    static readonly string[] LancerDirs = { "Right", "UpRight", "Up", "DownRight", "Down" };

    static readonly string[] ActionColours = { "Blue", "Red" };                       // Warrior / Archer
    static readonly string[] EliteColours = { "Blue", "Red", "Purple", "Yellow", "Black" };  // Lancer / Monk

    [MenuItem("Tools/动画/重建战斗单位动画")]
    public static void BuildAll()
    {
        var sb = new StringBuilder();

        foreach (var c in ActionColours)
        {
            BuildSimple(c, "Warrior", "Warrior_Idle", "Warrior_Attack1", "Attack", sb);
            BuildSimple(c, "Archer", "Archer_Idle", "Archer_Shoot", "Attack", sb);
        }
        foreach (var c in EliteColours)
        {
            BuildLancer(c, sb);
            BuildMonk(c, sb);
        }

        CleanOrphanPawnClips(sb);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[战斗单位动画] 完成\n" + sb);
    }

    // ------------------------------------------------------------------ builders

    /// <summary>Warrior / Archer: Idle + Run on Speed, one action state on a trigger.</summary>
    static void BuildSimple(string colour, string unit, string idleSheet, string actSheet, string actState, StringBuilder sb)
    {
        string art = "Assets/Art/Units/" + colour + "/" + unit;
        string outDir = "Assets/Animation/Units/" + colour + "/" + unit;
        EnsureFolder(outDir);
        string spritePath = SpritePathOf(unit + "_" + colour);

        var idle = MakeClip(Sheet(art + "/" + idleSheet + ".png"), outDir + "/" + colour + "_" + unit + "_Idle.anim", spritePath, true);
        var run = MakeClip(Sheet(art + "/" + unit + "_Run.png"), outDir + "/" + colour + "_" + unit + "_Run.anim", spritePath, true);
        var act = MakeClip(Sheet(art + "/" + actSheet + ".png"), outDir + "/" + colour + "_" + unit + "_" + actState + ".anim", spritePath, false);

        if (idle == null || run == null || act == null)
        { sb.AppendLine($"  {unit}_{colour} 缺素材，跳过"); return; }

        string ctrlPath = outDir + "/" + unit + "_Controller.controller";
        var ctrl = FreshController(ctrlPath, sb);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;
        var sIdle = State(sm, "Idle", idle, new Vector3(260f, 40f));
        var sRun = State(sm, "Run", run, new Vector3(520f, 40f));
        var sAct = State(sm, actState, act, new Vector3(260f, 190f));
        sm.defaultState = sIdle;

        // action first: it must be able to interrupt Run as well as Idle
        var t = sm.AddAnyStateTransition(sAct);
        t.hasExitTime = false; t.duration = 0f; t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0f, "Attack");

        var back = sAct.AddTransition(sIdle, false);
        back.hasExitTime = true; back.exitTime = 1f; back.duration = 0.05f;

        Move(sIdle, sRun, AnimatorConditionMode.Greater, 0.1f, "Speed");
        Move(sRun, sIdle, AnimatorConditionMode.Less, 0.1f, "Speed");

        Assign(unit + "_" + colour, ctrl, sb, $"Idle{idle.length:F2}s Run{run.length:F2}s {actState}{act.length:F2}s");
    }

    /// <summary>Lancer: five directional attacks selected by Dir, matching CombatAI.DirIndex.</summary>
    static void BuildLancer(string colour, StringBuilder sb)
    {
        string art = "Assets/Art/Units/" + colour + "/Lancer";
        string outDir = "Assets/Animation/Units/" + colour + "/Lancer";
        EnsureFolder(outDir);
        string spritePath = SpritePathOf("Lancer_" + colour);

        var idle = MakeClip(Frames(art, "Lancer_Idle"), outDir + "/" + colour + "_Lancer_Idle.anim", spritePath, true);
        var run = MakeClip(Frames(art, "Lancer_Run"), outDir + "/" + colour + "_Lancer_Run.anim", spritePath, true);
        if (idle == null || run == null) { sb.AppendLine($"  Lancer_{colour} 缺 Idle/Run，跳过"); return; }

        var acts = new AnimationClip[LancerDirs.Length];
        for (int i = 0; i < LancerDirs.Length; i++)
            acts[i] = MakeClip(Frames(art, "Lancer_" + LancerDirs[i] + "_Attack"),
                               outDir + "/" + colour + "_Lancer_Attack_" + LancerDirs[i] + ".anim", spritePath, false);
        if (acts.Any(a => a == null)) { sb.AppendLine($"  Lancer_{colour} 缺攻击帧，跳过"); return; }

        string ctrlPath = outDir + "/Lancer_Controller.controller";
        var ctrl = FreshController(ctrlPath, sb);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Dir", AnimatorControllerParameterType.Int);

        var sm = ctrl.layers[0].stateMachine;
        var sIdle = State(sm, "Idle", idle, new Vector3(300f, 40f));
        var sRun = State(sm, "Run", run, new Vector3(560f, 40f));
        sm.defaultState = sIdle;

        var dirStates = new AnimatorState[LancerDirs.Length];
        for (int i = 0; i < LancerDirs.Length; i++)
        {
            var st = State(sm, "Attack" + LancerDirs[i], acts[i], new Vector3(60f, 240f + i * 70f));
            dirStates[i] = st;
            var t = sm.AddAnyStateTransition(st);
            t.hasExitTime = false; t.duration = 0f; t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            t.AddCondition(AnimatorConditionMode.Equals, i, "Dir");

            var back = st.AddTransition(sIdle, false);
            back.hasExitTime = true; back.exitTime = 1f; back.duration = 0.05f;
        }

        Move(sIdle, sRun, AnimatorConditionMode.Greater, 0.1f, "Speed");
        Move(sRun, sIdle, AnimatorConditionMode.Less, 0.1f, "Speed");

        Assign("Lancer_" + colour, ctrl, sb, $"Idle{idle.length:F2}s Run{run.length:F2}s 攻击{acts[0].length:F2}s×{acts.Length}方向");
    }

    /// <summary>Monk: Idle + Run on Speed, Heal on a trigger.</summary>
    static void BuildMonk(string colour, StringBuilder sb)
    {
        string art = "Assets/Art/Units/" + colour + "/Monk";
        string outDir = "Assets/Animation/Units/" + colour + "/Monk";
        EnsureFolder(outDir);
        string spritePath = SpritePathOf("Monk_" + colour);

        var idle = MakeClip(Frames(art, "Idle"), outDir + "/" + colour + "_Monk_Idle.anim", spritePath, true);
        var run = MakeClip(Frames(art, "Run"), outDir + "/" + colour + "_Monk_Run.anim", spritePath, true);
        var heal = MakeClip(Frames(art, "Heal"), outDir + "/" + colour + "_Monk_Heal.anim", spritePath, false);
        if (idle == null || run == null || heal == null) { sb.AppendLine($"  Monk_{colour} 缺素材，跳过"); return; }

        string ctrlPath = outDir + "/Monk_Controller.controller";
        var ctrl = FreshController(ctrlPath, sb);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Heal", AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;
        var sIdle = State(sm, "Idle", idle, new Vector3(260f, 40f));
        var sRun = State(sm, "Run", run, new Vector3(520f, 40f));
        var sHeal = State(sm, "Heal", heal, new Vector3(260f, 190f));
        sm.defaultState = sIdle;

        var t = sm.AddAnyStateTransition(sHeal);
        t.hasExitTime = false; t.duration = 0f; t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0f, "Heal");

        var back = sHeal.AddTransition(sIdle, false);
        back.hasExitTime = true; back.exitTime = 1f; back.duration = 0.05f;

        Move(sIdle, sRun, AnimatorConditionMode.Greater, 0.1f, "Speed");
        Move(sRun, sIdle, AnimatorConditionMode.Less, 0.1f, "Speed");

        Assign("Monk_" + colour, ctrl, sb, $"Idle{idle.length:F2}s Run{run.length:F2}s Heal{heal.length:F2}s");
    }

    /// <summary>The first pass left pa0n clips bound to the root; the tool-aware pawn
    /// controller does not use them any more.</summary>
    static void CleanOrphanPawnClips(StringBuilder sb)
    {
        int n = 0;
        foreach (var c in new[] { "Blue", "Red" })
        foreach (var name in new[] { "_Pawn_Idle", "_Pawn_Run" })
        {
            string p = "Assets/Animation/Units/" + c + "/Pawn/" + c + name + ".anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(p) != null) { AssetDatabase.DeleteAsset(p); n++; }
        }
        if (n > 0) sb.AppendLine($"  清掉 {n} 个绑错路径的旧 Pawn 片段");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Gets the controller at a path, wiping it clean if it already exists.
    ///
    /// Deliberately NOT delete-and-recreate: deleting the asset and creating a new one
    /// at the same path gives it a new GUID, and every prefab that pointed at the old
    /// one ends up with a dangling reference that reads back as null — which is exactly
    /// how Warrior and Archer lost their animators. Editing in place keeps the GUID.
    /// </summary>
    static AnimatorController FreshController(string path, StringBuilder sb)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (ctrl == null)
            return AnimatorController.CreateAnimatorControllerAtPath(path);

        var sm = ctrl.layers[0].stateMachine;
        foreach (var t in sm.anyStateTransitions.ToArray()) sm.RemoveAnyStateTransition(t);
        foreach (var t in sm.entryTransitions.ToArray()) sm.RemoveEntryTransition(t);
        foreach (var cs in sm.states.ToArray()) sm.RemoveState(cs.state);
        while (ctrl.parameters.Length > 0) ctrl.RemoveParameter(ctrl.parameters[0]);
        // sub state machines too, if a previous run left any
        foreach (var sub in sm.stateMachines.ToArray()) sm.RemoveStateMachine(sub.stateMachine);

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

    /// <summary>Points the prefab at its controller and reads the reference back off disk,
    /// so a build that silently fails to stick is reported instead of assumed.</summary>
    static void Assign(string prefabUnitName, AnimatorController ctrl, StringBuilder sb, string summary)
    {
        string path = "Assets/Prefabs/Units/" + prefabUnitName + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        if (root == null) { sb.AppendLine($"  {prefabUnitName}: 预制体读不到"); return; }

        var anim = root.GetComponentInChildren<Animator>(true);
        if (anim == null) { PrefabUtility.UnloadPrefabContents(root); sb.AppendLine($"  {prefabUnitName}: 找不到 Animator"); return; }
        anim.runtimeAnimatorController = ctrl;
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);

        var check = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                                  ?.GetComponentInChildren<Animator>(true)?.runtimeAnimatorController;
        string verdict = check == null ? "** 引用没写进预制体 **" : "✓";
        sb.AppendLine($"  {prefabUnitName,-16} {summary}  {verdict}");
    }

    /// <summary>The transform path the clip has to bind to.
    ///
    /// The Animator resolves a binding path against ITS OWN GameObject, not the prefab
    /// root. Our prefabs put the Animator and the SpriteRenderer on the same "Sprite"
    /// child, so the correct path is "". We measured with the wrong root and wrote
    /// "Sprite", which is why every combat unit played a frozen first frame: the track
    /// matched nothing. Measured from the animator now, so it cannot drift again.</summary>
    static string SpritePathOf(string prefabUnitName)
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Units/" + prefabUnitName + ".prefab");
        if (p == null) return "";
        var anim = p.GetComponentInChildren<Animator>(true);
        var sr = p.GetComponentInChildren<SpriteRenderer>(true);
        if (anim == null || sr == null) return "";
        return AnimationUtility.CalculateTransformPath(sr.transform, anim.transform);
    }

    static void EnsureFolder(string path)
    {
        if (Directory.Exists(Path.GetFullPath(path))) return;
        Directory.CreateDirectory(Path.GetFullPath(path));
        AssetDatabase.Refresh();
    }

    static Sprite[] Sheet(string png)
    {
        if (!File.Exists(Path.GetFullPath(png))) return null;
        var all = AssetDatabase.LoadAllAssetsAtPath(png).OfType<Sprite>().ToArray();
        return all.Length == 0 ? null : all.OrderBy(TrailingIndex).ToArray();
    }

    /// <summary>All sprites for a frame sequence: the loose "prefix_N.png" frames if that
    /// folder was split by hand, otherwise the "prefix.png" sheet as a whole.</summary>
    static Sprite[] Frames(string folder, string prefix)
    {
        string abs = Path.GetFullPath(folder);
        if (!Directory.Exists(abs)) return null;
        var list = new List<(int, Sprite)>();
        foreach (var f in Directory.GetFiles(abs, prefix + "_*.png"))
        {
            string stem = Path.GetFileNameWithoutExtension(f);
            if (!int.TryParse(stem.Substring(prefix.Length + 1), out int n)) continue;
            var s = AssetDatabase.LoadAllAssetsAtPath(folder + "/" + Path.GetFileName(f))
                                 .OfType<Sprite>().FirstOrDefault();
            if (s != null) list.Add((n, s));
        }
        if (list.Count > 0) return list.OrderBy(x => x.Item1).Select(x => x.Item2).ToArray();
        return Sheet(folder + "/" + prefix + ".png");
    }

    static int TrailingIndex(Sprite s)
    {
        int i = s.name.LastIndexOf('_');
        return i >= 0 && int.TryParse(s.name.Substring(i + 1), out int v) ? v : 0;
    }

    static AnimationClip MakeClip(Sprite[] sprites, string outPath, string spritePath, bool loop)
    {
        if (sprites == null || sprites.Length == 0) return null;

        var clip = new AnimationClip { frameRate = Fps };
        var binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = spritePath,
            propertyName = "m_Sprite",
        };
        var keys = new ObjectReferenceKeyframe[sprites.Length];
        for (int i = 0; i < sprites.Length; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / Fps, value = sprites[i] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.DeleteAsset(outPath);
        AssetDatabase.CreateAsset(clip, outPath);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
    }
}
