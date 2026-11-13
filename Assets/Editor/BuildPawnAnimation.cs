using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Text;
using System.IO;
using System.Linq;

/// <summary>
/// Rebuilds the Pawn's animator with the full tool set.
///
/// Tiny Swords ships the worker twenty times over — a plain set plus Axe / Pickaxe /
/// Knife / Hammer for idle and running, and an Interact set for swinging each tool —
/// plus carry poses for wood, gold and meat. Those all have to become real clips and
/// real states before <see cref="WorkerLook"/> can pick between them.
///
/// Controller shape:
///   Look  (int 0..7)  which Idle_/ Run_ variant — 0 plain, 1 axe, 2 pickaxe, 3 knife,
///                     4 hammer, 5 carrying wood, 6 gold, 7 meat
///   Moving(bool)      walking or standing
///   Act   (int 0..4)  0 = not swinging, otherwise the tool being swung
///
/// Act is an int rather than a trigger on purpose: a trigger plus an Any-State
/// transition back to Idle races the "am I swinging" question, and the chop gets cut off
/// the moment it starts. With Act the script owns the timing and the states just follow.
/// </summary>
public static class BuildPawnAnimation
{
    const float Fps = 10f;

    // Look index -> sheet suffix (index 0 is the plain, empty-handed worker)
    static readonly string[] LookSuffix = { "", " Axe", " Pickaxe", " Knife", " Hammer", " Wood", " Gold", " Meat" };
    // Act index -> Interact sheet suffix (1..4)
    static readonly string[] ActSuffix = { "", "Axe", "Pickaxe", "Knife", "Hammer" };

    [MenuItem("Tools/动画/重建工人动画")]
    public static void BuildAll()
    {
        var sb = new StringBuilder();
        foreach (var colour in new[] { "Blue", "Red" })
            Build(colour, sb);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[工人动画] 完成\n" + sb);
    }

