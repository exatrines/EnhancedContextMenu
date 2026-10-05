using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;

namespace EnhancedContextMenu.Context;

/// <summary>
/// Takes Dalamud context entries off the game menu while it is being built.
/// Entries that cannot be tracked or moved are put back on the game menu.
/// </summary>
internal sealed unsafe class ContextCapture : IDisposable
{
    private delegate ushort OpenDelegate(
        AtkModule* module,
        CStringPointer name,
        int count,
        AtkValue* values,
        AgentInterface* agent,
        nint a7,
        bool a8);

    private readonly Plugin _plugin;
    private readonly IContextMenu _parent;
    private readonly FieldInfo _selectedItems;
    private readonly FieldInfo _submenuItems;
    private readonly Action<SeString, IReadOnlyList<IMenuItem>, int, int> _openNative;
    private readonly HashSet<string> _nativeOnly = [];
    private readonly Hook<OpenDelegate> _hook;
    private int _depth;
    private bool _observed;
    private bool _passingNative;
    private int _nativeCount;
    private uint _returnMask;
    private uint _submenuMask;

    internal ContextCapture(Plugin plugin, IContextMenu scoped, IGameInteropProvider interop)
    {
        _plugin = plugin;
        _parent = ResolveParent(scoped);
        _selectedItems = Require(_parent.GetType(), "<SelectedItems>k__BackingField");
        _submenuItems = Require(_parent.GetType(), "<SubmenuItems>k__BackingField");
        if (_selectedItems.FieldType != typeof(List<IMenuItem>))
            throw new NotSupportedException("Dalamud SelectedItems layout does not match this build.");

        var openNative = _parent.GetType().GetMethod(
            "OpenSubmenu",
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(SeString), typeof(IReadOnlyList<IMenuItem>), typeof(int), typeof(int)]);
        if (openNative == null)
            throw new NotSupportedException("Native submenu fallback is unavailable.");

        _openNative = openNative.CreateDelegate<Action<SeString, IReadOnlyList<IMenuItem>, int, int>>(_parent);

        var table = RaptureAtkModule.StaticVirtualTablePointer;
        if (table == null)
            throw new NotSupportedException("RaptureAtkModule vtable is unavailable.");

        var address = ((nint*)table)[22];
        if (address == 0)
            throw new NotSupportedException("Context menu open address is empty.");

        _hook = interop.HookFromAddress<OpenDelegate>(address, Open);
        _hook.Enable();
    }

    internal bool CanMoveAll(IReadOnlyList<IMenuItem> items)
    {
        foreach (var item in items)
        {
            if (!CanMove(item))
                return false;
        }

        return true;
    }

    internal void ReturnToGame(SeString title, IReadOnlyList<IMenuItem> items, IMenuItem? parent)
    {
        RememberNative(parent);
        var addon = PluginServices.GameGui.GetAddonByName("ContextMenu", 1);
        var x = addon.IsNull ? 0 : (int)addon.X;
        var y = addon.IsNull ? 0 : (int)addon.Y;
        _plugin.CloseSession();
        _passingNative = true;
        try
        {
            _openNative(title, items, x, y);
        }
        finally
        {
            _passingNative = false;
        }
    }

    public void Dispose() => _hook.Dispose();

    private ushort Open(
        AtkModule* module,
        CStringPointer name,
        int count,
        AtkValue* values,
        AgentInterface* agent,
        nint a7,
        bool a8)
    {
        var addonName = name.AsSpan();
        var isRoot = addonName.SequenceEqual("ContextMenu"u8);
        var isSub = addonName.SequenceEqual("AddonContextSub"u8);

        if (isSub && _plugin.ShouldCapture)
        {
            _plugin.CloseSession();
            if (!_passingNative)
                _submenuItems.SetValue(_parent, null);
        }

        if (!isRoot || !_plugin.ShouldCapture || _depth > 0)
            return _hook.Original(module, name, count, values, agent, a7, a8);

        _plugin.CloseSession();
        if (values != null && count >= 8)
        {
            _nativeCount = (int)values[0].UInt;
            _returnMask = values[2].UInt;
            _submenuMask = values[3].UInt;
        }
        else
        {
            _nativeCount = 0;
            _returnMask = 0;
            _submenuMask = 0;
        }

        _depth++;
        _observed = false;
        var expectEvent = IsSupportedAgent(agent);
        _parent.OnMenuOpened += Capture;
        try
        {
            var result = _hook.Original(module, name, count, values, agent, a7, a8);
            _plugin.BindPadList();
            if (expectEvent && !_observed)
            {
                _plugin.Fail(new InvalidOperationException(
                    "Context menu capture did not run. Hook order does not match this Dalamud build."));
            }

            return result;
        }
        catch (Exception ex)
        {
            _plugin.Fail(ex);
            return 0;
        }
        finally
        {
            _parent.OnMenuOpened -= Capture;
            _depth--;
        }
    }

    private void Capture(IMenuOpenedArgs args)
    {
        _observed = true;
        if (_passingNative)
            return;

        if (_selectedItems.GetValue(_parent) is not List<IMenuItem> items || items.Count == 0)
        {
            _plugin.CloseSession();
            return;
        }

        var visible = C.SelectVisible(args.MenuType, items.ToArray());
        items.Clear();
        if (visible.Length == 0)
        {
            PluginServices.Log.Information("Every plugin entry on the last menu is hidden.");
            _plugin.CloseSession();
            return;
        }

        string? stayed = null;
        if (!MenuTargetWatch.TryCreate(args, out var watch))
            stayed = "The last menu stayed on the game menu. Its target could not be tracked.";
        else if (!CanMoveAll(visible))
            stayed = "The last menu stayed on the game menu. An entry could not be moved.";

        if (stayed != null)
        {
            items.AddRange(visible);
            PluginServices.Log.Information(stayed);
            _plugin.CloseSession();
            return;
        }

        _plugin.BeginSession(args, visible, _nativeCount, _returnMask, _submenuMask, watch);
    }

    private bool CanMove(IMenuItem item)
    {
        if (!item.IsEnabled || item.OnClicked == null)
            return true;

        try
        {
            foreach (var callback in item.OnClicked.GetInvocationList())
            {
                if (callback.Method.IsDefined(typeof(AsyncStateMachineAttribute), false))
                    return false;

                if (_nativeOnly.Contains(MenuEntryKey.MethodId(callback.Method)))
                    return false;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private void RememberNative(IMenuItem? parent)
    {
        if (parent?.OnClicked == null)
            return;

        try
        {
            foreach (var callback in parent.OnClicked.GetInvocationList())
                _nativeOnly.Add(MenuEntryKey.MethodId(callback.Method));
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static bool IsSupportedAgent(AgentInterface* agent)
    {
        if (agent == null)
            return false;

        return agent == (AgentInterface*)AgentInventoryContext.Instance()
            || agent == (AgentInterface*)AgentContext.Instance();
    }

    private static IContextMenu ResolveParent(IContextMenu scoped)
    {
        var parentField = scoped.GetType().GetField("parentService", BindingFlags.Instance | BindingFlags.NonPublic);
        if (parentField?.GetValue(scoped) is IContextMenu parent)
            return parent;

        return scoped;
    }

    private static FieldInfo Require(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new NotSupportedException($"Dalamud ContextMenu field '{name}' was not found.");
}
