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
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceNavigationNode Node { get; }

    public Guid Id => Node.Id;

    public string DisplayName => Node.DisplayName;

    public string FullPath => Node.FullPath;

    public WorkspaceNodeKind Kind => Node.Kind;

    public ObservableCollection<NavigationNodeViewModel> Children { get; }

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
    /// <returns>The result of the operation.</returns>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
}
