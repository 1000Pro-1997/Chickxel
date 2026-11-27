using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>
/// 효과 창의 효과 업데이트(<see cref="EffectUpdates"/>).
/// <list type="bullet">
/// <item>효과 탭 맨 아래 "모든 효과 업데이트 확인": 온라인 목록을 받아 상점에서 받은 효과 에셋(스크립트 효과·효과 프리셋)의 새 버전을 찾고, "모두 업데이트"로
/// 차례로 설치한다. 받는 동안 진행 막대(목록은 끝을 모르는 막대, 설치는 전체 비율)와 취소 단추를 보인다.</item>
/// <item>효과 목록 "삭제" 옆 "업데이트"(고른 줄이 업데이트할 수 있는 스크립트 효과 하나일 때만 보임): 그 효과를 원본 상점 에셋의 최신 원문으로 바꾼다
/// (새 버전이 있으면 먼저 설치). 설정 값은 새 schema에 맞춰 같은 키 값을 잇고(<see cref="ScriptSettingValues.Normalized"/>), 범위·빠르기·반복 같은 공통 값은 그대로 둔다.</item>
/// </list>
/// </summary>
public sealed partial class LayerEffectsDialog
{
    private readonly Button _updateEffect;
    private Button _checkUpdates = null!, _updateAll = null!;
    private Control _updatesProgressRow = null!;
    private readonly ProgressBar _updatesProgress = new()
    {
        Minimum = 0, Maximum = 1, Height = 4, MinHeight = 4, Foreground = AppTheme.AccentBrush, VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _updatesStatus = new()
    {
        TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0),
    };
    /// <summary>창을 닫으면 취소(목록 받기·설치).</summary>
    private readonly CancellationTokenSource _updatesClosing = new();
    /// <summary>지금 하는 확인·모두 업데이트(없으면 null — 취소 단추가 이것을 취소).</summary>
    private CancellationTokenSource? _updatesWork;
    /// <summary>마지막 확인에서 찾은, 이 앱에서 설치할 수 있는 효과 에셋 업데이트("모두 업데이트"가 설치함).</summary>
    private IReadOnlyList<RemoteUpdate> _pendingUpdates = [];
    /// <summary>고른 효과를 업데이트하는 중(단추를 끔).</summary>
    private bool _updatingEffect;

    private static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>강조색 바탕의 단추(마우스를 올려도 바탕 유지).</summary>
    private static Button AccentButton(VectorIcon icon, string text, Action onClick)
    {
        var b = Ui.Button(icon, text, (_, _) => onClick());
        b.Background = AppTheme.AccentBrush;
        b.Foreground = Brushes.White;
        if (b.Content is StackPanel { Children: [IconView view, ..] }) view.Foreground = Brushes.White;
        Ui.KeepFillOnHover(b);
        return b;
    }

