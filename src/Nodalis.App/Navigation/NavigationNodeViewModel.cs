using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Nodalis.Core.Navigation;

namespace Nodalis.App.Navigation;

public sealed class NavigationNodeViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    /// <summary>
    /// Initializes a new instance of <see cref="NavigationNodeViewModel"/>.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <param name="expandedNodeIds">The <c>expandedNodeIds</c> value.</param>
    public NavigationNodeViewModel(
            WorkspaceNavigationNode node,
            IReadOnlySet<Guid> expandedNodeIds)
    {
        Node = node;
        _isExpanded = expandedNodeIds.Contains(node.Id);

        Children = new ObservableCollection<NavigationNodeViewModel>(
            node.Children.Select(child =>
                new NavigationNodeViewModel(child, expandedNodeIds)));
        List<NavigationNodeViewModel> visible = new List<NavigationNodeViewModel>();
        foreach (NavigationNodeViewModel child in Children)
        {
            AppendVisible(child, visible, this);
        }

        VisibleChildren = visible;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceNavigationNode Node { get; }

    public Guid Id => Node.Id;

    public string DisplayName => Node.DisplayName;

    public string FullPath => Node.FullPath;

    public WorkspaceNodeKind Kind => Node.Kind;

    /// <summary>Canonical children, including grouping nodes used by business operations.</summary>
    public ObservableCollection<NavigationNodeViewModel> Children { get; }

    /// <summary>Children projected for WPF, omitting only structural grouping rows.</summary>
    public IReadOnlyList<NavigationNodeViewModel> VisibleChildren { get; }

    /// <summary>Actual parent visible in the WPF TreeView.</summary>
    public NavigationNodeViewModel? DisplayParent { get; private set; }

    /// <summary>Segoe MDL2 symbol used to distinguish node types at a glance.</summary>
    public string NavigationGlyph => Kind switch
    {
        WorkspaceNodeKind.Global => "\uE774",
        WorkspaceNodeKind.Application => "\uE80F",
        WorkspaceNodeKind.Module => "\uE8B7",
        WorkspaceNodeKind.Project => "\uE8F1",
        WorkspaceNodeKind.Document => GetDocumentGlyph(),
        WorkspaceNodeKind.Folder or WorkspaceNodeKind.Section => "\uE8B7",
        _ => "\uE8B7"
    };

    /// <summary>Resolves a document-specific symbol based on its Markdown filename.</summary>
    /// <returns>A visible document glyph.</returns>
    private string GetDocumentGlyph()
    {
        string name = Path.GetFileNameWithoutExtension(FullPath);
        if (name.Contains("glossaire", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE82D";
        }

        if (name.Contains("jalon", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE7C1";
        }

        if (name.Contains("tâche", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("tache", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE73E";
        }

        if (name.Contains("calendrier", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE787";
        }

        if (name.Contains("note", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE70B";
        }

        return "\uE8A5";
    }

    /// <summary>Adds visual children while retaining their original business ownership.</summary>
    /// <param name="node">Canonical candidate.</param>
    /// <param name="result">Visual children.</param>
    /// <param name="visualParent">Nearest displayed ancestor.</param>
    private static void AppendVisible(
        NavigationNodeViewModel node,
        List<NavigationNodeViewModel> result,
        NavigationNodeViewModel visualParent)
    {
        if (NavigationPresentationRules.IsTransparentContainer(node.Kind))
        {
            foreach (NavigationNodeViewModel child in node.Children)
            {
                AppendVisible(child, result, visualParent);
            }

            return;
        }

        node.DisplayParent = visualParent;
        result.Add(node);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Performs the <c>DescendantsAndSelf</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    public IEnumerable<NavigationNodeViewModel> DescendantsAndSelf()
    {
        yield return this;

        foreach (global::Nodalis.App.Navigation.NavigationNodeViewModel child in Children)
        {
            foreach (global::Nodalis.App.Navigation.NavigationNodeViewModel descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Performs the <c>OnPropertyChanged</c> operation.
    /// </summary>
    /// <param name="propertyName">The <c>propertyName</c> value.</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
}
