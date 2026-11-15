using Avalonia.Controls;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.App.PlayTest;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Play;
using Chickxel.Core.Tiles;
using DPoint = System.Drawing.Point;

namespace GuideShots;

/// <summary>
/// 움직임 테스트 쪽(screen-play-test) 그림. 예시: 플랫포머 맵(땅, 왼쪽 돌 절벽, 오른쪽 돌 턱과 "Ladders" 층의 사다리)을 둔 탭과
/// 늑대귀 모험가를 반으로 줄인 캐릭터 탭(idle·run·jump·fall·climb 태그 — hang·wall_* 은 없어 climb으로 대신)을 열고,
/// 캐릭터 탭에서 움직임 테스트를 열어 플랫포머·맵·사다리 층·벽타기·콜라이더 표시를 켜고 사다리에 매달린 모습.
/// </summary>
internal static class PlayTestShots
{
    private const int Cell = 16, MapW = 20, MapH = 13;
    private const string MapLayer = "Level";
    private const string LadderFloor = "Ladders";
    private const int LadderX = 9;

    [Shot("screen-play-test")]
    public static void PlayTest(ShotKit k)
    {
        var main = k.Main(BuildMap(k), null, 1600, 900);
        App.Settings.FilesVisible = false;
        // 캐릭터는 F5를 누른 때의 탭이므로 캐릭터 문서를 마지막에 열어 지금 탭으로 둔다.
        ShotKit.Call(main, "OpenFile", BuildCharacter(k));
        k.Settle(300);
        ShotKit.Call(main, "ShowPlayTest");
        k.Settle(500);
        var play = ShotKit.Get<PlayTestForm>(main, "_play");
        play.Height = 830;
        k.Settle(200);

        ShotKit.Get<TabStrip>(play, "_mode").SelectedIndex = 1;
        k.Settle(200);
        ShotKit.Get<ComboBox>(play, "_map").SelectedIndex = 1;
        k.Settle(300);
        var ladders = ShotKit.Get<WrapPanel>(play, "_ladderList");
        ladders.Children.OfType<CheckBox>().First(c => c.Content as string == LadderFloor).IsChecked = true;
        ShotKit.Get<CheckBox>(play, "_wallClimb").IsChecked = true;
        ShotKit.Get<CheckBox>(play, "_showCollider").IsChecked = true;
        ShotKit.Get<NumericUpDown>(play, "_scale").Value = 2;
        k.Settle(300);

        // 사다리 아래(몸이 오른쪽 돌 턱에 걸리지 않게 사다리 칸 왼쪽 가까이)에 세우고 위 키로 붙어 두 칸쯤 오르다가 키를 떼어 매달린다(hang이 없어 climb 프레임에서 멈춤).
        var sim = ShotKit.Get<object>(play, "_sim");
        var platformer = (PlatformerController)sim.GetType().GetProperty("Platformer")!.GetValue(sim)!;
        platformer.Position = new System.Numerics.Vector2(LadderX * Cell + 2, 10 * Cell);
        var inputType = typeof(PlayTestForm).Assembly.GetType("Chickxel.App.PlayTest.PlayInput")!;
        object Input(bool up) => Activator.CreateInstance(inputType, false, false, up, false, false, false)!;
        for (int i = 0; i < 36; i++) ShotKit.Call(sim, "Tick", 1f / 60f, Input(true));
        for (int i = 0; i < 5; i++) ShotKit.Call(sim, "Tick", 1f / 60f, Input(false));
        ShotKit.Get<Control>(play, "_view").InvalidateVisual();
        k.Settle(300);

        Control Row(Control c) => (Control)c.Parent!;
        // ① 모드 ② 캐릭터 ③ 콜라이더 표시 ④ 맵 ⑤ 지형 층 ⑥ 사다리 층 ⑦ 오르기 속도 ⑧ 벽타기 허용
        k.Save(play, marks:
        [
            ShotKit.Get<Control>(play, "_mode"),
            ShotKit.Get<Control>(play, "_character"),
            Row(ShotKit.Get<Control>(play, "_showCollider")),
            Union(play, ((Panel)Row(ShotKit.Get<Control>(play, "_map"))).Children[0], ShotKit.Get<Control>(play, "_map")),
            ShotKit.Get<Control>(play, "_terrainList"),
            Union(play, ShotKit.Get<Control>(play, "_ladderHeader"), ShotKit.Get<Control>(play, "_ladderList")),
            Row(ShotKit.Get<Control>(play, "_climbSpeed")),
            Row(ShotKit.Get<Control>(play, "_wallClimb")),
        ]);

        // 옆 칸 아래(재생 중·애니메이션 이름 확인)가 다 보이게 창을 늘려 그 부분만: ⑨ 재생 중 ⑩ 애니메이션 이름 확인
        play.Height = 1400;
        k.Settle(300);
        var playing = ShotKit.Get<Control>(play, "_playingLabel");
        var keys = ShotKit.Get<Control>(play, "_keysLabel");
        var side = ShotKit.Bounds((Control)Row(playing).Parent!, play);
        var top = ShotKit.Bounds(playing, play).Y - 40;
        k.Save(play, "screen-play-test-2", crop: new Avalonia.Rect(side.X, top, side.Width, ShotKit.Bounds(keys, play).Bottom + 12 - top), firstNumber: 9, marks:
        [
            Union(play, playing, ShotKit.Get<Control>(play, "_candidateList")),
            Union(play, ShotKit.Get<Control>(play, "_expectedList"), ShotKit.Get<Control>(play, "_hintLabel")),
        ]);
    }

