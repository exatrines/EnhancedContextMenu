using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace EnhancedContextMenu.Context;

internal readonly record struct OmenRow(SeString Text, int ListIndex, bool Enabled, bool IsSubmenu);

internal readonly record struct OmenView(string Title, OmenRow[] Rows, bool Focused, int Selected);

/// <summary>
/// Moves trailing OmenTools rows onto the side panel before the game builds the menu.
/// A shorter copy is passed only when every extra row is a trailing suffix.
/// The built list is not resized.
/// </summary>
internal sealed unsafe class OmenMenuBridge : IDisposable
{
    private readonly object _gate = new();
    private readonly IGameInteropProvider _interop;
    private readonly List<Page> _pages = [];
    private Hook<RaptureAtkModule.Delegates.OpenAddon>? _open;
    private Type? _managerType;
    private Type? _serviceType;
    private FieldInfo? _currentMenu;
    private nint _addon;
    private bool _installed;
    private bool _wasOpen;
    private bool _stopped;
    private bool _loggedTake;
    private bool _loggedSkip;
    private bool _loggedBuild;
    private bool _pushFailed;
    private bool _building;

    internal OmenMenuBridge(IGameInteropProvider interop)
    {
        _interop = interop;
        if (RaptureAtkModule.MemberFunctionPointers.OpenAddon == null)
            throw new NotSupportedException("Context menu OpenAddon address is empty.");
    }

    internal void Tick()
    {
        if (_stopped)
            return;

        try
        {
            Install();
            if (!C.Enabled)
            {
                Clear();
                _wasOpen = false;
                return;
            }

            var open = NativeContextMenu.IsOpen();
            if (_wasOpen && !open)
                Clear();

            _wasOpen = open;
            if (open)
                BindAddon();
        }
        catch (Exception ex)
        {
            Stop(ex);
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _pages.Clear();
            _addon = 0;
        }
    }

    public void Dispose()
    {
        _stopped = true;
        Clear();
        _open?.Dispose();
    }

    internal int Count
    {
        get
        {
            lock (_gate)
                return _pages.Count == 1 ? _pages[0].Rows.Count : 0;
        }
    }

    internal bool Nested
    {
        get
        {
            lock (_gate)
                return _pages.Count > 1;
        }
    }

    internal OmenRow[] CopyRows()
    {
        lock (_gate)
            return _pages.Count == 1 ? ToRows(_pages[0].Rows) : [];
    }

    internal bool TryPeek(out string title, out OmenRow[] rows, out int selected)
    {
        lock (_gate)
        {
            if (_pages.Count < 2)
            {
                title = "";
                rows = [];
                selected = MenuLevel.BackRow;
                return false;
            }

            var page = _pages[^1];
            title = page.Title;
            rows = ToRows(page.Rows);
            selected = page.Selected;
            return true;
        }
    }

    internal bool IsSubmenu(int index)
    {
        lock (_gate)
            return _pages.Count == 1
                && (uint)index < (uint)_pages[0].Rows.Count
                && _pages[0].Rows[index].IsSubmenu;
    }

    internal void Move(int direction)
    {
        lock (_gate)
        {
            if (_pages.Count < 2 || direction == 0)
                return;

            var page = _pages[^1];
            var choices = new List<int> { MenuLevel.BackRow };
            for (var i = 0; i < page.Rows.Count; i++)
            {
                if (page.Rows[i].Enabled)
                    choices.Add(i);
            }

            var at = choices.IndexOf(page.Selected);
            if (at < 0)
                at = 0;

            var slot = at + direction;
            slot %= choices.Count;
            if (slot < 0)
                slot += choices.Count;
            page.Selected = choices[slot];
        }
    }

    internal void Confirm()
    {
        int index;
        lock (_gate)
        {
            if (_pages.Count < 2)
                return;

            index = _pages[^1].Selected;
        }

        if (index < 0)
        {
            Pop();
            return;
        }

        Activate(index);
    }

