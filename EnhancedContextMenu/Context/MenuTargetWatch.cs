using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace EnhancedContextMenu.Context;

/// <summary>Identity of the menu target when its plugin entries were taken.</summary>
internal sealed unsafe class MenuTargetWatch
{
    private readonly bool _inventory;
    private readonly nint _agent;
    private readonly ulong _contentId;
    private readonly ulong _accountId;
    private readonly GameObjectId _objectId;
    private readonly short _world;
    private readonly byte[] _name;
    private readonly nint _objectPointer;
    private readonly nint _itemPointer;
    private readonly byte[] _itemSnapshot;

    private MenuTargetWatch(IMenuArgs args)
    {
        Parent = args.AddonPtr;
        if (Parent != 0)
            ParentId = ((AtkUnitBase*)Parent)->Id;

        if (args.MenuType == ContextMenuType.Inventory)
        {
            _inventory = true;
            var agent = (AgentInventoryContext*)args.AgentPtr;
            _agent = args.AgentPtr;
            _itemPointer = (nint)agent->TargetInventorySlot;
            _itemSnapshot = Copy((void*)_itemPointer, sizeof(InventoryItem));
            _name = [];
            return;
        }

        _itemSnapshot = [];
        var context = (AgentContext*)args.AgentPtr;
        _agent = args.AgentPtr;
        _contentId = context->TargetContentId;
        _accountId = context->TargetAccountId;
        _objectId = context->TargetObjectId;
        _world = context->TargetHomeWorldId;
        _name = context->TargetName.AsSpan().ToArray();
        _objectPointer = FindObject(_objectId);
    }

    internal nint Parent { get; }

    internal ushort ParentId { get; }

    internal static bool TryCreate(IMenuArgs args, out MenuTargetWatch watch)
    {
        watch = null!;
        if (!CanCapture(args))
            return false;

        watch = new MenuTargetWatch(args);
        return true;
    }

    internal bool Unchanged()
    {
        if (_inventory)
            return InventoryUnchanged();

        var agent = AgentContext.Instance();
        return agent != null && (nint)agent == _agent
            && agent->TargetContentId == _contentId
            && agent->TargetAccountId == _accountId
            && agent->TargetObjectId.Equals(_objectId)
            && agent->TargetHomeWorldId == _world
            && agent->TargetName.AsSpan().SequenceEqual(_name)
            && (_objectPointer == 0 || FindObject(agent->TargetObjectId) == _objectPointer);
    }

    private bool InventoryUnchanged()
    {
        var agent = AgentInventoryContext.Instance();
        return agent != null && (nint)agent == _agent
            && (nint)agent->TargetInventorySlot == _itemPointer
            && new ReadOnlySpan<byte>((void*)_itemPointer, _itemSnapshot.Length).SequenceEqual(_itemSnapshot);
    }

    private static bool CanCapture(IMenuArgs args)
    {
        if (args.MenuType == ContextMenuType.Inventory)
            return InventoryCanCapture(args);

        if (args.MenuType != ContextMenuType.Default || args.Target is not MenuTargetDefault)
            return false;

        if (args.AddonName is "BlackList" or "MuteList")
            return false;

        var agent = AgentContext.Instance();
        return agent != null && (nint)agent == args.AgentPtr
            && agent->TargetHomeWorldId > 0
            && !agent->TargetName.AsSpan().IsEmpty;
    }

    private static bool InventoryCanCapture(IMenuArgs args)
    {
        var agent = AgentInventoryContext.Instance();
        var manager = InventoryManager.Instance();
        return agent != null && manager != null && (nint)agent == args.AgentPtr
            && agent->TargetInventorySlot != null
            && agent->TargetInventorySlotId >= 0
            && manager->GetInventorySlot(agent->TargetInventoryId, agent->TargetInventorySlotId) == agent->TargetInventorySlot;
    }

    private static nint FindObject(GameObjectId id)
    {
        if (id.Equals(default(GameObjectId)))
            return 0;

        var manager = GameObjectManager.Instance();
        return manager == null ? 0 : (nint)manager->Objects.GetObjectByGameObjectId(id);
    }

    private static byte[] Copy(void* source, int size)
    {
        var buffer = new byte[size];
        new ReadOnlySpan<byte>(source, size).CopyTo(buffer);
        return buffer;
    }
}
