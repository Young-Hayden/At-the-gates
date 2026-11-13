using UnityEngine;
using UnityEditor;
using System.Text;
using System.IO;
using System.Linq;

/// <summary>
/// Cuts a square portrait out of each unit's idle sprite for the production buttons.
///
/// A unit frame is 3x3 world units of mostly empty space with a ~1 unit character in
/// the middle, so dropping the frame straight into a 64px button shows a speck. These
/// are cropped to the character's own bounds (plus a margin) instead.
/// </summary>
public static class MakeUnitIcons
{
    const string OutDir = "Assets/Art/UI/UnitIcons";
    const int Pixels = 128;    // square, and the sprite's pixelsPerUnit, so it fills the button

    static readonly (string unit, string colour)[] Units =
        (from u in new[] { "Pawn", "Warrior", "Archer", "Lancer", "Monk" }
         from c in new[] { "Blue", "Red" }
         select (u, c)).ToArray();

    [MenuItem("Tools/UI/生成单位头像")]
    public static void Generate() { GenerateCore(false); }

    [MenuItem("Tools/UI/重修枪兵头像")]
    public static void GenerateLancerOnly() { GenerateCore(true); }

    static void GenerateCore(bool lancerOnly)
    {
        var sb = new StringBuilder();
        if (!AssetDatabase.IsValidFolder(OutDir))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art/UI")) AssetDatabase.CreateFolder("Assets/Art", "UI");
            AssetDatabase.CreateFolder("Assets/Art/UI", "UnitIcons");
            AssetDatabase.Refresh();
        }

        foreach (var (unit, colour) in Units.Where(u => !lancerOnly || u.unit == "Lancer"))
        {
            string prefabPath = "Assets/Prefabs/Units/" + unit + "_" + colour + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { sb.AppendLine($"  跳过 {unit}_{colour}：预制体不存在"); continue; }

            var sr = prefab.GetComponentInChildren<SpriteRenderer>();
            if (sr == null || sr.sprite == null) { sb.AppendLine($"  跳过 {unit}_{colour}：无精灵"); continue; }

            var spr = sr.sprite;
            string srcPng = AssetDatabase.GetAssetPath(spr.texture);
            if (string.IsNullOrEmpty(srcPng) || !File.Exists(Path.GetFullPath(srcPng)))
            { sb.AppendLine($"  跳过 {unit}_{colour}：找不到源图 {srcPng}"); continue; }

            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(File.ReadAllBytes(Path.GetFullPath(srcPng)), false);
            var srcPx = src.GetPixels32();
            int sw = src.width, sh = src.height;

            var rect = spr.rect;   // frame inside the atlas, origin bottom-left
            int fx = Mathf.RoundToInt(rect.x), fy = Mathf.RoundToInt(rect.y);
            int fw = Mathf.RoundToInt(rect.width), fh = Mathf.RoundToInt(rect.height);

            // content bounds inside the frame
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int y = fy; y < fy + fh && y < sh; y++)
            for (int x = fx; x < fx + fw && x < sw; x++)
                if (srcPx[y * sw + x].a > 24)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            Object.DestroyImmediate(src);

            if (minX > maxX) { sb.AppendLine($"  跳过 {unit}_{colour}：整帧透明"); continue; }

            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            int side, cx, x0, y0;
            if (unit == "Lancer")
            {
                // A full-height spear dominates the square and shrinks the soldier to a
                // speck. Centre on the lower 65% (helmet, torso, feet), bottom-align the
                // crop, and allow the upper spear tip to go. Other units keep the old crop.
                int bodyHeight = Mathf.Max(1, Mathf.RoundToInt(ch * 0.65f));
                int bodyMaxY = Mathf.Min(maxY, minY + bodyHeight - 1);
                int bodyMinX = int.MaxValue, bodyMaxX = int.MinValue;
                for (int y = minY; y <= bodyMaxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (srcPx[y * sw + x].a > 24)
                    {
                        if (x < bodyMinX) bodyMinX = x;
                        if (x > bodyMaxX) bodyMaxX = x;
                    }
                int bodyWidth = bodyMinX <= bodyMaxX ? bodyMaxX - bodyMinX + 1 : cw;
                cx = bodyMinX <= bodyMaxX ? (bodyMinX + bodyMaxX) / 2 : (minX + maxX) / 2;
                side = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(bodyWidth, bodyHeight) * 1.18f));
                x0 = cx - side / 2;
                y0 = minY;
            }
            else
            {
                side = Mathf.Max(cw, ch) + Mathf.RoundToInt(Mathf.Max(cw, ch) * 0.18f);   // ~18% margin
                cx = (minX + maxX) / 2;
                x0 = cx - side / 2;
                y0 = (minY + maxY) / 2 - side / 2;
            }

            var outTex = new Texture2D(side, side, TextureFormat.RGBA32, false);
            var outPx = new Color32[side * side];
            var src2 = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src2.LoadImage(File.ReadAllBytes(Path.GetFullPath(srcPng)), false);
            var px2 = src2.GetPixels32();
            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int sx = x0 + x, sy = y0 + y;
                outPx[y * side + x] = (sx >= 0 && sx < sw && sy >= 0 && sy < sh)
                    ? px2[sy * sw + sx] : new Color32(0, 0, 0, 0);
            }
            Object.DestroyImmediate(src2);

            outTex.SetPixels32(outPx);
            outTex.Apply();
            string outPath = OutDir + "/" + unit + "_" + colour + ".png";
            File.WriteAllBytes(Path.GetFullPath(outPath), outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);

            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            var ti = AssetImporter.GetAtPath(outPath) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = side;      // one icon = one world unit, pivot centred
                ti.alphaIsTransparency = true;
                ti.filterMode = FilterMode.Point;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                ti.SetTextureSettings(settings);
                ti.SaveAndReimport();
            }
            sb.AppendLine($"  {unit}_{colour,-8} 帧{fw}x{fh} 内容{cw}x{ch} → 头像 {side}x{side}");
        }

        AssetDatabase.Refresh();
        Debug.Log("[单位头像] 完成\n" + sb);
    }
}
