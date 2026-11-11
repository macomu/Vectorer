using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Commands;

namespace Vectorer.App.ViewModels;

public enum TreeDropPosition
{
    Before,
    Inside,
    After
}

public sealed partial class SceneTreeItem : ObservableObject
{
    public SceneNode Node { get; }
    public ContainerNode? ParentContainer { get; private set; }
    public LayerNode OwningLayer { get; private set; }
    public ObservableCollection<SceneTreeItem> Children { get; } = [];

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isEditingName;
    [ObservableProperty] private string _editingNameText = "";

    public bool IsLayer => Node is LayerNode;
    public bool IsContainer => Node is ContainerNode;

    public string KindIcon => Node switch
    {
        LayerNode => "📁",
        GroupNode => "📦",
        PolygonShapeNode { PolygonKind: PolygonKind.ConcaveStar } => "⭐",
        PolygonShapeNode => "⬡",
        RectangleShapeNode => "▭",
        EllipseShapeNode => "◯",
        ArcShapeNode => "◑",
        TextShapeNode => "T",
        PathShapeNode => "✒",
        _ => "•"
    };

    public string VisibilityIcon => IsVisible ? "👁" : "🚫";
    public string LockIcon => IsLocked ? "🔒" : "🔓";
    public string? BlendBadge => Node.BlendMode != LayerBlendMode.Normal ? $"[{Node.BlendMode}]" : null;
    public bool HasBlendBadge => Node.BlendMode != LayerBlendMode.Normal;
    public string? EffectBadge => (Node.Filter is { HasActiveFilter: true } || Node.ShaderEffect is not null) ? "[fx]" : null;
    public bool HasEffectBadge => Node.Filter is { HasActiveFilter: true } || Node.ShaderEffect is not null;

    public SceneTreeItem(SceneNode node, ContainerNode? parentContainer, LayerNode owningLayer)
    {
        Node = node;
        ParentContainer = parentContainer;
        OwningLayer = owningLayer;
        _name = node.Name;
        _isVisible = node.IsVisible;
        _isLocked = node.IsLocked;
        _editingNameText = node.Name;
    }

    public void RefreshFromNode(ContainerNode? parentContainer, LayerNode owningLayer)
    {
        ParentContainer = parentContainer;
        OwningLayer = owningLayer;
        Name = Node.Name;
        if (!IsEditingName)
        {
            EditingNameText = Node.Name;
        }
        IsVisible = Node.IsVisible;
        IsLocked = Node.IsLocked;
        OnPropertyChanged(nameof(KindIcon));
        OnPropertyChanged(nameof(VisibilityIcon));
        OnPropertyChanged(nameof(LockIcon));
        OnPropertyChanged(nameof(BlendBadge));
        OnPropertyChanged(nameof(HasBlendBadge));
        OnPropertyChanged(nameof(EffectBadge));
        OnPropertyChanged(nameof(HasEffectBadge));
    }
}

public sealed partial class MainWindowViewModel
{
    private bool _syncingTreeSelection;

    public ObservableCollection<SceneTreeItem> SceneTree { get; } = [];

    [ObservableProperty] private SceneTreeItem? _selectedTreeItem;

    public void SyncSceneTree()
    {
        SyncTreeCollection(SceneTree, CurrentDocument.Layers, parentContainer: null, owningLayer: null);
        SyncTreeSelectionFromWorkspace();
    }

    private static void SyncTreeCollection(
        ObservableCollection<SceneTreeItem> target,
        IReadOnlyList<SceneNode> sourceNodes,
        ContainerNode? parentContainer,
        LayerNode? owningLayer)
    {
        var sameSequence = target.Count == sourceNodes.Count;
        if (sameSequence)
        {
            for (var i = 0; i < sourceNodes.Count; i++)
            {
                if (!ReferenceEquals(target[i].Node, sourceNodes[i]))
                {
                    sameSequence = false;
                    break;
                }
            }
        }

        if (!sameSequence)
        {
            var existingByNode = target.ToDictionary(item => item.Node);
            target.Clear();
            foreach (var node in sourceNodes)
            {
                var layer = node as LayerNode ?? owningLayer!;
                if (!existingByNode.TryGetValue(node, out var item))
                {
                    item = new SceneTreeItem(node, parentContainer, layer);
                }
                else
                {
                    item.RefreshFromNode(parentContainer, layer);
                }
                target.Add(item);
            }
        }
        else
        {
            for (var i = 0; i < target.Count; i++)
            {
                var item = target[i];
                var layer = item.Node as LayerNode ?? owningLayer!;
                item.RefreshFromNode(parentContainer, layer);
            }
        }

        for (var i = 0; i < target.Count; i++)
        {
            var item = target[i];
            if (item.Node is ContainerNode container)
            {
                var layer = item.Node as LayerNode ?? owningLayer!;
                SyncTreeCollection(item.Children, container.Children, container, layer);
            }
            else if (item.Children.Count > 0)
            {
                item.Children.Clear();
            }
        }
    }

