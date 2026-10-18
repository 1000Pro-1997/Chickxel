using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 새 프로젝트: 종류(애니메이션 / 아틀라스), 캔버스(아틀라스면 첫 스프라이트) 가로·세로, 배경(투명/보조색),
/// 파일 브라우저에서 만들 때는 파일 이름도.
/// </summary>
public sealed class NewImageDialog : DialogBase
{
    private readonly NumericUpDown _w, _h;
    private readonly CheckBox _transparent;
    private readonly TextBox? _name;
    private readonly RadioButton _animation, _atlas;
    private readonly Label _sizeLabel;

    public NewImageDialog(int width, int height, string? fileName = null) : base("새 프로젝트", "만들기")
    {
        _w = Ui.Num(1, PixelImage.MaxSize, width, 80);
        _h = Ui.Num(1, PixelImage.MaxSize, height, 80);
        _transparent = Ui.Check("배경 투명", true);
        _animation = new RadioButton { Text = "애니메이션 (모든 프레임이 같은 캔버스 · 프레임·태그)", AutoSize = true, Checked = true, ForeColor = Theme.TextColor };
        _atlas = new RadioButton { Text = "아틀라스 (스프라이트마다 크기·피벗이 다름 · 예: 캐릭터 부위)", AutoSize = true, ForeColor = Theme.TextColor };
        _sizeLabel = Ui.Label("캔버스");
        _animation.CheckedChanged += (_, _) => _sizeLabel.Text = _atlas.Checked ? "첫 스프라이트" : "캔버스";
        if (fileName is not null)
        {
            _name = Ui.Text(fileName, 220);
            Body.Controls.Add(Ui.Row(Ui.Label("파일 이름"), _name, Ui.Label(SpriteProject.FileExtension)));
        }
        Body.Controls.Add(Ui.Label("종류", bold: true));
        Body.Controls.Add(_animation);
        Body.Controls.Add(_atlas);
        Body.Controls.Add(Ui.Row(_sizeLabel, Ui.Label("가로"), _w, Ui.Label("세로"), _h, Ui.Label("px")));
        var presets = Ui.Row();
        foreach (var (pw, ph) in new[] { (16, 16), (32, 32), (16, 32), (64, 64), (128, 128), (256, 256) })
        {
            var b = Ui.Button($"{pw}×{ph}");
            b.Click += (_, _) => { _w.Value = pw; _h.Value = ph; };
            presets.Controls.Add(b);
        }
        Body.Controls.Add(presets);
        Body.Controls.Add(_transparent);
        Body.Controls.Add(Ui.Label("투명이 아니면 보조색으로 채웁니다."));
        OkButton.Click += (_, _) =>
        {
            if (_name is null || FileName.Length > 0 && FileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) return;
            Ui.Error(this, "파일 이름이 비었거나 쓸 수 없는 문자가 있습니다.");
            DialogResult = DialogResult.None;
        };
    }

    public int ImageWidth => (int)_w.Value;
    public int ImageHeight => (int)_h.Value;
    public bool Transparent => _transparent.Checked;
    public string FileName => _name?.Text.Trim() ?? "";
    public ProjectKind Kind => _atlas.Checked ? ProjectKind.Atlas : ProjectKind.Animation;
}

/// <summary>아틀라스 스프라이트 추가: 이름 + 크기.</summary>
public sealed class NewSpriteDialog : DialogBase
{
    private readonly TextBox _name;
    private readonly NumericUpDown _w, _h;

