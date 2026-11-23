using Avalonia.Platform.Storage;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scene3D;

namespace Chickxel.App;

// 여러 형식 열기: 파일 > 열기(.chickxel·그림·움직이는 그림·영상, 여러 개), 창 어디든(캔버스 밖) 끌어 놓기 → 파일마다 새 탭.
// 그림(PNG 아닌 것·움직이는 PNG)·움직이는 그림은 프레임마다 한 장(레이어 1개, 파일 이름), 영상은 ffmpeg로 뽑는다(가져오기 설정 → 진행·취소).
// PNG·.chickxel은 OpenFile의 원래 흐름(가져오기 방식 묻기 등)을 그대로 쓴다.
public sealed partial class MainForm
{
    /// <summary>프레임이 이보다 많은 그림은 열기 전에 묻는다(모두 / 앞쪽만 / 취소).</summary>
    private const int ManyFrames = 300;

    /// <summary>한 번에 이보다 많은 파일을 열면 한 번 묻는다.</summary>
    private const int ManyFiles = 8;

    /// <summary>프레임이 이보다 많으면 진행 창을 띄우고 다른 스레드에서 푼다.</summary>
    private const int BackgroundDecodeFrames = 60;

    private void OpenFileDialogCmd()
    {
        var paths = Ui.OpenFiles(this, MainStrings.Message_290, _browser.CurrentFolder ?? _settings.LastFolder,
            new FileFilter(MainStrings.Open_FilterSupported, MediaFormats.Patterns(MediaFormats.AllExtensions)),
            new FileFilter(MainStrings.Ui_292, MediaFormats.Patterns(SpriteProject.FileExtensions)),
            new FileFilter(MainStrings.Open_FilterImages, MediaFormats.Patterns(MediaFormats.ImageExtensions)),
            new FileFilter(MainStrings.Open_FilterAnimations, MediaFormats.Patterns(MediaFormats.AnimationExtensions)),
            new FileFilter(MainStrings.Open_FilterVideos, MediaFormats.Patterns(MediaFormats.VideoExtensions)),
            new FileFilter(MainStrings.Open_FilterModels, MediaFormats.Patterns(MediaFormats.ModelExtensions)),
            new FileFilter(MainStrings.Open_FilterAllFiles, "*.*"));
        OpenFiles(paths);
    }

    /// <summary>파일마다 새 탭으로 연다(이미 열린 문서면 그 탭). <see cref="ManyFiles"/>개보다 많으면 한 번 묻는다.</summary>
    private void OpenFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        if (paths.Count > ManyFiles
            && Ui.Ask(this, MainStrings.FormatOpen_ManyFiles(count: paths.Count), MainStrings.Message_290, MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes)
            return;
        foreach (var path in paths) OpenFile(path);
    }

    // ───────────── 창 끌어 놓기 ─────────────

