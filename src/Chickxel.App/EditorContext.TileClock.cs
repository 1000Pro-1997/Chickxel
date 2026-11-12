using System.Diagnostics;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

// 움직이는 타일의 화면 시계: 지금 문서에 움직이는 타일이 있을 때만 UI 타이머로 돌고, 보이는 그림이 바뀌면 화면 합성만 무효로 한다.
// 프로젝트의 시계(SpriteProject.TileClockMs)는 화면 합성 순서표를 만드는 동안에만 이 시계로 두고 곧 0으로 되돌린다 —
// 그래서 내보내기·저장·AI 도구·실행취소 같은 다른 모든 그리기는 늘 프레임 시각 기준(시계 0)으로 같은 결과를 낸다.
public sealed partial class EditorContext
{
    /// <summary>화면 시계를 살피는 간격(이보다 짧게 바뀌는 그림은 이 간격으로 보임).</summary>
    private static readonly TimeSpan TileClockInterval = TimeSpan.FromMilliseconds(33);

    private readonly Stopwatch _tileClock = new();
    private DispatcherTimer? _tileClockTimer;
    // 지금 문서가 쓰는 타일셋 가운데 움직이는 타일이 있는 것(문서가 바뀌거나 내용이 바뀔 때 다시 셈)
    private IReadOnlyList<Tileset> _clockTilesets = [];
    // 마지막으로 화면에 알린 그림 묶음(타일셋마다 시계·지금 프레임 시각의 묶음 번호를 섞은 값)
    private int _shownTilePhase;
    // 화면 합성 순서표를 만드는 동안 시계를 넣어 둔 프로젝트와 겹친 깊이
    private SpriteProject? _clockedProject;
    private int _clockDepth;
    private TileClockScope? _tileClockScope;

    /// <summary>
    /// 움직이는 타일 화면 시계(ms): 지금 문서에 움직이는 타일이 있는 동안만 실제 시간으로 흐른다(없으면 멈춤).
    /// 캔버스·미리보기 합성은 이 시각으로 그리고, 타일 목록 같은 곳도 이 값으로 움직이는 타일 그림을 고른다.
    /// </summary>
    public long TileClockMs => _tileClock.ElapsedMilliseconds;

    /// <summary>화면 시계가 흘러 움직이는 타일의 보이는 그림이 바뀌었을 때(캔버스·미리보기·타일 목록 다시 그리기 — 내용은 그대로).</summary>
    public event Action? TileClockTick;

    /// <summary>생성자에서: 문서·내용이 바뀌면 시계를 켜고 끄고, 화면 합성을 만들 때 시계를 넣는다.</summary>
    private void InitTileClock()
    {
        _tileClockScope = new TileClockScope(this);
        ProjectReplaced += (_, _) => SyncTileClock();
        ProjectChanged += (_, e) =>
        {
            // 그리는 중에는 픽셀만 바뀌므로 타일셋을 다시 셀 까닭이 없다
            if (e.Change == ProjectChange.Pixels && Project is { IsStrokeActive: true }) return;
            SyncTileClock();
        };
        _renders.PlanScope = ScreenTileClock;
    }

    /// <summary>움직이는 타일이 있으면 시계를 돌리고, 없으면 멈춘다(배터리 — 타이머도 끔).</summary>
    private void SyncTileClock()
    {
        _clockTilesets = Project is { } p ? p.UsedTilesets.Where(t => t.HasAnimation).ToList() : [];
        bool run = _clockTilesets.Count > 0;
        if (run == _tileClock.IsRunning) return;
        if (run)
        {
            if (_tileClockTimer is null)
            {
                _tileClockTimer = new DispatcherTimer { Interval = TileClockInterval };
                _tileClockTimer.Tick += (_, _) => OnTileClockTick();
            }
            _shownTilePhase = TilePhase();
            _tileClock.Start();
            _tileClockTimer.Start();
        }
        else
        {
            _tileClock.Stop();
            _tileClockTimer?.Stop();
        }
    }