    public NewSpriteDialog(string name, int width, int height) : base("새 스프라이트", "추가")
    {
        _name = Ui.Text(name, 200);
        _w = Ui.Num(1, PixelImage.MaxSize, width, 80);
        _h = Ui.Num(1, PixelImage.MaxSize, height, 80);
        Body.Controls.Add(Ui.Row(Ui.Label("이름"), _name));
        Body.Controls.Add(Ui.Row(Ui.Label("가로"), _w, Ui.Label("세로"), _h, Ui.Label("px")));
        var presets = Ui.Row();
        foreach (var (pw, ph) in new[] { (8, 8), (16, 16), (16, 32), (32, 32), (64, 64) })
        {
            var b = Ui.Button($"{pw}×{ph}");
            b.Click += (_, _) => { _w.Value = pw; _h.Value = ph; };
            presets.Controls.Add(b);
        }
        Body.Controls.Add(presets);
        Body.Controls.Add(Ui.Label("이름이 겹치면 _1, _2 …가 붙습니다. 피벗은 프로젝트 기본 피벗."));
        Shown += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    public string SpriteName => _name.Text.Trim();
    public int SpriteWidth => (int)_w.Value;
    public int SpriteHeight => (int)_h.Value;
}

/// <summary>PNG 가져오기: 칸이 모두 같은 크기일 때 아틀라스 / 애니메이션 프레임 중 고르기.</summary>
public sealed class ImportKindDialog : DialogBase
{
    public ImportKindDialog(string fileName, int sliceCount, Size sliceSize) : base("PNG 가져오기", "아틀라스로")
    {
        Body.Controls.Add(Ui.Label($"{fileName}: 칸 {sliceCount}개가 모두 같은 크기({sliceSize.Width}×{sliceSize.Height})입니다.", bold: true));
        Body.Controls.Add(Ui.Label("어떻게 가져올까요?"));
        Body.Controls.Add(Ui.Label("· 아틀라스로: 칸마다 스프라이트 1개(이름·크기·피벗 그대로, 원래 위치 유지)"));
        Body.Controls.Add(Ui.Label("· 애니메이션 프레임으로: 칸마다 프레임 1개(같은 캔버스, 태그로 애니메이션)"));
        OkButton.DialogResult = DialogResult.Yes;
        OkButton.Width = 110;
        var anim = AddButton("애니메이션 프레임으로", DialogResult.No);
        anim.Width = 150;
    }

    /// <summary>고른 종류(취소하면 null).</summary>
    public static ProjectKind? Ask(IWin32Window owner, string fileName, int sliceCount, Size sliceSize)
    {
        using var dlg = new ImportKindDialog(fileName, sliceCount, sliceSize);
        return dlg.ShowDialog(owner) switch
        {
            DialogResult.Yes => ProjectKind.Atlas,
            DialogResult.No => ProjectKind.Animation,
            _ => null,
        };
    }
}

/// <summary>캔버스 크기 바꾸기: 새 크기 + 기존 그림 기준점(9방향).</summary>
public sealed class ResizeDialog : DialogBase
{
    private readonly NumericUpDown _w, _h;
    private readonly RadioButton[] _anchors = new RadioButton[9];

    public ResizeDialog(int width, int height, string title = "캔버스 크기") : base(title)
    {
        _w = Ui.Num(1, PixelImage.MaxSize, width, 80);
        _h = Ui.Num(1, PixelImage.MaxSize, height, 80);
        Body.Controls.Add(Ui.Label($"현재 크기: {width} × {height}"));
        Body.Controls.Add(Ui.Row(Ui.Label("가로"), _w, Ui.Label("세로"), _h, Ui.Label("px")));
        Body.Controls.Add(Ui.Label("기준점(기존 그림이 놓일 위치)"));
        var grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 3, AutoSize = true, BackColor = Theme.PanelColor };
        string[] arrows = ["↖", "↑", "↗", "←", "●", "→", "↙", "↓", "↘"];
        for (int i = 0; i < 9; i++)
        {
            _anchors[i] = new RadioButton
            {
                Appearance = Appearance.Button,
                Text = arrows[i],
                Width = 32,
                Height = 28,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(1),
                Checked = i == 4,
            };
            grid.Controls.Add(_anchors[i], i % 3, i / 3);
        }
        Body.Controls.Add(grid);
    }

