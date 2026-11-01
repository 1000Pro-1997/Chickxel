using System.Diagnostics;
using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Assets;

/// <summary>
/// 진단 결과(<see cref="PreviewSceneRender.Probe"/>): <c>render</c>가 던진 오류(<see cref="ScriptEffectException"/> — <c>padding</c>이 실패했으면 그 오류, 없으면 null)와
/// <c>render</c> 한 번의 시간.
/// </summary>
public sealed record ScriptProbeResult(Exception? Error, TimeSpan Elapsed);

/// <summary>
/// 미리보기 장면 위에서 효과를 그리는 렌더러. 장면·효과·병아리·칸 수마다 하나 만들어 칸마다 부른다(스레드 하나에서).
/// <para>
/// 장면을 <paramref name="steps"/>프레임짜리 프로젝트로 만든다: 장면 레이어의 프레임 i 셀 = 장면 프레임 i % <see cref="PreviewScene.FrameCount"/>,
/// 병아리 레이어(<see cref="ChickLayerName"/>)의 셀 = 병아리를 <see cref="PreviewScene.ChickOffset"/>에 놓은 그림, 효과 = <paramref name="effects"/>(프레임 범위를 지워
/// 모든 프레임에 걸침). 그래서 프레임 i의 진행은 효과 만들기의 칸 진행(<see cref="ScriptFrameContext.PreviewStep"/>, <see cref="AssetPreview.Render"/>)과 같고,
/// 다른 레이어 함수(<c>ctx.layer</c> 등 — 레이어 고르기 설정으로 고른 장면 레이어)는 장면 레이어를 본다. 합성은 프로젝트와 같다(<see cref="SpriteProject.Flatten"/> — 캔버스 밖은 잘림).
/// </para>
/// <para>
/// <see cref="AssetPreview"/>와 맞춘 것: 다른 프레임을 쓰는 효과(잔상)가 있으면 병아리가 칸마다 한 칸씩 오른쪽으로 간다(<see cref="AssetPreview"/>의 늘린 원본처럼,
/// 움직임 가운데가 병아리 자리). 프레임 피벗은 그 원본(움직이면 움직이는 범위 전체) 사각형의 가운데 — <see cref="AssetPreview"/>의 "피벗 없음 = 원본 가운데"와 같은 자리다
/// (0~1 비율로 저장하므로 float 반올림만큼 다를 수 있음). 다른 것: 효과 원본(<c>img</c>)이 병아리 크기가 아니라 캔버스 크기라 크기를 쓰는 스크립트
/// (<c>img.width</c>, <c>padding(w, h)</c>의 크기)는 다른 값을 받는다. 불투명 픽셀만 보는 효과는 병아리만 둔 장면에서 <see cref="AssetPreview.Render"/>와 같은 픽셀이다.
/// </para>
/// </summary>
public sealed class PreviewSceneRender
{
    /// <summary>렌더 프로젝트의 병아리 레이어 이름.</summary>
    public const string ChickLayerName = "chick";

    private readonly PreviewScene _scene;
    private readonly PixelImage _chick;
    private readonly int _steps;
    private readonly bool _moves;
    private readonly SpriteProject _project;
    private readonly Layer _chickLayer;
    private (SpriteProject Project, Layer ChickLayer)? _original;

    public PreviewSceneRender(PreviewScene scene, IReadOnlyList<LayerEffect> effects, PixelImage chick, int steps)
    {
        _scene = scene;
        _chick = chick;
        _steps = Math.Max(1, steps);
        _moves = effects.Any(effect => EffectRegistry.Of(effect) is { } definition && definition.UsesOtherFrames(effect));
        (_project, _chickLayer) = Build(_moves);
        _project.SetLayerEffects(_scene.ChickIndex, [.. effects.Select(effect => effect with { Frames = null })]);
        _project.History.Clear();
    }

    /// <summary>렌더 프로젝트(그리기 전용 — 고치지 않는다). 시험·진단용.</summary>
    internal SpriteProject Project => _project;

    /// <summary>
    /// <paramref name="step"/>칸(0..steps-1로 자름)의 캔버스 전체 합성: 병아리 레이어에 효과를 입히고 다른 레이어는 프로젝트처럼 그린다.
    /// <paramref name="hideSceneLayers"/>면 장면 레이어는 그리지 않고 병아리 레이어만 같은 캔버스에 그린다(효과는 여전히 <c>ctx.layer</c> 등으로 장면 레이어를 읽음).
    /// </summary>
    public PixelImage Render(int step, bool hideSceneLayers = false) => Flatten(_project, _chickLayer, step, hideSceneLayers);

