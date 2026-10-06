using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using MirageUI.Layout;

namespace EnhancedContextMenu.UI;

/// <summary>Settings: entry visibility and capture options.</summary>
internal sealed class ConfigWindow : Window
{
    private const string PageSettings = "settings";
    private const string PageEntries = "entries";
    private const float FieldLabelWidth = 160f;

    private readonly Plugin _plugin;
    private string _selectedId = PageSettings;
    private string _filter = "";
    private EntryFilter _shown = EntryFilter.All;
    private ImRaii.ColorDisposable? _themeScope;

    internal ConfigWindow(Plugin plugin)
        : base("Enhanced Context Menu###ectx-config", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _plugin = plugin;
        MirageWindowDefaults.ApplyTo(this);
    }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        MirageTheme.EnsureDefaultsCaptured();
        _themeScope = MirageTheme.PushCustom(MirageTheme.ResolveAppliedColors());
    }

    public override void PostDraw()
    {
        MirageTheme.Pop(_themeScope);
        _themeScope = null;
        ImGui.PopStyleVar();
    }

    public override void Draw()
    {
        MirageUi.TwoColumn.Draw(CreateState(), DrawPage);
    }

    private MirageTwoColumnState CreateState() =>
        new()
        {
            ShowSidebarHeader = true,
            ShowSearch = false,
            AllowDeselect = false,
            SelectedId = _selectedId,
            OnSelectionChanged = id =>
            {
                if (!string.IsNullOrEmpty(id))
                    _selectedId = id;
            },
            SidebarHeader = new MirageTwoColumnSidebarHeader
            {
                ImagePath = MirageUi.PluginInfo.IconPath,
                ImageWidth = 48f,
                ImageHeight = 48f,
                Title = PluginServices.PluginInterface.Manifest.Name ?? "Enhanced Context Menu",
                Subtitle = $"v{PluginServices.PluginInterface.Manifest.AssemblyVersion}",
            },
            Entries =
            [
                new MirageTwoColumnEntry { Id = PageSettings, Label = "Settings" },
                new MirageTwoColumnEntry { Id = PageEntries, Label = "Entries" },
            ],
        };

    private void DrawPage()
    {
        if (_selectedId == PageEntries)
            DrawEntries();
        else
            DrawSettings();
    }

    private void DrawSettings()
    {
        MirageUi.SubHeader(I18n.Get("settings.header.general"), pushDown: false);
        var enabled = C.Enabled;
        if (MirageUi.Checkbox(I18n.Get("options.enabled"), ref enabled))
        {
            C.Enabled = enabled;
            C.Save();
        }

        DrawLanguage();

        MirageUi.SubHeader(I18n.Get("settings.header.menu"));
        DrawDirections();
        DrawOffset();
        DrawFont();
        DrawColors();

        MirageUi.SubHeader(I18n.Get("settings.header.gamepad"));
        var nest = C.PadHorizontalNest;
        if (!MirageUi.Checkbox(I18n.Get("options.pad_nest_horizontal"), ref nest))
            return;

        C.PadHorizontalNest = nest;
        C.Save();
    }

    private static void DrawColors()
    {
        DrawColor(I18n.Get("settings.color.background"), "color-bg", C.Background, Configuration.DefaultBackground, value => C.Background = value);
        DrawColor(I18n.Get("settings.color.background_active"), "color-bg-active", C.BackgroundActive, Configuration.DefaultBackgroundActive, value => C.BackgroundActive = value);
        DrawColor(I18n.Get("settings.color.text"), "color-text", C.Text, Configuration.DefaultText, value => C.Text = value);
        DrawColor(I18n.Get("settings.color.text_active"), "color-text-active", C.TextActive, Configuration.DefaultTextActive, value => C.TextActive = value);

        var overrideColor = C.OverridePluginTextColor;
        if (MirageUi.Checkbox(I18n.Get("settings.color.override"), ref overrideColor))
        {
            C.OverridePluginTextColor = overrideColor;
            C.Save();
        }

        DrawColor(I18n.Get("settings.color.border"), "color-border", C.Border, Configuration.DefaultBorder, value => C.Border = value);

        var showHeader = C.ShowHeader;
        if (MirageUi.Checkbox(I18n.Get("options.show_header"), ref showHeader))
        {
            C.ShowHeader = showHeader;
            C.Save();
        }

        using (MirageUi.DisabledIf(!C.ShowHeader))
            DrawColor(I18n.Get("settings.color.header"), "color-header", C.HeaderText, Configuration.DefaultHeaderText, value => C.HeaderText = value);
    }

    private static void DrawColor(string label, string id, Vector4 color, Vector4 fallback, Action<Vector4> set)
    {
        Label(label);
        var changed = MirageUi.ColorEdit4("", ref color, id: id, width: FieldWidth(1));
        if (Undo(id))
        {
            if (color != fallback)
            {
                set(fallback);
                C.Save();
            }

            return;
        }

        if (!changed)
            return;

        set(color);
        C.Save();
    }

    private static void DrawOffset()
    {
        Label(I18n.Get("settings.label.offset"));
        var slider = FieldWidth(2);
        var gap = ImGui.GetStyle().ItemInnerSpacing.X;
        var x = (float)C.OffsetX;
        var y = (float)C.OffsetY;
        var changedX = MirageUi.SliderFloat("", ref x, -400f, 400f, "X: %.0f", id: "offset-x", width: slider);
        ImGui.SameLine(0f, gap);
        var changedY = MirageUi.SliderFloat("", ref y, -400f, 400f, "Y: %.0f", id: "offset-y", width: slider);
        if (Undo("offset"))
        {
            if (C.OffsetX != 0 || C.OffsetY != 0)
            {
                C.OffsetX = 0;
                C.OffsetY = 0;
                C.Save();
            }

            return;
        }

        if (changedX)
        {
            var nextX = (int)MathF.Round(x);
            if (C.OffsetX != nextX)
            {
                C.OffsetX = nextX;
                C.Save();
            }
        }

        if (!changedY)
            return;

        var nextY = (int)MathF.Round(y);
        if (C.OffsetY == nextY)
            return;

        C.OffsetY = nextY;
        C.Save();
    }

    private static void DrawDirections()
    {
        var current = (int)C.Direction;
        Label(I18n.Get("settings.label.direction"));
        var changed = Radio("dir-right", "settings.offset.right", ref current, (int)PanelDirection.Right);
        ImGui.SameLine();
        changed |= Radio("dir-left", "settings.offset.left", ref current, (int)PanelDirection.Left);
        ImGui.SameLine();
        changed |= Radio("dir-up", "settings.offset.up", ref current, (int)PanelDirection.Up);
        ImGui.SameLine();
        changed |= Radio("dir-down", "settings.offset.down", ref current, (int)PanelDirection.Down);
        if (!changed || current == (int)C.Direction)
            return;

        C.Direction = (PanelDirection)current;
        C.Save();
    }

    private static bool Radio(string id, string key, ref int current, int value) =>
        MirageUi.Radio($"{I18n.Get(key)}##{id}", ref current, value);

    private static void DrawFont()
    {
        var size = (float)C.FontSizePx;
        if (DrawFontSlider(
                I18n.Get("settings.label.text_size"),
                "font-size",
                ref size,
                12f,
                32f,
                "%.0f px",
                I18n.Get("settings.font.reset_size"),
                out var resetSize))
        {
            var next = (int)MathF.Round(size);
            if (C.FontSizePx != next)
            {
                C.FontSizePx = next;
                C.Save();
            }
        }

        if (resetSize)
        {
            var next = Math.Clamp((int)MathF.Round(ImGui.GetFontSize()), 12, 32);
            if (C.FontSizePx != next)
            {
                C.FontSizePx = next;
                C.Save();
            }
        }

        var scale = (float)C.FontScalePercent;
        if (DrawFontSlider(
                I18n.Get("settings.label.font_scale"),
                "font-scale",
                ref scale,
                100f,
                300f,
                "%.0f%%",
                I18n.Get("settings.font.reset_scale"),
                out var resetScale))
        {
            var next = (int)MathF.Round(scale);
            if (C.FontScalePercent != next)
            {
                C.FontScalePercent = next;
                C.Save();
            }
        }

        if (!resetScale || C.FontScalePercent == 100)
            return;

        C.FontScalePercent = 100;
        C.Save();
    }

    private static bool DrawFontSlider(
        string label,
        string id,
        ref float value,
        float min,
        float max,
        string format,
        string resetTooltip,
        out bool reset)
    {
        Label(label);
        var changed = MirageUi.SliderFloat("", ref value, min, max, format, id: id, width: FieldWidth(1));
        reset = Undo(id, resetTooltip);
        return changed;
    }

    private static void Label(string text)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(text);
        ImGui.SameLine(FieldLabelWidth);
    }

    private static float FieldWidth(int fields, int buttons = 1)
    {
        var gap = ImGui.GetStyle().ItemInnerSpacing.X;
        var button = ImGui.GetFrameHeight();
        var gaps = Math.Max(0, fields + buttons - 1);
        return MathF.Max(40f, (ImGui.GetContentRegionAvail().X - button * buttons - gap * gaps) / fields);
    }

    private static bool Undo(string id, string? tooltip = null)
    {
        ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
        return MirageUi.IconButton(
            FontAwesomeIcon.Undo,
            id: id + "-reset",
            size: new Vector2(ImGui.GetFrameHeight()),
            tooltip: tooltip ?? I18n.Get("settings.font.reset_scale"));
    }

    private void DrawLanguage()
    {
        var selected = C.Language is "en" or "ja" ? C.Language : "dalamud";
        var labels = new[]
        {
            I18n.Get("settings.language.client"),
            I18n.Get("settings.language.en"),
            I18n.Get("settings.language.ja"),
        };
        var values = new[] { "dalamud", "en", "ja" };
        var selectedLabel = labels[Array.IndexOf(values, selected)];
        if (!MirageUi.Dropdown(
                I18n.Get("settings.label.ui_language"),
                ref selectedLabel,
                labels,
                id: "uiLanguage",
                allowClear: false))
            return;

        var index = Array.IndexOf(labels, selectedLabel);
        if (index < 0)
            return;

        var next = values[index];
        if (C.Language == next)
            return;

        C.Language = next;
        C.Save();
        _plugin.ApplyLanguage();
    }

    private void DrawEntries()
    {
        DrawEntryTools();

        var entries = C.Snapshot();
        if (entries.Length == 0)
        {
            MirageUi.Text(I18n.Get("entries.empty"), MirageUi.Color.Secondary);
            return;
        }

        var filter = _filter.Trim();
        var shown = entries.Where(entry => Matches(entry, filter)).ToArray();
        if (shown.Length == 0)
        {
            MirageUi.Text(I18n.Get("entries.no_match"), MirageUi.Color.Secondary);
            return;
        }

        var index = 0;
        foreach (var group in shown
            .GroupBy(entry => string.IsNullOrWhiteSpace(entry.Source) ? I18n.Get("entries.unknown") : entry.Source)
            .OrderBy(group => group.Key))
        {
            MirageUi.SubHeader(group.Key);
            foreach (var entry in group.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var visible = !entry.Hidden;
                if (MirageUi.Checkbox($"{Caption(entry)}##ectx-entry-{index}", ref visible))
                    C.SetShown(entry, visible);
                index++;
            }
        }
    }

    private bool Matches(MenuEntryRecord entry, string filter)
    {
        if (_shown == EntryFilter.Enabled && entry.Hidden)
            return false;
        if (_shown == EntryFilter.Disabled && !entry.Hidden)
            return false;
        if (filter.Length == 0)
            return true;

        return entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Source.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string Caption(MenuEntryRecord entry)
    {
        var label = entry.Name.Replace("#", "").Trim();
        if (label.Length == 0)
            label = I18n.Get("entries.unnamed");
        if (label.Length > 80)
            label = label[..80];
        return label;
    }

    private void DrawEntryTools()
    {
        var filters = new[] { EntryFilter.All, EntryFilter.Enabled, EntryFilter.Disabled };
        var labels = filters.Select(FilterLabel).ToArray();
        var selected = FilterLabel(_shown);
        if (MirageUi.Dropdown("", ref selected, labels, id: "entry-shown", allowClear: false, width: PulldownWidth(labels)))
        {
            var index = Array.IndexOf(labels, selected);
            if (index >= 0)
                _shown = filters[index];
        }

        var gap = ImGui.GetStyle().ItemSpacing.X;
        var icon = MirageUi.ResolveControlHeight();
        ImGui.SameLine(0f, gap);
        var search = MathF.Max(40f, ImGui.GetContentRegionAvail().X - icon - gap);
        MirageUi.InputText("", ref _filter, id: "entry-filter", width: search);
        ImGui.SameLine(0f, gap);
        MirageUi.IconButton(FontAwesomeIcon.InfoCircle, "entry-info", tooltip: I18n.Get("entries.auto"));
    }

    private static float PulldownWidth(string[] labels)
    {
        var width = 0f;
        foreach (var label in labels)
            width = MathF.Max(width, ImGui.CalcTextSize(label).X);

        return width + ImGui.GetFrameHeight() + ImGui.GetStyle().FramePadding.X * 2f + 8f;
    }

    private static string FilterLabel(EntryFilter filter) => filter switch
    {
        EntryFilter.Enabled => I18n.Get("entries.filter.enable"),
        EntryFilter.Disabled => I18n.Get("entries.filter.disable"),
        _ => I18n.Get("entries.filter.all"),
    };

    private enum EntryFilter
    {
        All,
        Enabled,
        Disabled,
    }
}