    internal void OpenSelected()
    {
        int index;
        lock (_gate)
        {
            if (_pages.Count < 2)
                return;

            var page = _pages[^1];
            index = page.Selected;
            if ((uint)index >= (uint)page.Rows.Count || !page.Rows[index].IsSubmenu)
                return;
        }

        Activate(index);
    }

    internal bool Pop()
    {
        lock (_gate)
        {
            if (_pages.Count < 2)
                return false;

            _pages.RemoveAt(_pages.Count - 1);
            return true;
        }
    }

    internal void Activate(int index)
    {
        Held? held;
        lock (_gate)
        {
            if (_pages.Count == 0)
                return;

            var rows = _pages[^1].Rows;
            if ((uint)index >= (uint)rows.Count || !rows[index].Enabled)
                return;

            held = rows[index];
        }

        try
        {
            Run(held);
        }
        catch (Exception ex)
        {
            Stop(ex);
        }
    }

    private void Install()
    {
        if (_installed)
            return;

        var manager = Manager();
        if (manager == null)
            return;

        var field = manager.GetType().GetField("OpenAddonHook", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new NotSupportedException("OmenTools OpenAddon hook was not found.");

        var hook = field.GetValue(manager);
        if (hook == null)
            return;

        if (hook.GetType().GetProperty("Original")?.GetValue(hook) is not Delegate original)
            throw new NotSupportedException("OmenTools OpenAddon hook has no original call.");

        var trampoline = Marshal.GetFunctionPointerForDelegate(original);
        var open = (nint)RaptureAtkModule.MemberFunctionPointers.OpenAddon;
        if (trampoline == 0 || trampoline == open)
            throw new NotSupportedException("OmenTools original call could not be hooked from the inside.");

        _open = _interop.HookFromAddress<RaptureAtkModule.Delegates.OpenAddon>(trampoline, OnOpen);
        try
        {
            _open.Enable();
        }
        catch
        {
            _open.Dispose();
            _open = null;
            throw;
        }

        _installed = true;
        PluginServices.Log.Information("OmenTools context menu hook is ready.");
    }

    private ushort OnOpen(
        RaptureAtkModule* module,
        uint addonNameId,
        uint valueCount,
        AtkValue* values,
        AtkModuleInterface.AtkEventInterface* eventInterface,
        ulong a6,
        ushort parentAddonId,
        int depthLayer)
    {
        if (_stopped || !C.Enabled || !IsContextMenu(module, addonNameId) || values == null || valueCount is < 8 or > 160)
            return Call(module, addonNameId, valueCount, values, eventInterface, a6, parentAddonId, depthLayer);

        var copy = stackalloc AtkValue[(int)valueCount];
        var nextCount = valueCount;
        var stripped = false;
        List<Held>? taken = null;
        try
        {
            stripped = TryStrip(valueCount, values, copy, out nextCount, out taken);
        }
        catch (Exception ex)
        {
            Stop(ex);
            stripped = false;
        }

        if (!stripped)
            Clear();

        var id = Call(
            module,
            addonNameId,
            stripped ? nextCount : valueCount,
            stripped ? copy : values,
            eventInterface,
            a6,
            parentAddonId,
            depthLayer);
        if (stripped && taken != null)
        {
            lock (_gate)
            {
                _pages.Clear();
                _addon = 0;
                _pages.Add(new Page("", false, taken));
            }

            BindAddon();
            NoteTake();
        }

        return id;
    }

    private ushort Call(
        RaptureAtkModule* module,
        uint addonNameId,
        uint valueCount,
        AtkValue* values,
        AtkModuleInterface.AtkEventInterface* eventInterface,
        ulong a6,
        ushort parentAddonId,
        int depthLayer)
    {
        return _open!.Original(module, addonNameId, valueCount, values, eventInterface, a6, parentAddonId, depthLayer);
    }

    private static bool IsContextMenu(RaptureAtkModule* module, uint addonNameId)
    {
        if (module == null)
            return false;

        var names = module->AddonNames.AsSpan();
        if (addonNameId >= (uint)names.Length)
            return false;

        return names[(int)addonNameId].AsSpan().SequenceEqual("ContextMenu"u8);
    }

    private void BindAddon()
    {
        lock (_gate)
        {
            if (_addon != 0 || _pages.Count == 0)
                return;
        }

        var handle = PluginServices.GameGui.GetAddonByName("ContextMenu", 1);
        if (handle.IsNull)
            return;

        lock (_gate)
            _addon = handle.Address;
    }

    private bool TryStrip(uint valueCount, AtkValue* values, AtkValue* copy, out uint nextCount, out List<Held>? taken)
    {
        nextCount = valueCount;
        taken = null;
        var manager = Manager();
        if (manager == null || _currentMenu == null)
            return false;

        var frame = _currentMenu.GetValue(manager);
        if (frame == null)
            return false;

        if (Flag(frame, "IsSubmenu"))
            return false;

        if (Require(frame, "Items") is not Array items || items.Length == 0)
            return false;

        var pending = new List<(string Label, bool Enabled, bool IsSubmenu, object Item)>();
        foreach (var item in items)
        {
            if (item == null)
                return false;

            if (Convert.ToInt32(Require(item, "Priority")) < 0)
            {
                NoteStay("An OmenTools entry is not at the end of the menu.");
                return false;
            }

            if (LeaveNative(item))
                continue;

            var label = DisplayLabel(manager, item);
            if (label.Length == 0)
            {
                NoteSkip(null, null);
                return false;
            }

            pending.Add((label, Flag(item, "IsEnabled"), HasSubmenu(item), item));
        }

        if (pending.Count == 0)
            return false;

        if (values[0].Type is not (AtkValueType.UInt or AtkValueType.Int))
        {
            NoteStay("The menu value list had no row count.");
            return false;
        }

        var count = values[0].UInt;
        if (count == 0 || count > 64 || pending.Count >= count || valueCount < 8 + count)
        {
            NoteStay($"The menu value list did not fit {pending.Count} trailing entries.");
            return false;
        }

        var hasDisabled = valueCount >= 8 + count * 2;

        var nativeCount = (int)count - pending.Count;
        var rows = new List<Held>(pending.Count);
        var shown = new string[pending.Count];
        for (var i = 0; i < pending.Count; i++)
        {
            if (!TryLabel(values[8 + nativeCount + i], out var row))
            {
                NoteSkip(pending.Select(entry => entry.Label), shown);
                return false;
            }

            shown[i] = row.Visible;
            if (Norm(row.Visible) != pending[i].Label)
            {
                NoteSkip(pending.Select(entry => entry.Label), shown);
                return false;
            }

            rows.Add(new Held(row.Text, nativeCount + i, pending[i].Enabled, pending[i].IsSubmenu, pending[i].Item));
        }

        for (var i = 0; i < 8; i++)
            copy[i] = values[i];

        copy[0].UInt = (uint)nativeCount;
        ClampSelection(ref copy[1], nativeCount);
        KeepLowBits(ref copy[2], nativeCount);
        KeepLowBits(ref copy[3], nativeCount);
        for (var i = 0; i < nativeCount; i++)
            copy[8 + i] = values[8 + i];

        if (hasDisabled)
        {
            for (var i = 0; i < nativeCount; i++)
                copy[8 + nativeCount + i] = values[8 + count + i];
        }

        nextCount = hasDisabled ? 8 + (uint)nativeCount * 2 : 8 + (uint)nativeCount;
        taken = rows;
        return true;
    }

    private static bool TryLabel(in AtkValue value, out MenuRow row)
    {
        row = default;
        if (value.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString))
            return false;

        if (value.String.Value == null)
            return false;

        row = ReadRow(Marshal.PtrToStringUTF8((nint)value.String.Value) ?? "");
        return row.Visible.Length > 0;
    }

