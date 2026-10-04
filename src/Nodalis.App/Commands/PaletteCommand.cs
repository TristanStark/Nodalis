namespace Nodalis.App.Commands;

public sealed record PaletteCommand
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public string[] Keywords { get; init; } = [];

    public required Func<Task> ExecuteAsync { get; init; }
}