    public int NewWidth => (int)_w.Value;
    public int NewHeight => (int)_h.Value;
    public Gpixel.Core.Model.Anchor ImageAnchor => (Gpixel.Core.Model.Anchor)Array.FindIndex(_anchors, r => r.Checked);
}

/// <summary>시트 한 장을 격자(칸 크기/칸 수) 또는 자동(덩어리)으로 나눠 프레임으로 만드는 설정.</summary>
public sealed class SplitFramesDialog : DialogBase
{
    private readonly ComboBox _method;
    private readonly NumericUpDown _cellW, _cellH, _offX, _offY, _padX, _padY, _cols, _rows, _minSize;
    private readonly CheckBox _skipEmpty;
    private readonly Label _result;
    private readonly Gpixel.Core.Model.PixelImage _image;

    public SplitFramesDialog(Gpixel.Core.Model.PixelImage image, int cellW, int cellH) : base("격자로 나눠 프레임 만들기", "나누기")
    {
        _image = image;
        _method = Ui.Combo(170, "격자 – 칸 크기", "격자 – 칸 수", "자동(덩어리마다)");
        _cellW = Ui.Num(1, image.Width, Math.Min(cellW, image.Width), 60);
        _cellH = Ui.Num(1, image.Height, Math.Min(cellH, image.Height), 60);
        _offX = Ui.Num(0, image.Width - 1, 0, 60);
        _offY = Ui.Num(0, image.Height - 1, 0, 60);
        _padX = Ui.Num(0, image.Width, 0, 60);
        _padY = Ui.Num(0, image.Height, 0, 60);
        _cols = Ui.Num(1, image.Width, Math.Max(1, image.Width / Math.Max(1, cellW)), 60);
        _rows = Ui.Num(1, image.Height, Math.Max(1, image.Height / Math.Max(1, cellH)), 60);
        _minSize = Ui.Num(1, 1024, 2, 60);
        _skipEmpty = Ui.Check("빈 칸 건너뛰기", true);
        _result = Ui.Label("");
        Body.Controls.Add(Ui.Label($"현재 프레임 그림({image.Width}×{image.Height})을 칸마다 프레임 하나로 나눕니다."));
        Body.Controls.Add(_method);
        Body.Controls.Add(Ui.Row(Ui.Label("칸 가로"), _cellW, Ui.Label("세로"), _cellH));
        Body.Controls.Add(Ui.Row(Ui.Label("여백 X"), _offX, Ui.Label("Y"), _offY));
        Body.Controls.Add(Ui.Row(Ui.Label("간격 X"), _padX, Ui.Label("Y"), _padY));
        Body.Controls.Add(Ui.Row(Ui.Label("열"), _cols, Ui.Label("행"), _rows));
        Body.Controls.Add(Ui.Row(Ui.Label("자동: 최소 크기"), _minSize));
        Body.Controls.Add(_skipEmpty);
        Body.Controls.Add(_result);
        foreach (Control c in new Control[] { _method, _cellW, _cellH, _offX, _offY, _padX, _padY, _cols, _rows, _minSize, _skipEmpty })
        {
            if (c is NumericUpDown n) n.ValueChanged += (_, _) => UpdateResult();
            else if (c is ComboBox cb) cb.SelectedIndexChanged += (_, _) => UpdateResult();
            else if (c is CheckBox ch) ch.CheckedChanged += (_, _) => UpdateResult();
        }
        UpdateResult();
        OkButton.Click += (_, _) =>
        {
            if (Rects.Count > 0) return;
            Ui.Error(this, "만들어질 칸이 없습니다. 설정을 확인하세요.");
            DialogResult = DialogResult.None;
        };
    }

