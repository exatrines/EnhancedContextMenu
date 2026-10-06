using System.Runtime.InteropServices;
using Dalamud.Game.NativeWrapper;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace EnhancedContextMenu.Context;

internal readonly record struct MenuAnchor(float X, float Y, float Right, float Bottom);

/// <summary>Reads and closes the game context menu addon.</summary>
internal static unsafe class NativeContextMenu
{
    private static readonly uint ProcessId = (uint)Environment.ProcessId;

    internal static bool GameIsForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        return processId == ProcessId;
    }

    internal static bool IsOpen()
    {
        var addon = Find("ContextMenu");
        return !addon.IsNull && addon.IsVisible;
    }

    internal static bool HasFocus(nint addon)
    {
        if (addon == 0 || !IsOpen())
            return false;

        var manager = RaptureAtkUnitManager.Instance();
        return manager != null && (nint)manager->FocusedAddon == addon;
    }

    internal static bool TryGetAnchor(out MenuAnchor anchor)
    {
        anchor = default;
        var addon = Find("ContextMenu");
        if (addon.IsNull || !addon.IsVisible)
            return false;

        float x = addon.X;
        float y = addon.Y;
        var right = x + addon.ScaledWidth;
        var bottom = y + addon.ScaledHeight;
        var unit = (AtkUnitBase*)addon.Address;
        var bounds = new Bounds();
        unit->GetWindowBounds(&bounds);
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            x = bounds.Pos1.X;
            y = bounds.Pos1.Y;
            right = bounds.Pos2.X;
            bottom = bounds.Pos2.Y;
        }

        anchor = new MenuAnchor(x, y, right, bottom);
        return true;
    }

    internal static bool ParentStillThere(nint parent, ushort parentId)
    {
        if (parent == 0)
            return true;

        var manager = RaptureAtkUnitManager.Instance();
        return manager != null && (nint)manager->GetAddonById(parentId) == parent;
    }

    internal static bool TryFindList(int nativeCount, out nint addonPtr, out nint listPtr)
    {
        addonPtr = 0;
        listPtr = 0;
        if (nativeCount <= 0)
            return false;

        var handle = Find("ContextMenu");
        if (handle.IsNull || !handle.IsVisible)
            return false;

        var addon = (AtkUnitBase*)handle.Address;
        addonPtr = (nint)addon;
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            // Component nodes are stored as values at or above 1000. GetNodeType() reports 10000 instead.
            if (node == null || (ushort)node->Type < 1000)
                continue;

            var component = ((AtkComponentNode*)node)->Component;
            if (component == null || component->GetComponentType() != ComponentType.List)
                continue;

            var list = (AtkComponentList*)component;
            if (list->ListLength != nativeCount)
                continue;

            listPtr = (nint)list;
            return true;
        }

        return false;
    }

    internal static void Close()
    {
        CloseOne("AddonContextSub");
        CloseOne("ContextMenu");
    }

    private static void CloseOne(string name)
    {
        var addon = Find(name);
        if (!addon.IsNull && addon.IsVisible)
            ((AtkUnitBase*)addon.Address)->FireCallbackInt(-2);
    }

    private static AtkUnitBasePtr Find(string name) => PluginServices.GameGui.GetAddonByName(name, 1);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
