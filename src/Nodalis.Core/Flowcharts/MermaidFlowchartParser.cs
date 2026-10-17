using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Nodalis.Core.Flowcharts;

/// <summary>
/// Parses the portable Mermaid flowchart subset supported by Nodalis without
/// executing JavaScript or contacting a remote service.
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

        string normalized =
            source
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
                    "Le diagramme doit commencer par « flowchart » ou « graph », avec une orientation optionnelle TD/TB/LR/RL/BT."));

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
            IReadOnlyList<string> statements =
                SplitStatements(
                    lines[index]);

            foreach (string sourceStatement in
                     statements)
            {
                string statement =
                    sourceStatement.Trim();

                if (statement.Length ==
                        0 ||
                    statement.StartsWith(
                        "%%",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsMetadataStatement(
                        statement))
                {
                    diagnostics.Add(
                        CreateWarning(
                            index + 1,
                            "Directive Mermaid de style, interaction ou sous-graphe ignorée par le rendu local."));
                    continue;
                }

                statement =
                    NormalizeTextEdgeLabel(
                        statement);

                if (FindNextEdgeOperator(
                        statement,
                        0) is EdgeOperatorMatch)
                {
                    if (!TryParseEdgeChain(
                            statement,
                            index + 1,
                            nodes,
                            nodeOrder,
                            edges))
                    {
                        diagnostics.Add(
                            CreateError(
                                index + 1,
                                "Lien flowchart invalide. Vérifiez les identifiants, définitions de nœuds et libellés."));
                    }

                    continue;
                }

                if (TryParseNodeToken(
                        statement,
                        out ParsedNodeToken? token))
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
    /// Splits semicolon-separated Mermaid statements while preserving separators
    /// embedded in labels, node definitions and quoted strings.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <returns>The independent statements on the line.</returns>
    private static IReadOnlyList<string> SplitStatements(
            string line)
    {
        List<string> statements =
            [];
        StringBuilder current =
            new StringBuilder();
        int squareDepth =
            0;
        int roundDepth =
            0;
        int curlyDepth =
            0;
        char quote =
            '\0';

        for (int index = 0;
             index < line.Length;
             index++)
        {
            char character =
                line[index];

            if (quote !=
                '\0')
            {
                current.Append(
                    character);

                if (character ==
                        quote &&
                    (index ==
                         0 ||
                     line[index - 1] !=
                         '\\'))
                {
                    quote =
                        '\0';
                }

                continue;
            }

            if (character ==
                    '"' ||
                character ==
                    '\'')
            {
                quote =
                    character;
                current.Append(
                    character);
                continue;
            }

            switch (character)
            {
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    squareDepth =
                        Math.Max(
                            0,
                            squareDepth - 1);
                    break;
                case '(':
                    roundDepth++;
                    break;
                case ')':
                    roundDepth =
                        Math.Max(
                            0,
                            roundDepth - 1);
                    break;
                case '{':
                    curlyDepth++;
                    break;
                case '}':
                    curlyDepth =
                        Math.Max(
                            0,
                            curlyDepth - 1);
                    break;
            }

            if (character ==
                    ';' &&
                squareDepth ==
                    0 &&
                roundDepth ==
                    0 &&
                curlyDepth ==
                    0)
            {
                statements.Add(
                    current.ToString());
                current.Clear();
                continue;
            }

            current.Append(
                character);
        }

        if (current.Length >
            0)
        {
            statements.Add(
                current.ToString());
        }

        return statements;
    }

    /// <summary>
    /// Determines whether a statement is valid Mermaid metadata that Nodalis can
    /// safely ignore without losing graph connectivity.
    /// </summary>
    /// <param name="statement">The trimmed statement.</param>
    /// <returns><see langword="true"/> for non-structural metadata.</returns>
    private static bool IsMetadataStatement(
            string statement)
    {
        if (string.Equals(
                statement,
                "end",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] prefixes =
        [
            "classDef ",
            "class ",
            "style ",
            "linkStyle ",
            "click ",
            "subgraph ",
            "direction "
        ];

        return prefixes.Any(prefix =>
            statement.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Normalizes Mermaid's readable edge-label form
    /// <c>A -- label --&gt; B</c> to the pipe-label form used internally.
    /// </summary>
    /// <param name="statement">The source statement.</param>
    /// <returns>The normalized statement.</returns>
    private static string NormalizeTextEdgeLabel(
            string statement)
    {
        Match match =
            TextLabelEdgePattern().Match(
                statement);

        if (!match.Success)
        {
            return statement;
        }

        string label =
            match.Groups["label"].Value.Trim();

        return
            statement[..match.Index] +
            "-->|" +
            label +
            "|" +
            statement[(match.Index + match.Length)..];
    }

    /// <summary>
    /// Parses a chain containing one or more supported Mermaid edge operators.
    /// </summary>
    /// <param name="line">The normalized edge statement.</param>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="nodes">The node registry.</param>
    /// <param name="nodeOrder">The stable node order.</param>
    /// <param name="edges">The destination edge list.</param>
    /// <returns><see langword="true"/> when the complete chain is valid.</returns>
    private static bool TryParseEdgeChain(
            string line,
            int lineNumber,
            IDictionary<string, MutableNode> nodes,
            ICollection<string> nodeOrder,
            ICollection<FlowchartEdgeDefinition> edges)
    {
        EdgeOperatorMatch? firstOperator =
            FindNextEdgeOperator(
                line,
                0);

        if (firstOperator is null ||
            firstOperator.Value.Index <=
                0)
        {
            return false;
        }

        string sourceText =
            line[..firstOperator.Value.Index].Trim();

        if (!TryParseNodeToken(
                sourceText,
                out ParsedNodeToken? source))
        {
            return false;
        }

        UpsertNode(
            source,
            nodes,
            nodeOrder);

        int operatorIndex =
            firstOperator.Value.Index;

        while (operatorIndex <
               line.Length)
        {
            EdgeOperatorMatch? edgeOperator =
                FindNextEdgeOperator(
                    line,
                    operatorIndex);

            if (edgeOperator is null ||
                edgeOperator.Value.Index !=
                    operatorIndex)
            {
                return false;
            }

            int targetStart =
                edgeOperator.Value.Index +
                edgeOperator.Value.Token.Length;

            while (targetStart <
                       line.Length &&
                   char.IsWhiteSpace(
                       line[targetStart]))
            {
                targetStart++;
            }

            string? label =
                null;

            if (targetStart <
                    line.Length &&
                line[targetStart] ==
                    '|')
            {
                int labelEnd =
                    line.IndexOf(
                        '|',
                        targetStart + 1);

                if (labelEnd <
                    0)
                {
                    return false;
                }

                label =
                    NormalizeLabel(
                        line[(targetStart + 1)..labelEnd]);

                targetStart =
                    labelEnd + 1;

                while (targetStart <
                           line.Length &&
                       char.IsWhiteSpace(
                           line[targetStart]))
                {
                    targetStart++;
                }
            }

            EdgeOperatorMatch? nextOperator =
                FindNextEdgeOperator(
                    line,
                    targetStart);

            int targetEnd =
                nextOperator?.Index ??
                line.Length;

            string targetText =
                line[targetStart..targetEnd].Trim();

            if (!TryParseNodeToken(
                    targetText,
                    out ParsedNodeToken? target))
            {
                return false;
            }

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
                        edgeOperator.Value.HasArrow
                });

            source =
                target;

            if (nextOperator is null)
            {
                break;
            }

            operatorIndex =
                nextOperator.Value.Index;
        }

        _ =
            lineNumber;

        return true;
    }

    /// <summary>
    /// Finds the next edge operator outside node definitions and quoted labels.
    /// </summary>
    /// <param name="text">The source statement.</param>
    /// <param name="startIndex">The first index to inspect.</param>
    /// <returns>The operator location, or <see langword="null"/>.</returns>
    private static EdgeOperatorMatch? FindNextEdgeOperator(
            string text,
            int startIndex)
    {
        int squareDepth =
            0;
        int roundDepth =
            0;
        int curlyDepth =
            0;
        char quote =
            '\0';

        for (int index = Math.Max(
                 0,
                 startIndex);
             index < text.Length;
             index++)
        {
            char character =
                text[index];

            if (quote !=
                '\0')
            {
                if (character ==
                        quote &&
                    (index ==
                         0 ||
                     text[index - 1] !=
                         '\\'))
                {
                    quote =
                        '\0';
                }

                continue;
            }

            if (character ==
                    '"' ||
                character ==
                    '\'')
            {
                quote =
                    character;
                continue;
            }

            if (character ==
                '[')
            {
                squareDepth++;
                continue;
            }

            if (character ==
                ']')
            {
                squareDepth =
                    Math.Max(
                        0,
                        squareDepth - 1);
                continue;
            }

            if (character ==
                '(')
            {
                roundDepth++;
                continue;
            }

            if (character ==
                ')')
            {
                roundDepth =
                    Math.Max(
                        0,
                        roundDepth - 1);
                continue;
            }

            if (character ==
                '{')
            {
                curlyDepth++;
                continue;
            }

            if (character ==
                '}')
            {
                curlyDepth =
                    Math.Max(
                        0,
                        curlyDepth - 1);
                continue;
            }

            if (squareDepth !=
                    0 ||
                roundDepth !=
                    0 ||
                curlyDepth !=
                    0)
            {
                continue;
            }

            if (MatchesOperator(
                    text,
                    index,
                    "-.->"))
            {
                return new EdgeOperatorMatch(
                    index,
                    "-.->",
                    true);
            }

            if (MatchesOperator(
                    text,
                    index,
                    "==>"))
            {
                return new EdgeOperatorMatch(
                    index,
                    "==>",
                    true);
            }

            if (MatchesOperator(
                    text,
                    index,
                    "-->"))
            {
                return new EdgeOperatorMatch(
                    index,
                    "-->",
                    true);
            }

            if (MatchesOperator(
                    text,
                    index,
                    "---"))
            {
                return new EdgeOperatorMatch(
                    index,
                    "---",
                    false);
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether an operator token starts at the requested source index.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="index">The candidate start index.</param>
    /// <param name="token">The edge token.</param>
    /// <returns><see langword="true"/> when the token matches.</returns>
    private static bool MatchesOperator(
            string text,
            int index,
            string token) =>
        index +
            token.Length <=
        text.Length &&
        text.AsSpan(
                index,
                token.Length)
            .SequenceEqual(
                token.AsSpan());

    /// <summary>
    /// Parses one supported Mermaid node token, including classic bracket forms
    /// and the newer <c>@{ shape: ..., label: ... }</c> definition syntax.
    /// </summary>
    /// <param name="text">The node token.</param>
    /// <param name="token">The parsed token when successful.</param>
    /// <returns><see langword="true"/> when the token is supported.</returns>
    private static bool TryParseNodeToken(
            string text,
            [NotNullWhen(true)] out ParsedNodeToken? token)
    {
        string trimmed =
            text.Trim();

        int inlineClassIndex =
            trimmed.IndexOf(
                ":::",
                StringComparison.Ordinal);

        if (inlineClassIndex >
            0)
        {
            trimmed =
                trimmed[..inlineClassIndex].Trim();
        }

        if (TryParseAttributeNodeToken(
                trimmed,
                out token))
        {
            return true;
        }

        Match idMatch =
            NodeIdPrefixPattern().Match(
                trimmed);

        if (!idMatch.Success ||
            idMatch.Index !=
                0)
        {
            token =
                null!;
            return false;
        }

        string id =
            idMatch.Groups["id"].Value;
        string suffix =
            trimmed[idMatch.Length..].Trim();

        if (suffix.Length ==
            0)
        {
            token =
                new ParsedNodeToken(
                    id,
                    id,
                    FlowchartNodeShape.Rectangle,
                    false);
            return true;
        }

        if (!TryParseClassicShape(
                suffix,
                out string? label,
                out FlowchartNodeShape shape))
        {
            token =
                null!;
            return false;
        }

        token =
            new ParsedNodeToken(
                id,
                NormalizeLabel(
                    label),
                shape,
                true);

        return true;
    }

    /// <summary>
    /// Parses the classic Mermaid delimiters used to define common node shapes.
    /// </summary>
    /// <param name="suffix">The text following the node identifier.</param>
    /// <param name="label">The visible node label.</param>
    /// <param name="shape">The node shape.</param>
    /// <returns><see langword="true"/> when the delimiters are supported.</returns>
    private static bool TryParseClassicShape(
            string suffix,
            [NotNullWhen(true)] out string? label,
            out FlowchartNodeShape shape)
    {
        if (TryUnwrap(
                suffix,
                "[[",
                "]]",
                out label))
        {
            shape =
                FlowchartNodeShape.Subroutine;
            return true;
        }

        if (TryUnwrap(
                suffix,
                "[(",
                ")]",
                out label))
        {
            shape =
                FlowchartNodeShape.Database;
            return true;
        }

        if (TryUnwrap(
                suffix,
                "((",
                "))",
                out label))
        {
            shape =
                FlowchartNodeShape.Circle;
            return true;
        }

        if (TryUnwrap(
                suffix,
                "([",
                "])",
                out label))
        {
            shape =
                FlowchartNodeShape.Stadium;
            return true;
        }

        if (TryUnwrap(
                suffix,
                "{{",
                "}}",
                out label))
        {
            shape =
                FlowchartNodeShape.Hexagon;
            return true;
        }

        if (TryUnwrap(
                suffix,
                "{",
                "}",
                out label))
        {
            shape =
                FlowchartNodeShape.Decision;
            return true;
        }

        if (TryUnwrap(
                suffix,
                "(",
                ")",
                out label))
        {
            shape =
                FlowchartNodeShape.Rounded;
            return true;
        }

        if (TryUnwrap(
                suffix,
                "[",
                "]",
                out label))
        {
            shape =
                FlowchartNodeShape.Rectangle;
            return true;
        }

        label =
            null!;
        shape =
            FlowchartNodeShape.Rectangle;
        return false;
    }

    /// <summary>
    /// Parses a Mermaid expanded node definition such as
    /// <c>A@{ shape: rect, label: "Texte" }</c>.
    /// </summary>
    /// <param name="text">The complete node token.</param>
    /// <param name="token">The parsed token.</param>
    /// <returns><see langword="true"/> when expanded syntax was recognized.</returns>
    private static bool TryParseAttributeNodeToken(
            string text,
            [NotNullWhen(true)] out ParsedNodeToken? token)
    {
        Match match =
            AttributeNodePattern().Match(
                text);

        if (!match.Success)
        {
            token =
                null!;
            return false;
        }

        string id =
            match.Groups["id"].Value;
        string label =
            id;
        FlowchartNodeShape shape =
            FlowchartNodeShape.Rectangle;

        IReadOnlyList<string> attributes =
            SplitAttributes(
                match.Groups["body"].Value);

        foreach (string attribute in
                 attributes)
        {
            int separator =
                attribute.IndexOf(
                    ':');

            if (separator <=
                0)
            {
                continue;
            }

            string key =
                attribute[..separator]
                    .Trim();
            string value =
                attribute[(separator + 1)..]
                    .Trim();

            if (key.Equals(
                    "label",
                    StringComparison.OrdinalIgnoreCase))
            {
                label =
                    NormalizeLabel(
                        value);
                continue;
            }

            if (key.Equals(
                    "shape",
                    StringComparison.OrdinalIgnoreCase))
            {
                shape =
                    ParseExpandedShape(
                        value);
            }
        }

        token =
            new ParsedNodeToken(
                id,
                label,
                shape,
                true);
        return true;
    }

    /// <summary>
    /// Splits comma-delimited expanded-node attributes while respecting quotes.
    /// </summary>
    /// <param name="body">The attribute body.</param>
    /// <returns>The parsed attribute fragments.</returns>
    private static IReadOnlyList<string> SplitAttributes(
            string body)
    {
        List<string> result =
            [];
        StringBuilder current =
            new StringBuilder();
        char quote =
            '\0';

        for (int index = 0;
             index < body.Length;
             index++)
        {
            char character =
                body[index];

            if (quote !=
                '\0')
            {
                current.Append(
                    character);

                if (character ==
                        quote &&
                    (index ==
                         0 ||
                     body[index - 1] !=
                         '\\'))
                {
                    quote =
                        '\0';
                }

                continue;
            }

            if (character ==
                    '"' ||
                character ==
                    '\'')
            {
                quote =
                    character;
                current.Append(
                    character);
                continue;
            }

            if (character ==
                ',')
            {
                result.Add(
                    current.ToString());
                current.Clear();
                continue;
            }

            current.Append(
                character);
        }

        if (current.Length >
            0)
        {
            result.Add(
                current.ToString());
        }

        return result;
    }

    /// <summary>
    /// Maps expanded Mermaid shape names to native Nodalis preview shapes.
    /// </summary>
    /// <param name="value">The Mermaid shape token.</param>
    /// <returns>The closest portable Nodalis shape.</returns>
    private static FlowchartNodeShape ParseExpandedShape(
            string value)
    {
        string normalized =
            NormalizeLabel(
                    value)
                .Trim()
                .ToLowerInvariant();

        return normalized switch
        {
            "round" or
            "rounded" or
            "rounded-rect" =>
                FlowchartNodeShape.Rounded,
            "diamond" or
            "diam" or
            "decision" =>
                FlowchartNodeShape.Decision,
            "circle" =>
                FlowchartNodeShape.Circle,
            "cylinder" or
            "cyl" or
            "database" =>
                FlowchartNodeShape.Database,
            "subroutine" or
            "subproc" or
            "process" =>
                FlowchartNodeShape.Subroutine,
            "stadium" or
            "terminal" =>
                FlowchartNodeShape.Stadium,
            "hexagon" or
            "hex" =>
                FlowchartNodeShape.Hexagon,
            _ =>
                FlowchartNodeShape.Rectangle
        };
    }

    /// <summary>
    /// Removes one matching pair of shape delimiters.
    /// </summary>
    /// <param name="value">The source suffix.</param>
    /// <param name="prefix">The opening delimiter.</param>
    /// <param name="suffix">The closing delimiter.</param>
    /// <param name="content">The unwrapped content.</param>
    /// <returns><see langword="true"/> when both delimiters match.</returns>
    private static bool TryUnwrap(
            string value,
            string prefix,
            string suffix,
            [NotNullWhen(true)] out string? content)
    {
        if (value.StartsWith(
                prefix,
                StringComparison.Ordinal) &&
            value.EndsWith(
                suffix,
                StringComparison.Ordinal) &&
            value.Length >=
                prefix.Length +
                suffix.Length)
        {
            content =
                value[
                    prefix.Length..(value.Length - suffix.Length)];
            return true;
        }

        content =
            null!;
        return false;
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
            @"^\s*(?:flowchart|graph)(?:\s+(?<direction>TD|TB|LR|RL|BT))?\s*;?\s*$",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();

    /// <summary>
    /// Matches a Mermaid node identifier at the start of a token.
    /// </summary>
    /// <returns>The generated identifier expression.</returns>
    [GeneratedRegex(
            @"^(?<id>[A-Za-z_][A-Za-z0-9_-]*)",
            RegexOptions.CultureInvariant)]
    private static partial Regex NodeIdPrefixPattern();

    /// <summary>
    /// Matches Mermaid's expanded node-definition syntax.
    /// </summary>
    /// <returns>The generated expanded-node expression.</returns>
    [GeneratedRegex(
            @"^(?<id>[A-Za-z_][A-Za-z0-9_-]*)\s*@\{(?<body>.*)\}\s*$",
            RegexOptions.CultureInvariant)]
    private static partial Regex AttributeNodePattern();

    /// <summary>
    /// Matches the alternate readable edge-label syntax <c>-- label --&gt;</c>.
    /// </summary>
    /// <returns>The generated edge-label expression.</returns>
    [GeneratedRegex(
            @"--\s+(?<label>[^|\[\]{}()]+?)\s+-->",
            RegexOptions.CultureInvariant)]
    private static partial Regex TextLabelEdgePattern();

    private sealed record ParsedNodeToken(
        string Id,
        string Label,
        FlowchartNodeShape Shape,
        bool ExplicitDefinition);

    private readonly record struct EdgeOperatorMatch(
        int Index,
        string Token,
        bool HasArrow);

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
