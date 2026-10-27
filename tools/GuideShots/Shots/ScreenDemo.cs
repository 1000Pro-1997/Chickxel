using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Gpixel.App;
using Gpixel.Core.Model;

namespace GuideShots;

/// <summary>
/// 화면 구성·시작하기 샷의 공통 준비: 예시 그림으로 프레임·레이어·그룹·태그(·효과)가 있는 프로젝트를 앱 안에서 만들고 저장한다.
/// </summary>
internal static class ScreenDemo
{
    public static string FolderName => "Sprites";
    public static string SampleName => "Wolf Adventurer";

    public static EditorContext Ctx(MainForm main) => ShotKit.Get<EditorContext>(main, "_ctx");
    public static SpriteProject Project(MainForm main) => Ctx(main).Project ?? throw new InvalidOperationException("no project");

    /// <summary>예시 그림 복사본(작업 폴더의 <see cref="FolderName"/> 아래)을 연 메인 창.</summary>
    public static MainForm Open(ShotKit k, bool browserFolder = false, int width = 1600, int height = 900)
    {
        var path = k.CopySample(SampleName + ".png", FolderName);
        var folder = browserFolder ? Path.GetDirectoryName(path) : null;
        return k.Main(path, folder, width, height);
    }

    /// <summary>
    /// 파일 브라우저용 예시 폴더(<see cref="FolderName"/>)를 만들고 예시 그림을 연 메인 창: 예시 그림 변형 PNG 몇 개와 하위 폴더 둘.
    /// 폴더 경로 줄은 임시 폴더 경로 대신 보기 좋은 경로로 바꿔 보인다.
    /// </summary>
    public static MainForm OpenFolder(ShotKit k, int width = 1600, int height = 900)
    {
        var src = Gpixel.Core.IO.PngIO.Load(ShotKit.SamplePng);
        var dir = Path.Combine(k.WorkDir, FolderName);
        Directory.CreateDirectory(dir);
        var icons = Path.Combine(dir, "Icons");
        var tiles = Path.Combine(dir, "Tiles");
        Directory.CreateDirectory(icons);
        Directory.CreateDirectory(tiles);
        Gpixel.Core.IO.PngIO.Save(Tint(src, 0.55, 0.65, 1.0), Path.Combine(dir, SampleName + "_night" + ".png"));
        Gpixel.Core.IO.PngIO.Save(Tint(src, 1.0, 0.75, 0.75), Path.Combine(dir, SampleName + "_hurt" + ".png"));
        var face = src.Crop(new System.Drawing.Rectangle(6, 4, 36, 36));
        Gpixel.Core.IO.PngIO.Save(face, Path.Combine(icons, "face" + ".png"));
        Gpixel.Core.IO.PngIO.Save(Tint(face, 0.6, 0.6, 0.6), Path.Combine(icons, "face_gray" + ".png"));
        var main = Open(k, browserFolder: true, width, height);
        PrettyRoot(main);
        return main;
    }

