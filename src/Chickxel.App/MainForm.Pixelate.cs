using Chickxel.App.Localization;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;
using Chickxel.Core.References;

namespace Chickxel.App;

// AI 그림 픽셀로 정리: AI 메뉴(빈 창 — 창에서 끌어 놓기·붙여 넣기·파일 찾기), 파일 브라우저 우클릭(그 파일) 또는 현재 레이어(AI 메뉴)를 원본으로 창을 열고,
// 고른 후보를 현재 문서의 새 레이어(ImageDrop — 실행취소 한 단계) 또는 새 문서로 넣는다. 고르면 팔레트 저장·AI 참고 자료·AI 다듬기(채팅에 미리 채움)까지.
public sealed partial class MainForm
{
    private PixelateDialog? _pixelate;

    private static MenuItem WithIcon(MenuItem item, VectorIcon icon)
    {
        item.Icon = Ui.Icon(icon, 14);
        return item;
    }

    /// <summary>그림 파일을 원본으로 연다(파일 브라우저 우클릭).</summary>
    private void PixelateFile(string path)
    {
        if (LoadPixelateSource(this, path) is { } input) ShowPixelate(input);
    }

    /// <summary>
    /// 그림 파일을 정리할 원본으로 푼다: 캔버스 끌어 놓기와 같은 <see cref="DecodeMedia"/>(많은 프레임이면 묻기·진행 창)로 풀고, 움직이는 그림(GIF·APNG·WebP)은
    /// 프레임마다 한 칸으로 이어 붙인다(<see cref="PixelateInput.FromFrames"/>). 못 읽으면 알리고(그만두면 알리지 않고) null. 창은 <paramref name="owner"/> 위에.
    /// </summary>
    private PixelateInput? LoadPixelateSource(Visual owner, string path)
    {
        if (DecodeMedia(owner, path, interactive: true, out var message, out bool cancelled) is not { } anim)
        {
            if (!cancelled) Ui.Error(owner, message);
            return null;
        }
        var input = PixelateInput.FromFrames(anim.Images, Path.GetFileNameWithoutExtension(path), path);
        WarnTrimmedFrames(owner, input, anim.FrameCount);
        return input;
    }

    /// <summary>프레임이 너무 많아 원본 격자에 앞쪽만 넣었으면 알린다.</summary>
    private static void WarnTrimmedFrames(Visual owner, PixelateInput input, int total)
    {
        if (input.Frames < total) Ui.Warn(owner, UiStrings.FormatPixelate_FramesTrimmed(used: input.Frames, total: total));
    }

    /// <summary>현재 레이어를 원본으로 연다: 그림이 있는 프레임이 여럿이면 프레임마다 한 칸으로 이어 붙이고(<see cref="PixelateInput.FromFrames"/>) 그 격자로 나눈다.</summary>
    private void PixelateCurrentLayer()
    {
        if (_ctx.Project is not { } p) return;
        _ctx.CommitFloating();
        int layer = _ctx.CurrentLayer;
        if (_ctx.CurrentLayerObject is not { IsGroup: false } current)
        {
            ShowMessage(MainStrings.Pixelate_EmptyLayer);
            return;
        }
        var cels = new List<PixelImage>();
        for (int f = 0; f < p.Frames.Count; f++)
            if (p.GetCel(layer, f) is { } cel && !cel.IsRegionEmpty(cel.Bounds)) cels.Add(cel);
        if (cels.Count == 0)
        {
            ShowMessage(MainStrings.Pixelate_EmptyLayer);
            return;
        }
        var input = PixelateInput.FromFrames(cels.Count == 1 ? [cels[0].Clone()] : cels, current.Name, null);
        WarnTrimmedFrames(this, input, cels.Count);
        ShowPixelate(input);
    }

    /// <summary>
    /// 창을 연다(<paramref name="input"/>이 null이면 빈 창 — 창에서 끌어 놓기·붙여 넣기·파일 찾기로 원본을 넣음). 창은 닫아도 숨기기만 하므로(앱을 끝낼 때까지)
    /// 원본 없이 다시 열면 지난 창(원본·후보·고른 칸·돌던 최적화)을 그대로 다시 띄우고, 새 원본으로 열면 지난 창을 정말 닫고 새로 만든다.
    /// </summary>
    /// <param name="preset">처음 단계 설정(채팅의 "변환 설정 보기" — 그 설정을 기억한 설정 대신 쓰고, 다시 최적화하지 않음). null = 기억한 설정.</param>
    private void ShowPixelate(PixelateInput? input, PixelateSettings? preset = null)
    {
        bool canInsertLayer = _ctx.Project is not null && !_ctx.IsCanvasResizing;
        if (input is null && preset is null && _pixelate is { } kept)
        {
            kept.Reopen(this, canInsertLayer);
            return;
        }
        _pixelate?.CloseForGood();
        var dlg = new PixelateDialog(_settings, input, _colors.Palettes, canInsertLayer,
            ApplyPixelate, AskAiAboutCandidates, () => _browser.CurrentFolder ?? _settings.LastFolder, LoadPixelateSource, ReadClipboard,
            img => { _ctx.Clipboard = img; PutOnSystemClipboard(img); }, preset);
        dlg.Closed += (_, _) => { if (ReferenceEquals(_pixelate, dlg)) _pixelate = null; };
        _pixelate = dlg;
        dlg.Show(this);
    }