    static void Build(string colour, StringBuilder sb)
    {
        string artDir = "Assets/Art/Units/" + colour + "/Pawn";
        string outDir = "Assets/Animation/Units/" + colour + "/Pawn";
        if (!Directory.Exists(Path.GetFullPath(outDir)))
        {
            Directory.CreateDirectory(Path.GetFullPath(outDir));
            AssetDatabase.Refresh();
        }

        // The Animator resolves a binding against its own GameObject, not the prefab
        // root — our prefabs keep Animator and SpriteRenderer together on the "Sprite"
        // child, so the path the clips need is "".
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Units/Pawn_" + colour + ".prefab");
        var animator = prefab != null ? prefab.GetComponentInChildren<Animator>(true) : null;
        var sr = prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>(true) : null;
        string spritePath = (animator != null && sr != null)
            ? AnimationUtility.CalculateTransformPath(sr.transform, animator.transform)
            : "";

        var idle = new AnimationClip[LookSuffix.Length];
        var run = new AnimationClip[LookSuffix.Length];
        var act = new AnimationClip[ActSuffix.Length];
        int made = 0, missing = 0;

        for (int k = 0; k < LookSuffix.Length; k++)
        {
            idle[k] = MakeClip(artDir + "/Pawn_Idle" + LookSuffix[k] + ".png",
                               outDir + "/" + colour + "_Pawn_Idle" + k + ".anim", spritePath, loop: true);
            run[k] = MakeClip(artDir + "/Pawn_Run" + LookSuffix[k] + ".png",
                              outDir + "/" + colour + "_Pawn_Run" + k + ".anim", spritePath, loop: true);
            if (idle[k] != null) made++; else { missing++; sb.AppendLine($"  缺 Idle{LookSuffix[k]}"); }
            if (run[k] != null) made++; else { missing++; sb.AppendLine($"  缺 Run{LookSuffix[k]}"); }
        }
        for (int k = 1; k < ActSuffix.Length; k++)
        {
            act[k] = MakeClip(artDir + "/Pawn_Interact " + ActSuffix[k] + ".png",
                              outDir + "/" + colour + "_Pawn_Act" + k + ".anim", spritePath, loop: false);
            if (act[k] != null) made++; else { missing++; sb.AppendLine($"  缺 Interact {ActSuffix[k]}"); }
        }

        string ctrlPath = outDir + "/Pawn_Controller.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        ctrl.AddParameter("Look", AnimatorControllerParameterType.Int);
        ctrl.AddParameter("Moving", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Act", AnimatorControllerParameterType.Int);
        // No "Speed" here on purpose: WorkerLook's Moving bool drives the states.
        // UnitMover checks the controller for a Speed parameter before writing it.

        var sm = ctrl.layers[0].stateMachine;

        var idleStates = new AnimatorState[LookSuffix.Length];
        var runStates = new AnimatorState[LookSuffix.Length];
        var actStates = new AnimatorState[ActSuffix.Length];

        for (int k = 0; k < LookSuffix.Length; k++)
        {
            idleStates[k] = State(sm, "Idle_" + k, idle[k], new Vector3(260f, 60f + k * 55f));
            runStates[k] = State(sm, "Run_" + k, run[k], new Vector3(540f, 60f + k * 55f));
        }
        for (int k = 1; k < ActSuffix.Length; k++)
            actStates[k] = State(sm, "Act_" + k, act[k], new Vector3(20f, 60f + k * 55f));

        if (idleStates[0] != null) sm.defaultState = idleStates[0];

        // ---- Act transitions first: they must win over the Idle/Run pair ----
        for (int k = 1; k < ActSuffix.Length; k++)
        {
            if (actStates[k] == null) continue;
            var t = sm.AddAnyStateTransition(actStates[k]);
            t.hasExitTime = false; t.duration = 0f; t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.Equals, k, "Act");

            if (idleStates[k] != null)
            {
                var back = actStates[k].AddTransition(idleStates[k], false);
                back.duration = 0f;
                back.hasExitTime = false;
                back.AddCondition(AnimatorConditionMode.Equals, 0, "Act");
            }
        }

        // ---- then the look-up table: Moving x Look -> one of the sixteen poses ----
        for (int k = 0; k < LookSuffix.Length; k++)
        {
            if (runStates[k] != null)
            {
                var t = sm.AddAnyStateTransition(runStates[k]);
                t.hasExitTime = false; t.duration = 0f; t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.If, 0, "Moving");
                t.AddCondition(AnimatorConditionMode.Equals, k, "Look");
                t.AddCondition(AnimatorConditionMode.Equals, 0, "Act");
            }
            if (idleStates[k] != null)
            {
                var t = sm.AddAnyStateTransition(idleStates[k]);
                t.hasExitTime = false; t.duration = 0f; t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "Moving");
                t.AddCondition(AnimatorConditionMode.Equals, k, "Look");
                t.AddCondition(AnimatorConditionMode.Equals, 0, "Act");
            }
        }

        // ---- point the prefab at the fresh controller (the old asset's GUID is gone) ----
        if (prefab != null)
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Units/Pawn_" + colour + ".prefab");
            var anim = root.GetComponentInChildren<Animator>();
            if (anim != null) anim.runtimeAnimatorController = ctrl;
            if (root.GetComponent<WorkerLook>() == null) root.AddComponent<WorkerLook>();
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Units/Pawn_" + colour + ".prefab");
            PrefabUtility.UnloadPrefabContents(root);
        }

        sb.AppendLine($"  {colour}: 剪辑 {made} 个，缺 {missing} 个；状态 {sm.states.Length} 个；精灵轨道 \"{spritePath}\"");
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, AnimationClip clip, Vector3 pos)
    {
        if (clip == null) return null;
        var st = sm.AddState(name, pos);
        st.motion = clip;
        st.writeDefaultValues = true;
        return st;
    }

    static Sprite[] Sheet(string png)
    {
        if (!File.Exists(Path.GetFullPath(png))) return null;
        return AssetDatabase.LoadAllAssetsAtPath(png).OfType<Sprite>()
            .OrderBy(s =>
            {
                int i = s.name.LastIndexOf('_');
                return i >= 0 && int.TryParse(s.name.Substring(i + 1), out int v) ? v : 0;
            }).ToArray();
    }

    static AnimationClip MakeClip(string png, string outPath, string spritePath, bool loop)
    {
        var sprites = Sheet(png);
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
