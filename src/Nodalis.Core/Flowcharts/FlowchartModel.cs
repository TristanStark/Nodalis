namespace Nodalis.Core.Flowcharts;

public enum FlowchartDirection
{
    TopDown,
    LeftRight,
    RightLeft,
    BottomTop
}

public enum FlowchartNodeShape
{
    Rectangle,
    Rounded,
    Decision
}

public enum FlowchartDiagnosticSeverity
{
    Warning,
    Error
}

public sealed record FlowchartNodeDefinition
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public FlowchartNodeShape Shape { get; init; } =
        FlowchartNodeShape.Rectangle;
}

public sealed record FlowchartEdgeDefinition
{
    public required string SourceId { get; init; }

    public required string TargetId { get; init; }

    public string? Label { get; init; }

    public bool HasArrow { get; init; } =
        true;
}

public sealed record FlowchartDefinition
{
    public FlowchartDirection Direction { get; init; } =
        FlowchartDirection.TopDown;

    public List<FlowchartNodeDefinition> Nodes { get; init; } =
        [];

    public List<FlowchartEdgeDefinition> Edges { get; init; } =
        [];
}

public sealed record FlowchartDiagnostic
{
    public int LineNumber { get; init; }

    public required string Message { get; init; }

    public FlowchartDiagnosticSeverity Severity { get; init; } =
        FlowchartDiagnosticSeverity.Warning;
}

public sealed record FlowchartParseResult
{
    public FlowchartDefinition? Diagram { get; init; }

    public List<FlowchartDiagnostic> Diagnostics { get; init; } =
        [];

    public bool Success =>
        Diagram is not null &&
        !Diagnostics.Any(diagnostic =>
            diagnostic.Severity ==
            FlowchartDiagnosticSeverity.Error);
}

public readonly record struct FlowchartLayoutPoint(
    double X,
    double Y);

public sealed record FlowchartLayoutNode
{
    public required FlowchartNodeDefinition Node { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public double CenterX =>
        X + Width / 2;

    public double CenterY =>
        Y + Height / 2;
}

public sealed record FlowchartLayoutEdge
{
    public required FlowchartEdgeDefinition Edge { get; init; }

    public List<FlowchartLayoutPoint> Points { get; init; } =
        [];

    public FlowchartLayoutPoint LabelPoint { get; init; }
}

public sealed record FlowchartLayout
{
    public double Width { get; init; }

    public double Height { get; init; }

    public List<FlowchartLayoutNode> Nodes { get; init; } =
        [];

    public List<FlowchartLayoutEdge> Edges { get; init; } =
        [];
}
