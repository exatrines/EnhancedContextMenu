using System.Reflection;
using Dalamud.Game.Gui.ContextMenu;

namespace EnhancedContextMenu.Context;

/// <summary>Identity of a context menu entry. The callback itself is not stored.</summary>
internal static class MenuEntryKey
{
    internal const int MaxEntries = 2048;

    internal static string MethodId(MethodInfo method) =>
        $"{method.Module.ModuleVersionId:N}:{method.MetadataToken}";

    internal static string Callback(IMenuItem item)
    {
        try
        {
            var method = item.OnClicked?.Method;
            return method == null ? "none" : MethodId(method);
        }
        catch (InvalidOperationException)
        {
            return "dynamic";
        }
        catch (NotSupportedException)
        {
            return "dynamic";
        }
    }

    internal static string Name(IMenuItem item) => item.Name?.TextValue ?? "";

    internal static string Source(IMenuItem item)
    {
        try
        {
            return item.OnClicked?.Method.DeclaringType?.Assembly.GetName().Name ?? "Unknown";
        }
        catch (InvalidOperationException)
        {
            return "Unknown";
        }
        catch (NotSupportedException)
        {
            return "Unknown";
        }
    }
}
