using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Nodalis.Core.Navigation;

namespace Nodalis.App.Navigation;

public sealed class NavigationNodeViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

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

    public IEnumerable<NavigationNodeViewModel> DescendantsAndSelf()
    {
        yield return this;

        foreach (var child in Children)
        {
            foreach (var descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
