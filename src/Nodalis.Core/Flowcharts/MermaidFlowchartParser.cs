using System.Text.RegularExpressions;

namespace Nodalis.Core.Flowcharts;

/// <summary>
/// Parses the intentionally small, portable Mermaid flowchart subset supported
/// by Nodalis without executing JavaScript or contacting a remote service.
/// </summary>
public static partial class MermaidFlowchartParser
{
    /// <summary>
    /// Parses a Mermaid flowchart code block into a portable Nodalis model.
    /// </summary>
    /// <param name="source">The source inside a mermaid fenced code block.</param>
    /// <returns>The parsed diagram and localized diagnostics.</returns>
    public static FlowchartParseResult Parse(
            string source)
    {
        ArgumentNullException.ThrowIfNull(
            source);

        string normalized = source
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');

        string[] lines =
            normalized.Split(
                '\n');

        List<FlowchartDiagnostic> diagnostics =
            [];
        Dictionary<string, MutableNode> nodes =
            new Dictionary<string, MutableNode>(
                StringComparer.OrdinalIgnoreCase);
        List<string> nodeOrder =
            [];
        List<FlowchartEdgeDefinition> edges =
            [];

        int headerIndex =
            FindFirstContentLine(
                lines);

        if (headerIndex <
            0)
        {
            diagnostics.Add(
                CreateError(
                    1,
                    "Le bloc Mermaid est vide."));

            return new FlowchartParseResult
            {
                Diagnostics =
                    diagnostics
            };
        }

        Match header =
            HeaderPattern().Match(
                lines[headerIndex]);

        if (!header.Success)
        {
            diagnostics.Add(
                CreateError(
                    headerIndex + 1,
                    "Le diagramme doit commencer par « flowchart TD », « graph LR » ou une orientation supportée."));

            return new FlowchartParseResult
            {
                Diagnostics =
                    diagnostics
            };
        }

        FlowchartDirection direction =
            ParseDirection(
                header.Groups["direction"].Value);

        for (int index = headerIndex + 1;
             index < lines.Length;
             index++)
        {
            string line =
                TrimStatement(
                    lines[index]);

            if (line.Length ==
                    0 ||
                line.StartsWith(
                    "%%",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (ContainsUnsupportedArrow(
                    line))
            {
                diagnostics.Add(
                    CreateWarning(
                        index + 1,
                        "Cette forme de lien Mermaid n'est pas encore supportée et a été ignorée."));
                continue;
            }

            int arrowIndex =
                line.IndexOf(
                    "-->",
                    StringComparison.Ordinal);

            if (arrowIndex >=
                0)
            {
                if (!TryParseEdge(
                        line,
                        index + 1,
                        nodes,
                        nodeOrder,
                        edges,
                        diagnostics))
                {
                    diagnostics.Add(
                        CreateError(
                            index + 1,
                            "Lien flowchart invalide. Format attendu : A --> B ou A -->|Libellé| B."));
                }

                continue;
            }

            if (TryParseNodeToken(
                    line,
                    out ParsedNodeToken token))
            {
                UpsertNode(
                    token,
                    nodes,
                    nodeOrder);
                continue;
            }

            diagnostics.Add(
                CreateWarning(
                    index + 1,
                    "Construction Mermaid non supportée ignorée."));
        }

        if (nodes.Count ==
            0)
        {
            diagnostics.Add(
                CreateError(
                    headerIndex + 1,
                    "Le flowchart ne contient aucun nœud supporté."));
        }

        if (diagnostics.Any(diagnostic =>
                diagnostic.Severity ==
                FlowchartDiagnosticSeverity.Error))
        {
            return new FlowchartParseResult
            {
                Diagnostics =
                    diagnostics
            };
        }

        List<FlowchartNodeDefinition> definitions =
            nodeOrder
                .Select(id =>
                    nodes[id].ToDefinition())
                .ToList();

        return new FlowchartParseResult
        {
            Diagram =
                new FlowchartDefinition
                {
                    Direction =
                        direction,
                    Nodes =
                        definitions,
                    Edges =
                        edges
                },
            Diagnostics =
                diagnostics
        };
    }

    /// <summary>
    /// Finds the first non-empty, non-comment line in a Mermaid block.
    /// </summary>
    /// <param name="lines">The normalized source lines.</param>
    /// <returns>The line index or -1 when no content exists.</returns>
    private static int FindFirstContentLine(
            IReadOnlyList<string> lines)
    {
        for (int index = 0;
             index < lines.Count;
             index++)
        {
            string line =
                lines[index].Trim();

            if (line.Length >
                    0 &&
                !line.StartsWith(
                    "%%",
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Converts a Mermaid orientation token into the Nodalis layout direction.
    /// </summary>
    /// <param name="token">The Mermaid orientation token.</param>
    /// <returns>The matching direction.</returns>
    private static FlowchartDirection ParseDirection(
            string token) =>
        token.ToUpperInvariant() switch
        {
            "LR" =>
                FlowchartDirection.LeftRight,
            "RL" =>
                FlowchartDirection.RightLeft,
            "BT" =>
                FlowchartDirection.BottomTop,
            _ =>
                FlowchartDirection.TopDown
        };

    /// <summary>
    /// Parses one supported directional edge and updates referenced nodes.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="nodes">The node registry.</param>
    /// <param name="nodeOrder">The stable node order.</param>
    /// <param name="edges">The destination edge list.</param>
    /// <param name="diagnostics">The destination diagnostic list.</param>
    /// <returns><see langword="true"/> when the edge is valid.</returns>
    private static bool TryParseEdge(
            string line,
            int lineNumber,
            IDictionary<string, MutableNode> nodes,
            ICollection<string> nodeOrder,
            ICollection<FlowchartEdgeDefinition> edges,
            ICollection<FlowchartDiagnostic> diagnostics)
    {
        int arrowIndex =
            line.IndexOf(
                "-->",
                StringComparison.Ordinal);

        if (arrowIndex <=
            0)
        {
            return false;
        }

        string sourceText =
            line[..arrowIndex].Trim();
        string targetText =
            line[(arrowIndex + 3)..].Trim();

        if (targetText.Contains(
                "-->",
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                CreateWarning(
                    lineNumber,
                    "Les chaînes de plusieurs flèches sur une ligne ne sont pas encore supportées ; la ligne a été ignorée."));
            return true;
        }

        string? label =
            null;

        if (targetText.StartsWith(
                '|'))
        {
            int labelEnd =
                targetText.IndexOf(
                    '|',
                    1);

            if (labelEnd <=
                1)
            {
                return false;
            }

            label =
                NormalizeLabel(
                    targetText[1..labelEnd]);
            targetText =
                targetText[(labelEnd + 1)..].Trim();
        }

        if (!TryParseNodeToken(
                sourceText,
                out ParsedNodeToken source) ||
            !TryParseNodeToken(
                targetText,
                out ParsedNodeToken target))
        {
            return false;
        }

        UpsertNode(
            source,
            nodes,
            nodeOrder);
        UpsertNode(
            target,
            nodes,
            nodeOrder);

        edges.Add(
            new FlowchartEdgeDefinition
            {
                SourceId =
                    source.Id,
                TargetId =
                    target.Id,
                Label =
                    string.IsNullOrWhiteSpace(
                        label)
                        ? null
                        : label,
                HasArrow =
                    true
            });

        return true;
    }

    /// <summary>
    /// Parses a Mermaid node token with rectangle, rounded or decision syntax.
    /// </summary>
    /// <param name="text">The node token.</param>
    /// <param name="token">The parsed token when successful.</param>
    /// <returns><see langword="true"/> when the token is supported.</returns>
    private static bool TryParseNodeToken(
            string text,
            out ParsedNodeToken? token)
    {
        Match match =
            NodePattern().Match(
                text.Trim());

        if (!match.Success)
        {
            token =
                null!;
            return false;
        }

        FlowchartNodeShape shape =
            FlowchartNodeShape.Rectangle;
        string label =
            match.Groups["id"].Value;
        bool explicitDefinition =
            false;

        if (match.Groups["decision"].Success)
        {
            shape =
                FlowchartNodeShape.Decision;
            label =
                match.Groups["decision"].Value;
            explicitDefinition =
                true;
        }
        else if (match.Groups["rounded"].Success)
        {
            shape =
                FlowchartNodeShape.Rounded;
            label =
                match.Groups["rounded"].Value;
            explicitDefinition =
                true;
        }
        else if (match.Groups["rectangle"].Success)
        {
            label =
                match.Groups["rectangle"].Value;
            explicitDefinition =
                true;
        }

        token =
            new ParsedNodeToken(
                match.Groups["id"].Value,
                NormalizeLabel(
                    label),
                shape,
                explicitDefinition);

        return true;
    }

    /// <summary>
    /// Adds a node reference or upgrades it when an explicit definition appears.
    /// </summary>
    /// <param name="token">The parsed node token.</param>
    /// <param name="nodes">The node registry.</param>
    /// <param name="nodeOrder">The stable node order.</param>
    private static void UpsertNode(
            ParsedNodeToken token,
            IDictionary<string, MutableNode> nodes,
            ICollection<string> nodeOrder)
    {
        if (!nodes.TryGetValue(
                token.Id,
                out MutableNode? existing))
        {
            nodes[token.Id] =
                new MutableNode(
                    token.Id,
                    token.Label,
                    token.Shape,
                    token.ExplicitDefinition);
            nodeOrder.Add(
                token.Id);
            return;
        }

        if (!token.ExplicitDefinition)
        {
            return;
        }

        existing.Label =
            token.Label;
        existing.Shape =
            token.Shape;
        existing.HasExplicitDefinition =
            true;
    }

    /// <summary>
    /// Normalizes a Mermaid label while preserving ordinary Markdown text.
    /// </summary>
    /// <param name="label">The source label.</param>
    /// <returns>The normalized label.</returns>
    private static string NormalizeLabel(
            string label)
    {
        string normalized =
            label.Trim();

        if (normalized.Length >=
                2 &&
            ((normalized[0] ==
                  '"' &&
              normalized[^1] ==
                  '"') ||
             (normalized[0] ==
                  '\'' &&
              normalized[^1] ==
                  '\'')))
        {
            normalized =
                normalized[1..^1];
        }

        return normalized
            .Replace(
                "<br/>",
                "\n",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "<br>",
                "\n",
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes optional trailing Mermaid statement separators.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <returns>The trimmed statement.</returns>
    private static string TrimStatement(
            string line) =>
        line
            .Trim()
            .TrimEnd(
                ';')
            .Trim();

    /// <summary>
    /// Determines whether a line uses an unsupported Mermaid link operator.
    /// </summary>
    /// <param name="line">The statement to inspect.</param>
    /// <returns><see langword="true"/> for unsupported link operators.</returns>
    private static bool ContainsUnsupportedArrow(
            string line) =>
        line.Contains(
            "-.->",
            StringComparison.Ordinal) ||
        line.Contains(
            "==>",
            StringComparison.Ordinal) ||
        line.Contains(
            "---",
            StringComparison.Ordinal) &&
        !line.Contains(
            "-->",
            StringComparison.Ordinal);

    /// <summary>
    /// Creates one localized warning diagnostic.
    /// </summary>
    /// <param name="lineNumber">The one-based source line number.</param>
    /// <param name="message">The diagnostic message.</param>
    /// <returns>The warning diagnostic.</returns>
    private static FlowchartDiagnostic CreateWarning(
            int lineNumber,
            string message) =>
        new FlowchartDiagnostic
        {
            LineNumber =
                lineNumber,
            Message =
                message,
            Severity =
                FlowchartDiagnosticSeverity.Warning
        };

    /// <summary>
    /// Creates one localized error diagnostic.
    /// </summary>
    /// <param name="lineNumber">The one-based source line number.</param>
    /// <param name="message">The diagnostic message.</param>
    /// <returns>The error diagnostic.</returns>
    private static FlowchartDiagnostic CreateError(
            int lineNumber,
            string message) =>
        new FlowchartDiagnostic
        {
            LineNumber =
                lineNumber,
            Message =
                message,
            Severity =
                FlowchartDiagnosticSeverity.Error
        };

    /// <summary>
    /// Matches a supported Mermaid flowchart header.
    /// </summary>
    /// <returns>The generated header expression.</returns>
    [GeneratedRegex(
            @"^\s*(?:flowchart|graph)\s+(?<direction>TD|TB|LR|RL|BT)\s*;?\s*$",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();

    /// <summary>
    /// Matches one supported Mermaid node token.
    /// </summary>
    /// <returns>The generated node expression.</returns>
    [GeneratedRegex(
            @"^(?<id>[A-Za-z_][A-Za-z0-9_-]*)(?:\s*(?:\[(?<rectangle>.*)\]|\{(?<decision>.*)\}|\((?<rounded>.*)\)))?$",
            RegexOptions.CultureInvariant)]
    private static partial Regex NodePattern();

    private sealed record ParsedNodeToken(
        string Id,
        string Label,
        FlowchartNodeShape Shape,
        bool ExplicitDefinition);

    private sealed class MutableNode
    {
        /// <summary>
        /// Initializes a mutable parser node.
        /// </summary>
        /// <param name="id">The stable Mermaid node identifier.</param>
        /// <param name="label">The visible label.</param>
        /// <param name="shape">The parsed node shape.</param>
        /// <param name="hasExplicitDefinition">Whether a shape/label was explicit.</param>
        public MutableNode(
                string id,
                string label,
                FlowchartNodeShape shape,
                bool hasExplicitDefinition)
        {
            Id =
                id;
            Label =
                label;
            Shape =
                shape;
            HasExplicitDefinition =
                hasExplicitDefinition;
        }

        public string Id { get; }

        public string Label { get; set; }

        public FlowchartNodeShape Shape { get; set; }

        public bool HasExplicitDefinition { get; set; }

        /// <summary>
        /// Converts the parser node into an immutable definition.
        /// </summary>
        /// <returns>The immutable node definition.</returns>
        public FlowchartNodeDefinition ToDefinition() =>
            new FlowchartNodeDefinition
            {
                Id =
                    Id,
                Label =
                    Label,
                Shape =
                    Shape
            };
    }
}
