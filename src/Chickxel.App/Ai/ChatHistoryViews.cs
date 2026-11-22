using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.IO;

namespace Chickxel.App.Ai;

/// <summary>
/// 저장한 대화(<see cref="ChatHistoryStore"/>)를 보이는 데 AI 채팅 창과 효과 만들기 AI 칸이 같이 쓰는 조각: 첨부 미리보기 저장·읽기, 지난 대화 목록 한 줄.
/// </summary>
internal static class ChatHistoryViews
{
    /// <summary>저장하는 첨부 미리보기의 긴 변 최대.</summary>
    public const int SavedThumbSide = 64;

    /// <summary>저장할 첨부 미리보기(긴 변 <see cref="SavedThumbSide"/> 이하 PNG, base64). 만들지 못하면 null.</summary>
    public static string? SavedThumb(ChatAttachment a)
    {
        try
        {
            var px = a.Thumb.PixelSize;
            int longSide = Math.Max(px.Width, px.Height);
            Bitmap src = a.Thumb;
            if (longSide > SavedThumbSide)
            {
                double k = (double)SavedThumbSide / longSide;
                src = a.Thumb.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(px.Width * k)), Math.Max(1, (int)(px.Height * k))),
                    a.PixelArt ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
            }
            using var ms = new MemoryStream();
            src.Save(ms);
            if (!ReferenceEquals(src, a.Thumb)) src.Dispose();
            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception)
        {
            // 플랫폼 그림 인코더가 던지는 예외 형식은 정해져 있지 않다 — 미리보기 없이 저장한다.
            return null;
        }
    }

    /// <summary>저장한 PNG(base64)를 비트맵으로. 깨졌으면 null.</summary>
    public static Bitmap? DecodeImage(string base64)
    {
        try
        {
            using var ms = new MemoryStream(Convert.FromBase64String(base64));
            return new Bitmap(ms);
        }
        catch (Exception)
        {
            // 깨진 base64·PNG(디코더 예외 형식은 플랫폼마다 다름)
            return null;
        }
    }

    /// <summary>목록의 날짜: 오늘이면 시각, 올해면 월·일, 아니면 짧은 날짜.</summary>
    public static string When(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        return local.Date == now.Date ? local.ToString("t", culture) : local.Year == now.Year ? local.ToString("M", culture) : local.ToString("d", culture);
    }

    /// <summary>
    /// 지난 대화 목록 한 줄: 즐겨찾기 별 · 제목(누르면 <paramref name="open"/>)과 날짜·모델(<paramref name="modelLabel"/>) · 지우기.
    /// <paramref name="helpPage"/>가 있으면 단추마다 그 도움말 쪽(AI 채팅의 지난 대화 쪽 표시)을 단다.
    /// </summary>
    public static Control Row(ChatSummary c, string modelLabel, Action open, Action toggleFavorite, Action delete, string? helpPage)
    {
        Control Help(Control control, string anchor) => helpPage is null ? control : control.HelpAt(helpPage, anchor: anchor);
        var star = new Button
        {
            Content = new IconView(VectorIcon.Star, c.Favorite ? AppTheme.FavoriteBrush : AppTheme.DimTextBrush, 1.8) { Filled = c.Favorite, Width = 15, Height = 15 },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(5),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        }.Tip(c.Favorite ? UiStrings.Chat_History_Unfavorite : UiStrings.Chat_History_Favorite);
        Help(star, "The ③ star icon");
        star.Click += (_, _) => toggleFavorite();
        var title = new TextBlock
        {
            Text = c.Title.Length > 0 ? c.Title : UiStrings.Chat_History_ImageTitle,
            Foreground = AppTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var meta = new TextBlock
        {
            Text = UiStrings.FormatChat_History_Meta(date: When(c.UpdatedUtc), model: modelLabel),
            Foreground = AppTheme.DimTextBrush,
            FontSize = AppTheme.SmallFontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var openButton = new Button
        {
            Content = new StackPanel { Spacing = 1, Children = { title, meta } },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
        }.Tip(UiStrings.Chat_History_Open);
        Help(openButton, "Click a row");
        openButton.Click += (_, _) => open();
        var deleteButton = Ui.IconButton(VectorIcon.Trash, UiStrings.Chat_History_Delete, (_, _) => delete(), size: 13);
        Help(deleteButton, "The ③ star icon");
        deleteButton.Background = Brushes.Transparent;
        deleteButton.BorderThickness = new Thickness(0);
        deleteButton.Padding = new Thickness(5);
        deleteButton.VerticalAlignment = VerticalAlignment.Center;
        deleteButton.Focusable = false;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(star);
        Grid.SetColumn(openButton, 1);
        grid.Children.Add(openButton);
        Grid.SetColumn(deleteButton, 2);
        grid.Children.Add(deleteButton);
        return new Border
        {
            Background = AppTheme.BackBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = grid,
        };
    }
}
