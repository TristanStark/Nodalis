namespace Nodalis.Core.Navigation;

public sealed record WorkspaceNavigationNode
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    public required WorkspaceNodeKind Kind { get; init; }

    public required string FullPath { get; init; }

    public List<WorkspaceNavigationNode> Children { get; init; } = [];
}
