using Dalamud.Game.Command;
using Dalamud.Game.Gui;

namespace EnhancedContextMenu.Services;

/// <summary>Dalamud services injected at plugin startup.</summary>
internal static class PluginServices
{
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    internal static ICommandManager CommandManager { get; private set; } = null!;
    internal static IFramework Framework { get; private set; } = null!;
    internal static IPluginLog Log { get; private set; } = null!;
    internal static IGameGui GameGui { get; private set; } = null!;

    internal static void Init(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IPluginLog log,
        IGameGui gameGui)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        Framework = framework;
        Log = log;
        GameGui = gameGui;
    }

    internal static void Clear()
    {
        PluginInterface = null!;
        CommandManager = null!;
        Framework = null!;
        Log = null!;
        GameGui = null!;
    }
}
