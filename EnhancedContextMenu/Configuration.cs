using Dalamud.Configuration;
using Dalamud.Game.Gui.ContextMenu;
using EnhancedContextMenu.Context;

namespace EnhancedContextMenu;

/// <summary>Persisted plugin settings.</summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    internal static readonly Vector4 DefaultBackground = new(87f / 255f, 85f / 255f, 87f / 255f, 230f / 255f);

    internal static readonly Vector4 DefaultBackgroundActive = new(
        (255f * 0.22f + 87f * 0.78f) / 255f,
        (255f * 0.22f + 85f * 0.78f) / 255f,
        (255f * 0.22f + 87f * 0.78f) / 255f,
        1f);

    internal static readonly Vector4 DefaultHeaderText = new(0.72f, 0.72f, 0.72f, 1f);

    internal static readonly Vector4 DefaultBorder = new(120f / 255f, 120f / 255f, 120f / 255f, 0.8f);

    internal static readonly Vector4 DefaultText = Vector4.One;

    internal static readonly Vector4 DefaultTextActive = Vector4.One;

    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public bool ShowHeader { get; set; } = true;

    public bool PadHorizontalNest { get; set; }

    public int FontSizePx { get; set; } = 16;

    public int FontScalePercent { get; set; } = 100;

    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    public PanelDirection Direction { get; set; } = PanelDirection.Right;

    public Vector4 Background { get; set; } = DefaultBackground;

    public Vector4 BackgroundActive { get; set; } = DefaultBackgroundActive;

    public Vector4 HeaderText { get; set; } = DefaultHeaderText;

    public Vector4 Border { get; set; } = DefaultBorder;

    public Vector4 Text { get; set; } = DefaultText;

    public Vector4 TextActive { get; set; } = DefaultTextActive;

    public bool OverridePluginTextColor { get; set; }

    public string Language { get; set; } = "dalamud";

    public List<MenuEntryRecord> Entries { get; set; } = [];

    public MirageColorSettings? ThemeColors { get; set; }

    [NonSerialized]
    private IDalamudPluginInterface? _pluginInterface;

    [NonSerialized]
    private object? _gate;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        Entries ??= [];
        if (Language is not ("dalamud" or "en" or "ja"))
            Language = "dalamud";
        FontSizePx = Math.Clamp(FontSizePx, 12, 32);
        FontScalePercent = Math.Clamp(FontScalePercent, 100, 300);
        OffsetX = Math.Clamp(OffsetX, -400, 400);
        OffsetY = Math.Clamp(OffsetY, -400, 400);
        if (!Enum.IsDefined(Direction))
            Direction = PanelDirection.Right;
        Background = ClampColor(Background);
        BackgroundActive = ClampColor(BackgroundActive);
        HeaderText = ClampColor(HeaderText);
        Border = ClampColor(Border);
        Text = ClampColor(Text);
        TextActive = ClampColor(TextActive);
        if (Entries.RemoveAll(entry => string.IsNullOrEmpty(entry.Callback)) > 0)
            Save();
        _gate ??= new object();
    }

    public void Save() => _pluginInterface?.SavePluginConfig(this);

    private static Vector4 ClampColor(Vector4 color) => new(
        Math.Clamp(color.X, 0f, 1f),
        Math.Clamp(color.Y, 0f, 1f),
        Math.Clamp(color.Z, 0f, 1f),
        Math.Clamp(color.W, 0f, 1f));

    internal IMenuItem[] SelectVisible(ContextMenuType type, IReadOnlyList<IMenuItem> items)
    {
        lock (Gate)
        {
            Remember(type, items);
            return items
                .Where(item => !IsHidden(type, item))
                .OrderBy(item => item.Priority)
                .ToArray();
        }
    }

    internal MenuEntryRecord[] Snapshot()
    {
        lock (Gate)
            return Entries.ToArray();
    }

    internal void SetShown(MenuEntryRecord sample, bool shown)
    {
        lock (Gate)
        {
            var entry = Find(sample.MenuType, sample.Callback, sample.Name);
            if (entry == null || entry.Hidden == !shown)
                return;

            entry.Hidden = !shown;
            Save();
        }
    }

    private object Gate => _gate ??= new object();

    private void Remember(ContextMenuType type, IReadOnlyList<IMenuItem> items)
    {
        var changed = false;
        foreach (var item in items)
        {
            var menuType = (int)type;
            var callback = MenuEntryKey.Callback(item);
            var name = MenuEntryKey.Name(item);
            var source = MenuEntryKey.Source(item);
            var existing = Find(menuType, callback, name);
            if (existing == null)
            {
                if (Entries.Count >= MenuEntryKey.MaxEntries)
                    continue;

                Entries.Add(new MenuEntryRecord
                {
                    MenuType = menuType,
                    Callback = callback,
                    Name = name,
                    Source = source,
                });
                changed = true;
                continue;
            }

            if (existing.Source == source)
                continue;

            existing.Source = source;
            changed = true;
        }

        if (changed)
            Save();
    }

    private bool IsHidden(ContextMenuType type, IMenuItem item) =>
        Find((int)type, MenuEntryKey.Callback(item), MenuEntryKey.Name(item)) is { Hidden: true };

    private MenuEntryRecord? Find(int menuType, string callback, string name) =>
        Entries.FirstOrDefault(entry =>
            entry.MenuType == menuType && entry.Callback == callback && entry.Name == name);
}

/// <summary>Which side of the native menu the panel opens toward.</summary>
public enum PanelDirection
{
    Right,
    Left,
    Up,
    Down,
}

/// <summary>One discovered context menu entry. Callbacks are not stored.</summary>
[Serializable]
public sealed class MenuEntryRecord
{
    public int MenuType { get; set; }

    public string Callback { get; set; } = "";

    public string Name { get; set; } = "";

    public string Source { get; set; } = "";

    public bool Hidden { get; set; }
}
