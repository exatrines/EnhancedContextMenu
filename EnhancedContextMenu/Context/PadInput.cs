using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace EnhancedContextMenu.Context;

internal readonly record struct PadState(
    bool Focused,
    nint Addon,
    nint List,
    int NativeCount,
    uint ReturnMask,
    uint SubmenuMask);

/// <summary>
/// Routes the game's mapped menu inputs while a context menu is open.
/// Confirm and cancel follow the player's button settings.
/// </summary>
internal sealed unsafe class PadInput : IDisposable
{
    private static readonly InputId[] Routed =
    [
        InputId.UP,
        InputId.DOWN,
        InputId.LEFT,
        InputId.RIGHT,
        InputId.OK,
        InputId.PAD_OK,
        InputId.CANCEL,
        InputId.PAD_CANCEL,
        InputId.ESC,
    ];

    private readonly Plugin _plugin;
    private readonly Hook<AtkEventDispatcher.Delegates.DispatchEvent> _dispatch;
    private readonly Hook<AddonContextMenu.Delegates.OnMenuSelected> _selection;
    private readonly Hook<AddonContextMenu.Delegates.HandleDPadInput> _dpad;
    private readonly InputGate _gate = new();
    private nint _cursorNode;
    private bool _cursorWasVisible;
    private long _swallowUntil;
    private bool _disabled;

    internal PadInput(Plugin plugin, IGameInteropProvider interop)
    {
        _plugin = plugin;
        var table = AddonContextMenu.StaticVirtualTablePointer;
        if (table == null)
            throw new NotSupportedException("Context menu input table is unavailable.");

        _dispatch = interop.HookFromAddress<AtkEventDispatcher.Delegates.DispatchEvent>(
            AtkEventDispatcher.MemberFunctionPointers.DispatchEvent,
            Dispatch);
        _selection = interop.HookFromAddress<AddonContextMenu.Delegates.OnMenuSelected>(
            (nint)table->OnMenuSelected,
            OnSelected);
        _dpad = interop.HookFromAddress<AddonContextMenu.Delegates.HandleDPadInput>(
            (nint)table->HandleDPadInput,
            OnDPad);

        _dispatch.Enable();
        _selection.Enable();
        _dpad.Enable();
    }

    internal void Tick()
    {
        PollRelease();
        if (_plugin.TryGetPadState(out var state) && state.Focused && NativeContextMenu.GameIsForeground())
            HideCursor();
    }

    internal void Boundary()
    {
        var now = Environment.TickCount64;
        _gate.Boundary(now);
        _swallowUntil = now + 180;
    }

    internal void Bind(MenuSession session)
    {
        if (!NativeContextMenu.TryFindList(session.NativeCount, out var addon, out var list))
            return;

        session.Addon = addon;
        session.List = list;
    }

    internal bool CanEnter(in PadState state)
    {
        if (state.Focused || state.List == 0 || state.NativeCount <= 0)
            return false;

        if (!NativeContextMenu.GameIsForeground() || !NativeContextMenu.HasFocus(state.Addon))
            return false;

        var list = (AtkComponentList*)state.List;
        var index = list->SelectedItemIndex;
        if (index < 0)
            index = list->HoveredItemIndex;

        var mask = state.ReturnMask | state.SubmenuMask;
        if (index < 0)
            return mask == 0;

        return index < state.NativeCount && index < 32 && (mask & (1u << index)) == 0;
    }

    internal void ApplyEnter(MenuSession session)
    {
        if (session.List != 0)
        {
            var list = (AtkComponentList*)session.List;
            session.NativeSelection = list->SelectedItemIndex;
            list->DeselectItem();
        }

        session.Focused = true;
        HideCursor();
    }

    internal void ApplyLeave(MenuSession session)
    {
        if (session.Focused && session.List != 0 && session.NativeSelection >= 0 && NativeContextMenu.IsOpen())
            ((AtkComponentList*)session.List)->SelectItem(session.NativeSelection, false);

        session.Focused = false;
        RestoreCursor();
    }

    public void Dispose()
    {
        _disabled = true;
        _dpad.Dispose();
        _selection.Dispose();
        _dispatch.Dispose();
        RestoreCursor();
        _gate.Clear();
    }

    private bool Dispatch(AtkEventDispatcher* dispatcher, AtkEventDispatcher.Event* evt)
    {
        if (_disabled || evt == null)
            return _dispatch.Original(dispatcher, evt);

        if (evt->State.EventType is not (AtkEventType.InputReceived or AtkEventType.InputNavigation or AtkEventType.InputBaseInputReceived))
            return _dispatch.Original(dispatcher, evt);

        try
        {
            var data = evt->EventData.InputData;
            if (Route((InputId)data.InputId, data.State))
                return Consume(evt);

            return _dispatch.Original(dispatcher, evt);
        }
        catch (Exception ex)
        {
            Stop(ex);
            return _dispatch.Original(dispatcher, evt);
        }
    }