    private void SyncTreeSelectionFromWorkspace()
    {
        if (_syncingTreeSelection) return;
        _syncingTreeSelection = true;
        try
        {
            var targetNode = SelectedNodes.Count > 0 ? SelectedNodes[0] : (SceneNode?)ActiveLayer;
            SelectedTreeItem = targetNode is null ? null : FindTreeItem(SceneTree, targetNode);
        }
        finally
        {
            _syncingTreeSelection = false;
        }
    }

    public SceneTreeItem? FindTreeItem(SceneNode node) => FindTreeItem(SceneTree, node);

    private static SceneTreeItem? FindTreeItem(IEnumerable<SceneTreeItem> items, SceneNode target)
    {
        foreach (var item in items)
        {
            if (ReferenceEquals(item.Node, target))
            {
                return item;
            }
            if (item.Children.Count > 0 && FindTreeItem(item.Children, target) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    partial void OnSelectedTreeItemChanged(SceneTreeItem? value)
    {
        if (_syncingTreeSelection || IsInteracting || value is null) return;
        _syncingTreeSelection = true;
        try
        {
            ActiveLayer = value.OwningLayer;
            if (value.Node is LayerNode)
            {
                SelectNodes([]);
            }
            else
            {
                EnsureScopeForNode(value.ParentContainer);
                if (!value.Node.IsLocked && value.Node.IsVisible)
                {
                    SelectNodes([value.Node]);
                }
                else
                {
                    SelectNodes([]);
                }
            }
        }
        finally
        {
            _syncingTreeSelection = false;
        }
    }

    private void EnsureScopeForNode(ContainerNode? parentContainer)
    {
        if (parentContainer is LayerNode or null)
        {
            while (IsInGroup)
            {
                ExitGroup();
            }
            return;
        }

        if (parentContainer is GroupNode targetGroup && EditingGroup != targetGroup)
        {
            while (IsInGroup)
            {
                ExitGroup();
            }

            var chain = new List<(GroupNode Group, SKMatrix Parent)>();
            if (TryBuildGroupChain(CurrentDocument, targetGroup, chain))
            {
                foreach (var step in chain)
                {
                    SelectNodes([step.Group]);
                    EnterGroup();
                }
            }
        }
    }

    private static bool TryBuildGroupChain(SvgDocument doc, GroupNode target, List<(GroupNode Group, SKMatrix Parent)> chain)
    {
        foreach (var layer in doc.Layers)
        {
            if (layer.IsLocked || !layer.IsVisible) continue;
            var parentMatrix = SKMatrix.Concat(doc.GetViewportTransform(), layer.Transform);
            if (SearchContainer(layer, parentMatrix, target, chain))
            {
                return true;
            }
        }
        return false;

        static bool SearchContainer(ContainerNode container, SKMatrix parentMatrix, GroupNode goal, List<(GroupNode Group, SKMatrix Parent)> path)
        {
            foreach (var child in container.Children.OfType<GroupNode>())
            {
                if (child.IsLocked || !child.IsVisible) continue;
                path.Add((child, parentMatrix));
                if (ReferenceEquals(child, goal)) return true;
                var nextMatrix = SKMatrix.Concat(parentMatrix, child.Transform);
                if (SearchContainer(child, nextMatrix, goal, path)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }
    }

    [RelayCommand]
    private void ToggleNodeVisibility(SceneTreeItem? item)
    {
        if (IsInteracting || IsBusy || item is null) return;
        var node = item.Node;
        var before = node.IsVisible;
        ExecuteCommand(new DelegateDocumentCommand(
            "Toggle visibility",
            () => node.IsVisible = !before,
            () => node.IsVisible = before));
        if (before && SelectedNodes.Contains(node))
        {
            SelectNodes(SelectedNodes.Where(n => !ReferenceEquals(n, node)));
        }
    }

    [RelayCommand]
    private void ToggleNodeLock(SceneTreeItem? item)
    {
        if (IsInteracting || IsBusy || item is null) return;
        var node = item.Node;
        var before = node.IsLocked;
        ExecuteCommand(new DelegateDocumentCommand(
            "Toggle lock",
            () => node.IsLocked = !before,
            () => node.IsLocked = before));
        if (!before && SelectedNodes.Contains(node))
        {
            SelectNodes(SelectedNodes.Where(n => !ReferenceEquals(n, node)));
        }
    }

    [RelayCommand]
    private void BeginRenameNode(SceneTreeItem? item)
    {
        if (item is null) return;
        item.EditingNameText = item.Node.Name;
        item.IsEditingName = true;
    }

    [RelayCommand]
    private void CommitRenameNode(SceneTreeItem? item)
    {
        if (item is null) return;
        item.IsEditingName = false;
        RenameNode(item.Node, item.EditingNameText);
    }

    [RelayCommand]
    private void CancelRenameNode(SceneTreeItem? item)
    {
        if (item is null) return;
        item.EditingNameText = item.Node.Name;
        item.IsEditingName = false;
    }

    public void RenameNode(SceneNode node, string? newName)
    {
        if (IsInteracting || IsBusy || node is null) return;
        var trimmed = newName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(node.Name, trimmed, StringComparison.Ordinal))
        {
            SyncSceneTree();
            return;
        }

        var before = node.Name;
        ExecuteCommand(new DelegateDocumentCommand(
            "Rename node",
            () => node.Name = trimmed,
            () => node.Name = before));
    }

    [RelayCommand]
    private void DeleteTreeTarget()
    {
        if (IsInteracting || IsBusy) return;
        if (SelectedNodes.Count > 0)
        {
            DeleteSelectedObjects();
        }
        else
        {
            DeleteLayerCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task OpenRotateLayerDialogAsync()
    {
        if (!CanEditLayer || ActiveLayer is null || ActiveLayer.IsLocked) return;
        var dialogVm = new RotateLayerViewModel(this, ActiveLayer, LayerAngle);
        var accepted = FileDialogs is null || await FileDialogs.ShowRotateLayerDialogAsync(dialogVm);
        if (accepted)
        {
            LayerAngle = dialogVm.Degrees;
            dialogVm.Commit();
        }
        else
        {
            dialogVm.CancelPreview();
        }
    }

    public bool TryMoveTreeNode(SceneNode sourceNode, SceneNode targetNode, TreeDropPosition dropPosition = TreeDropPosition.After)
    {
        if (IsInteracting || IsBusy || ReferenceEquals(sourceNode, targetNode))
        {
            return false;
        }

        if (sourceNode is LayerNode sourceLayer)
        {
            var targetLayer = targetNode as LayerNode
                ?? (TryFindNodeContext(CurrentDocument, targetNode, out _, out var owning, out _, out _, out _) ? owning : null);
            if (targetLayer is null || ReferenceEquals(sourceLayer, targetLayer))
            {
                return false;
            }

            var oldLayerIndex = CurrentDocument.Layers.IndexOf(sourceLayer);
            var targetLayerIndex = CurrentDocument.Layers.IndexOf(targetLayer);
            if (oldLayerIndex < 0 || targetLayerIndex < 0)
            {
                return false;
            }

            var newLayerIndex = dropPosition == TreeDropPosition.Before ? targetLayerIndex : targetLayerIndex + 1;
            if (newLayerIndex > oldLayerIndex)
            {
                newLayerIndex--;
            }
            newLayerIndex = Math.Clamp(newLayerIndex, 0, CurrentDocument.Layers.Count - 1);
            if (newLayerIndex == oldLayerIndex)
            {
                return false;
            }

            ExecuteCommand(new DelegateDocumentCommand(
                "Reorder layer",
                () =>
                {
                    CurrentDocument.Layers.RemoveAt(oldLayerIndex);
                    CurrentDocument.Layers.Insert(newLayerIndex, sourceLayer);
                },
                () =>
                {
                    CurrentDocument.Layers.RemoveAt(newLayerIndex);
                    CurrentDocument.Layers.Insert(oldLayerIndex, sourceLayer);
                }));
            ActiveLayer = sourceLayer;
            return true;
        }

        if (sourceNode.IsLocked || !TryFindNodeContext(CurrentDocument, sourceNode, out var oldParent, out _, out var oldIndex, out var oldParentWorld, out var oldAncestorLocked) || oldAncestorLocked)
        {
            return false;
        }

        ContainerNode newParent;
        LayerNode newOwningLayer;
        int rawInsertIndex;
        SKMatrix newParentWorld;
        bool newAncestorLocked;

        if (targetNode is LayerNode layerTarget)
        {
            newParent = layerTarget;
            newOwningLayer = layerTarget;
            rawInsertIndex = dropPosition == TreeDropPosition.Before ? 0 : layerTarget.Children.Count;
            newParentWorld = layerTarget.Transform;
            newAncestorLocked = layerTarget.IsLocked;
        }
        else if (targetNode is GroupNode groupTarget && dropPosition == TreeDropPosition.Inside)
        {
            if (!TryFindNodeContext(CurrentDocument, groupTarget, out _, out var gLayer, out _, out var gParentWorld, out var gAncLocked))
            {
                return false;
            }
            newParent = groupTarget;
            newOwningLayer = gLayer;
            rawInsertIndex = groupTarget.Children.Count;
            newParentWorld = SKMatrix.Concat(gParentWorld, groupTarget.Transform);
            newAncestorLocked = gAncLocked || groupTarget.IsLocked;
        }
        else
        {
            if (!TryFindNodeContext(CurrentDocument, targetNode, out var tParent, out var tLayer, out var tIndex, out var tParentWorld, out var tAncLocked))
            {
                return false;
            }
            newParent = tParent;
            newOwningLayer = tLayer;
            rawInsertIndex = dropPosition == TreeDropPosition.Before ? tIndex : tIndex + 1;
            newParentWorld = tParentWorld;
            newAncestorLocked = tAncLocked;
        }

        if (newAncestorLocked || newParent.IsLocked)
        {
            return false;
        }

        if (sourceNode is ContainerNode sourceContainer && (ReferenceEquals(sourceContainer, newParent) || sourceContainer.FindNodeById(newParent.Id) is not null))
        {
            return false;
        }

        if (!newParentWorld.TryInvert(out var invNewParent))
        {
            return false;
        }

        var sameParent = ReferenceEquals(oldParent, newParent);
        var insertIndex = rawInsertIndex;
        if (sameParent)
        {
            if (insertIndex > oldIndex)
            {
                insertIndex--;
            }
            insertIndex = Math.Clamp(insertIndex, 0, newParent.Children.Count - 1);
            if (insertIndex == oldIndex)
            {
                return false;
            }
        }
        else
        {
            insertIndex = Math.Clamp(insertIndex, 0, newParent.Children.Count);
        }

        var beforeTransform = sourceNode.Transform;
        var afterTransform = sameParent
            ? beforeTransform
            : SKMatrix.Concat(invNewParent, SKMatrix.Concat(oldParentWorld, beforeTransform));

        ExecuteCommand(new DelegateDocumentCommand(
            "Move node in hierarchy",
            () =>
            {
                oldParent.Children.RemoveAt(oldIndex);
                sourceNode.Transform = afterTransform;
                newParent.Children.Insert(insertIndex, sourceNode);
            },
            () =>
            {
                newParent.Children.Remove(sourceNode);
                sourceNode.Transform = beforeTransform;
                oldParent.Children.Insert(oldIndex, sourceNode);
            }));

        ActiveLayer = newOwningLayer;
        SelectNodes([sourceNode]);
        return true;
    }

    private static bool TryFindNodeContext(
        SvgDocument doc,
        SceneNode target,
        out ContainerNode parent,
        out LayerNode owningLayer,
        out int index,
        out SKMatrix parentWorld,
        out bool anyAncestorLocked)
    {
        foreach (var layer in doc.Layers)
        {
            if (SearchChildren(layer, layer, layer.Transform, layer.IsLocked, target, out parent, out owningLayer, out index, out parentWorld, out anyAncestorLocked))
            {
                return true;
            }
        }

        parent = null!;
        owningLayer = null!;
        index = -1;
        parentWorld = SKMatrix.CreateIdentity();
        anyAncestorLocked = false;
        return false;

        static bool SearchChildren(
            ContainerNode currentParent,
            LayerNode currentLayer,
            SKMatrix currentParentWorld,
            bool lockedSoFar,
            SceneNode goal,
            out ContainerNode foundParent,
            out LayerNode foundLayer,
            out int foundIndex,
            out SKMatrix foundWorld,
            out bool foundLocked)
        {
            for (var i = 0; i < currentParent.Children.Count; i++)
            {
                var child = currentParent.Children[i];
                if (ReferenceEquals(child, goal))
                {
                    foundParent = currentParent;
                    foundLayer = currentLayer;
                    foundIndex = i;
                    foundWorld = currentParentWorld;
                    foundLocked = lockedSoFar;
                    return true;
                }

                if (child is ContainerNode nested)
                {
                    var nextWorld = SKMatrix.Concat(currentParentWorld, nested.Transform);
                    if (SearchChildren(nested, currentLayer, nextWorld, lockedSoFar || nested.IsLocked, goal, out foundParent, out foundLayer, out foundIndex, out foundWorld, out foundLocked))
                    {
                        return true;
                    }
                }
            }

            foundParent = null!;
            foundLayer = null!;
            foundIndex = -1;
            foundWorld = SKMatrix.CreateIdentity();
            foundLocked = false;
            return false;
        }
    }
}
