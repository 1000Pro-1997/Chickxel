using System.Drawing;
using Avalonia.Controls;
using Gpixel.App;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace GuideShots;

/// <summary>
/// 부위·스킨 장 예시 프로젝트: 늑대귀 모험가(49×98)를 Head/Body/Legs 부위로 잘라 "캐릭터" 그룹에 넣고(스킨: 기본·knight·zombie, Head 전용 "모자"),
/// 그 위 "장비" 그룹(스킨: 없음·검, 부위 Weapon)과 착장 프리셋 하나, 머리가 까딱이는 4프레임 idle 태그를 둔다.
/// 저장한 상태: 캐릭터 = knight, 장비 = 검, 부위 재정의 없음.
/// </summary>
internal static class PartsSample
{
    public const int CanvasW = 64, CanvasH = 104, CharX = 6, CharY = 3;

    public const string GroupName = "Character";
    public const string GearName = "Gear";
    public const string BaseSkin = "Default";
    public const string HatSkin = "Hat";
    public const string NoneSkin = "None";
    public const string SwordSkin = "Sword";
    public const string OutfitName = "Knight set";

    /// <summary>예시 프로젝트를 만들어 저장하고 그 경로를 돌려준다.</summary>
    public static string Build(ShotKit k, Action<SpriteProject>? tweak = null)
    {
        var wolf = PngIO.Load(ShotKit.SamplePng);
        var p = new SpriteProject(CanvasW, CanvasH);
        for (int i = 1; i < 4; i++) p.AddFrame();
        p.AddTag("idle", 0, 3);

        // ── 캐릭터 그룹: 아래 부위부터 넣어 Head가 맨 위 ──
        var group = p.AddGroup(GroupName);
        var bands = new (string Name, int Top, int Bottom, bool Bob)[] { ("Legs", 72, wolf.Height, false), ("Body", 40, 72, false), ("Head", 0, 40, true) };
        var parts = new Dictionary<string, (Layer Part, PixelImage Image)>();
        foreach (var (name, top, bottom, bob) in bands)
        {
            var band = new Rectangle(0, top, wolf.Width, bottom - top);
            var r = wolf.GetOpaqueBounds(band);
            var img = wolf.Crop(r);
            var part = p.AddPart(group, name, r.Width, r.Height + (bob ? 1 : 0), new Point(CharX + r.X, CharY + r.Y));
            parts[name] = (part, img);
        }
        // 첫 스킨 이름(언어별)
        p.RenameSkin(group, group.Skins[0], BaseSkin);
        var knight = p.AddSkin(group, "knight");
        var zombie = p.AddSkin(group, "zombie");
        var head = parts["Head"].Part;
        var hat = p.AddPartSkin(head, HatSkin);
        p.SetPartSkin(head, null);

        p.SetActiveSkin(group, group.Skins[0]);
        foreach (var (name, (part, img)) in parts) FillFrames(p, part, img, name == "Head");
        p.SetActiveSkin(group, knight);
        FillFrames(p, parts["Body"].Part, Recolor(parts["Body"].Image, Knight), false);
        FillFrames(p, parts["Legs"].Part, Recolor(parts["Legs"].Image, Knight), false);
        p.SetActiveSkin(group, zombie);
        FillFrames(p, head, Recolor(parts["Head"].Image, Zombie), true);
        p.SetPartSkin(head, hat);
        FillFrames(p, head, DrawHat(parts["Head"].Image), true);
        p.SetPartSkin(head, null);

        // ── 장비 그룹(위에 겹침): 첫 스킨 "없음"은 비워 둠 ──
        var gear = p.AddGroup(GearName);
        var weapon = p.AddPart(gear, "Weapon", 7, 34, new Point(CharX + 40, CharY + 36));
        p.RenameSkin(gear, gear.Skins[0], NoneSkin);
        var sword = p.AddSkin(gear, SwordSkin);
        p.SetActiveSkin(gear, sword);
        p.DeleteLayer(p.IndexOf(p.Layers.First(l => l.Kind == LayerKind.Normal)));
        FillFrames(p, weapon, DrawSword(), false);

        p.SetActiveSkin(group, knight);
        p.SaveOutfit(OutfitName);
        p.SetActiveSkin(group, zombie);
        p.SetActiveSkin(gear, gear.Skins[0]);
        p.SaveOutfit("Zombie");
        p.SetActiveSkin(group, knight);
        p.SetActiveSkin(gear, sword);

        p.Edit("export", ProjectChange.Settings, () =>
        {
            p.Export.ExportFrames = true;
            p.Export.ExportParts = true;
        });
        tweak?.Invoke(p);
        var path = Path.Combine(k.WorkDir, "wolf adventurer" + SpriteProject.FileExtension);
        ProjectIO.Save(p, path);
        return path;
    }

