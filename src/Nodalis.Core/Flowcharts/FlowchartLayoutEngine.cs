namespace Nodalis.Core.Flowcharts;

/// <summary>
/// Computes a deterministic layered layout for the Mermaid subset supported by
/// Nodalis. The layout is UI-framework agnostic so it can be smoke-tested.
/// </summary>
public static class FlowchartLayoutEngine
{
    private const double Margin = 48;
    private const double RectangleWidth = 180;
    private const double RectangleHeight = 72;
    private const double DecisionWidth = 170;
    private const double DecisionHeight = 100;
    private const double CircleSize = 110;
    private const double DatabaseHeight = 88;
    private const double SubroutineWidth = 190;
    private const double HexagonHeight = 82;
    private const double MaximumNodeWidth = 190;
    private const double MaximumNodeHeight = 110;
    private const double LayerGap = 110;
    private const double SiblingGap = 46;

    /// <summary>
    /// Creates a layered layout for one parsed flowchart.
    /// </summary>
    /// <param name="diagram">The parsed flowchart definition.</param>
    /// <returns>Node bounds, routed edges and total canvas size.</returns>
    public static FlowchartLayout Layout(
            FlowchartDefinition diagram)
    {
        ArgumentNullException.ThrowIfNull(
            diagram);

        if (diagram.Nodes.Count ==
            0)
        {
            return new FlowchartLayout
            {
                Width =
                    Margin * 2,
                Height =
                    Margin * 2
            };
        }

        IReadOnlyDictionary<string, int> ranks =
            CalculateRanks(
                diagram);
        int maximumRank =
            ranks.Values.DefaultIfEmpty(
                    0)
                .Max();

        Dictionary<string, FlowchartLayoutNode> positioned =
            new Dictionary<string, FlowchartLayoutNode>(
                StringComparer.OrdinalIgnoreCase);

        bool horizontal =
            diagram.Direction is
                FlowchartDirection.LeftRight or
                FlowchartDirection.RightLeft;

        if (horizontal)
        {
            PositionHorizontal(
                diagram,
                ranks,
                maximumRank,
                positioned);
        }
        else
        {
            PositionVertical(
                diagram,
                ranks,
                maximumRank,
                positioned);
        }

        double width =
            positioned.Values.Max(node =>
                node.X +
                node.Width) +
            Margin;
        double height =
            positioned.Values.Max(node =>
                node.Y +
                node.Height) +
            Margin;

        List<FlowchartLayoutEdge> edges =
            diagram.Edges
                .Where(edge =>
                    positioned.ContainsKey(
                        edge.SourceId) &&
                    positioned.ContainsKey(
                        edge.TargetId))
                .Select(edge =>
                    RouteEdge(
                        edge,
                        positioned[edge.SourceId],
                        positioned[edge.TargetId],
                        horizontal))
                .ToList();

        List<FlowchartLayoutNode> nodes =
            diagram.Nodes
                .Where(node =>
                    positioned.ContainsKey(
                        node.Id))
                .Select(node =>
                    positioned[node.Id])
                .ToList();

        return new FlowchartLayout
        {
            Width =
                Math.Max(
                    width,
                    320),
            Height =
                Math.Max(
                    height,
                    220),
            Nodes =
                nodes,
            Edges =
                edges
        };
    }

