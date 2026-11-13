using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Adds hold clips/states in place. Existing controllers, GUIDs and combat
/// states are preserved; this never invokes the full animation rebuild.</summary>
public static class AddHoldAnimations
{
    const float Fps = 12f;
    static readonly string[] Dirs = { "Right", "UpRight", "Up", "DownRight", "Down" };

    [MenuItem("Tools/第二阶段/定向添加驻守动画")]
    public static void Apply()
    {
        foreach (var colour in new[] { "Blue", "Red" })
        {
            BuildWarrior(colour);
            BuildLancer(colour);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[驻守动画] Warrior Guard 与 Lancer Defence 已定向接入");
    }

    static void BuildWarrior(string colour)
    {
        string folder = "Assets/Animation/Units/" + colour + "/Warrior";
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/Warrior_Controller.controller");
        if (ctrl == null) throw new System.InvalidOperationException("缺 Warrior controller " + colour);
        EnsureHoldingParameter(ctrl);
        var sm = ctrl.layers[0].stateMachine;
        var idle = RequireState(sm, "Idle");
        var run = RequireState(sm, "Run");
        var attack = RequireState(sm, "Attack");
        var clip = EnsureClip(
            "Assets/Art/Units/" + colour + "/Warrior/Warrior_Guard.png",
            folder + "/" + colour + "_Warrior_Hold.anim",
            attack.motion as AnimationClip);
        var hold = EnsureState(sm, "Hold", clip);

        LinkIdleRun(idle, run, hold, -1);
        LinkAttackExit(attack, hold);
        EditorUtility.SetDirty(ctrl);
    }

    static void BuildLancer(string colour)
    {
        string folder = "Assets/Animation/Units/" + colour + "/Lancer";
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/Lancer_Controller.controller");
        if (ctrl == null) throw new System.InvalidOperationException("缺 Lancer controller " + colour);
        EnsureHoldingParameter(ctrl);
        var sm = ctrl.layers[0].stateMachine;
        var idle = RequireState(sm, "Idle");
        var run = RequireState(sm, "Run");

        for (int i = 0; i < Dirs.Length; i++)
        {
            var attack = RequireState(sm, "Attack" + Dirs[i]);
            var clip = EnsureClip(
                "Assets/Art/Units/" + colour + "/Lancer/Lancer_" + Dirs[i] + "_Defence.png",
                folder + "/" + colour + "_Lancer_Hold_" + Dirs[i] + ".anim",
                attack.motion as AnimationClip);
            var hold = EnsureState(sm, "Hold" + Dirs[i], clip);
            LinkIdleRun(idle, run, hold, i);
            LinkAttackExit(attack, hold);
        }
        EditorUtility.SetDirty(ctrl);
    }

    static void EnsureHoldingParameter(AnimatorController ctrl)
    {
        if (!ctrl.parameters.Any(p => p.name == "Holding"))
            ctrl.AddParameter("Holding", AnimatorControllerParameterType.Bool);
    }

    static AnimatorState RequireState(AnimatorStateMachine sm, string name)
    {
        var found = sm.states.FirstOrDefault(s => s.state.name == name).state;
        if (found == null) throw new System.InvalidOperationException("缺原动画状态 " + name);
        return found;
    }

    static AnimatorState EnsureState(AnimatorStateMachine sm, string name, AnimationClip clip)
    {
        var state = sm.states.FirstOrDefault(s => s.state.name == name).state;
        if (state == null) state = sm.AddState(name);
        state.motion = clip;
        return state;
    }

    static AnimationClip EnsureClip(string spritePath, string outPath, AnimationClip bindingSource)
    {
        if (bindingSource == null) throw new System.InvalidOperationException("缺绑定来源 " + outPath);
        var sprites = AssetDatabase.LoadAllAssetsAtPath(spritePath).OfType<Sprite>()
            .OrderBy(TrailingIndex).ToArray();
        if (sprites.Length != 6)
            throw new System.InvalidOperationException(spritePath + " 应有 6 帧，实际 " + sprites.Length);

        var binding = AnimationUtility.GetObjectReferenceCurveBindings(bindingSource)
            .First(b => b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite");
        var keys = new ObjectReferenceKeyframe[sprites.Length];
        for (int i = 0; i < sprites.Length; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / Fps, value = sprites[i] };

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, outPath);
        }
        clip.frameRate = Fps;
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static int TrailingIndex(Sprite sprite)
    {
        int i = sprite.name.LastIndexOf('_');
        return i >= 0 && int.TryParse(sprite.name.Substring(i + 1), out int value) ? value : 0;
    }

    static void LinkIdleRun(AnimatorState idle, AnimatorState run, AnimatorState hold, int dir)
    {
        LinkBool(idle, hold, true, dir);
        LinkBool(run, hold, true, dir);
        LinkBool(hold, idle, false, -1);
    }

    static void LinkBool(AnimatorState from, AnimatorState to, bool value, int dir)
    {
        RemoveTransitionsTo(from, to);
        var transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "Holding");
        if (dir >= 0) transition.AddCondition(AnimatorConditionMode.Equals, dir, "Dir");
    }

    static void LinkAttackExit(AnimatorState attack, AnimatorState hold)
    {
        RemoveTransitionsTo(attack, hold);
        var transition = attack.AddTransition(hold);
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.duration = 0.05f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, "Holding");

        // The original Attack -> Idle transition stays intact and becomes the fallback
        // whenever Holding is false. Put the Hold branch first so it wins while stationed.
        var list = new List<AnimatorStateTransition>(attack.transitions);
        list.Remove(transition);
        list.Insert(0, transition);
        attack.transitions = list.ToArray();
    }

    static void RemoveTransitionsTo(AnimatorState from, AnimatorState to)
    {
        foreach (var transition in from.transitions.Where(t => t.destinationState == to).ToArray())
            from.RemoveTransition(transition);
    }
}