    private static void ClampSelection(ref AtkValue value, int count)
    {
        if (value.Type is not (AtkValueType.UInt or AtkValueType.Int))
            return;

        if (value.UInt >= (uint)count)
            value.UInt = (uint)(count - 1);
    }

    private static void KeepLowBits(ref AtkValue value, int bits)
    {
        if (value.Type is not (AtkValueType.UInt or AtkValueType.Int) || bits is < 0 or >= 32)
            return;

        var keep = bits == 0 ? 0u : (uint)((1 << bits) - 1);
        value.UInt &= keep;
    }

    private void Run(Held held)
    {
        if (held.IsSubmenu && held.Item != null)
        {
            if (TryPush(held.Item.GetType().GetProperty("Submenu")?.GetValue(held.Item)))
                return;

            if (held.ListIndex >= 0)
                Fire(AddonPtr(), held.ListIndex);

            return;
        }

        if (held.ListIndex >= 0)
        {
            Fire(AddonPtr(), held.ListIndex);
            return;
        }

        if (held.Item != null && InvokeLeaf(held.Item))
            return;

        NativeContextMenu.Close();
    }

    private static void Fire(nint addon, int listIndex)
    {
        if (addon == 0)
            return;

        var values = stackalloc AtkValue[2];
        values[0].Type = AtkValueType.Int;
        values[0].Int = 0;
        values[1].Type = AtkValueType.Int;
        values[1].Int = listIndex;
        ((AtkUnitBase*)addon)->FireCallback(2, values, true);
    }

