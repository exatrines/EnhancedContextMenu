using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Interface.Utility;
using EnhancedContextMenu.Context;

namespace EnhancedContextMenu.UI;

/// <summary>Side panel for plugin context entries. It follows the open game menu.</summary>
internal sealed class ActionsPanel
{
    private static readonly Vector4 PanelBg = new(87f / 255f, 85f / 255f, 87f / 255f, 230f / 255f);
    private static readonly Vector4 PanelBorder = new(120f / 255f, 120f / 255f, 120f / 255f, 0.8f);
    private static readonly Vector4 HeaderText = new(0.72f, 0.72f, 0.72f, 1f);
    private static readonly Vector4 RowHover = new(1f, 1f, 1f, 0.12f);
    private static readonly Vector4 RowSelected = new(1f, 1f, 1f, 0.22f);

    private readonly Plugin _plugin;
    private Vector2 _lastSize = new(220, 80);
    private float _fontScale = 1f;

    internal ActionsPanel(Plugin plugin)
    {
        _plugin = plugin;
    }

    internal void Draw()
    {
        if (!_plugin.TryCopyLevel(out var level))
            return;

        if (!NativeContextMenu.TryGetAnchor(out var anchor))
            return;

        var screen = ImGui.GetIO().DisplaySize;
        var baseSize = ImGui.GetFontSize();
        var target = Math.Clamp(C.FontSizePx, 12, 32) * Math.Clamp(C.FontScalePercent, 100, 300) / 100f;
        _fontScale = baseSize > 0 ? target / baseSize : 1f;

        var gap = 6f;
        var spot = Spot(anchor, C.Direction, gap);
        if (!Fits(spot, screen))
        {
            var flipped = Spot(anchor, Opposite(C.Direction), gap);
            if (Fits(flipped, screen))
                spot = flipped;
        }

        ImGui.SetNextWindowPos(spot.Position, ImGuiCond.Always, spot.Pivot);

        var maxHeight = MathF.Max(96f, screen.Y - 12f);
        ImGui.SetNextWindowSizeConstraints(new Vector2(160 * _fontScale, 0), new Vector2(460 * _fontScale, maxHeight));
        ImGui.SetNextWindowBgAlpha(1f);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, PanelBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6) * _fontScale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 2) * _fontScale);

        var flags = ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav;

        var began = false;
        try
        {
            var visible = ImGui.Begin("Plugin actions###ectx-actions", flags);
            began = true;
            if (visible)
            {
                ImGui.SetWindowFontScale(_fontScale);
                DrawLevel(level);
                _lastSize = ImGui.GetWindowSize();
            }
        }
        finally
        {
            if (began)
                ImGui.End();

            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(2);
        }
    }

    private readonly record struct PanelSpot(Vector2 Position, Vector2 Pivot);

    private static PanelSpot Spot(MenuAnchor anchor, PanelDirection direction, float gap)
    {
        var x = anchor.X + C.OffsetX;
        var y = anchor.Y + C.OffsetY;
        var pivot = Vector2.Zero;
        switch (direction)
        {
            case PanelDirection.Left:
                x = anchor.X - gap + C.OffsetX;
                pivot = new Vector2(1f, 0f);
                break;
            case PanelDirection.Up:
                y = anchor.Y - gap + C.OffsetY;
                pivot = new Vector2(0f, 1f);
                break;
            case PanelDirection.Down:
                y = anchor.Bottom + gap + C.OffsetY;
                break;
            default:
                x = anchor.Right + gap + C.OffsetX;
                break;
        }

        return new PanelSpot(new Vector2(x, y), pivot);
    }

    private bool Fits(PanelSpot spot, Vector2 screen)
    {
        var left = spot.Position.X - spot.Pivot.X * _lastSize.X;
        var top = spot.Position.Y - spot.Pivot.Y * _lastSize.Y;
        return left >= 0f
            && top >= 0f
            && left + _lastSize.X <= screen.X
            && top + _lastSize.Y <= screen.Y;
    }

    private static PanelDirection Opposite(PanelDirection direction) => direction switch
    {
        PanelDirection.Left => PanelDirection.Right,
        PanelDirection.Up => PanelDirection.Down,
        PanelDirection.Down => PanelDirection.Up,
        _ => PanelDirection.Left,
    };

    private void DrawLevel(PanelLevel level)
    {
        if (C.ShowHeader)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, HeaderText);
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(level.Title) ? I18n.Get("panel.title") : level.Title);
            ImGui.PopStyleColor();
            ImGui.Separator();
        }

        var width = Measure(level.Items);
        if (level.Nested && DrawBackRow(width))
            _plugin.Back();
        for (var i = 0; i < level.Items.Length; i++)
        {
            var item = level.Items[i];
            if (DrawEntryRow(i, item, width, level.Focused && i == level.Selected))
                _plugin.Execute(i);
        }
    }

    private float Measure(IMenuItem[] items)
    {
        var width = 200f * _fontScale;
        foreach (var item in items)
        {
            var text = item.Name?.TextValue ?? "";
            var row = ImGui.CalcTextSize(text).X + 56f * _fontScale;
            if (row > width)
                width = row;
        }

        return MathF.Min(width, 420f * _fontScale);
    }

    private bool DrawBackRow(float width)
    {
        var label = I18n.Get("panel.back");
        var height = ImGui.GetTextLineHeight() + 6f * _fontScale;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##ectx-back", new Vector2(width, height));
        if (ImGui.IsItemHovered())
            ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(RowHover));

        DrawListText(pos + new Vector2(6, 3) * _fontScale, ImGui.GetColorU32(ImGuiCol.Text), label);
        return clicked;
    }

    private bool DrawEntryRow(int index, IMenuItem item, float width, bool selected)
    {
        var height = ImGui.GetTextLineHeight() + 6f * _fontScale;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##ectx-item-{index}", new Vector2(width, height));
        if (selected)
            ImGui.SetScrollHereY(0.5f);
        var next = ImGui.GetCursorPos();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var enabled = MenuLevel.CanRun(item);
        if ((hovered || selected) && enabled)
            ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(selected ? RowSelected : RowHover));

        var style = new SeStringDrawParams
        {
            Opacity = enabled ? 1f : 0.45f,
            ScreenOffset = pos + new Vector2(6, 3) * _fontScale,
            TargetDrawList = ImGui.GetWindowDrawList(),
            Font = ImGui.GetFont(),
            FontSize = ImGui.GetFontSize(),
            Edge = false,
            Shadow = false,
        };
        ImGuiHelpers.SeStringWrapped(DisplayName(item).Encode(), style);
        ImGui.SetCursorPos(next);

        if (item.IsSubmenu)
            DrawChevron(min, max, ImGui.GetColorU32(ImGuiCol.Text, enabled ? 1f : 0.45f));

        return clicked && enabled;
    }

    private void DrawChevron(Vector2 min, Vector2 max, uint color)
    {
        var text = ">";
        var size = ImGui.CalcTextSize(text);
        var pos = new Vector2(max.X - size.X - 8f * _fontScale, min.Y + (max.Y - min.Y - size.Y) * 0.5f);
        DrawListText(pos, color, text);
    }

    private static void DrawListText(Vector2 pos, uint color, string text)
    {
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos, color, text);
    }

    private static SeString DisplayName(IMenuItem item)
    {
        if (!item.Prefix.HasValue)
        {
            item.Prefix = IMenuItem.DalamudDefaultPrefix;
            item.PrefixColor = IMenuItem.DalamudDefaultPrefixColor;
        }

        var name = item.Name ?? new SeString();
        // Values outside SeIconChar are strip markers. The game menu removes them before display.
        if (item.Prefix is not { } prefix || !Enum.IsDefined(prefix))
            return name;

        return new SeStringBuilder()
            .AddUiForeground($"{prefix.ToIconString()} ", item.PrefixColor)
            .Append(name)
            .Build();
    }
}