    /// <summary>창 어디든(캔버스가 처리하지 않은 곳) 파일을 놓으면 새 탭으로 연다. 모양 펜 세트 파일은 세트로 가져온다(<see cref="ImportDroppedShapeSets"/>).</summary>
    private void AttachWindowDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnWindowDragOver);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
    }

    /// <summary>끌어 온 파일 중 열 수 있는 것(프로젝트·그림·영상).</summary>
    private static List<string> OpenableFiles(IDataTransfer data)
        => data.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().Where(p => MediaFormats.KindOf(p) != MediaKind.None).ToList() ?? [];

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = !WorkOverlay.IsActive && (OpenableFiles(e.DataTransfer).Count > 0 || ShapeSetFiles(e.DataTransfer).Count > 0)
            ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        List<string> files = WorkOverlay.IsActive ? [] : OpenableFiles(e.DataTransfer);
        List<string> shapeSets = WorkOverlay.IsActive ? [] : ShapeSetFiles(e.DataTransfer);
        e.Handled = true;
        e.DragEffects = files.Count > 0 || shapeSets.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        // 끌어 놓기가 끝난 뒤에 연다(대화상자가 끌기 도중에 뜨지 않게).
        if (shapeSets.Count > 0) Dispatcher.UIThread.Post(() => ImportDroppedShapeSets(shapeSets));
        if (files.Count > 0) Dispatcher.UIThread.Post(() => OpenFiles(files));
    }

    // ───────────── 그림·영상 → 새 문서 ─────────────

    /// <summary>
    /// 프레임으로 풀어 여는 파일인지: 영상, PNG가 아닌 그림(GIF·WebP·JPG·AVIF …), 움직이는 PNG, 확장자를 모르지만 SkiaSharp가 푸는 그림.
    /// 그냥 PNG와 .chickxel은 아니다(가져오기 흐름).
    /// </summary>
    private static bool IsMediaFile(string full)
    {
        switch (MediaFormats.KindOf(full))
        {
            case MediaKind.Video or MediaKind.Model:
                return true;
            case MediaKind.Project:
                return false;
            case MediaKind.Image:
                return !HasExtension(full, ".png") || ApngReader.CountFrames(full) > 1;
            default:
                return File.Exists(full) && ImageUtil.CountFrames(full) > 0;
        }
    }

    /// <summary>그림·영상을 프레임으로 풀어 새 탭으로 연다(이미 열려 있으면 그 탭). 취소·실패하면 false.</summary>
    private bool OpenMediaFile(string full, bool interactive, out string message)
    {
        if (FindDoc(full) is { } open)
        {
            ShowDocument(open);
            message = MainStrings.FormatMessage_355(value: Path.GetFileName(full)) + UnsavedSuffix;
            ShowMessage(message);
            return true;
        }
        if (!File.Exists(full))
        {
            message = MainStrings.FormatMessage_357(full: full);
            if (interactive) Ui.Error(this, message);
            _settings.RecentFiles.Remove(full);
            return false;
        }
        if (DecodeMedia(this, full, interactive, out message, out bool cancelled) is not { } anim)
        {
            if (!cancelled && interactive) Ui.Error(this, message);
            return false;
        }
        OpenDecoded(full, anim);
        message = MainStrings.FormatOpen_Opened(name: Path.GetFileName(full), frames: anim.FrameCount, width: anim.Width, height: anim.Height,
            ms: anim.DurationsMs.Sum());
        ShowMessage(message);
        return true;
    }

    /// <summary>풀어 둔 프레임을 새 탭의 새 문서로(아직 .chickxel이 없다 — 저장하면 위치를 묻는다, 같은 이름 PNG 내보내기가 원본 옆 파일을 덮지 않게).</summary>
    private void OpenDecoded(string full, DecodedAnimation anim)
    {
        var project = SpriteProject.FromFrames(anim.Images, anim.DurationsMs, Path.GetFileNameWithoutExtension(full));
        OpenInNewTab(new OpenDocument(project, full));
        project.IsDirty = true;
        AppSettings.PushRecent(_settings.RecentFiles, full);
        _browser.SelectFile(full);
    }

    /// <summary>
    /// 파일 > 3D 모델 열기…: 모델 없이 3D 모델 가져오기 창(<see cref="ModelImportDialog"/>)을 열어 모델 파일을 끌어 놓거나 '모델 열기…'로 고르게 하고,
    /// 가져오면 그린 프레임을 새 탭으로 연다(파일 > 열기로 모델을 연 것과 같은 결과). 그 모델이 이미 탭으로 열려 있으면 그 탭으로.
    /// </summary>
    private void OpenModelWindow()
    {
        var dlg = new ModelImportDialog(null, null, _browser.CurrentFolder ?? _settings.LastFolder);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ModelPath is not { } path) return;
        if (FindDoc(path) is { } open)
        {
            ShowDocument(open);
            ShowMessage(MainStrings.FormatMessage_355(value: Path.GetFileName(path)) + UnsavedSuffix);
            return;
        }
        DecodedAnimation? anim;
        try
        {
            anim = RenderModel(this, dlg);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            Ui.Error(this, MainStrings.FormatMessage_358(full: path, message: ex.Message));
            return;
        }
        if (anim is null) return;
        OpenDecoded(path, anim);
        ShowMessage(MainStrings.FormatOpen_Opened(name: Path.GetFileName(path), frames: anim.FrameCount, width: anim.Width, height: anim.Height,
            ms: anim.DurationsMs.Sum()));
    }

    /// <summary>
    /// 그림·움직이는 그림·영상을 프레임으로 푼다(파일 열기·캔버스 끌어 놓기·픽셀로 정리 원본이 함께 씀). 실패하면 null과 <paramref name="message"/>(오류 창은 부른 쪽이 띄움).
    /// 사용자가 그만두면(많은 프레임 묻기·영상 설정·진행 취소, ffmpeg 없음 안내) null과 <paramref name="cancelled"/> = true.
    /// <paramref name="interactive"/>가 false면(AI 도구) 묻지 않고 기본 설정으로 모두 푼다. 묻는 창·진행 창은 <paramref name="owner"/> 위에 띄운다.
    /// </summary>
    private DecodedAnimation? DecodeMedia(Visual owner, string path, bool interactive, out string message, out bool cancelled)
    {
        message = "";
        cancelled = false;
        var name = Path.GetFileName(path);
        try
        {
            if (MediaFormats.KindOf(path) == MediaKind.Model) return DecodeModel(owner, path, interactive, out message, out cancelled);
            if (MediaFormats.NeedsFfmpeg(path) && !(MediaFormats.KindOf(path) == MediaKind.Image && ImageUtil.CountFrames(path) > 0))
                return DecodeVideo(owner, path, interactive, out message, out cancelled);
            int count = ImageUtil.CountFrames(path);
            int max = int.MaxValue;
            if (interactive && count > ManyFrames)
            {
                switch (Ui.Ask(owner, MainStrings.FormatOpen_ManyFrames(name: name, count: count, limit: ManyFrames), MainStrings.Message_290,
                            MessageButtons.YesNoCancel, MessageIcon.Question, MainStrings.Open_AllFrames, null,
                            MainStrings.FormatOpen_FirstFrames(limit: ManyFrames)))
                {
                    case DialogResult.Yes:
                        break;
                    case DialogResult.No:
                        max = ManyFrames;
                        break;
                    default:
                        cancelled = true;
                        return null;
                }
            }
            var anim = count > BackgroundDecodeFrames && interactive
                ? ProgressDialog<DecodedAnimation>.Run(owner, MainStrings.Open_DecodingTitle, MainStrings.FormatOpen_Decoding(name: name), 0,
                    (_, _) => ImageUtil.DecodeAnimation(path, max) ?? throw new InvalidDataException(MainStrings.FormatOpen_Unreadable(name: name)))
                : ImageUtil.DecodeAnimation(path, max);
            if (anim is null)
            {
                cancelled = interactive && count > BackgroundDecodeFrames;
                message = MainStrings.FormatOpen_Unreadable(name: name);
            }
            return anim;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException
                                       or System.ComponentModel.Win32Exception)
        {
            message = MainStrings.FormatMessage_358(full: path, message: ex.Message);
            return null;
        }
    }

    /// <summary>ffmpeg로 프레임을 뽑는다: 정보 읽기 → 가져오기 설정(정지 그림이면 묻지 않음) → 진행 창. ffmpeg가 없으면 안내하고 취소로 본다.</summary>
    private DecodedAnimation? DecodeVideo(Visual owner, string path, bool interactive, out string message, out bool cancelled)
    {
        message = "";
        cancelled = false;
        var name = Path.GetFileName(path);
        if (FfmpegTool.Locate(_settings) is not { } ffmpeg)
        {
            message = UiStrings.Ffmpeg_MissingShort;
            if (interactive)
            {
                FfmpegTool.ShowMissing(owner);
                cancelled = true;
            }
            return null;
        }
        if (FfmpegTool.Probe(ffmpeg, path) is not { } info)
        {
            message = MainStrings.FormatOpen_Unreadable(name: name);
            return null;
        }
        VideoImportOptions options;
        if (info.IsStill) options = new VideoImportOptions { Scale = VideoScaleMode.Keep };
        else if (interactive)
        {
            var dlg = new VideoImportDialog(name, info);
            if (dlg.ShowDialog(owner) != DialogResult.OK)
            {
                cancelled = true;
                return null;
            }
            options = dlg.Options;
        }
        else options = VideoImportPlan.Defaults(info);
        var anim = ProgressDialog<DecodedAnimation>.Run(owner, UiStrings.Video_DecodingTitle, MainStrings.FormatOpen_Decoding(name: name),
            VideoImportPlan.EstimateFrames(info, options), (progress, token) => FfmpegTool.Decode(ffmpeg, path, info, options, progress, token));
        if (anim is null) cancelled = true;
        return anim;
    }

    /// <summary>
    /// 3D 모델을 그려 프레임으로: 읽기(진행 창) → 가져오기 설정(각도·빛·프레임 — <see cref="ModelImportDialog"/>) → 그리기(진행 창). 그만두면 null과 <paramref name="cancelled"/>.
    /// <paramref name="interactive"/>가 false면(AI 도구) 묻지 않고 기본 설정으로 한 장.
    /// </summary>
    private static DecodedAnimation? DecodeModel(Visual owner, string path, bool interactive, out string message, out bool cancelled)
    {
        message = "";
        cancelled = false;
        var name = Path.GetFileName(path);
        var scene = interactive
            ? ProgressDialog<ModelScene>.Run(owner, UiStrings.Model_LoadingTitle, UiStrings.FormatModel_Loading(name: name), 0, (_, _) => ModelLoader.Load(path))
            : ModelLoader.Load(path);
        if (scene is null)
        {
            cancelled = true;
            return null;
        }
        if (!interactive) return ModelFrames.Render(scene, new ModelRenderSettings(), new ModelFrameOptions());
        var dlg = new ModelImportDialog(path, scene);
        if (dlg.ShowDialog(owner) != DialogResult.OK)
        {
            cancelled = true;
            return null;
        }
        var anim = RenderModel(owner, dlg);
        if (anim is null) cancelled = true;
        return anim;
    }

    /// <summary>가져오기 창에서 고른 설정을 다음 처음 값으로 남기고, 그 설정으로 모든 프레임을 그린다(진행 창 — 취소하면 null).</summary>
    private static DecodedAnimation? RenderModel(Visual owner, ModelImportDialog dlg)
    {
        dlg.Remember();
        var (scene, render, frames) = (dlg.Scene, dlg.RenderSettings, dlg.FrameOptions);
        return ProgressDialog<DecodedAnimation>.Run(owner, UiStrings.Model_RenderingTitle, UiStrings.FormatModel_Rendering(name: Path.GetFileName(dlg.ModelPath)),
            ModelFrames.Plan(scene, frames).Count, (progress, token) => ModelFrames.Render(scene, render, frames, progress, token));
    }
}
