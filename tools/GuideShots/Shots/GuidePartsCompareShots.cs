using Avalonia;
using DPoint = System.Drawing.Point;
using Avalonia.Controls;
using Avalonia.Input;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace GuideShots;

/// <summary>부위 장(parts-*) 비교 그림: 같은 예시 프로젝트를 실제 프로젝트 편집·합성으로 바꾼 결과를 한 장에 나란히(왼쪽·오른쪽).</summary>
internal static class GuidePartsCompareShots
{
    /// <summary>한 픽셀을 몇 배로 키울지.</summary>
    private const int Zoom = 2;

    /// <summary>예시의 칼(Weapon 부위)을 옮길 거리(px, 위로).</summary>
    private const int Lift = -12;

    /// <summary>
    /// 부위 위치 옮기기: 왼쪽 = 기준 위치를 옮김(그룹 화면 끌기·방향키·위치… — <see cref="SpriteProject.SetPartOffset"/>, 네 프레임 모두),
    /// 오른쪽 = 프레임 3부터 옮김(레이어 크기 모드 — <see cref="SpriteProject.MovePartFrom"/>, 프레임 1·2는 그대로). 각 쪽은 프레임 1~4 합성을 가로로.
    /// </summary>
    [Shot("parts-frame-position-compare")]
    public static void FramePosition(ShotKit k)
    {
        SpriteProject Load() => ProjectIO.Load(PartsSample.Build(k));
        var all = Load();
        var weapon = all.Layers.First(l => l.Name == "Weapon");
        all.SetPartOffset(all.IndexOf(weapon), new DPoint(weapon.PartOffset.X, weapon.PartOffset.Y + Lift));

        var from = Load();
        from.MovePartFrom(from.IndexOf(from.Layers.First(l => l.Name == "Weapon")), 2, 0, Lift);

        const int pad = 8, gap = 4, between = 32;
        int fw = all.Width * Zoom, fh = all.Height * Zoom, n = all.Frames.Count;
        int side = n * fw + (n - 1) * gap;
        var sheet = new PixelImage(pad * 2 + side * 2 + between, pad * 2 + fh);
        void Strip(SpriteProject p, int x0)
        {
            for (int f = 0; f < n; f++)
            {
                var frame = new PixelImage(p.Width, p.Height);
                frame.Clear(new Color32(58, 68, 102, 255));
                frame.Composite(p.PlanFrame(f).Render());
                Paste(sheet, frame, x0 + f * (fw + gap), pad);
            }
        }
        Strip(all, pad);
        Strip(from, pad + side + between);

        var file = Path.Combine(ShotKit.OutDir, "parts-frame-position-compare.png");
        Directory.CreateDirectory(ShotKit.OutDir);
        PngIO.Save(sheet, file);
        k.Saved.Add(Path.GetRelativePath(ShotKit.Root, file));
    }

    /// <summary>
    /// 일반 레이어 Body 우클릭 → 스킨 추가 (그룹으로 바꿈) 하위 메뉴. parts-nest 쪽에서 앞 그림(①②) 다음이라 ③④로 찍는다.
    /// </summary>
    [Shot("parts-nest-2")]
    public static void LayerSkinMenu(ShotKit k)
    {
        App.Settings.BottomHeight = 350;
        App.Settings.RightWidth = 390;
        App.Settings.PreviewHeight = 190;
        var m = ScreenDemo.Open(k, height: 1020);
        ScreenDemo.Build(k, m, effects: false, group: false);
        var p = ScreenDemo.Project(m);
        var grid = ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_timeline"), "_grid");
        int body = p.IndexOf(p.Layers.First(l => l.Name == "Body"));
        var y = (int)ShotKit.Call(grid, "LayerY", body)!;
        var row = new Avalonia.Rect(grid.TranslatePoint(new Avalonia.Point(0, y), m)!.Value, new Avalonia.Size(270, 36));
        k.Click(m, new Avalonia.Point(row.X + 110, row.Center.Y), MouseButton.Right, settleMs: 450);
        var item = ScreenTimelineShots.OpenPopups()
            .SelectMany(pp => ShotKit.All<MenuItem>(pp.Child!))
            .First(mi => mi.ItemsSource is not null && mi.Header is TextBlock t && t.Text == UiStrings.Timeline_AddSkinToLayer);
        item.IsSubMenuOpen = true;
        k.Settle(450);
        var menu = ScreenTimelineShots.PopupArea(m) ?? throw new InvalidOperationException("skin submenu did not open");
        k.Save(m, crop: row.Union(menu).Inflate(12).Intersect(new Avalonia.Rect(m.ClientSize)), marks: [row, menu], firstNumber: 3);
    }

    /// <summary><paramref name="image"/>를 <see cref="Zoom"/>배 최근접으로 키워 (<paramref name="dx"/>, <paramref name="dy"/>)에 놓는다.</summary>
    private static void Paste(PixelImage sheet, PixelImage image, int dx, int dy)
    {
        for (int y = 0; y < image.Height * Zoom; y++)
            for (int x = 0; x < image.Width * Zoom; x++)
                sheet[dx + x, dy + y] = image[x / Zoom, y / Zoom];
    }
}