    private bool OnDPad(AddonContextMenu* addon, int inputId, bool a3)
    {
        if (_disabled || !_plugin.TryGetPadState(out var state) || state.Addon != (nint)addon)
            return _dpad.Original(addon, inputId, a3);

        try
        {
            if (Route((InputId)inputId, InputState.Repeat))
                return true;

            return _dpad.Original(addon, inputId, a3);
        }
        catch (Exception ex)
        {
            Stop(ex);
            return _dpad.Original(addon, inputId, a3);
        }
    }

    private bool OnSelected(AddonContextMenu* addon, int index, byte a3)
    {
        if (!_disabled
            && _plugin.TryGetPadState(out var state)
            && state.Focused
            && state.Addon == (nint)addon)
            return false;

        return _selection.Original(addon, index, a3);
    }

    private bool Route(InputId id, InputState state)
    {
        if (!_plugin.TryGetPadState(out var pad) || !NativeContextMenu.GameIsForeground() || !NativeContextMenu.HasFocus(pad.Addon))
            return false;

        var now = Environment.TickCount64;
        var entering = !pad.Focused && id == InputId.RIGHT && CanEnter(pad);
        var held = _gate.IsHeld((int)id);
        if (!pad.Focused && now >= _swallowUntil && !entering && !held)
            return false;

        if (pad.Focused && !Routed.Contains(id))
            return true;

        if (!_gate.Accept(
                (int)id,
                down: state == InputState.Down,
                repeat: state == InputState.Repeat,
                navigation: id is InputId.UP or InputId.DOWN,
                now: now,
                directionEdge: id is InputId.LEFT or InputId.RIGHT))
            return true;

        if (entering)
            _plugin.EnterPad();
        else if (pad.Focused)
            ApplyFocused(id);

        return true;
    }

    private void ApplyFocused(InputId id)
    {
        switch (id)
        {
            case InputId.UP:
                _plugin.MovePad(-1);
                break;
            case InputId.DOWN:
                _plugin.MovePad(1);
                break;
            case InputId.OK:
            case InputId.PAD_OK:
                _plugin.ConfirmPad();
                break;
            case InputId.LEFT:
            case InputId.CANCEL:
            case InputId.PAD_CANCEL:
            case InputId.ESC:
                _plugin.BackPad();
                break;
        }
    }

    private void PollRelease()
    {
        var input = UIInputData.Instance();
        if (input == null)
            return;

        foreach (var id in Routed)
        {
            if (!input->IsInputIdDown(id))
                _gate.Release((int)id);
        }
    }

    private void HideCursor()
    {
        var manager = RaptureAtkUnitManager.Instance();
        var cursor = manager == null ? null : (AtkUnitBase*)manager->AddonCursor;
        if (cursor == null || cursor->RootNode == null)
            return;

        if (_cursorNode == 0)
        {
            _cursorNode = (nint)cursor->RootNode;
            _cursorWasVisible = cursor->RootNode->IsVisible();
        }

        if (_cursorNode == (nint)cursor->RootNode)
            cursor->RootNode->ToggleVisibility(false);
    }

    private void RestoreCursor()
    {
        var saved = _cursorNode;
        _cursorNode = 0;
        if (saved == 0)
            return;

        var manager = RaptureAtkUnitManager.Instance();
        var cursor = manager == null ? null : (AtkUnitBase*)manager->AddonCursor;
        if (cursor != null && (nint)cursor->RootNode == saved)
            cursor->RootNode->ToggleVisibility(_cursorWasVisible);
    }

    private void Stop(Exception ex)
    {
        if (_disabled)
            return;

        _disabled = true;
        PluginServices.Log.Error(ex, "Gamepad context menu input stopped.");
        _plugin.LeavePad();
    }

    private static bool Consume(AtkEventDispatcher.Event* evt)
    {
        evt->State.StateFlags |= AtkEventStateFlags.Handled
            | AtkEventStateFlags.ViewportDispatchSuppressed
            | AtkEventStateFlags.SuppressViewportDispatch;
        return true;
    }

    private sealed class InputGate
    {
        private readonly HashSet<int> _held = [];
        private long _boundaryUntil;
        private long _lastNavigation = long.MinValue / 2;

        internal void Boundary(long now) => _boundaryUntil = now + 180;

        internal void Release(int id) => _held.Remove(id);

        internal bool IsHeld(int id) => _held.Contains(id);

        internal void Clear()
        {
            _held.Clear();
            _boundaryUntil = 0;
        }

        internal bool Accept(int id, bool down, bool repeat, bool navigation, long now, bool directionEdge)
        {
            if (!down && !repeat)
            {
                Release(id);
                return false;
            }

            var fresh = _held.Add(id);
            if (now < _boundaryUntil)
                return false;

            if (navigation)
            {
                if ((!fresh && !repeat) || now - _lastNavigation < 75)
                    return false;

                _lastNavigation = now;
                return true;
            }

            return fresh && (down || (directionEdge && repeat));
        }
    }
}