    void Ai.IAiHost.ShowPixelate(PixelateInput input, PixelateSettings settings) => ShowPixelate(input, settings);

    string Ai.IAiHost.InsertImage(string name, PixelImage image)
    {
        _ctx.CommitFloating();
        InsertPixelated(name, [image], DefaultChatImageDurationMs, newDocument: _ctx.Project is null, interactive: true, out var message);
        ShowMessage(message);
        return message;
    }

    /// <summary>대화 속 그림을 새 문서로 열 때의 프레임 길이(ms).</summary>
    private const int DefaultChatImageDurationMs = 100;

    Task<string?> Ai.IAiHost.AskUserAsync(string question, IReadOnlyList<string> options, CancellationToken ct)
        => _chat?.AskUserAsync(question, options, ct) ?? Task.FromResult<string?>(null);

    async Task<Ai.AiInsertResult> Ai.IAiHost.InsertPixelArtAsync(
        string name, IReadOnlyList<PixelImage> frames, int frameDurationMs, bool newDocument, CancellationToken ct)
    {
        if (frames.Count == 0) return new(false, UiStrings.AiDraw_Error_NoSprite);
        if (!newDocument && _ctx.IsCanvasResizing) return new(false, UiStrings.AiDraw_Insert_CanvasChanged);
        if (!newDocument && _ctx.Project is { } project && !_ctx.IsCanvasResizing)
        {
            int width = frames.Max(f => f.Width), height = frames.Max(f => f.Height);
            int canvasWidth = project.Width, canvasHeight = project.Height;
            if (width > canvasWidth || height > canvasHeight)
            {
                var choices = new[] { UiStrings.AiDraw_Insert_Expand, UiStrings.AiDraw_Insert_Crop };
                var question = UiStrings.FormatAiDraw_Insert_Question(
                    width: width, height: height, canvasWidth: canvasWidth, canvasHeight: canvasHeight);
                var answer = await ((Ai.IAiHost)this).AskUserAsync(question, choices, ct);
                ct.ThrowIfCancellationRequested();
                if (_ctx.IsCanvasResizing || !ReferenceEquals(project, _ctx.Project)
                    || project.Width != canvasWidth || project.Height != canvasHeight)
                    return new(false, UiStrings.AiDraw_Insert_CanvasChanged);
                if (answer == choices[0])
                {
                    if (project.Layers.Where((_, i) => project.IsLayerLocked(i)).Any())
                        return new(false, UiStrings.AiDraw_Insert_Locked);
                    _ctx.Structural(() => project.ResizeCanvas(Math.Max(width, canvasWidth), Math.Max(height, canvasHeight), Anchor.Center));
                }
                else if (answer == choices[1])
                {
                    var crop = new Ai.AiCropDialog(frames[0], canvasWidth, canvasHeight);
                    using var cancelCrop = ct.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(crop.Close));
                    if (crop.ShowDialog((Avalonia.Controls.Window?)_chat ?? this) != DialogResult.OK)
                        return new(false, UiStrings.AiDraw_Insert_Cancelled);
                    ct.ThrowIfCancellationRequested();
                    if (_ctx.IsCanvasResizing || !ReferenceEquals(project, _ctx.Project)
                        || project.Width != canvasWidth || project.Height != canvasHeight)
                        return new(false, UiStrings.AiDraw_Insert_CanvasChanged);
                    var origin = crop.CropOrigin;
                    frames = frames.Select(frame =>
                    {
                        var kept = new PixelImage(canvasWidth, canvasHeight);
                        kept.Blit(frame, frame.Width < canvasWidth ? (canvasWidth - frame.Width) / 2 : -origin.X,
                            frame.Height < canvasHeight ? (canvasHeight - frame.Height) / 2 : -origin.Y);
                        return kept;
                    }).ToList();
                }
                else if (answer is null) return new(false, UiStrings.AiDraw_Insert_Cancelled);
                else return new(false, UiStrings.FormatAiDraw_Insert_Custom(answer: answer));
            }
        }
        bool inserted = InsertPixelated(name, frames, frameDurationMs, newDocument, interactive: false, out var message);
        return new(inserted, message);
    }

    private void ApplyPixelate(PixelateOutcome o)
    {
        var frames = o.Result.CommonFrames();
        if (frames.Count == 0) return;
        var name = o.Input.Name;
        if (!InsertPixelated(name, frames, o.FrameDurationMs, o.NewDocument, interactive: true, out var message, o.GrowCanvas)) return;

        if (o.SavePalette && o.Result.Palette.Count > 0)
            _colors.AddPalette(MainStrings.FormatPixelate_PaletteName(name: name), o.Result.Palette);
        if (o.AddReference)
        {
            try
            {
                ((Ai.IAiHost)this).References.AddImage(ReferenceFrames.Single(o.Input.Image), name, null, null, null, ReferenceKinds.Character, CaptionSources.None);
                message += MainStrings.Pixelate_ReferenceAdded;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                message += MainStrings.FormatPixelate_ReferenceFailed(message: ex.Message);
            }
        }
        ShowMessage(message);
        if (o.AiTouchUp && !string.IsNullOrWhiteSpace(o.AiPrompt))
        {
            ToggleChat();
            _chat?.SelectProvider(o.AiProvider);
            _chat?.AttachImage(o.Input.Image, pixelArt: false);
            _chat?.PrefillInput(o.AiPrompt);
        }
    }

    /// <summary>
    /// 정리한 프레임을 넣는다: 열린 문서의 새 레이어(ImageDrop — 실행취소 한 단계, 프레임이 모자라면 더함, 캔버스 밖 픽셀은 캔버스 밖 그림으로 남음) 또는
    /// 새 탭의 새 문서(결과 크기). <paramref name="growCanvas"/>면 열린 문서의 캔버스를 결과 크기까지 가운데 기준으로 늘린 뒤 넣는다(같은 실행취소 단계).
    /// <paramref name="interactive"/>면(창의 적용) 새 레이어로 넣은 뒤 변형 모드로 들어간다(AI 도구는 그대로 둠).
    /// </summary>
    private bool InsertPixelated(string name, IReadOnlyList<PixelImage> frames, int durationMs, bool newDocument, bool interactive, out string message,
        bool growCanvas = false)
    {
        message = "";
        if (frames.Count == 0) return false;
        var size = frames[0].Size;
        if (!newDocument && _ctx.Project is { } p && !_ctx.IsCanvasResizing)
        {
            _ctx.CommitFloating();
            var durations = Enumerable.Repeat(Math.Max(1, durationMs), frames.Count).ToList();
            DroppedImage[] images = [new DroppedImage(name, frames, durations)];
            int frame = _ctx.CurrentFrame;
            ImageDropResult? result = null;
            _ctx.Structural(() => result = ImageDrop.Apply(p, images, _ctx.CurrentLayer, frame,
                new Point(p.Width / 2, p.Height / 2), MainStrings.Pixelate_UndoLabel, growCanvas));
            if (result is not { Layers.Count: > 0 }) return false;
            _ctx.SetCurrentLayer(p.IndexOf(result.Layers[^1]));
            message = MainStrings.FormatPixelate_AddedLayer(name: result.Layers[^1].Name, frames: frames.Count, width: size.Width, height: size.Height);
            if (result.AddedFrames > 0) message += MainStrings.FormatDrop_FramesAdded(count: result.AddedFrames);
            // 창의 적용이면 넣은 그림 전체를 들고 변형 모드로(옮기거나 크기를 맞춘 뒤 Enter — 캔버스 밖은 캔버스 밖 그림으로 남음).
            var (transforming, hint) = interactive ? TransformDropped(result, images, frame) : (false, "");
            if (result.Oversized && !transforming) message += MainStrings.Drop_Oversized;
            message += hint;
        }
        else
        {
            var doc = BuildProject(size.Width, size.Height, null);
            doc.Edit(MainStrings.Pixelate_UndoLabel, ProjectChange.Layers | ProjectChange.Frames | ProjectChange.Pixels, () =>
            {
                doc.Layers[0].Name = name;
                for (int i = 0; i < frames.Count; i++)
                {
                    if (i > 0) doc.AddFrame();
                    doc.SetFrameDuration(i, Math.Max(1, durationMs));
                    doc.SetCel(0, i, frames[i].Clone());
                }
            });
            doc.History.Clear();
            OpenInNewTab(new OpenDocument(doc, null));
            doc.IsDirty = true;
            message = MainStrings.FormatPixelate_NewDocument(frames: frames.Count, width: size.Width, height: size.Height);
        }
        return true;
    }

    /// <summary>"AI에게 후보 평가 부탁": 채팅을 열어 원본과 후보 그림을 첨부하고 부탁 글을 채운다(보내지 않음).</summary>
    private void AskAiAboutCandidates(string provider, string prompt, IReadOnlyList<PixelImage> images)
    {
        ToggleChat();
        if (_chat is not { } chat) return;
        chat.SelectProvider(provider);
        for (int i = 0; i < images.Count; i++) chat.AttachImage(images[i], pixelArt: i > 0);
        chat.PrefillInput(prompt);
        chat.Activate();
    }
}