    private bool TryPush(object? submenu)
    {
        if (submenu == null || _building)
            return false;

        _building = true;
        try
        {
            var manager = Manager();
            var args = manager == null ? null : CurrentArgs(manager);
            if (manager == null || args == null || Require(submenu, "Entries") is not System.Collections.IEnumerable entries)
                return false;

            var rows = new List<Held>();
            foreach (var entry in entries)
            {
                if (entry != null)
                    Collect(entry, args, manager, rows);
            }

            // Their menu folds anything past 30 entries back into the game submenu.
            if (rows.Count == 0 || rows.Count > 30)
                return false;

            var title = SeText(Require(submenu, "Title"));
            lock (_gate)
            {
                if (_pages.Count == 0 || _pages.Count >= MenuSession.MaxDepth)
                    return false;

                _pages.Add(new Page(title, true, rows));
            }

            return true;
        }
        catch (Exception ex)
        {
            NoteBuild(ex);
            return false;
        }
        finally
        {
            _building = false;
        }
    }

    private static void Collect(object entry, object args, object manager, List<Held> rows)
    {
        var many = Method(entry, "CreateMultiple", args)?.Invoke(entry, [args]);
        if (many is System.Collections.IEnumerable list)
        {
            foreach (var item in list)
            {
                if (item != null)
                    AddItem(entry, item, manager, rows);
            }
        }

        var one = Method(entry, "Create", args)?.Invoke(entry, [args]);
        if (one != null)
            AddItem(entry, one, manager, rows);
    }

