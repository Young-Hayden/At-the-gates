using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Brings the scenery to life: swaying trees and bubbling water rocks.
///
/// The Free Pack ships four animated tree sheets (Tree1-4, eight frames each) but the
/// map was built from the static Tree.png — six frozen trees. And the Water Rocks sheets
/// are sixteen-frame bubble loops, yet each garnish prefab only ever showed frame zero.
///
/// Everything here puts the Animator on the SAME object that owns the SpriteRenderer, so
/// the clip's binding path is "" and cannot drift (the combat-unit freeze was exactly a
/// path that matched nothing — see BuildUnitAnimation's header).
///
/// Trees are also <see cref="ResourceNode"/>s: the node pauses the sway when it shows a
/// stump (<see cref="ResourceNode.swayAnimator"/>), so chopping one down still works.
/// </summary>
public static class AnimateScenery
{
    const float TreeFps = 7f;     // gentle sway
    const float FoamFps = 12f;    // livelier bubbling
    const string TreeArtDir = "Assets/Art/Terrain/Trees";
    const string AnimDir = "Assets/Animation/Scenery";
    const string TreeSrcDir = @"C:\Users\22492\Desktop\Tiny Swords (Free Pack)\Tiny Swords (Free Pack)\Terrain\Resources\Wood\Trees";

    // file, cellW, cellH, frames. All sheets are a single full-height row, so the sprite
    // rect y-origin convention is irrelevant (rect spans the whole texture height).
    static readonly (string file, int w, int h, int frames)[] TreeSrc =
    {
        ("Tree1.png", 192, 256, 8),   // pine
        ("Tree2.png", 192, 256, 8),   // pine
        ("Tree3.png", 192, 192, 8),   // leafy
        ("Tree4.png", 192, 192, 8),   // leafy
    };

    // The six placed tree prefabs mapped onto the four sway sheets.
    static readonly Dictionary<string, string> TreePrefabToSheet = new Dictionary<string, string>
    {
        { "Tree1", "Tree1" }, { "Tree2", "Tree2" }, { "Tree3", "Tree3" },
        { "Tree4", "Tree4" }, { "Tree5", "Tree1" }, { "Tree6", "Tree3" },
    };

    [MenuItem("Tools/动画/场景动画：摇摆树·水岩水花")]
    public static void Run()
    {
        var sb = new StringBuilder();
        ImportTrees(sb);
        var treeCtrls = BuildControllers(TreeSrc.Select(t => t.file).ToArray(),
                                         TreeArtDir, AnimDir + "/Trees", "_Sway", TreeFps, sb);
        ApplyToTrees(treeCtrls, sb);

        var rockFiles = new[] { "Water Rocks_01.png", "Water Rocks_02.png", "Water Rocks_03.png", "Water Rocks_04.png" };
        var rockCtrls = BuildControllers(rockFiles, "Assets/Art/Deco/WaterRocks",
                                         AnimDir + "/WaterRocks", "_Loop", FoamFps, sb);
        ApplyToRocks(rockCtrls, sb);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[场景动画] 完成\n" + sb);
    }

    // ------------------------------------------------------------------ import