    /// <summary>나눌 칸들(위→아래, 왼쪽→오른쪽).</summary>
    public List<Rectangle> Rects
    {
        get
        {
            switch (_method.SelectedIndex)
            {
                case 1:
                    return Gpixel.Core.Slicing.CountSlicer.Slice(_image, (int)_cols.Value, (int)_rows.Value, _skipEmpty.Checked);
                case 2:
                    return Gpixel.Core.Slicing.AutoSlicer.Slice(_image, (int)_minSize.Value);
                default:
                    var grid = new Gpixel.Core.Model.GridSettings
                    {
                        CellW = (int)_cellW.Value, CellH = (int)_cellH.Value,
                        OffsetX = (int)_offX.Value, OffsetY = (int)_offY.Value,
                        PadX = (int)_padX.Value, PadY = (int)_padY.Value,
                    };
                    return Gpixel.Core.Slicing.GridSlicer.Slice(_image, grid, _skipEmpty.Checked);
            }
        }
    }

    private void UpdateResult()
    {
        var rects = Rects;
        _result.Text = rects.Count == 0
            ? "만들어질 칸: 없음"
            : $"만들어질 프레임: {rects.Count}개 (캔버스 {rects.Max(r => r.Width)}×{rects.Max(r => r.Height)})";
    }
}

/// <summary>색 칸 + HEX 입력(칸을 누르면 색 대화상자).</summary>
public sealed class ColorField : FlowLayoutPanel
{
    private readonly Panel _swatch;
    private readonly TextBox _hex;
    private Color32 _color;

    public ColorField(Color32 color)
    {
        AutoSize = true;
        WrapContents = false;
        Margin = new Padding(0);
        BackColor = System.Drawing.Color.Transparent;
        _swatch = new Panel { Size = new Size(28, 22), Margin = new Padding(3), Cursor = Cursors.Hand, BorderStyle = BorderStyle.FixedSingle };
        _swatch.Paint += (_, e) => ImageUtil.DrawSwatch(e.Graphics, _swatch.ClientRectangle, _color);
        _swatch.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _color.ToColor(), FullOpen = true };
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Color = dlg.Color.ToColor32().WithAlpha(_color.A == 0 ? (byte)255 : _color.A);
        };
        _hex = Ui.Text("", 90);
        _hex.Leave += (_, _) => { if (Color32.TryParseHex(_hex.Text.Trim(), out var c)) Color = c; else _hex.Text = _color.ToHex(); };
        Controls.Add(_swatch);
        Controls.Add(_hex);
        Color = color;
    }

    public Color32 Color
    {
        get => Color32.TryParseHex(_hex.Text.Trim(), out var c) ? c : _color;
        set
        {
            _color = value;
            _hex.Text = value.ToHex();
            _swatch.Invalidate();
        }
    }
}

/// <summary>색 바꾸기: 한 색을 다른 색으로(선택 영역 / 현재 셀).</summary>
public sealed class ReplaceColorDialog : DialogBase
{
    private readonly ColorField _from, _to;
    private readonly CheckBox _selectionOnly;

    public ReplaceColorDialog(Color32 from, Color32 to, bool hasSelection) : base("색 바꾸기", "바꾸기")
    {
        _from = new ColorField(from);
        _to = new ColorField(to);
        _selectionOnly = Ui.Check("선택 영역 안만", hasSelection);
        _selectionOnly.Enabled = hasSelection;
        Body.Controls.Add(Ui.Row(Ui.Label("바꿀 색"), _from));
        Body.Controls.Add(Ui.Row(Ui.Label("새 색  "), _to));
        Body.Controls.Add(_selectionOnly);
        Body.Controls.Add(Ui.Label("투명(알파 0)은 모두 같은 색으로 봅니다. 대상: 현재 레이어의 현재 프레임."));
    }

    public Color32 From => _from.Color;
    public Color32 To => _to.Color;
    public bool SelectionOnly => _selectionOnly.Checked;
}

/// <summary>격자 설정: 픽셀 격자 색, 타일 격자 크기·색.</summary>
public sealed class GridSettingsDialog : DialogBase
{
    private readonly ColorField _pixelColor, _tileColor;
    private readonly NumericUpDown _tileW, _tileH;