    /// <summary>창 안 글자의 예시 폴더 실제 경로(샷 작업 폴더)를 <see cref="PrettyRoot"/>와 같은 예시 경로로.</summary>
    public static void ScrubPaths(ShotKit k, Visual root)
    {
        var real = Path.Combine(k.WorkDir, FolderName);
        var shown = @"D:\MyGame\Assets\" + FolderName;
        foreach (var tb in ShotKit.All<TextBlock>(root))
            if (tb.Text is { } t && t.Contains(real, StringComparison.OrdinalIgnoreCase))
                tb.Text = t.Replace(real, shown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>파일 브라우저 위 폴더 경로 줄을 임시 경로 대신 예시 경로로.</summary>
    public static void PrettyRoot(MainForm main)
    {
        var browser = ShotKit.Get<Control>(main, "_browser");
        var label = ShotKit.Get<TextBlock>(browser, "_rootLabel");
        label.Text = @"D:\MyGame\Assets\" + FolderName;
        ToolTip.SetTip(label, label.Text);
    }

    private static PixelImage Tint(PixelImage src, double r, double g, double b)
    {
        var img = src.Clone();
        for (int i = 0; i < img.Pixels.Length; i++)
        {
            var c = img.Pixels[i];
            img.Pixels[i] = new Color32((byte)(c.R * r), (byte)(c.G * g), (byte)Math.Min(255, c.B * b), c.A);
        }
        return img;
    }

    /// <summary>
    /// 지금 문서를 애니메이션 프로젝트로 키운다: 6프레임(idle 1~4 들썩임, hit 5~6 흔들림), 그림자 레이어, 그룹+부위 2개, 태그 2개.
    /// <paramref name="effects"/>면 몸 레이어에 외곽선(모든 프레임)·피격 번쩍임(5~6) 효과. <paramref name="save"/>면 .gpixel로 저장해 깨끗한 상태로 둔다.
    /// </summary>
    public static void Build(ShotKit k, MainForm main, bool effects = false, bool group = true, bool save = true)
    {
        var ctx = Ctx(main);
        var p = Project(main);
        ctx.Structural(() =>
        {
            var body = p.Layers[0];
            p.SetLayerProperties(0, name: "Body");
            for (int i = 0; i < 5; i++) p.DuplicateFrame(0);
            // 1~4: 숨쉬기 들썩임(2·4 프레임 1px 위), 5~6: 피격 흔들림(좌우 1px).
            Shift(p, 0, 1, 0, -1);
            Shift(p, 0, 3, 0, -1);
            Shift(p, 0, 4, -1, 0);
            Shift(p, 0, 5, 1, 0);
            for (int f = 4; f < 6; f++) p.SetFrameDuration(f, 80);

            var shadow = p.AddLayer(0, "Shadow");
            int si = p.IndexOf(shadow);
            for (int f = 0; f < p.Frames.Count; f++)
            {
                var img = new PixelImage(p.Width, p.Height);
                Ellipse(img, p.Width / 2, p.Height - 3, f is 1 or 3 ? 13 : 14, 2, new Color32(30, 20, 40, 110));
                p.SetCel(si, f, img);
            }
            p.SetLayerProperties(si, opacity: 200);

            if (group)
            {
                var g = p.AddGroup("Props");
                var star = p.AddPart(g, "Sparkle", 7, 7, new System.Drawing.Point(38, 6));
                var gem = p.AddPart(g, "Gem", 5, 5, new System.Drawing.Point(4, 40));
                for (int f = 0; f < p.Frames.Count; f++)
                {
                    p.SetCel(p.IndexOf(star), f, Sparkle(f % 2 == 0));
                    p.SetCel(p.IndexOf(gem), f, Gem());
                }
                // 기본 스킨 이름은 앱에 한국어("기본")로 고정되어 있어 영어 화면 그림에서는 영어 이름으로 바꿔 둔다.
                foreach (var skin in g.Skins) if (skin.Name == "기본") p.RenameSkin(g, skin, "Default");
            }

            p.AddTag("idle", 0, 3);
            p.AddTag("hit", 4, 5);

            if (effects)
            {
                int bi = p.IndexOf(body);
                p.SetLayerEffects(bi,
                [
                    new OutlineEffect { Color = new Color32(255, 230, 120, 255) },
                    new HitFlashEffect { Frames = new FrameRange(4, 5), Color = Color32.White },
                ]);
            }
        });
        ctx.SetCurrent(p.IndexOf(p.Layers.First(l => l.Name == "Body")), 0);
        k.Settle(200);
        if (save)
        {
            k.Command("파일/저장(+ 시트 내보내기)");
            k.Settle(400);
        }
    }

    /// <summary>창 제목을 임시 폴더 경로 없이 "문서 — Gpixel"로.</summary>
    public static void CleanTitle(MainForm main)
    {
        var p = Ctx(main).Project;
        main.Title = p is null ? "Gpixel" : p.DisplayName + " — Gpixel";
    }

    private static void Shift(SpriteProject p, int layer, int frame, int dx, int dy)
    {
        var src = p.GetCel(layer, frame);
        if (src is null) return;
        var dst = new PixelImage(src.Width, src.Height);
        dst.Blit(src, dx, dy);
        p.SetCel(layer, frame, dst);
    }

    private static void Ellipse(PixelImage img, int cx, int cy, int rx, int ry, Color32 c)
    {
        for (int y = -ry; y <= ry; y++)
        for (int x = -rx; x <= rx; x++)
            if (x * x * ry * ry + y * y * rx * rx <= rx * rx * ry * ry) img.Set(cx + x, cy + y, c);
    }

    private static PixelImage Sparkle(bool big)
    {
        var img = new PixelImage(7, 7);
        var y = new Color32(255, 236, 120, 255);
        var w = new Color32(255, 255, 255, 255);
        int r = big ? 3 : 2;
        for (int i = -r; i <= r; i++)
        {
            img.Set(3 + i, 3, y);
            img.Set(3, 3 + i, y);
        }
        img.Set(3, 3, w);
        return img;
    }

    private static PixelImage Gem()
    {
        var img = new PixelImage(5, 5);
        var d = new Color32(40, 90, 200, 255);
        var m = new Color32(80, 160, 255, 255);
        var l = new Color32(200, 240, 255, 255);
        string[] rows = [".ddd.", "dmmmd", "dmlmd", ".dmd.", "..d.."];
        for (int yy = 0; yy < 5; yy++)
        for (int xx = 0; xx < 5; xx++)
            img.Set(xx, yy, rows[yy][xx] switch { 'd' => d, 'm' => m, 'l' => l, _ => default });
        return img;
    }

    // ───────────── 찾기 ─────────────

    public static Control Named(Visual root, string typeName)
        => ShotKit.All<Control>(root).FirstOrDefault(c => c.GetType().Name == typeName)
           ?? throw new InvalidOperationException("no " + typeName);

    /// <summary>창 좌표 사각형 여럿을 감싼 사각형 + 여백(창 안으로 자름).</summary>
    public static Rect Around(TopLevel top, double pad, params Control[] controls)
    {
        var r = controls.Select(c => ShotKit.Bounds(c, top)).Aggregate((a, b) => a.Union(b)).Inflate(pad);
        return r.Intersect(new Rect(top.ClientSize));
    }
}
