using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Chickxel.Core.IO;
using Chickxel.App.Localization;
using SkiaSharp;

namespace Chickxel.App;

/// <summary>
/// 파일 목록 우클릭 "상세 정보 보기…": 파일·폴더 하나의 이름·위치·종류·파일 크기·만든 날짜·수정한 날짜(읽기 전용, 닫기 단추만).
/// 그 아래 종류별 정보 — 그림 = 픽셀 크기·프레임 수(움직이는 그림), 영상·AVIF·HEIC = ffmpeg로 읽은 픽셀 크기·길이·초당 프레임,
/// <c>.chickxel</c> = 캔버스 크기·프레임·레이어·부위 수, 폴더 = 바로 안의 파일·폴더 수와 파일 크기 합 — 는 창을 띄운 뒤 백그라운드에서 읽어 채운다.
/// 읽지 못한 것은 "읽을 수 없음"으로 두고 나머지는 보인다.
/// </summary>
public sealed class FileInfoDialog : DialogBase
{
    private const double ValueWidth = 420;
    private const string LabelGroup = "FileInfoLabel";
    private readonly CancellationTokenSource _cts = new();

    public FileInfoDialog(string path) : base(UiStrings.FileInfo_Title)
    {
        // 대상 도움말(F1): 창 = "파일 브라우저" 쪽 우클릭 메뉴 표의 상세 정보 보기 줄
        this.HelpAt("screen-file-browser", anchor: "Details");
        CloseOnly();
        // 기본 정보 표와 나중에 채우는 종류별 표가 이름 칸 폭을 같이 쓴다.
        Grid.SetIsSharedSizeScope(Body, true);
        var facts = FactsGrid();
        var more = FactsGrid();
        Body.Children.Add(facts);
        Body.Children.Add(more);

        bool folder = Directory.Exists(path);
        FileSystemInfo info = folder ? new DirectoryInfo(path) : new FileInfo(path);
        AddRow(facts, UiStrings.FileInfo_Name, info.Name);
        AddRow(facts, UiStrings.FileInfo_Location, Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? "");
        if (!info.Exists)
        {
            AddRow(facts, UiStrings.FileInfo_Kind, UiStrings.FileInfo_Missing);
            return;
        }
        var kind = folder ? MediaKind.None : MediaFormats.KindOf(path);
        AddRow(facts, UiStrings.FileInfo_Kind, KindText(path, folder, kind));
        if (info is FileInfo file) AddRow(facts, UiStrings.FileInfo_Size, Read(() => SizeText(file.Length)));
        AddRow(facts, UiStrings.FileInfo_Created, Read(() => DateText(info.CreationTime)));
        AddRow(facts, UiStrings.FileInfo_Modified, Read(() => DateText(info.LastWriteTime)));

        // 종류별 정보: 첫 줄 이름으로 "읽는 중…"을 보이다가 다 읽으면 바꾼다.
        string? firstLabel = folder ? UiStrings.FileInfo_Contents
            : kind switch
            {
                MediaKind.Project => UiStrings.FileInfo_Canvas,
                MediaKind.Image or MediaKind.Video => UiStrings.FileInfo_Pixels,
                _ => null,
            };
        if (firstLabel is null) return;
        AddRow(more, firstLabel, UiStrings.FileInfo_Loading);
        Closed += (_, _) => _cts.Cancel();
        _ = FillDetailsAsync(more, path, folder, kind, _cts.Token);
    }

    private async Task FillDetailsAsync(Grid more, string path, bool folder, MediaKind kind, CancellationToken token)
    {
        List<(string Label, string Value)> rows;
        try
        {
            rows = await Task.Run(() => ReadDetails(path, folder, kind, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        more.Children.Clear();
        more.RowDefinitions.Clear();
        foreach (var (label, value) in rows) AddRow(more, label, value);
    }

    /// <summary>종류별 정보(백그라운드). 읽지 못하면 그 줄 값이 "읽을 수 없음".</summary>
    private static List<(string Label, string Value)> ReadDetails(string path, bool folder, MediaKind kind, CancellationToken token)
    {
        if (folder) return ReadFolder(path, token);
        if (kind == MediaKind.Project) return ReadProject(path);
        // ffmpeg가 있어야 푸는 그림(AVIF·HEIC)은 직접 읽기가 못 세면 영상처럼 ffmpeg로 읽는다(썸네일과 같은 길).
        if (kind == MediaKind.Video || (MediaFormats.NeedsFfmpeg(path) && ImageUtil.CountFrames(path) == 0)) return ReadVideo(path, token);
        return ReadImage(path);
    }

    private static List<(string, string)> ReadFolder(string path, CancellationToken token)
    {
        int files = 0, folders = 0;
        long bytes = 0;
        try
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true }))
            {
                token.ThrowIfCancellationRequested();
                if (entry is FileInfo f)
                {
                    files++;
                    bytes += f.Length;
                }
                else folders++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [(UiStrings.FileInfo_Contents, UiStrings.FileInfo_Unreadable)];
        }
        return
        [
            (UiStrings.FileInfo_Contents, UiStrings.FormatFileInfo_FolderItems(files: Count(files), folders: Count(folders))),
            (UiStrings.FileInfo_FilesSize, UiStrings.FormatFileInfo_FolderSize(size: SizeText(bytes))),
        ];
    }