    private static Avalonia.Rect Union(TopLevel top, params Control[] controls)
        => controls.Select(c => ShotKit.Bounds(c, top)).Aggregate((a, b) => a.Union(b));

    /// <summary>캐릭터: 늑대귀 모험가를 반으로 줄인 한 장을 8프레임에 두고 idle·run·jump·fall·climb 태그를 단다.</summary>
    private static string BuildCharacter(ShotKit k)
    {
        var wolf = Half(PngIO.Load(ShotKit.SamplePng));
        var p = new SpriteProject(wolf.Width + 4, wolf.Height + 2);
        for (int i = 1; i < 8; i++) p.AddFrame();
        for (int f = 0; f < 8; f++)
        {
            var cel = new PixelImage(p.Width, p.Height);
            cel.Blit(wolf, 2, f % 2 == 1 && f < 4 ? 2 : 1);
            p.SetCel(0, f, cel);
        }
        p.AddTag("idle", 0, 1);
        p.AddTag("run", 2, 3);
        p.AddTag("jump", 4, 4);
        p.AddTag("fall", 5, 5);
        p.AddTag("climb", 6, 7);
        p.History.Clear();
        var path = Path.Combine(k.WorkDir, "Wolf" + SpriteProject.FileExtension);
        ProjectIO.Save(p, path);
        return path;
    }

    /// <summary>2×2 칸마다 불투명 픽셀의 평균(반 넘게 비면 투명).</summary>
    private static PixelImage Half(PixelImage src)
    {
        var img = new PixelImage((src.Width + 1) / 2, (src.Height + 1) / 2);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int r = 0, g = 0, b = 0, n = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = 2 * x + dx, sy = 2 * y + dy;
                        if (sx >= src.Width || sy >= src.Height) continue;
                        var c = src[sx, sy];
                        if (c.A < 128) continue;
                        r += c.R; g += c.G; b += c.B; n++;
                    }
                if (n >= 2) img[x, y] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n));
            }
        return img;
    }

    /// <summary>
    /// 맵(20×13칸): 아래 흙 땅(풀 덮개), 왼쪽 돌 절벽(벽타기), 가운데 사다리와 그 오른쪽 돌 턱. 사다리 자동 타일 세트는 새 층 "Ladders"에 찍는다.
    /// </summary>
    private static string BuildMap(ShotKit k)
    {
        var ts = BuiltInTilesets.Sandbox();
        (ts, int ladderFloor) = TilesetEdit.AddFloor(ts, LadderFloor);
        ts = ts with { AutoTiles = [.. ts.AutoTiles.Select(a => a.Name == DomainStrings.TileBuiltIn_Ladder ? a with { Floor = ladderFloor } : a)] };

        var p = new SpriteProject(MapW * Cell, MapH * Cell);
        p.Layers[0].Name = "Sky";
        var sky = new PixelImage(p.Width, p.Height);
        for (int i = 0; i < sky.Pixels.Length; i++) sky.Pixels[i] = new Color32(150, 200, 236);
        p.SetCel(0, 0, sky);
        var layer = p.AddTilemapLayer(ts, name: MapLayer);

        var map = TileMap.Empty;
        TileMap Paint(TileMap m, string set, IEnumerable<DPoint> cells) => AutoTiler.Paint(ts, m, ts.AutoTiles.First(a => a.Name == set), cells);
        map = Paint(map, DomainStrings.TileBuiltIn_Dirt, Box(0, 10, MapW - 1, MapH - 1));
        map = Paint(map, DomainStrings.TileBuiltIn_Stone, Box(0, 3, 1, 9).Concat(Box(LadderX + 1, 5, MapW - 1, 9)));
        map = Paint(map, DomainStrings.TileBuiltIn_Grass, Box(2, 10, LadderX, 10).Concat(Box(0, 3, 1, 3)).Concat(Box(LadderX + 1, 5, MapW - 1, 5)));
        map = Paint(map, DomainStrings.TileBuiltIn_Ladder, Box(LadderX, 5, LadderX, 9));
        p.SetTileMap(layer, map, "map");
        p.History.Clear();
        var path = Path.Combine(k.WorkDir, "Cliffs" + SpriteProject.FileExtension);
        ProjectIO.Save(p, path);
        return path;
    }

    private static IEnumerable<DPoint> Box(int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                yield return new DPoint(x, y);
    }
}