    /// <summary>효과 탭 맨 아래 업데이트 칸: 확인 단추, (하는 동안) 진행 막대와 취소, 결과 글과 "모두 업데이트".</summary>
    private Control BuildUpdatesPanel()
    {
        _checkUpdates = Ui.Button(VectorIcon.Refresh, UiStrings.EffectUpdates_Check, (_, _) => CheckUpdates()).Tip(UiStrings.EffectUpdates_CheckTip);
        _checkUpdates.HorizontalAlignment = HorizontalAlignment.Stretch;
        _checkUpdates.HorizontalContentAlignment = HorizontalAlignment.Center;
        _updateAll = AccentButton(VectorIcon.Download, UiStrings.EffectUpdates_UpdateAll, UpdateAll);
        _updateAll.IsVisible = false;
        var cancel = Ui.IconButton(VectorIcon.X, UiStrings.Assets_CancelDownload, (_, _) => _updatesWork?.Cancel());
        Grid.SetColumn(cancel, 1);
        _updatesProgressRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(2, 0), IsVisible = false,
            Children = { _updatesProgress, cancel },
        };
        DockPanel.SetDock(_updateAll, Dock.Right);
        var result = new DockPanel { Children = { _updateAll, _updatesStatus } };
        _updatesStatus.MaxWidth = (TileWidth + 4) * PaletteColumns - 120;
        ShowUpdatesStatus("");
        Closed += (_, _) => _updatesClosing.Cancel();
        return new StackPanel { Spacing = 2, Margin = new Thickness(2, 0), Children = { _checkUpdates, _updatesProgressRow, result } };
    }

    private void ShowUpdatesStatus(string text, bool warning = false)
    {
        _updatesStatus.Text = text;
        _updatesStatus.Foreground = warning ? AppTheme.WarningBrush : AppTheme.DimTextBrush;
        _updatesStatus.IsVisible = text.Length > 0;
    }

    private CancellationTokenSource BeginUpdatesWork(string status, bool indeterminate)
    {
        _updatesWork = CancellationTokenSource.CreateLinkedTokenSource(_updatesClosing.Token);
        _checkUpdates.IsEnabled = false;
        _updateAll.IsVisible = false;
        _updatesProgress.IsIndeterminate = indeterminate;
        _updatesProgress.Value = 0;
        _updatesProgressRow.IsVisible = true;
        ShowUpdatesStatus(status);
        return _updatesWork;
    }

    private void EndUpdatesWork()
    {
        _updatesWork?.Dispose();
        _updatesWork = null;
        _checkUpdates.IsEnabled = true;
        _updatesProgressRow.IsVisible = false;
    }

    /// <summary>온라인 목록을 받아 효과 에셋 업데이트를 찾는다(받지 못하면 그 까닭).</summary>
    private async void CheckUpdates()
    {
        if (_updatesWork is not null) return;
        var work = BeginUpdatesWork(UiStrings.EffectUpdates_Checking, indeterminate: true);
        string status = "";
        bool warning = false;
        try
        {
            var snapshot = await EffectUpdates.FetchAsync(work.Token);
            if (snapshot.Error is { } error)
            {
                status = F(UiStrings.EffectUpdates_FetchFailed, error.Message);
                warning = true;
            }
            else status = FindPendingUpdates();
        }
        catch (OperationCanceledException) { }
        finally
        {
            EndUpdatesWork();
        }
        if (_updatesClosing.IsCancellationRequested) return;
        ShowUpdatesStatus(status, warning);
        _updateAll.IsVisible = !warning && _pendingUpdates.Count > 0;
        UpdateButtons();
    }

    /// <summary>알고 있는 온라인 목록으로 <see cref="_pendingUpdates"/>를 다시 정하고 결과 글을 돌려준다.</summary>
    private string FindPendingUpdates()
    {
        var all = EffectUpdates.AssetUpdates();
        _pendingUpdates = all.Where(update => EffectUpdates.Installable(update.Remote)).ToList();
        int blocked = all.Count - _pendingUpdates.Count;
        var parts = new List<string>(2);
        if (_pendingUpdates.Count > 0) parts.Add(F(UiStrings.EffectUpdates_Found, _pendingUpdates.Count));
        if (blocked > 0) parts.Add(F(UiStrings.EffectUpdates_NeedsNewerApp, blocked));
        return parts.Count > 0 ? string.Join(" ", parts) : UiStrings.EffectUpdates_None;
    }

    /// <summary>찾은 업데이트를 차례로 설치한다(전체 진행 막대, 취소하면 남은 것은 두고 멈춤).</summary>
    private async void UpdateAll()
    {
        if (_updatesWork is not null || _pendingUpdates.Count == 0) return;
        var updates = _pendingUpdates;
        var work = BeginUpdatesWork("", indeterminate: false);
        int installed = 0;
        try
        {
            for (int k = 0; k < updates.Count && !work.IsCancellationRequested; k++)
            {
                var remote = updates[k].Remote;
                int index = k;
                ShowUpdatesStatus(F(UiStrings.EffectUpdates_Updating, k + 1, updates.Count, remote.DisplayName));
                var progress = new Progress<double>(fraction => _updatesProgress.Value = (index + fraction) / updates.Count);
                if (await AppAssets.InstallRemoteAsync(this, remote, progress, work.Token) is not null) installed++;
            }
        }
        finally
        {
            EndUpdatesWork();
        }
        if (_updatesClosing.IsCancellationRequested) return;
        // 남은 것(취소했거나 실패한 것, 앱 업데이트가 필요한 것)이 있으면 결과 뒤에 그것도 적는다.
        string done = F(UiStrings.EffectUpdates_Done, installed), rest = FindPendingUpdates();
        ShowUpdatesStatus(rest == UiStrings.EffectUpdates_None ? done : done + " " + rest);
        _updateAll.IsVisible = _pendingUpdates.Count > 0;
        UpdateButtons();
    }

    // ───────────── 고른 효과 업데이트 ─────────────

    private Button UpdateEffectButton()
    {
        var b = AccentButton(VectorIcon.Download, UiStrings.EffectUpdate_Button, UpdateSelectedEffect);
        b.IsVisible = false;
        return b;
    }

    /// <summary>고른 줄이 스크립트 효과 하나이고 그 원본 상점 에셋에 더 새 원문이 있으면 그 효과와 업데이트.</summary>
    private (ScriptEffect Effect, EffectUpdates.EffectUpdate Update)? SelectedUpdate()
        => SelectedBlock is (var first, var last) && first == last && _effects[first] is ScriptEffect script && EffectUpdates.For(script.ScriptHash) is { } update
            ? (script, update)
            : null;

    /// <summary>업데이트 단추: 고른 효과에 업데이트가 있을 때만 보이고(하는 동안은 끄고 보임), 풍선에 무엇으로 바뀌는지.</summary>
    private void UpdateEffectButtonState()
    {
        var chosen = _updatingEffect ? null : SelectedUpdate();
        _updateEffect.IsVisible = _updatingEffect || chosen is not null;
        _updateEffect.IsEnabled = !_updatingEffect;
        if (chosen is { Update: var update })
            ToolTip.SetTip(_updateEffect, update.Remote is { } remote ? F(UiStrings.EffectUpdate_RemoteTip, remote.Version) : UiStrings.EffectUpdate_LocalTip);
    }

    /// <summary>
    /// 고른 스크립트 효과를 최신 원문으로 바꾼다: 온라인 새 버전이 있으면 먼저 설치(설치가 실패하거나 취소되면 그대로), 그다음 원본 에셋의 원문으로.
    /// </summary>
    private async void UpdateSelectedEffect()
    {
        if (_updatingEffect || SelectedUpdate() is not { } chosen) return;
        _updatingEffect = true;
        UpdateButtons();
        try
        {
            var asset = chosen.Update.Asset;
            if (chosen.Update.Remote is { } remote)
            {
                if (await AppAssets.InstallRemoteAsync(this, remote, null, _updatesClosing.Token) is not { } installed) return;
                asset = AppAssets.Catalog.Find(asset.Id) ?? installed.FirstOrDefault(entry => entry.Script is not null);
            }
            if (_updatesClosing.IsCancellationRequested || asset?.Script is not { } script) return;
            ApplyUpdatedScript(chosen.Effect, script.Source);
        }
        finally
        {
            _updatingEffect = false;
            if (!_updatesClosing.IsCancellationRequested) UpdateButtons();
        }
    }

    /// <summary>
    /// 효과 <paramref name="target"/>(아직 목록에 있으면)의 원문을 <paramref name="source"/>로 바꾼다. 설정 값은 새 schema에 맞춰 같은 키 값을 잇고(해석하지 못한
    /// 원문이면 그대로), 공통 값(범위·빠르기·반복 등)은 그대로 둔다.
    /// </summary>
    private void ApplyUpdatedScript(ScriptEffect target, string source)
    {
        int i = IndexOfEffect(target);
        if (i < 0) return;
        var hash = _project.AddScript(source);
        var settings = ScriptLibrary.Runnable(hash) is { } program ? ScriptSettingValues.Normalized(program, target.Settings) : target.Settings;
        Update(i, target with { ScriptHash = hash, Settings = settings });
        if (Selected == i) ShowSettings();
    }
}
