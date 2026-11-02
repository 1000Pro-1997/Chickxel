using Chickxel.Core.Text;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 글꼴 고르기 목록(글씨 도구 설정·효과 창 글씨 설정): 내장 글꼴 다음 사용자 글꼴(<see cref="FontLibrary.List"/> 순서).
/// 고른 이름이 이 컴퓨터에 없으면 맨 끝에 "이름 (없음)"으로 보인다(그 글씨는 기본 글꼴로 그려짐).
/// 화면에 붙어 있는 동안 글꼴 폴더가 바뀌면(<see cref="FontLibrary.Changed"/>, 아무 스레드) UI 스레드에서 목록을 다시 채운다.
/// </summary>
internal sealed class FontPicker
{
    private string[] _names = [];
    private string[] _labels = [];
    private string _name = FontLibrary.DefaultName;
    private bool _filling;
    private string? _tip;

    public ComboBox Combo { get; }

    /// <summary>사용자가 목록에서 다른 글꼴을 고름(글꼴 이름).</summary>
    public event Action<string>? Picked;
    /// <summary>글꼴 폴더가 바뀌어 목록을 다시 채움(고른 글꼴이 생기거나 없어졌을 수 있음).</summary>
    public event Action? ListChanged;

    public FontPicker(int width)
    {
        Combo = Ui.Combo(width);
        Combo.SelectionChanged += (_, _) =>
        {
            int i = Combo.SelectedIndex;
            if (_filling || i < 0 || i >= _names.Length || _names[i] == _name) return;
            _name = _names[i];
            Picked?.Invoke(_name);
        };
        Combo.AttachedToVisualTree += (_, _) => { FontLibrary.Changed += OnLibraryChanged; Fill(); };
        Combo.DetachedFromVisualTree += (_, _) => FontLibrary.Changed -= OnLibraryChanged;
        Fill();
    }

    /// <summary>고른 글꼴 이름(목록에 없으면 "이름 (없음)" 항목을 더해 고름).</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            Fill();
        }
    }

    /// <summary>목록 풍선(없는 글꼴을 고르면 대신 경고 풍선).</summary>
    public string? Tip
    {
        get => _tip;
        set { _tip = value; Combo.Tip(IsMissing ? UiStrings.TextEffect_FontMissingTip : _tip); }
    }

    /// <summary>고른 글꼴이 이 컴퓨터에 없음(기본 글꼴로 그려짐).</summary>
    public bool IsMissing => FontLibrary.IsMissing(_name);

    /// <summary>고른 글꼴이 벡터 글꼴(TTF·OTF — 매끄럽게 그리기를 쓸 수 있음).</summary>
    public bool IsVector => FontLibrary.Find(_name)?.Kind == FontKind.Vector;

    private void OnLibraryChanged() => Dispatcher.UIThread.Post(() =>
    {
        Fill();
        ListChanged?.Invoke();
    });

    /// <summary>목록이 바뀌었을 때만 항목을 새로 넣고, 고른 항목을 맞춘다.</summary>
    private void Fill()
    {
        var fonts = FontLibrary.List();
        var names = fonts.Select(f => f.Name).ToList();
        // 표시 이름이 겹치면(예: 내장 글꼴과 같은 글꼴을 더함) 파일 이름을 붙여 가른다.
        var labels = fonts.Select(f => fonts.Count(o => o.DisplayName == f.DisplayName) > 1 && !f.BuiltIn ? $"{f.DisplayName} ({f.Name})" : f.DisplayName).ToList();
        int index = names.FindIndex(n => string.Equals(n, _name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            names.Add(_name);
            labels.Add(MissingLabel(_name));
            index = names.Count - 1;
        }
        _filling = true;
        try
        {
            if (!names.SequenceEqual(_names) || !labels.SequenceEqual(_labels))
            {
                _names = [.. names];
                _labels = [.. labels];
                Combo.Items.Clear();
                foreach (var label in _labels) Combo.Items.Add(label);
            }
            Combo.SelectedIndex = index;
        }
        finally
        {
            _filling = false;
        }
        Combo.Tip(IsMissing ? UiStrings.TextEffect_FontMissingTip : _tip);
    }

    /// <summary>목록·요약에 보일 글꼴 이름(표시 이름, 없는 글꼴은 "이름 (없음)").</summary>
    public static string Label(string name) => FontLibrary.Find(name) is { } f ? f.DisplayName : MissingLabel(name);

    private static string MissingLabel(string name)
        => string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.TextEffect_FontMissing, name);

    /// <summary>글꼴 파일(.ttf .otf .ttc .bdf)을 골라 앱 글꼴 폴더에 더한다. 더한 글꼴 이름(취소·실패하면 null, 실패는 알림 창).</summary>
    public static string? AddFont(Visual? owner)
    {
        var path = Ui.OpenFile(owner, UiStrings.TextTool_AddFontTitle, null,
            new FileFilter(UiStrings.TextTool_FontFiles, [.. FontLibrary.Extensions.Select(e => "*" + e)]));
        if (path is null) return null;
        try
        {
            return FontLibrary.Add(path).Name;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Ui.Error(owner, ex.Message, UiStrings.TextTool_AddFontFailed);
            return null;
        }
    }

    /// <summary>앱 글꼴 폴더를 파일 관리자로 연다(없으면 만듦).</summary>
    public static void OpenFolder()
    {
        if (FontLibrary.UserFolder is not { } folder) return;
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        Platform.OpenFolder(folder);
    }
}