    static void ImportTrees(StringBuilder sb)
    {
        EnsureFolder(TreeArtDir);
        foreach (var (file, w, h, frames) in TreeSrc)
        {
            string src = Path.Combine(TreeSrcDir, file);
            string dst = TreeArtDir + "/" + file;
            if (!File.Exists(src)) { sb.AppendLine("  缺源图 " + file); continue; }
            File.Copy(src, Path.GetFullPath(dst), true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            // pivot 0.073 matches the static trees the map was placed against, so the new
            // trunks land on the same ground point as the ones they replace.
            SliceSheet(dst, w, h, frames, 0.073f);
            sb.AppendLine($"  导入 {file}  {w}x{h} ×{frames}帧");
        }
    }

    static void SliceSheet(string assetPath, int cellW, int cellH, int frames, float pivotY)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Multiple;
        imp.spritePixelsPerUnit = 64;
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = false;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        // Multiple mode has to be on disk before the data provider can carve it up.
        imp.SaveAndReimport();

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(imp);
        if (provider == null) { Debug.LogError("[场景动画] 拿不到 SpriteDataProvider：" + assetPath); return; }
        provider.InitSpriteEditorDataProvider();

        string stem = Path.GetFileNameWithoutExtension(assetPath);
        var rects = new SpriteRect[frames];
        for (int i = 0; i < frames; i++)
            rects[i] = new SpriteRect
            {
                name = stem + "_" + i,
                rect = new Rect(i * cellW, 0, cellW, cellH),
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, pivotY),
                border = Vector4.zero,
                spriteID = GUID.Generate(),
            };
        provider.SetSpriteRects(rects);
        provider.Apply();
        imp.SaveAndReimport();
    }

    // ------------------------------------------------------------------ controllers

    static Dictionary<string, RuntimeAnimatorController> BuildControllers(
        string[] files, string artDir, string outDir, string clipSuffix, float fps, StringBuilder sb)
    {
        EnsureFolder(outDir);
        var map = new Dictionary<string, RuntimeAnimatorController>();
        foreach (var file in files)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            var sprites = Sheet(artDir + "/" + file);
            if (sprites == null || sprites.Length == 0) { sb.AppendLine("  " + stem + " 没切出帧，跳过"); continue; }
            var clip = MakeClip(sprites, outDir + "/" + stem + clipSuffix + ".anim", fps);
            var ctrl = FreshController(outDir + "/" + stem + ".controller");
            var sm = ctrl.layers[0].stateMachine;
            var st = sm.AddState("Loop", new Vector3(300f, 40f));
            st.motion = clip;
            sm.defaultState = st;
            map[stem] = ctrl;
            sb.AppendLine($"  {stem,-16} {sprites.Length}帧 {clip.length:F2}s");
        }
        return map;
    }

    // ------------------------------------------------------------------ apply

    static void ApplyToTrees(Dictionary<string, RuntimeAnimatorController> ctrls, StringBuilder sb)
    {
        foreach (var kv in TreePrefabToSheet)
        {
            string path = "Assets/Prefabs/Resources/" + kv.Key + ".prefab";
            if (!ctrls.TryGetValue(kv.Value, out var ctrl)) { sb.AppendLine("  " + kv.Key + " 无控制器"); continue; }
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { sb.AppendLine("  读不到 " + kv.Key); continue; }

            var sr = root.GetComponent<SpriteRenderer>();
            var node = root.GetComponent<ResourceNode>();
            if (sr == null || node == null)
            { PrefabUtility.UnloadPrefabContents(root); sb.AppendLine("  " + kv.Key + " 缺 SpriteRenderer/ResourceNode"); continue; }

            var anim = root.GetComponent<Animator>();
            if (anim == null) anim = root.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;

            var frames = Sheet(TreeArtDir + "/" + kv.Value + ".png");
            if (frames != null && frames.Length > 0)
            {
                sr.sprite = frames[0];          // idle base frame
                node.spriteFull = frames[0];    // ResourceNode "full" = swaying base
            }
            node.swayAnimator = anim;           // pause this when the stump shows

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);

            var check = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                        ?.GetComponent<Animator>()?.runtimeAnimatorController;
            sb.AppendLine($"  {kv.Key,-8} → {kv.Value}  {(check == null ? "** 没写进预制体 **" : "✓")}");
        }
    }

    static void ApplyToRocks(Dictionary<string, RuntimeAnimatorController> ctrls, StringBuilder sb)
    {
        for (int i = 1; i <= 4; i++)
        {
            string stem = "Water Rocks_0" + i;
            string path = "Assets/Prefabs/Garnish/WaterRock" + i + ".prefab";
            if (!ctrls.TryGetValue(stem, out var ctrl)) { sb.AppendLine("  " + stem + " 无控制器"); continue; }
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { sb.AppendLine("  读不到 WaterRock" + i); continue; }

            // garnish keeps the SpriteRenderer on a "Sprite" child — animate that object
            var sr = root.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null) { PrefabUtility.UnloadPrefabContents(root); sb.AppendLine("  WaterRock" + i + " 无 SpriteRenderer"); continue; }
            var anim = sr.GetComponent<Animator>();
            if (anim == null) anim = sr.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            sb.AppendLine($"  WaterRock{i} → {stem}  ✓");
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>In-place wipe, never delete-and-recreate: a new GUID would leave every
    /// prefab pointing at the old controller reading null (how the combat units froze).</summary>
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

    static AnimationClip MakeClip(Sprite[] sprites, string outPath, float fps)
    {
        var clip = new AnimationClip { frameRate = fps };
        // Animator sits on the same object as the SpriteRenderer, so the path is "".
        var binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = "",
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

    static void EnsureFolder(string path)
    {
        if (Directory.Exists(Path.GetFullPath(path))) return;
        Directory.CreateDirectory(Path.GetFullPath(path));
        AssetDatabase.Refresh();
    }
}