    public GridSettingsDialog(Color32 pixelColor, Size tile, Color32 tileColor) : base("격자 설정")
    {
        _pixelColor = new ColorField(pixelColor);
        _tileColor = new ColorField(tileColor);
        _tileW = Ui.Num(1, 4096, tile.Width, 64);
        _tileH = Ui.Num(1, 4096, tile.Height, 64);
        Body.Controls.Add(Ui.Label("픽셀 격자(6배 이상 확대에서 보임)", bold: true));
        Body.Controls.Add(Ui.Row(Ui.Label("색"), _pixelColor));
        Body.Controls.Add(Ui.Label("타일 격자", bold: true));
        Body.Controls.Add(Ui.Row(Ui.Label("가로"), _tileW, Ui.Label("세로"), _tileH, Ui.Label("px")));
        Body.Controls.Add(Ui.Row(Ui.Label("색"), _tileColor));
        Body.Controls.Add(Ui.Label("알파(#RRGGBBAA)로 진하기를 정합니다."));
    }

    public Color32 PixelGridColor => _pixelColor.Color;
    public Color32 TileGridColor => _tileColor.Color;
    public Size TileSize => new((int)_tileW.Value, (int)_tileH.Value);
}

/// <summary>한 줄 입력(선택 목록 제안 포함 가능).</summary>
public sealed class InputDialog : DialogBase
{
    private readonly ComboBox _box;

    public InputDialog(string title, string prompt, string value, IEnumerable<string>? suggestions = null) : base(title)
    {
        _box = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = 280,
            Text = value,
            FlatStyle = FlatStyle.Flat,
        };
        if (suggestions is not null) _box.Items.AddRange(suggestions.Cast<object>().ToArray());
        _box.Text = value;
        Body.Controls.Add(Ui.Label(prompt));
        Body.Controls.Add(_box);
        Shown += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public string Value => _box.Text.Trim();

    /// <summary>입력을 받는다. 취소하거나 비었으면 null.</summary>
    public static string? Ask(IWin32Window owner, string title, string prompt, string value, IEnumerable<string>? suggestions = null)
    {
        using var dlg = new InputDialog(title, prompt, value, suggestions);
        return dlg.ShowDialog(owner) == DialogResult.OK && dlg.Value.Length > 0 ? dlg.Value : null;
    }
}

