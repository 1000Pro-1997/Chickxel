using System.Globalization;
using Gpixel.App.Localization;
using Gpixel.Core.IO;

namespace Gpixel.App;

/// <summary>Tabs of <see cref="PresetsWindow"/>, in tab order.</summary>
public enum PresetsTab
{
    Gradients,
    AutoOptimize,
}

/// <summary>
/// One window for every preset kind, one tab each. It is modal: the gradient catalog is kept in memory while the window
/// is open and saved whole, so a gradient bar's "save current" or an open pixelate dialog must not change the same data
/// meanwhile (their next menu or run reads the saved result).
/// </summary>
public sealed class PresetsWindow : DialogBase
{
    private readonly ListBox _list;
    private readonly Button _favorite;
    private readonly Button _rename;
    private readonly Button _delete;
    private readonly Button _save;
    private readonly TextBlock _title;
    private readonly GradientSwatch _preview;
    private readonly GradientEditor _editor;
    private readonly Control _detail;
    private List<GradientPreset> _presets;
    /// <summary>The preset shown in the editor (null when nothing is selected).</summary>
    private GradientPreset? _loaded;
    private bool _dirty;
    private bool _refreshing;
    private bool _switchPending;

    public static void Open(Window owner, PresetsTab tab = PresetsTab.Gradients)
        => new PresetsWindow(GradientPresetUi.Load(owner), tab).ShowDialog(owner);

    /// <param name="presets">Gradient catalog; null when it could not be read (the tab is then read-only and empty
    /// so a damaged catalog is never overwritten).</param>
    private PresetsWindow(List<GradientPreset>? presets, PresetsTab tab) : base(UiStrings.Presets_Title)
    {
        _presets = presets ?? [];
        CloseOnly();
        CancelButtonControl.IsDefault = false; // Enter belongs to the number fields.
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        Width = 1000;
        Height = 680;
        MinWidth = 720;
        MinHeight = 480;

        // ── Gradients: list on the left, preview and editor of the selected preset on the right.
        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Background = AppTheme.InputBrush,
            Foreground = AppTheme.TextBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) => OnSelectionChanged();
        _favorite = Ui.Button(VectorIcon.Star, UiStrings.GradientPreset_AddFavorite, (_, _) => ToggleFavorite());
        _rename = Ui.Button(UiStrings.GradientPreset_Rename, (_, _) => Rename());
        _delete = Ui.Button(UiStrings.GradientPreset_Delete, (_, _) => Delete());
        var listButtons = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        listButtons.Children.AddRange([_favorite, _rename, _delete]);
        var left = new DockPanel();
        DockPanel.SetDock(listButtons, Dock.Bottom);
        left.Children.Add(listButtons);
        left.Children.Add(_list);

        _title = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            Foreground = AppTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _preview = new GradientSwatch { Height = 56, Margin = new Thickness(0, 0, 0, 8) };
        _editor = new GradientEditor(spatialAngle: true, presetMenu: false);
        _editor.Changed += OnEditorChanged;
        _save = Ui.Button(UiStrings.GradientPreset_SaveChanges, (_, _) => SaveChanges());
        _save.Margin = new Thickness(0, 8, 0, 0);
        _save.HorizontalAlignment = HorizontalAlignment.Left;
        _detail = new StackPanel { Children = { _preview, _editor, _save } };
        var right = new ScrollViewer
        {
            Content = new StackPanel { Children = { _title, _detail } },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(12, 0, 0, 0),
            Padding = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(right, 1);
        var gradients = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("300,*"),
            Margin = new Thickness(0, 8, 0, 0),
            Children = { left, right },
            IsEnabled = presets is not null,
        };