    /// <summary>프로젝트를 읽어(파일 목록 썸네일과 같은 <see cref="ProjectIO"/> 읽기 — 백그라운드라 화면은 멈추지 않음) 크기·프레임·레이어·부위 수.</summary>
    private static List<(string, string)> ReadProject(string path)
    {
        try
        {
            var p = ProjectIO.Load(path);
            int parts = p.Layers.Count(l => l.IsPart);
            var rows = new List<(string, string)>
            {
                (UiStrings.FileInfo_Canvas, PixelSize(p.Width, p.Height)),
                (UiStrings.FileInfo_Frames, Count(p.Frames.Count)),
                (UiStrings.FileInfo_Layers, Count(p.Layers.Count - parts)),
            };
            if (parts > 0) rows.Add((UiStrings.FileInfo_Parts, Count(parts)));
            return rows;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException or NotSupportedException)
        {
            return [(UiStrings.FileInfo_Canvas, UiStrings.FileInfo_Unreadable)];
        }
    }

    /// <summary>그림: 머리만 읽은 픽셀 크기(SkiaSharp 코덱 — 픽셀은 풀지 않음)와 프레임 수(<see cref="ImageUtil.CountFrames"/>).</summary>
    private static List<(string, string)> ReadImage(string path)
    {
        int width, height;
        using (var codec = SKCodec.Create(path))
        {
            if (codec is null) return [(UiStrings.FileInfo_Pixels, UiStrings.FileInfo_Unreadable)];
            (width, height) = (codec.Info.Width, codec.Info.Height);
        }
        var rows = new List<(string, string)> { (UiStrings.FileInfo_Pixels, PixelSize(width, height)) };
        int frames = ImageUtil.CountFrames(path);
        if (frames > 1) rows.Add((UiStrings.FileInfo_Frames, Count(frames)));
        return rows;
    }

    /// <summary>영상(과 ffmpeg로만 푸는 그림): ffmpeg로 읽은 가장 큰 비디오 스트림의 크기, 길이·초당 프레임(정지 그림이면 뺌).</summary>
    private static List<(string, string)> ReadVideo(string path, CancellationToken token)
    {
        if (FfmpegTool.Locate(App.Settings) is not { } ffmpeg) return [(UiStrings.FileInfo_Pixels, UiStrings.FileInfo_NeedsFfmpeg)];
        VideoInfo? video;
        try
        {
            video = FfmpegTool.Probe(ffmpeg, path, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            video = null;
        }
        if (video is not { Streams.Count: > 0 }) return [(UiStrings.FileInfo_Pixels, UiStrings.FileInfo_Unreadable)];
        var stream = video.Best;
        var rows = new List<(string, string)> { (UiStrings.FileInfo_Pixels, PixelSize(stream.Width, stream.Height)) };
        if (!video.IsStill)
        {
            var length = TimeSpan.FromSeconds(video.DurationSec!.Value);
            rows.Add((UiStrings.FileInfo_Duration, length.ToString(length.TotalHours >= 1 ? @"h\:mm\:ss\.f" : @"m\:ss\.f", CultureInfo.CurrentCulture)));
            if (stream.Fps > 0) rows.Add((UiStrings.FileInfo_FrameRate, stream.Fps.ToString("0.##", CultureInfo.CurrentCulture) + " fps"));
        }
        return rows;
    }

    private static string KindText(string path, bool folder, MediaKind kind)
    {
        if (folder) return UiStrings.FileInfo_KindFolder;
        var format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return kind switch
        {
            MediaKind.Project => UiStrings.FileInfo_KindProject,
            MediaKind.Image => UiStrings.FormatFileInfo_KindImage(format: format),
            MediaKind.Video => UiStrings.FormatFileInfo_KindVideo(format: format),
            _ => UiStrings.FormatFileInfo_KindFile(format: format),
        };
    }

    /// <summary>"1.2 MB (1,234,567바이트)"처럼.</summary>
    private static string SizeText(long bytes)
        => UiStrings.FormatFileInfo_SizeValue(size: AppTrash.FormatSize(bytes), bytes: bytes.ToString("N0", CultureInfo.CurrentCulture));

    private static string DateText(DateTime local) => local.ToString("F", CultureInfo.CurrentCulture);

    private static string PixelSize(int width, int height) => $"{width}×{height}px";

    private static string Count(int n) => n.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>파일 시스템 값 하나(읽지 못하면 "읽을 수 없음").</summary>
    private static string Read(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UiStrings.FileInfo_Unreadable;
        }
    }

    /// <summary>이름: 값 표(이름 칸 폭은 창 안의 표끼리 같음).</summary>
    private static Grid FactsGrid() => new()
    {
        ColumnDefinitions =
        {
            new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = LabelGroup },
            new ColumnDefinition(GridLength.Star),
        },
    };

    /// <summary>표에 줄 하나(값은 골라 복사할 수 있는 글).</summary>
    private static void AddRow(Grid grid, string label, string value)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var name = new TextBlock { Text = label, Foreground = AppTheme.DimTextBrush, Margin = new Thickness(0, 2, 16, 2) };
        var text = new SelectableTextBlock
        {
            Text = value,
            Foreground = AppTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = ValueWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2),
        };
        Grid.SetRow(name, row);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        grid.Children.Add(name);
        grid.Children.Add(text);
    }
}