    /// <summary>예시 프로젝트를 만들어 열고, 그룹 "캐릭터"를 고른 상태의 메인 창을 돌려준다.</summary>
    public static MainForm Open(ShotKit k, Action<SpriteProject>? tweak = null, int width = 1600, int height = 900)
    {
        var path = Build(k, tweak);
        App.Settings.BottomHeight = 330;
        App.Settings.FilesVisible = false;
        var main = k.Main(path, null, width, height);
        ShotKit.Call(ShotKit.Get<Control>(main, "_canvas"), "FitToWindow");
        Select(k, main, GroupName);
        return main;
    }

    public static EditorContext Ctx(MainForm main) => ShotKit.Get<EditorContext>(main, "_ctx");
    public static SpriteProject Project(MainForm main) => Ctx(main).Project!;
    public static Layer Layer(MainForm main, string name) => Project(main).Layers.First(l => l.Name == name);
    public static int Index(MainForm main, string name) => Project(main).IndexOf(Layer(main, name));

    public static void Select(ShotKit k, MainForm main, string layerName)
    {
        Ctx(main).SetCurrentLayer(Index(main, layerName));
        k.Settle(250);
    }

    // ───────────── 그림 ─────────────

    private static void FillFrames(SpriteProject p, Layer part, PixelImage img, bool bob)
    {
        int index = p.IndexOf(part);
        for (int f = 0; f < p.Frames.Count; f++)
        {
            var cel = new PixelImage(part.PartSize.Width, part.PartSize.Height);
            cel.Blit(img, 0, bob && (f == 1 || f == 2) ? 1 : 0);
            p.SetCel(index, f, cel);
        }
    }

    private static PixelImage Recolor(PixelImage src, Func<Color32, Color32> map)
    {
        var dst = src.Clone();
        for (int i = 0; i < dst.Pixels.Length; i++)
            if (!dst.Pixels[i].IsTransparent) dst.Pixels[i] = map(dst.Pixels[i]);
        return dst;
    }

    /// <summary>어두운 남색 옷 → 붉은 갑옷 색(밝은 머리카락은 그대로).</summary>
    private static Color32 Knight(Color32 c)
        => c.B > c.R + 10 && c.B >= c.G && c.R + c.G + c.B < 330
            ? new Color32((byte)Math.Min(255, c.B + 50), (byte)(c.G * 0.5), (byte)(c.R * 0.5), c.A) : c;

    /// <summary>살색·밝은 색 → 초록빛.</summary>
    private static Color32 Zombie(Color32 c)
        => c.R > c.B + 15 && c.R >= c.G ? new Color32((byte)(c.G * 0.65), (byte)Math.Min(255, c.R * 0.9), (byte)(c.B * 0.7), c.A) : c;

    private static PixelImage DrawHat(PixelImage head)
    {
        var img = head.Clone();
        var brown = new Color32(96, 52, 28);
        var dark = new Color32(58, 30, 16);
        var band = new Color32(214, 170, 52);
        int w = img.Width;
        img.FillRect(new Rectangle(w / 2 - 12, 1, 24, 10), brown);
        img.FillRect(new Rectangle(w / 2 - 12, 1, 24, 1), dark);
        img.FillRect(new Rectangle(w / 2 - 12, 8, 24, 2), band);
        img.FillRect(new Rectangle(w / 2 - 17, 11, 34, 2), dark);
        return img;
    }

    private static PixelImage DrawSword()
    {
        var img = new PixelImage(7, 34);
        var steel = new Color32(196, 206, 220);
        var light = new Color32(240, 246, 255);
        var edge = new Color32(90, 100, 120);
        var gold = new Color32(222, 176, 60);
        var grip = new Color32(110, 64, 34);
        img.Set(3, 0, light);
        img.FillRect(new Rectangle(2, 1, 3, 22), steel);
        img.FillRect(new Rectangle(3, 1, 1, 22), light);
        img.FillRect(new Rectangle(1, 2, 1, 21), edge);
        img.FillRect(new Rectangle(5, 2, 1, 21), edge);
        img.FillRect(new Rectangle(0, 23, 7, 2), gold);
        img.FillRect(new Rectangle(2, 25, 3, 6), grip);
        img.FillRect(new Rectangle(2, 31, 3, 2), gold);
        return img;
    }
}