    /// <summary>
    /// Calculates stable graph ranks using Kahn traversal and a safe fallback
    /// for cycles.
    /// </summary>
    /// <param name="diagram">The parsed diagram.</param>
    /// <returns>A rank for every node identifier.</returns>
    private static IReadOnlyDictionary<string, int> CalculateRanks(
            FlowchartDefinition diagram)
    {
        Dictionary<string, int> indegree =
            diagram.Nodes.ToDictionary(
                node =>
                    node.Id,
                _ =>
                    0,
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> outgoing =
            diagram.Nodes.ToDictionary(
                node =>
                    node.Id,
                _ =>
                    new List<string>(),
                StringComparer.OrdinalIgnoreCase);

        foreach (FlowchartEdgeDefinition edge in
                 diagram.Edges)
        {
            if (!indegree.ContainsKey(
                    edge.SourceId) ||
                !indegree.ContainsKey(
                    edge.TargetId))
            {
                continue;
            }

            outgoing[edge.SourceId].Add(
                edge.TargetId);
            indegree[edge.TargetId]++;
        }

        Dictionary<string, int> ranks =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
        Queue<string> queue =
            new Queue<string>();

        foreach (FlowchartNodeDefinition node in
                 diagram.Nodes)
        {
            if (indegree[node.Id] ==
                0)
            {
                queue.Enqueue(
                    node.Id);
                ranks[node.Id] =
                    0;
            }
        }

        while (queue.Count >
               0)
        {
            string sourceId =
                queue.Dequeue();
            int sourceRank =
                ranks[sourceId];

            foreach (string targetId in
                     outgoing[sourceId])
            {
                int candidateRank =
                    sourceRank + 1;

                if (!ranks.TryGetValue(
                        targetId,
                        out int currentRank) ||
                    candidateRank >
                    currentRank)
                {
                    ranks[targetId] =
                        candidateRank;
                }

                indegree[targetId]--;

                if (indegree[targetId] ==
                    0)
                {
                    queue.Enqueue(
                        targetId);
                }
            }
        }

        int fallbackRank =
            ranks.Values.DefaultIfEmpty(
                    0)
                .Max();

        foreach (FlowchartNodeDefinition node in
                 diagram.Nodes)
        {
            if (!ranks.ContainsKey(
                    node.Id))
            {
                ranks[node.Id] =
                    fallbackRank;
            }
        }

        return ranks;
    }

    /// <summary>
    /// Positions top-down or bottom-up diagrams.
    /// </summary>
    /// <param name="diagram">The diagram definition.</param>
    /// <param name="ranks">Calculated graph ranks.</param>
    /// <param name="maximumRank">The largest calculated rank.</param>
    /// <param name="positioned">Destination node map.</param>
    private static void PositionVertical(
            FlowchartDefinition diagram,
            IReadOnlyDictionary<string, int> ranks,
            int maximumRank,
            IDictionary<string, FlowchartLayoutNode> positioned)
    {
        Dictionary<int, List<FlowchartNodeDefinition>> layers =
            BuildVisualLayers(
                diagram,
                ranks,
                maximumRank);

        double widestLayer =
            layers.Values
                .Select(CalculateLayerWidth)
                .DefaultIfEmpty(
                    RectangleWidth)
                .Max();

        foreach (KeyValuePair<int, List<FlowchartNodeDefinition>> pair in
                 layers.OrderBy(item =>
                     item.Key))
        {
            int visualRank =
                pair.Key;
            List<FlowchartNodeDefinition> layer =
                pair.Value;
            double layerWidth =
                CalculateLayerWidth(
                    layer);
            double x =
                Margin +
                (widestLayer - layerWidth) /
                2;
            double y =
                Margin +
                visualRank *
                (MaximumNodeHeight + LayerGap);

            foreach (FlowchartNodeDefinition node in
                     layer)
            {
                (double Width, double Height) size =
                    GetNodeSize(
                        node);

                positioned[node.Id] =
                    new FlowchartLayoutNode
                    {
                        Node =
                            node,
                        X =
                            x,
                        Y =
                            y +
                            (MaximumNodeHeight - size.Height) /
                            2,
                        Width =
                            size.Width,
                        Height =
                            size.Height
                    };

                x +=
                    size.Width +
                    SiblingGap;
            }
        }
    }

    /// <summary>
    /// Positions left-right or right-left diagrams.
    /// </summary>
    /// <param name="diagram">The diagram definition.</param>
    /// <param name="ranks">Calculated graph ranks.</param>
    /// <param name="maximumRank">The largest calculated rank.</param>
    /// <param name="positioned">Destination node map.</param>
    private static void PositionHorizontal(
            FlowchartDefinition diagram,
            IReadOnlyDictionary<string, int> ranks,
            int maximumRank,
            IDictionary<string, FlowchartLayoutNode> positioned)
    {
        Dictionary<int, List<FlowchartNodeDefinition>> layers =
            BuildVisualLayers(
                diagram,
                ranks,
                maximumRank);

        double tallestLayer =
            layers.Values
                .Select(CalculateLayerHeight)
                .DefaultIfEmpty(
                    DecisionHeight)
                .Max();

        foreach (KeyValuePair<int, List<FlowchartNodeDefinition>> pair in
                 layers.OrderBy(item =>
                     item.Key))
        {
            int visualRank =
                pair.Key;
            List<FlowchartNodeDefinition> layer =
                pair.Value;
            double layerHeight =
                CalculateLayerHeight(
                    layer);
            double x =
                Margin +
                visualRank *
                (MaximumNodeWidth + LayerGap);
            double y =
                Margin +
                (tallestLayer - layerHeight) /
                2;

            foreach (FlowchartNodeDefinition node in
                     layer)
            {
                (double Width, double Height) size =
                    GetNodeSize(
                        node);

                positioned[node.Id] =
                    new FlowchartLayoutNode
                    {
                        Node =
                            node,
                        X =
                            x +
                            (MaximumNodeWidth - size.Width) /
                            2,
                        Y =
                            y,
                        Width =
                            size.Width,
                        Height =
                            size.Height
                    };

                y +=
                    size.Height +
                    SiblingGap;
            }
        }
    }

    /// <summary>
    /// Builds visual layers and reverses rank order for RL/BT directions.
    /// </summary>
    /// <param name="diagram">The flowchart definition.</param>
    /// <param name="ranks">Logical graph ranks.</param>
    /// <param name="maximumRank">The maximum logical rank.</param>
    /// <returns>Nodes grouped by visual rank.</returns>
    private static Dictionary<int, List<FlowchartNodeDefinition>> BuildVisualLayers(
            FlowchartDefinition diagram,
            IReadOnlyDictionary<string, int> ranks,
            int maximumRank)
    {
        Dictionary<int, List<FlowchartNodeDefinition>> result =
            [];

        bool reverse =
            diagram.Direction is
                FlowchartDirection.RightLeft or
                FlowchartDirection.BottomTop;

        foreach (FlowchartNodeDefinition node in
                 diagram.Nodes)
        {
            int logicalRank =
                ranks[node.Id];
            int visualRank =
                reverse
                    ? maximumRank - logicalRank
                    : logicalRank;

            if (!result.TryGetValue(
                    visualRank,
                    out List<FlowchartNodeDefinition>? layer))
            {
                layer =
                    [];
                result[visualRank] =
                    layer;
            }

            layer.Add(
                node);
        }

        return result;
    }

    /// <summary>
    /// Routes an edge orthogonally between node boundaries.
    /// </summary>
    /// <param name="edge">The source edge.</param>
    /// <param name="source">The positioned source node.</param>
    /// <param name="target">The positioned target node.</param>
    /// <param name="horizontal">Whether the diagram's primary axis is horizontal.</param>
    /// <returns>The routed edge.</returns>
    private static FlowchartLayoutEdge RouteEdge(
            FlowchartEdgeDefinition edge,
            FlowchartLayoutNode source,
            FlowchartLayoutNode target,
            bool horizontal)
    {
        List<FlowchartLayoutPoint> points =
            horizontal
                ? RouteHorizontalEdge(
                    source,
                    target)
                : RouteVerticalEdge(
                    source,
                    target);

        int middleIndex =
            Math.Max(
                0,
                points.Count / 2 - 1);
        FlowchartLayoutPoint firstMiddle =
            points[middleIndex];
        FlowchartLayoutPoint secondMiddle =
            points[Math.Min(
                middleIndex + 1,
                points.Count - 1)];

        FlowchartLayoutPoint labelPoint =
            new FlowchartLayoutPoint(
                (firstMiddle.X + secondMiddle.X) /
                2,
                (firstMiddle.Y + secondMiddle.Y) /
                2);

        return new FlowchartLayoutEdge
        {
            Edge =
                edge,
            Points =
                points,
            LabelPoint =
                labelPoint
        };
    }

    /// <summary>
    /// Creates an orthogonal route for a horizontal diagram.
    /// </summary>
    /// <param name="source">The source node.</param>
    /// <param name="target">The target node.</param>
    /// <returns>The route points.</returns>
    private static List<FlowchartLayoutPoint> RouteHorizontalEdge(
            FlowchartLayoutNode source,
            FlowchartLayoutNode target)
    {
        bool targetOnRight =
            target.CenterX >=
            source.CenterX;

        FlowchartLayoutPoint start =
            new FlowchartLayoutPoint(
                targetOnRight
                    ? source.X + source.Width
                    : source.X,
                source.CenterY);
        FlowchartLayoutPoint end =
            new FlowchartLayoutPoint(
                targetOnRight
                    ? target.X
                    : target.X + target.Width,
                target.CenterY);
        double middleX =
            (start.X + end.X) /
            2;

        return
        [
            start,
            new FlowchartLayoutPoint(
                middleX,
                start.Y),
            new FlowchartLayoutPoint(
                middleX,
                end.Y),
            end
        ];
    }

    /// <summary>
    /// Creates an orthogonal route for a vertical diagram.
    /// </summary>
    /// <param name="source">The source node.</param>
    /// <param name="target">The target node.</param>
    /// <returns>The route points.</returns>
    private static List<FlowchartLayoutPoint> RouteVerticalEdge(
            FlowchartLayoutNode source,
            FlowchartLayoutNode target)
    {
        bool targetBelow =
            target.CenterY >=
            source.CenterY;

        FlowchartLayoutPoint start =
            new FlowchartLayoutPoint(
                source.CenterX,
                targetBelow
                    ? source.Y + source.Height
                    : source.Y);
        FlowchartLayoutPoint end =
            new FlowchartLayoutPoint(
                target.CenterX,
                targetBelow
                    ? target.Y
                    : target.Y + target.Height);
        double middleY =
            (start.Y + end.Y) /
            2;

        return
        [
            start,
            new FlowchartLayoutPoint(
                start.X,
                middleY),
            new FlowchartLayoutPoint(
                end.X,
                middleY),
            end
        ];
    }

    /// <summary>
    /// Calculates total node width for one vertical layer.
    /// </summary>
    /// <param name="layer">The nodes in the layer.</param>
    /// <returns>The layer width.</returns>
    private static double CalculateLayerWidth(
            IReadOnlyList<FlowchartNodeDefinition> layer) =>
        layer.Sum(node =>
            GetNodeSize(
                node).Width) +
        Math.Max(
            0,
            layer.Count - 1) *
        SiblingGap;

    /// <summary>
    /// Calculates total node height for one horizontal layer.
    /// </summary>
    /// <param name="layer">The nodes in the layer.</param>
    /// <returns>The layer height.</returns>
    private static double CalculateLayerHeight(
            IReadOnlyList<FlowchartNodeDefinition> layer) =>
        layer.Sum(node =>
            GetNodeSize(
                node).Height) +
        Math.Max(
            0,
            layer.Count - 1) *
        SiblingGap;

    /// <summary>
    /// Returns the fixed native preview size for one supported node shape.
    /// </summary>
    /// <param name="node">The node definition.</param>
    /// <returns>The node width and height.</returns>
    private static (double Width, double Height) GetNodeSize(
            FlowchartNodeDefinition node) =>
        node.Shape switch
        {
            FlowchartNodeShape.Decision =>
                (DecisionWidth, DecisionHeight),
            FlowchartNodeShape.Circle =>
                (CircleSize, CircleSize),
            FlowchartNodeShape.Database =>
                (RectangleWidth, DatabaseHeight),
            FlowchartNodeShape.Subroutine =>
                (SubroutineWidth, RectangleHeight),
            FlowchartNodeShape.Hexagon =>
                (RectangleWidth, HexagonHeight),
            _ =>
                (RectangleWidth, RectangleHeight)
        };
}