        // ── Auto optimize: the panel edits the presets inside the app settings; they are saved when the window closes.
        var options = App.Settings.Pixelate ??= new PixelateOptions();
        options.Migrate();
        var optimizer = new OptimizePresetsPanel(options) { Margin = new Thickness(0, 8, 0, 0) };

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = UiStrings.GradientPreset_Category, Content = gradients });
        tabs.Items.Add(new TabItem { Header = UiStrings.Presets_OptimizerTab, Content = optimizer });
        tabs.SelectedIndex = (int)tab;

        // The dialog body (StackPanel) gives no height, so the tabs go straight into the outer DockPanel's fill slot.
        var root = (DockPanel)Content!;
        root.Children.Remove(Body);
        root.Children.Add(tabs);

        Closing += (_, e) =>
        {
            if (!_dirty || _loaded is null) return;
            e.Cancel = true;
            Dispatcher.UIThread.Post(() =>
            {
                AskSaveChanges();
                Close();
            });
        };
        Closed += (_, _) => App.Settings.Save();
        Refresh(null, fallbackRow: 0);
    }

    private GradientPreset? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as GradientPreset;

    private static string Format(string format, string name) => string.Format(CultureInfo.CurrentCulture, format, name);

    private static Control Row(GradientPreset preset)
    {
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (preset.Favorite) name.Children.Add(Ui.Icon(VectorIcon.Star, 13, filled: true));
        name.Children.Add(Ui.Label(preset.Name));
        var swatch = new GradientSwatch { Preset = preset, Height = 14, Margin = new Thickness(0, 3, 0, 2) };
        return new StackPanel { Children = { name, swatch } };
    }

    /// <summary>Rebuilds the list (favorites first) and selects <paramref name="selectName"/>, else the row at
    /// <paramref name="fallbackRow"/>. <paramref name="keepEdits"/> keeps unsaved editor changes of the same preset.</summary>
    private void Refresh(string? selectName, int fallbackRow = -1, bool keepEdits = false)
    {
        _refreshing = true;
        try
        {
            _list.Items.Clear();
            var ordered = _presets.Where(p => p.Favorite).Concat(_presets.Where(p => !p.Favorite)).ToList();
            foreach (var preset in ordered)
                _list.Items.Add(new ListBoxItem { Content = Row(preset), Tag = preset, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            if (ordered.Count == 0)
                _list.Items.Add(new ListBoxItem { Content = UiStrings.GradientPreset_Empty, IsEnabled = false });
            else
            {
                int row = selectName is null ? -1 : ordered.FindIndex(p => p.Name == selectName);
                if (row < 0 && fallbackRow >= 0) row = Math.Min(fallbackRow, ordered.Count - 1);
                _list.SelectedIndex = row;
            }
        }
        finally { _refreshing = false; }
        if (keepEdits && _dirty && Selected is { } selected)
        {
            _loaded = selected;
            _title.Text = selected.Name;
            UpdateButtons();
        }
        else LoadSelected();
    }

    private void OnSelectionChanged()
    {
        if (_refreshing) return;
        if (!_dirty)
        {
            LoadSelected();
            return;
        }
        // Ask after the list has finished its selection change.
        if (_switchPending) return;
        _switchPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _switchPending = false;
            AskSaveChanges();
            LoadSelected();
        });
    }

    private void LoadSelected()
    {
        var preset = Selected;
        _loaded = preset;
        _dirty = false;
        _title.Text = preset?.Name ?? UiStrings.GradientPreset_SelectHint;
        _detail.IsEnabled = preset is not null;
        if (preset is not null) _editor.LoadPreset(preset);
        _preview.Preset = preset;
        UpdateButtons();
    }

    private void OnEditorChanged()
    {
        if (_loaded is not { } preset) return;
        _dirty = true;
        _preview.Preset = _editor.ToPreset(preset.Name, preset.Favorite);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool enabled = _loaded is not null;
        _favorite.IsEnabled = enabled;
        _rename.IsEnabled = enabled;
        _delete.IsEnabled = enabled;
        _save.IsEnabled = enabled && _dirty;
        _favorite.Content = Ui.IconLabel(VectorIcon.Star, _loaded?.Favorite == true
            ? UiStrings.GradientPreset_RemoveFavorite : UiStrings.GradientPreset_AddFavorite);
    }

    private int IndexOf(GradientPreset preset) => _presets.FindIndex(p => ReferenceEquals(p, preset));

    /// <summary>Saves the catalog with <paramref name="change"/> applied; the in-memory list changes only on success.</summary>
    private bool Commit(Action<List<GradientPreset>> change)
    {
        var next = _presets.ToList();
        change(next);
        if (!GradientPresetUi.Save(this, next)) return false;
        _presets = next;
        return true;
    }

    /// <summary>Unsaved edits of the previously shown preset: save them on Yes, drop them on No (list order is unchanged).</summary>
    private void AskSaveChanges()
    {
        if (!_dirty || _loaded is not { } preset) return;
        _dirty = false;
        if (Ui.Ask(this, Format(UiStrings.GradientPreset_SaveChangesQuestion, preset.Name), UiStrings.GradientPreset_SaveChanges,
                MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        int index = IndexOf(preset);
        if (index < 0) return;
        var updated = _editor.ToPreset(preset.Name, preset.Favorite);
        if (!Commit(next => next[index] = updated)) return;
        foreach (var item in _list.Items.OfType<ListBoxItem>())
        {
            if (!ReferenceEquals(item.Tag, preset)) continue;
            item.Tag = updated;
            item.Content = Row(updated);
        }
    }

    private void SaveChanges()
    {
        if (_loaded is not { } preset) return;
        int index = IndexOf(preset);
        if (index < 0) return;
        var updated = _editor.ToPreset(preset.Name, preset.Favorite);
        if (!Commit(next => next[index] = updated)) return;
        _dirty = false;
        Refresh(updated.Name);
    }

    private void ToggleFavorite()
    {
        if (_loaded is not { } preset) return;
        int index = IndexOf(preset);
        if (index < 0 || !Commit(next => next[index] = preset with { Favorite = !preset.Favorite })) return;
        Refresh(preset.Name, keepEdits: true);
    }

    private void Rename()
    {
        if (_loaded is not { } preset) return;
        int index = IndexOf(preset);
        if (index < 0) return;
        string? name = InputDialog.Ask(this, UiStrings.GradientPreset_RenameTitle,
            UiStrings.GradientPreset_NamePrompt, preset.Name, _presets.Select(p => p.Name))?.Trim();
        if (string.IsNullOrEmpty(name) || name == preset.Name) return;
        if (_presets.Where((_, i) => i != index).Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Ui.Warn(this, Format(UiStrings.GradientPreset_DuplicateName, name), UiStrings.GradientPreset_RenameTitle);
            return;
        }
        if (!Commit(next => next[index] = preset with { Name = name })) return;
        Refresh(name, keepEdits: true);
    }

    private void Delete()
    {
        if (_loaded is not { } preset) return;
        int index = IndexOf(preset);
        if (index < 0 || Ui.Ask(this, Format(UiStrings.GradientPreset_DeleteQuestion, preset.Name), UiStrings.GradientPreset_Delete,
                MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        int row = _list.SelectedIndex;
        if (!Commit(next => next.RemoveAt(index))) return;
        _dirty = false;
        Refresh(null, row);
    }
}
