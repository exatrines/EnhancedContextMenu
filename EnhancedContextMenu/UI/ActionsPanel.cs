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
        var gap = 6f;
        var placeLeft = anchor.Right + gap + _lastSize.X > screen.X;
        if (placeLeft)
            ImGui.SetNextWindowPos(new Vector2(anchor.X - gap, anchor.Y), ImGuiCond.Always, new Vector2(1, 0));
        else
            ImGui.SetNextWindowPos(new Vector2(anchor.Right + gap, anchor.Y), ImGuiCond.Always);

        var maxHeight = MathF.Max(96f, screen.Y - 12f);
        ImGui.SetNextWindowSizeConstraints(new Vector2(160, 0), new Vector2(460, maxHeight));
        ImGui.SetNextWindowBgAlpha(1f);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, PanelBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 2));

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

    private static float Measure(IMenuItem[] items)
    {
        var width = 200f;
        foreach (var item in items)
        {
            var text = item.Name?.TextValue ?? "";
            var row = ImGui.CalcTextSize(text).X + 56f;
            if (row > width)
                width = row;
        }

        return MathF.Min(width, 420f);
    }

    private static bool DrawBackRow(float width)
    {
        var label = I18n.Get("panel.back");
        var height = ImGui.GetTextLineHeight() + 6f;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##ectx-back", new Vector2(width, height));
        if (ImGui.IsItemHovered())
            ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(RowHover));

        ImGui.GetWindowDrawList().AddText(pos + new Vector2(6, 3), ImGui.GetColorU32(ImGuiCol.Text), label);
        return clicked;
    }

    private static bool DrawEntryRow(int index, IMenuItem item, float width, bool selected)
    {
        var height = ImGui.GetTextLineHeight() + 6f;
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
            ScreenOffset = pos + new Vector2(6, 3),
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

    private static void DrawChevron(Vector2 min, Vector2 max, uint color)
    {
        var text = ">";
        var size = ImGui.CalcTextSize(text);
        var pos = new Vector2(max.X - size.X - 8f, min.Y + (max.Y - min.Y - size.Y) * 0.5f);
        ImGui.GetWindowDrawList().AddText(pos, color, text);
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