    private static void AddItem(object entry, object item, object manager, List<Held> rows)
    {
        if (entry.GetType().Name != "LocalMenuItemEntry")
        {
            item.GetType().GetProperty("Entry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(item, entry);
        }

        if (Flag(item, "IsReturn"))
            return;

        var text = new SeStringBuilder().Append(VisibleText(manager, item, true)).Build();
        if (text.TextValue.Length == 0)
            return;

        rows.Add(new Held(text, -1, Flag(item, "IsEnabled"), HasSubmenu(item), item));
    }

    private bool InvokeLeaf(object item)
    {
        if (item.GetType().GetProperty("OnClicked")?.GetValue(item) is not Delegate clicked)
            return false;

        object? args;
        try
        {
            args = MakeClickArgs();
        }
        catch (Exception ex)
        {
            NoteBuild(ex);
            return true;
        }

        if (args == null)
            return true;

        var before = PageCount();
        _pushFailed = false;
        try
        {
            clicked.DynamicInvoke(args);
        }
        catch (Exception ex)
        {
            PluginServices.Log.Warning(ex.InnerException ?? ex, "OmenTools menu entry failed.");
            return false;
        }

        return PageCount() > before || _pushFailed;
    }

    private object? MakeClickArgs()
    {
        var manager = Manager();
        var source = manager == null ? null : CurrentArgs(manager);
        if (source == null)
            return null;

        var clickType = source.GetType().Assembly.GetType("OmenTools.OmenService.ContextMenuItemClickedArgs");
        var ctor = clickType?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.GetParameters().Length == 2);
        if (ctor == null)
            return null;

        var parameters = ctor.GetParameters();
        var submenuType = parameters[1].ParameterType.GetGenericArguments()[0];
        var cloned = source.GetType().GetMethod("Clone", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(source, null) ?? source;
        var value = Expression.Parameter(submenuType, "submenu");
        var callback = Expression.Lambda(
            parameters[1].ParameterType,
            Expression.Call(
                Expression.Constant(this),
                typeof(OmenMenuBridge).GetMethod(nameof(ReceiveSubmenu), BindingFlags.Instance | BindingFlags.NonPublic)!,
                Expression.Convert(value, typeof(object))),
            value).Compile();
        return ctor.Invoke([cloned, callback]);
    }

    private void ReceiveSubmenu(object submenu)
    {
        if (!TryPush(submenu))
            _pushFailed = true;
    }

    private object? Manager()
    {
        if (_stopped)
            return null;

        if (_managerType != null && _serviceType != null)
        {
            var cached = ReadManager(_serviceType, _managerType);
            if (cached != null)
                return cached;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, "OmenTools", StringComparison.Ordinal))
                continue;

            var managerType = assembly.GetType("OmenTools.OmenService.ContextMenuManager");
            var serviceType = assembly.GetType("OmenTools.DService");
            if (managerType == null || serviceType == null)
                continue;

            var manager = ReadManager(serviceType, managerType);
            if (manager == null)
                continue;

            var field = managerType.GetField("currentMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new NotSupportedException("OmenTools context menu layout does not match this build.");

            _managerType = managerType;
            _serviceType = serviceType;
            _currentMenu = field;
            return manager;
        }

        return null;
    }

    private static object? ReadManager(Type serviceType, Type managerType)
    {
        var held = serviceType.GetProperty("InternalInstance", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        if (held == null)
            return null;

        var method = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(candidate => candidate.Name == "GetOmenService" && candidate.IsGenericMethodDefinition);
        if (method == null)
            throw new NotSupportedException("OmenTools service lookup does not match this build.");

        return method.MakeGenericMethod(managerType).Invoke(held, null);
    }

    private object? CurrentArgs(object manager) =>
        manager.GetType().GetField("currentArgs", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(manager);


    private static string DisplayLabel(object manager, object item) => Norm(VisibleText(manager, item, false));

    private static string VisibleText(object manager, object item, bool isSubmenu)
    {
        var name = SeText(Require(item, "Name"));
        var prefix = SeText(Require(item, "Prefix"));
        if (prefix.Length == 0)
        {
            var entry = item.GetType().GetProperty(
                "Entry",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
            if (entry != null)
                prefix = SeText(Require(entry, "Prefix"));

            if (prefix.Length == 0 && !isSubmenu && (entry == null || !Flag(entry, "OmitPrefix")))
                prefix = SeText(Require(manager, "DefaultPrefix"));
        }

        return prefix.Length == 0 ? name : prefix + " " + name;
    }

    private static bool LeaveNative(object item) => Flag(item, "IsReturn");

    private static bool HasSubmenu(object item) =>
        item.GetType().GetProperty("Submenu")?.GetValue(item) != null;

    private readonly record struct MenuRow(string Visible, SeString Text);

    private static MenuRow ReadRow(string raw)
    {
        if (raw.Length == 0)
            return new MenuRow("", new SeString());

        if (!raw.Contains('\u0002'))
            return new MenuRow(raw, new SeStringBuilder().Append(raw).Build());

        try
        {
            var parsed = SeString.Parse(Encoding.UTF8.GetBytes(raw));
            return new MenuRow(parsed.TextValue, parsed);
        }
        catch (ArgumentException)
        {
            return new MenuRow(raw, new SeStringBuilder().Append(raw).Build());
        }
    }

    private nint AddonPtr()
    {
        lock (_gate)
            return _addon;
    }

    private int PageCount()
    {
        lock (_gate)
            return _pages.Count;
    }

    private static OmenRow[] ToRows(List<Held> rows)
    {
        var copy = new OmenRow[rows.Count];
        for (var i = 0; i < rows.Count; i++)
            copy[i] = new OmenRow(rows[i].Text, rows[i].ListIndex, rows[i].Enabled, rows[i].IsSubmenu);

        return copy;
    }

    private static MethodInfo? Method(object target, string name, object arg)
    {
        foreach (var method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != name)
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(arg))
                return method;
        }

        return null;
    }

