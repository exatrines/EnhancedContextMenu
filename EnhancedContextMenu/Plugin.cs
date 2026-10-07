using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using EnhancedContextMenu.Context;
using EnhancedContextMenu.UI;

namespace EnhancedContextMenu;

/// <summary>Moves Dalamud context menu entries into a side panel.</summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/enhancedcontextmenu";

    internal static Configuration C = null!;

    private readonly WindowSystem _windowSystem;
    private readonly ConfigWindow _configWindow;
    private readonly ActionsPanel _panel;
    private readonly IAddonLifecycle _lifecycle;
    private readonly CommandInfo _command;
    private readonly object _sessionGate = new();
    private ContextCapture? _capture;
    private PadInput? _pad;
    private OmenMenuBridge? _omen;
    private MenuSession? _session;
    private bool _faulted;
    private bool _executing;
    private int _openWaitFrames;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IPluginLog log,
        IGameGui gameGui,
        IGameInteropProvider gameInterop,
        IContextMenu contextMenu,
        IAddonLifecycle lifecycle,
        ITextureProvider textureProvider)
    {
        PluginServices.Init(pluginInterface, commandManager, framework, log, gameGui);
        _lifecycle = lifecycle;

        C = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        C.Initialize(pluginInterface);
        C.ThemeColors ??= MirageColorSettings.CreateDefault();
        I18n.Apply();

        MirageUi.ConfigureTheme(() => C.ThemeColors ?? MirageColorSettings.CreateDefault());
        MirageUi.Init(pluginInterface, textureProvider, log);
        MirageUi.ConfigurePluginInfo(info =>
        {
            info.Message = "Join our Discord for updates and support!";
            info.DiscordUrl = "https://discord.gg/gRfxXNZWMs";
            info.SupportUrl = "https://exatrines.github.io/support/";
        });

        _panel = new ActionsPanel(this);
        _configWindow = new ConfigWindow(this);
        _windowSystem = new WindowSystem("EnhancedContextMenu");
        _windowSystem.AddWindow(_configWindow);

        try
        {
            _capture = new ContextCapture(this, contextMenu, gameInterop);
            try
            {
                _pad = new PadInput(this, gameInterop);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Gamepad context menu input is unavailable.");
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }

        try
        {
            _omen = new OmenMenuBridge(gameInterop);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "OmenTools context menu path is unavailable.");
        }

        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleConfig;
        pluginInterface.UiBuilder.OpenMainUi += ToggleConfig;
        pluginInterface.UiBuilder.HideUi += OnUiHidden;
        pluginInterface.LanguageChanged += OnLanguageChanged;
        pluginInterface.ActivePluginsChanged += OnPluginsChanged;
        framework.Update += OnFrameworkUpdate;
        lifecycle.RegisterListener(AddonEvent.PreFinalize, "ContextMenu", OnMenuGone);
        _command = new CommandInfo(OnCommand)
        {
            HelpMessage = I18n.Get("command.help"),
        };
        commandManager.AddHandler(CommandName, _command);
    }

    internal bool ShouldCapture => _capture != null && !_faulted && C.Enabled;

    internal void Fail(Exception ex)
    {
        if (_faulted)
            return;

        _faulted = true;
        PluginServices.Log.Error(ex, "Context menu capture stopped.");
        CloseSession();
    }

    internal void BeginSession(
        IMenuOpenedArgs args,
        IMenuItem[] items,
        int nativeCount,
        uint returnMask,
        uint submenuMask,
        MenuTargetWatch watch)
    {
        var session = new MenuSession(args, watch)
        {
            NativeCount = nativeCount,
            ReturnMask = returnMask,
            SubmenuMask = submenuMask,
            LoadedPlugins = PluginServices.PluginInterface.InstalledPlugins.Where(plugin => plugin.IsLoaded).ToArray(),
        };
        session.Push(PluginServices.PluginInterface.Manifest.Name ?? "Enhanced Context Menu", items);
        lock (_sessionGate)
        {
            _session = session;
            _openWaitFrames = 0;
        }
    }

    internal void CloseSession()
    {
        lock (_sessionGate)
        {
            if (_session != null)
                _pad?.ApplyLeave(_session);

            _session = null;
        }

        _omen?.Clear();
    }

    private bool SessionAlive(MenuSession session) =>
        NativeContextMenu.GameIsForeground()
        && session.StillValid()
        && session.PluginsStillLoaded();

    internal bool TryCopyLevel(out PanelLevel level)
    {
        lock (_sessionGate)
        {
            if (_session == null)
            {
                level = default;
                return false;
            }

            var current = _session.Current;
            level = new PanelLevel(current.Title, current.Items, _session.Depth > 1, _session.Focused, current.Selected);
            return true;
        }
    }

    internal OmenRow[] CopyOmenRows() => _omen?.CopyRows() ?? [];

    internal bool TryCopyOmenNest(out OmenView view)
    {
        view = default;
        if (_omen == null || !_omen.TryPeek(out var title, out var rows, out var selected))
            return false;

        var focused = false;
        lock (_sessionGate)
            focused = _session is { Focused: true };

        view = new OmenView(title, rows, focused, selected);
        return true;
    }

    internal void Back()
    {
        if (_omen?.Pop() == true)
            return;

        lock (_sessionGate)
            _session?.Pop();
    }

    internal void Execute(int index)
    {
        if (_executing)
            return;

        IMenuItem? item = null;
        IMenuArgs? source = null;
        var expired = false;
        lock (_sessionGate)
        {
            if (_session == null || index < 0 || index >= _session.Current.Items.Length)
                return;

            if (!SessionAlive(_session))
                expired = true;
            else
            {
                item = _session.Current.Items[index];
                source = _session.Args;
            }
        }

        if (expired)
        {
            CloseSession();
            return;
        }

        if (item == null || source == null)
            return;

        if (item.OnClicked is not { } clicked || !item.IsEnabled)
            return;

        _executing = true;
        var opened = false;
        var args = new ClickArgs(source, item.Name ?? new SeString(), (name, children) =>
        {
            opened = OpenSubmenu(item, name, children);
        });

        try
        {
            clicked(args);
        }
        catch (Exception ex)
        {
            PluginServices.Log.Error(ex, "Context menu entry failed.");
            opened = false;
        }
        finally
        {
            args.Active = false;
            _executing = false;
        }

        if (!opened)
        {
            NativeContextMenu.Close();
            CloseSession();
        }
    }

    private bool OpenSubmenu(IMenuItem parent, SeString? name, IReadOnlyList<IMenuItem> children)
    {
        if (_capture == null || children.Count == 0)
            return false;

        ContextMenuType type;
        lock (_sessionGate)
        {
            if (_session == null)
                return false;

            if (_session.Depth >= MenuSession.MaxDepth)
                return true;

            type = _session.Args.MenuType;
        }

        var visible = C.SelectVisible(type, children);
        if (visible.Length == 0)
            return true;

        var title = string.IsNullOrWhiteSpace(name?.TextValue) ? I18n.Get("panel.title") : name!.TextValue;
        if (!_capture.CanMoveAll(visible))
        {
            PluginServices.Log.Information("The last submenu was returned to the game menu.");
            var label = string.IsNullOrWhiteSpace(name?.TextValue)
                ? new SeStringBuilder().Append(title).Build()
                : name!;
            _capture.ReturnToGame(label, visible, parent);
            return true;
        }

        lock (_sessionGate)
        {
            if (_session == null)
                return false;

            _session.Push(title, visible);
            _pad?.Boundary();
            return true;
        }
    }

    internal void ActivateOmen(int index) => _omen?.Activate(index);

    internal void BindPadList()
    {
        lock (_sessionGate)
        {
            if (_session != null)
                _pad?.Bind(_session);
        }
    }

    internal bool TryGetPadState(out PadState state)
    {
        lock (_sessionGate)
        {
            if (_session == null)
            {
                state = default;
                return false;
            }

            state = new PadState(
                _session.Focused,
                _session.Addon,
                _session.List,
                _session.NativeCount,
                _session.ReturnMask,
                _session.SubmenuMask);
            return true;
        }
    }

    internal void EnterPad()
    {
        lock (_sessionGate)
        {
            if (_session is not { Focused: false } session)
                return;

            _pad?.ApplyEnter(session);
        }

        _pad?.Boundary();
    }

    internal void LeavePad()
    {
        lock (_sessionGate)
        {
            if (_session != null)
                _pad?.ApplyLeave(_session);
        }
    }

    internal void MovePad(int direction)
    {
        if (_omen?.Nested == true)
        {
            _omen.Move(direction);
            return;
        }

        lock (_sessionGate)
        {
            if (_session is not { Focused: true } session)
                return;

            var extra = session.Depth == 1 ? _omen?.Count ?? 0 : 0;
            session.Move(direction, extra);
        }
    }

    internal void OpenNestPad()
    {
        if (!C.PadHorizontalNest)
            return;

        if (_omen?.Nested == true)
        {
            _omen.OpenSelected();
            _pad?.Boundary();
            return;
        }

        int index;
        var omenIndex = -1;
        lock (_sessionGate)
        {
            if (_session is not { Focused: true } session)
                return;

            index = session.Current.Selected;
            if (index < 0)
                return;

            if (index >= session.Current.Items.Length)
            {
                omenIndex = index - session.Current.Items.Length;
            }
            else if (!session.Current.Items[index].IsSubmenu)
            {
                return;
            }
        }

        if (omenIndex >= 0)
        {
            if (_omen?.IsSubmenu(omenIndex) == true)
                ActivateOmen(omenIndex);

            _pad?.Boundary();
            return;
        }

        Execute(index);
        _pad?.Boundary();
    }

    internal void ConfirmPad()
    {
        if (_omen?.Nested == true)
        {
            _omen.Confirm();
            _pad?.Boundary();
            return;
        }

        int index;
        var omenIndex = -1;
        lock (_sessionGate)
        {
            if (_session is not { Focused: true } session)
                return;

            index = session.Current.Selected;
            if (index == MenuLevel.BackRow)
            {
                if (session.Depth > 1)
                    session.Pop();
                return;
            }

            if (index >= session.Current.Items.Length)
                omenIndex = index - session.Current.Items.Length;
        }

        if (omenIndex >= 0)
        {
            ActivateOmen(omenIndex);
            _pad?.Boundary();
            return;
        }

        Execute(index);
        _pad?.Boundary();
    }

    internal void BackPad()
    {
        if (_omen?.Pop() == true)
        {
            _pad?.Boundary();
            return;
        }

        lock (_sessionGate)
        {
            if (_session == null)
                return;

            if (_session.Depth > 1)
                _session.Pop();
            else
                _pad?.ApplyLeave(_session);
        }

        _pad?.Boundary();
    }

    public void Dispose()
    {
        PluginServices.Framework.Update -= OnFrameworkUpdate;
        PluginServices.CommandManager.RemoveHandler(CommandName);
        PluginServices.PluginInterface.UiBuilder.Draw -= Draw;
        PluginServices.PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfig;
        PluginServices.PluginInterface.UiBuilder.OpenMainUi -= ToggleConfig;
        PluginServices.PluginInterface.UiBuilder.HideUi -= OnUiHidden;
        PluginServices.PluginInterface.LanguageChanged -= OnLanguageChanged;
        PluginServices.PluginInterface.ActivePluginsChanged -= OnPluginsChanged;
        _lifecycle.UnregisterListener(AddonEvent.PreFinalize, "ContextMenu", OnMenuGone);

        CloseSession();
        _pad?.Dispose();
        _pad = null;
        _capture?.Dispose();
        _capture = null;
        _omen?.Dispose();
        _omen = null;

        C.Save();
        MirageUi.Dispose();
        _windowSystem.RemoveAllWindows();
        PluginServices.Clear();
        C = null!;
    }

    private void OnCommand(string command, string args) => ToggleConfig();

    private void ToggleConfig() => _configWindow.Toggle();

    private void OnPluginsChanged(IActivePluginsChangedEventArgs args) => CloseSession();

    internal void ApplyLanguage()
    {
        I18n.Apply();
        _command.HelpMessage = I18n.Get("command.help");
    }

    private void OnLanguageChanged(string lang) => ApplyLanguage();

    private void OnUiHidden() => CloseSession();

    private void OnMenuGone(AddonEvent type, AddonArgs args) => CloseSession();

    private void OnFrameworkUpdate(IFramework framework)
    {
        _pad?.Tick();
        _omen?.Tick();

        MenuSession? session;
        lock (_sessionGate)
            session = _session;

        if (session == null)
            return;

        if (!SessionAlive(session))
        {
            CloseSession();
            return;
        }

        var open = NativeContextMenu.IsOpen();
        var drop = false;
        lock (_sessionGate)
        {
            if (_session == null)
                return;

            if (open)
            {
                _openWaitFrames = -1;
                if (_session.List == 0)
                    _pad?.Bind(_session);
                return;
            }

            if (_openWaitFrames < 0 || ++_openWaitFrames > 5)
                drop = true;
        }

        if (drop)
            CloseSession();
    }

    private void Draw()
    {
        _windowSystem.Draw();
        _panel.Draw();
    }
}
