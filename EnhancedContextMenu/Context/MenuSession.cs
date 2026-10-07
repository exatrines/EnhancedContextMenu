using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin;

namespace EnhancedContextMenu.Context;

internal readonly record struct PanelLevel(
    string Title,
    IMenuItem[] Items,
    bool Nested,
    bool Focused,
    int Selected);

/// <summary>Plugin entries taken from one open of the game context menu.</summary>
internal sealed class MenuSession
{
    internal const int MaxDepth = 32;

    private readonly Stack<MenuLevel> _levels = new();

    internal MenuSession(IMenuArgs args, MenuTargetWatch watch)
    {
        Args = args;
        Watch = watch;
    }

    internal IMenuArgs Args { get; }

    internal MenuTargetWatch Watch { get; }

    internal IExposedPlugin[] LoadedPlugins { get; set; } = [];

    internal int NativeCount { get; set; }

    internal uint ReturnMask { get; set; }

    internal uint SubmenuMask { get; set; }

    internal nint Addon { get; set; }

    internal nint List { get; set; }

    internal int NativeSelection { get; set; } = -1;

    internal bool Focused { get; set; }

    internal bool StillValid() =>
        Watch.Unchanged() && NativeContextMenu.ParentStillThere(Watch.Parent, Watch.ParentId);

    internal bool PluginsStillLoaded()
    {
        foreach (var plugin in LoadedPlugins)
        {
            if (!plugin.IsLoaded)
                return false;
        }

        return true;
    }

    internal int Depth => _levels.Count;

    internal MenuLevel Current => _levels.Peek();

    internal void Move(int direction, int extra = 0) => Current.Move(direction, Depth > 1, extra);

    internal void Push(string title, IMenuItem[] items)
    {
        var level = new MenuLevel(title, items);
        level.Selected = _levels.Count > 0 ? MenuLevel.BackRow : FirstEnabled(items);
        _levels.Push(level);
    }

    internal void Pop()
    {
        if (_levels.Count > 1)
            _levels.Pop();
    }

    private static int FirstEnabled(IMenuItem[] items)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (MenuLevel.CanRun(items[i]))
                return i;
        }

        return 0;
    }
}

internal sealed class MenuLevel
{
    internal const int BackRow = -1;

    internal MenuLevel(string title, IMenuItem[] items)
    {
        Title = title;
        Items = items;
    }

    internal string Title { get; }

    internal IMenuItem[] Items { get; }

    internal int Selected { get; set; }

    internal static bool CanRun(IMenuItem item) => item.IsEnabled && item.OnClicked != null;

    internal void Move(int direction, bool includeBack, int extra = 0)
    {
        if (direction == 0)
            return;

        var choices = new List<int>();
        if (includeBack)
            choices.Add(BackRow);
        for (var i = 0; i < Items.Length; i++)
        {
            if (CanRun(Items[i]))
                choices.Add(i);
        }

        for (var i = 0; i < extra; i++)
            choices.Add(Items.Length + i);

        if (choices.Count == 0)
            return;

        var at = choices.IndexOf(Selected);
        if (at < 0)
            at = 0;

        var slot = at + direction;
        slot %= choices.Count;
        if (slot < 0)
            slot += choices.Count;
        Selected = choices[slot];
    }
}

/// <summary>Click arguments passed back to the plugin that registered the entry.</summary>
internal sealed class ClickArgs : IMenuItemClickedArgs
{
    private static readonly IReadOnlySet<nint> EmptyInterfaces = new HashSet<nint>();

    private readonly IMenuArgs _source;
    private readonly SeString _fallbackName;
    private readonly Action<SeString?, IReadOnlyList<IMenuItem>> _openSubmenu;

    internal ClickArgs(IMenuArgs source, SeString fallbackName, Action<SeString?, IReadOnlyList<IMenuItem>> openSubmenu)
    {
        _source = source;
        _fallbackName = fallbackName;
        _openSubmenu = openSubmenu;
    }

    internal bool Active { get; set; } = true;

    public IReadOnlySet<nint> EventInterfaces => _source.EventInterfaces ?? EmptyInterfaces;

    public string AddonName => _source.AddonName ?? "";

    public nint AddonPtr => _source.AddonPtr;

    public nint AgentPtr => _source.AgentPtr;

    public ContextMenuType MenuType => _source.MenuType;

    public MenuTarget Target => _source.Target;

    public void OpenSubmenu(SeString name, IReadOnlyList<IMenuItem> items) =>
        TryOpen(name ?? _fallbackName, items);

    public void OpenSubmenu(IReadOnlyList<IMenuItem> items) =>
        TryOpen(_fallbackName, items);

    private void TryOpen(SeString name, IReadOnlyList<IMenuItem>? items)
    {
        if (!Active || items == null || items.Count == 0)
            return;

        _openSubmenu(name, items);
    }
}
