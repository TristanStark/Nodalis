namespace Nodalis.Core.Importing;

public sealed record DocxRelationship
{
    public required string Id { get; init; }

    public required string Type { get; init; }

    public required string Target { get; init; }

    public bool IsExternal { get; init; }
}