    private void NoteTake()
    {
        if (_loggedTake)
            return;

        _loggedTake = true;
        PluginServices.Log.Information("OmenTools entries are shown on the side panel.");
    }

    private void NoteSkip(IEnumerable<string>? expected, IEnumerable<string>? shown)
    {
        if (_loggedSkip)
            return;

        _loggedSkip = true;
        if (expected == null || shown == null)
        {
            PluginServices.Log.Information("OmenTools entries stayed on the game menu. Their labels did not match.");
            return;
        }

        PluginServices.Log.Information(
            "OmenTools entries stayed on the game menu. Expected [{Expected}] but the menu showed [{Shown}].",
            string.Join(" | ", expected),
            string.Join(" | ", shown));
    }

    private void NoteStay(string reason)
    {
        if (_loggedSkip)
            return;

        _loggedSkip = true;
        PluginServices.Log.Information("OmenTools entries stayed on the game menu. {Reason}", reason);
    }

    private void Stop(Exception ex)
    {
        if (_stopped)
            return;

        _stopped = true;
        Clear();
        PluginServices.Log.Warning(ex, "OmenTools context menu path stopped.");
    }

    private void NoteBuild(Exception ex)
    {
        if (_loggedBuild)
            return;

        _loggedBuild = true;
        PluginServices.Log.Information(ex, "OmenTools submenu stayed on the game menu.");
    }

    private static object? Require(object source, string name)
    {
        var property = source.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null)
            throw new NotSupportedException($"OmenTools field '{name}' was not found.");

        return property.GetValue(source);
    }

    private static bool Flag(object source, string name) => Require(source, name) is true;

    private static string SeText(object? value)
    {
        if (value == null)
            return "";

        var extract = value.GetType().GetMethod("ExtractText", Type.EmptyTypes);
        var text = extract?.Invoke(value, null) as string ?? value.ToString() ?? "";
        return text;
    }

    private static string Norm(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var text = value.Replace('\0', ' ').Replace('\u3000', ' ').Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);

        return text;
    }

    private sealed class Page
    {
        public Page(string title, bool nested, List<Held> rows)
        {
            Title = title;
            Rows = rows;
            Selected = nested ? MenuLevel.BackRow : 0;
        }

        public string Title { get; }

        public int Selected { get; set; }

        public List<Held> Rows { get; }
    }

    private sealed class Held
    {
        public Held(SeString text, int listIndex, bool enabled, bool isSubmenu, object? item)
        {
            Text = text;
            ListIndex = listIndex;
            Enabled = enabled;
            IsSubmenu = isSubmenu;
            Item = item;
        }

        public SeString Text { get; }

        public int ListIndex { get; }

        public bool Enabled { get; }

        public bool IsSubmenu { get; }

        public object? Item { get; }
    }
}