    /// <summary>
    /// 타이머: 보이는 그림 묶음이 바뀌었으면 프레임 합성·부위 하나 뺀 합성·타일맵 레이어 셀만 무효로 하고(썸네일·수정됨 표시는 그대로)
    /// <see cref="TileClockTick"/>을 낸다. 저장·내보내기 덮개가 떠 있거나 지금 프레임을 아직 계산하는 중이면 다음 차례로 미룬다.
    /// </summary>
    private void OnTileClockTick()
    {
        if (Project is not { } p || WorkOverlay.IsActive || _frame < 0 || _frame >= p.Frames.Count) return;
        int phase = TilePhase();
        if (phase == _shownTilePhase) return;
        if (_renders.IsPending(new RenderKey(RenderKind.Composite, p.Frames[_frame], Raw: !_canvasEffects))) return;
        _shownTilePhase = phase;
        _renders.Invalidate(k => k.Kind is RenderKind.Composite or RenderKind.Context
                                 || k.Kind is RenderKind.Cel or RenderKind.Part && k.Layer is { IsTilemap: true });
        TileClockTick?.Invoke();
    }

    /// <summary>지금 보일 그림 묶음: 움직이는 타일셋마다 시계 시각(타일 목록)과 지금 프레임의 타일 시각(캔버스)의 묶음 번호를 섞은 값.</summary>
    private int TilePhase()
    {
        if (Project is not { } p || _clockTilesets.Count == 0) return 0;
        long now = TileClockMs, frame = now + FrameTileStart(p, _frame);
        var hash = new HashCode();
        foreach (var t in _clockTilesets)
        {
            hash.Add(t.AnimationPhase(now));
            hash.Add(t.AnimationPhase(frame));
        }
        return hash.ToHashCode();
    }

    /// <summary>프레임 <paramref name="frame"/>이 시작하는 시각(ms) — 타일 시각에서 시계를 뺀 것.</summary>
    private static long FrameTileStart(SpriteProject p, int frame)
        => frame < 0 || frame >= p.Frames.Count ? 0 : p.TileTimeMs(frame) - p.TileClockMs;

    /// <summary>
    /// 화면 합성 순서표를 만드는 동안(<see cref="RenderService.PlanScope"/>) 프로젝트 시계를 화면 시계로 둔다. 썸네일, 움직이는 타일이 없을 때,
    /// 저장·내보내기가 작업 스레드에서 프로젝트를 읽는 동안에는 건드리지 않는다(그쪽 결과가 시계 0으로 정해지게).
    /// </summary>
    private IDisposable? ScreenTileClock(RenderKey key) => key.Kind == RenderKind.Thumb ? null : BeginTileClock();

    private IDisposable? BeginTileClock()
    {
        if (_clockTilesets.Count == 0 || WorkOverlay.IsActive || Project is not { } p) return null;
        if (_clockDepth++ == 0)
        {
            _clockedProject = p;
            p.TileClockMs = TileClockMs;
        }
        return _tileClockScope;
    }

    /// <summary><paramref name="read"/>를 화면 시계로 부른다(캔버스가 직접 읽는 타일맵 캔버스 밖 그림처럼 합성 결과를 거치지 않는 화면 그리기).</summary>
    internal T WithTileClock<T>(Func<T> read)
    {
        using (BeginTileClock())
            return read();
    }

    /// <summary>순서표를 다 만들면 프로젝트 시계를 0으로 되돌린다(겹쳐 불렸으면 마지막에).</summary>
    private sealed class TileClockScope(EditorContext owner) : IDisposable
    {
        public void Dispose()
        {
            if (--owner._clockDepth > 0) return;
            if (owner._clockedProject is { } p) p.TileClockMs = 0;
            owner._clockedProject = null;
        }
    }
}