    /// <summary><see cref="Render"/>와 같은 장면·병아리 자리에 효과 없이(<paramref name="hideSceneLayers"/>면 병아리 레이어만).</summary>
    public PixelImage Original(int step, bool hideSceneLayers = false)
    {
        var (project, chickLayer) = _original ??= Build(_moves);
        return Flatten(project, chickLayer, step, hideSceneLayers);
    }

    private PixelImage Flatten(SpriteProject project, Layer chickLayer, int step, bool hideSceneLayers)
    {
        var image = new PixelImage(_scene.Width, _scene.Height);
        project.FlattenInto(image, Frame(step), layerFilter: hideSceneLayers ? layer => layer == chickLayer : null);
        return image;
    }

    /// <summary>
    /// 진단: <paramref name="program"/>(설정 <paramref name="values"/>)을 병아리 레이어의 <paramref name="step"/>칸에 프로젝트 렌더와 같은 그림(캔버스 크기 셀 + <c>padding</c> 여백)과
    /// <c>ctx</c>(진행·피벗·다른 프레임 원본·다른 레이어 — <see cref="SpriteProject.ScriptLayersAt"/>)로 직접 한 번 돌려 실행 오류와 시간을 잰다(렌더는 오류를 삼키므로).
    /// 다른 레이어는 실행을 허락한 원문일 때만 준다(렌더와 같음). 취소는 <see cref="OperationCanceledException"/>으로 나간다.
    /// </summary>
    public ScriptProbeResult Probe(ScriptEffectProgram program, IReadOnlyDictionary<string, object>? values, int step, CancellationToken cancel)
    {
        int frame = Frame(step), width = _scene.Width, height = _scene.Height;
        ScriptLibrary.Add(program.Source);
        var effect = new ScriptEffect { ScriptHash = program.ContentHash, Settings = ScriptSettingValues.Normalized(program, values), Easing = program.Easing };
        EffectPadding padding;
        try
        {
            padding = program.Padding(values, width, height, cancel);
        }
        catch (ScriptEffectException ex)
        {
            return new ScriptProbeResult(ex, TimeSpan.Zero);
        }
        var image = new PixelImage(width + padding.Left + padding.Right, height + padding.Top + padding.Bottom);
        if (_project.EffectSource(_chickLayer, frame) is { } cel) image.Blit(cel, padding.Left, padding.Top);
        var source = LayerEffects.UsesOtherFrames([effect]) ? _project.EffectFrameSource(_chickLayer) : null;
        var drawn = (ScriptEffect)effect.AtFrame(frame, _steps, _project.EffectPivot(_chickLayer, frame), source)
            with { Layers = _project.ScriptLayersAt(_chickLayer, frame, [effect]) };
        var context = ScriptFrameContext.From(drawn);
        if (!program.Animated) context = context with { Time = 0, Phase = 0, Progress = 0 };
        var watch = Stopwatch.StartNew();
        try
        {
            program.Render(image, new Rectangle(padding.Left, padding.Top, width, height), context, values, cancel);
            return new ScriptProbeResult(null, watch.Elapsed);
        }
        catch (ScriptEffectException ex)
        {
            return new ScriptProbeResult(ex, watch.Elapsed);
        }
    }

    private int Frame(int step) => Math.Clamp(step, 0, _steps - 1);

    /// <summary>
    /// 렌더 프로젝트(효과 없음): 칸 수만큼 프레임, 병아리는 <paramref name="moves"/>면 칸마다 한 칸씩 오른쪽(움직임 가운데 = 병아리 자리),
    /// 프레임 피벗은 병아리 원본 사각형(움직이면 움직이는 범위까지 넓힌 것)의 가운데.
    /// </summary>
    private (SpriteProject Project, Layer ChickLayer) Build(bool moves)
    {
        var at = _scene.ChickOffset(_chick);
        int span = moves ? _steps : 0, left = at.X - (moves ? (_steps - 1) / 2 : 0);
        var project = _scene.Build(_steps, f => f, _chick, f => new Point(left + (moves ? Math.Clamp(f, 0, _steps - 1) : 0), at.Y), ChickLayerName, copy: false);
        // AssetPreview의 원본(병아리, 움직이면 폭 + 칸 수)과 같은 사각형의 가운데를 피벗으로
        var pivot = Pivot.FromLocalPixel(left + (_chick.Width + span) / 2f, at.Y + _chick.Height / 2f, _scene.Width, _scene.Height);
        for (int f = 0; f < _steps; f++) project.SetFramePivot(f, pivot);
        project.History.Clear();
        return (project, project.Layers[_scene.ChickIndex]);
    }
}
