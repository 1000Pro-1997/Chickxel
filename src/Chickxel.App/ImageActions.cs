using Avalonia.Input.Platform;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>그림을 시스템 클립보드에 올리거나 PNG로 저장하는 공통 동작(메인 창 복사, 미리보기 그림 우클릭 메뉴).</summary>
public static class ImageActions
{
    /// <summary>시스템 클립보드의 PNG 형식(Windows "PNG", macOS "public.png", 그 밖 "image/png").</summary>
    public static readonly DataFormat<byte[]> PngClipboardFormat = DataFormat.CreateBytesPlatformFormat(
        OperatingSystem.IsWindows() ? "PNG" : Platform.IsMac ? "public.png" : "image/png");

    /// <summary>시스템 클립보드에 PNG로 올린다(Windows는 비트맵도 함께). 실패하면 false.</summary>
    public static bool Copy(Visual owner, PixelImage image)
    {
        if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } cb) return false;
        try
        {
            using var png = new MemoryStream();
            PngIO.Save(image, png);
            var item = new DataTransferItem();
            item.Set(PngClipboardFormat, png.ToArray());
            // Windows 프로그램 대부분은 PNG 대신 비트맵(DIB)을 읽는다.
            if (OperatingSystem.IsWindows()) item.SetBitmap(ImageUtil.ToBitmap(image));
            var data = new DataTransfer();
            data.Add(item);
            Ui.Wait(cb.SetDataAsync(data));
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 다른 프로그램이 클립보드를 잡고 있으면 실패로 알린다.
            return false;
        }
    }

    /// <summary>PNG 저장 대화상자를 열어 저장한다. 저장한 경로(취소·실패면 null, 실패는 오류 창으로 알림).</summary>
    public static string? SaveAs(Visual owner, PixelImage image, string suggestedName, string? folder = null)
    {
        var path = Ui.SaveFile(owner, UiStrings.Image_SaveTitle, folder, suggestedName, ".png",
            new FileFilter(UiStrings.Preview_PngFilter, "*.png"));
        if (path is null) return null;
        try
        {
            PngIO.Save(image, path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Ui.Error(owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.Image_SaveFailed, ex.Message),
                UiStrings.Image_SaveTitle);
            return null;
        }
    }

    /// <summary>
    /// 우클릭 메뉴 "이미지 저장…"·"이미지 복사". <paramref name="image"/>가 null을 주면 두 항목을 끈다.
    /// <paramref name="done"/>은 결과 안내 글(저장함·복사함·복사 실패)을 받는다.
    /// </summary>
    public static IEnumerable<object> Menu(Visual owner, Func<PixelImage?> image, Func<string> suggestedName, Action<string>? done = null,
        Func<string?>? folder = null)
    {
        var current = image();
        var save = new MenuItem { Header = Ui.IconLabel(VectorIcon.Download, UiStrings.Image_SaveMenu), IsEnabled = current is not null };
        save.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (image() is not { } img || SaveAs(owner, img, suggestedName(), folder?.Invoke()) is not { } path) return;
            done?.Invoke(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.Preview_Saved,
                Path.GetFileName(path), img.Width, img.Height));
        });
        var copy = new MenuItem { Header = Ui.IconLabel(VectorIcon.Copy, UiStrings.Preview_Menu_CopyImage), IsEnabled = current is not null };
        // 복사는 대화상자가 없으므로 메뉴 Click 안에서 끝낸다. 다음 UI 작업으로 미루면
        // 채팅 결과 칸이 갱신되어 owner가 트리에서 빠진 뒤 Clipboard를 찾지 못할 수 있다.
        copy.Click += (_, _) =>
        {
            if (image() is not { } img) return;
            var copied = Copy(owner, img);
            done?.Invoke(copied
                ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.Image_Copied, img.Width, img.Height)
                : UiStrings.Image_CopyFailed);
        };
        return [save, copy];
    }
}
