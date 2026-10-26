using Gpixel.App;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>화면 구성: 내보내기 창, GIF 애니메이션으로 내보내기 창.</summary>
internal static class ScreenDialogShots
{
    [Shot("screen-export")]
    public static void Export(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        k.Dialog(() => k.Command("파일/내보내기…"), w =>
        {
            ShotKit.Pump(400);
            k.Save(w);
            var tabs = ShotKit.Get<object>(w, "_tabs");
            ShotKit.Set(tabs, "SelectedIndex", 1);
            ShotKit.Pump(500);
            k.Save(w, "screen-export-2");
        }, settleMs: 900);
    }

    [Shot("screen-gif")]
    public static void Gif(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        k.Dialog(() => k.Command("파일/GIF 애니메이션으로 내보내기…"), w =>
        {
            ShotKit.Pump(400);
            k.Save(w);
        }, settleMs: 900);
    }
}