/// <summary>단축키 목록(F1).</summary>
public sealed class ShortcutHelpDialog : DialogBase
{
    public static readonly (string Group, string Key, string Action)[] Shortcuts =
    [
        ("파일", "Ctrl+N", "새 프로젝트"),
        ("파일", "Ctrl+O", "열기(.gpixel / PNG 가져오기)"),
        ("파일", "Ctrl+Shift+O", "폴더 열기"),
        ("파일", "Ctrl+Shift+U", "Unity 프로젝트 열기(Unity Hub)"),
        ("파일", "Ctrl+S", "저장(.gpixel) + PNG 시트·.meta 내보내기"),
        ("파일", "Ctrl+Shift+S", "다른 이름으로 저장"),
        ("파일", "Ctrl+E", "지금 내보내기"),
        ("파일", "Ctrl+W", "닫기"),
        ("편집", "Ctrl+Z", "실행취소"),
        ("편집", "Ctrl+Y / Ctrl+Shift+Z", "다시 실행"),
        ("편집", "Ctrl+X / Ctrl+C / Ctrl+V", "잘라내기 / 복사(선택 없으면 프레임 전체) / 붙여넣기"),
        ("편집", "Delete", "선택 내용 지우기"),
        ("편집", "Ctrl+A / Ctrl+D", "모두 선택 / 선택 해제"),
        ("편집", "Enter / Esc", "떠 있는 선택 내려놓기 / 선택 해제"),
        ("편집", "Shift+H / Shift+V", "좌우 / 상하 뒤집기(선택, 없으면 현재 셀)"),
        ("편집", "Ctrl+R / Ctrl+Shift+R", "90° 회전(선택, 없으면 캔버스 전체)"),
        ("편집", "방향키 · Ctrl+Shift+방향키", "선택(이동 도구면 셀 내용) 1픽셀 옮기기"),
        ("편집", "Shift+방향키", "8픽셀 옮기기"),
        ("편집", "Ctrl+Alt+O", "외곽선 추가(기본색)"),
        ("편집", "Ctrl+Alt+R", "색 바꾸기"),
        ("편집", "Ctrl+Alt+C", "캔버스 크기"),
        ("프레임", "Shift+Enter", "재생 / 정지"),
        ("프레임", ", / .", "이전 / 다음 프레임(아틀라스: 스프라이트)"),
        ("프레임", "Home / End", "첫 / 마지막 프레임·스프라이트(캔버스 포커스)"),
        ("프레임", "Alt+N / Alt+D", "새 빈 프레임 / 프레임 복제(아틀라스: 새 스프라이트 / 스프라이트 복제)"),
        ("스프라이트", "F2 / 머리글 더블클릭", "아틀라스 스프라이트 이름 바꾸기"),
        ("프레임", "Shift+N", "새 레이어"),
        ("프레임", "H (누르고 있기)", "이전 프레임 보기"),
        ("도구", "B", "연필"),
        ("도구", "E", "지우개"),
        ("도구", "G", "채우기 (Shift+G: 같은 색 전체 전환)"),
        ("도구", "I (또는 Alt/Ctrl+클릭)", "스포이드"),
        ("도구", "L", "선 (Shift: 45° 단위)"),
        ("도구", "R", "사각형 (Shift: 정사각형)"),
        ("도구", "O", "원 (Shift: 정원)"),
        ("도구", "U", "도형 외곽 / 채움 전환"),
        ("도구", "M", "사각 선택"),
        ("도구", "V", "이동(선택 없으면 셀 전체, 순환 옵션)"),
        ("도구", "D", "밝게(왼쪽) / 어둡게(오른쪽)"),
        ("도구", "T", "디더링(체커 무늬)"),
        ("도구", "C", "색 바꾸기 브러시"),
        ("도구", "K", "피벗 끌기"),
        ("도구", "[ / ]", "브러시 크기 줄이기 / 늘리기"),
        ("도구", "P", "픽셀 퍼펙트 켜기/끄기"),
        ("도구", "X", "기본색 ↔ 보조색 교환"),
        ("도구", "Shift+X / Shift+Y", "좌우 / 상하 대칭 그리기"),
        ("도구", "Shift+클릭(연필류)", "마지막 점에서 직선"),
        ("보기", "휠", "확대 / 축소(1~64배)"),
        ("보기", "Space+드래그 / 가운데 버튼", "화면 이동"),
        ("보기", "Ctrl+0 / Ctrl+1", "화면에 맞추기 / 실제 크기"),
        ("보기", "Ctrl+= / Ctrl+-", "확대 / 축소"),
        ("보기", "Ctrl+G / Ctrl+Shift+G", "픽셀 격자 / 타일 격자"),
        ("보기", "Ctrl+T", "타일 모드(3×3 반복)"),
        ("보기", "Ctrl+J", "양파 껍질"),
        ("보기", "F8", "파일 브라우저 보이기/숨기기"),
        ("테스트", "F5", "움직임 테스트"),
        ("도움말", "F1", "단축키 목록"),
        ("마우스", "왼쪽 / 오른쪽 버튼", "기본색 / 보조색(옵션: 지우개)으로 그리기"),
    ];

    public ShortcutHelpDialog() : base("단축키")
    {
        CloseOnly();
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Width = 560,
            Height = 520,
            GridLines = false,
        };
        list.Columns.Add("분류", 70);
        list.Columns.Add("키", 200);
        list.Columns.Add("동작", 270);
        foreach (var (group, key, action) in Shortcuts)
            list.Items.Add(new ListViewItem([group, key, action]));
        Body.Controls.Add(list);
    }
}
