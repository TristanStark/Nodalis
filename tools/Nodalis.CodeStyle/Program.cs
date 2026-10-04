using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Nodalis.CodeStyle;

internal static partial class Program
{
    private static readonly string[] SourceRoots = ["src", "tests"];

    /// <summary>
    /// Runs the Nodalis code-style checker or fixer.
    /// </summary>
    /// <param name="args">Command-line arguments. Use <c>--check</c> or <c>--fix</c>.</param>
    /// <returns>Zero when the requested operation succeeds; otherwise a non-zero exit code.</returns>
    public static int Main(string[] args)
    {
        string mode = args.Length == 0 ? "--check" : args[0];
        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        IReadOnlyList<string> files = EnumerateCSharpFiles(repositoryRoot);

        return mode switch
        {
            "--check" => Check(files, repositoryRoot),
            "--fix" => Fix(files, repositoryRoot),
            _ => PrintUsage(),
        };
    }

    /// <summary>
    /// Locates the repository root by walking upward until <c>Nodalis.sln</c> is found.
    /// </summary>
    /// <param name="startDirectory">Directory from which to start the search.</param>
    /// <returns>The absolute repository root path.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the repository root cannot be located.</exception>
    private static string FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);

        while (directory is not null)
        {
            string solutionPath = Path.Combine(directory.FullName, "Nodalis.sln");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Nodalis.sln.");
    }

    /// <summary>
    /// Enumerates C# source files that participate in the Nodalis application and tests.
    /// </summary>
    /// <param name="repositoryRoot">Absolute repository root path.</param>
    /// <returns>The ordered list of C# source file paths.</returns>
    private static IReadOnlyList<string> EnumerateCSharpFiles(string repositoryRoot)
    {
        List<string> files = [];

        foreach (string sourceRoot in SourceRoots)
        {
            string absoluteRoot = Path.Combine(repositoryRoot, sourceRoot);
            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            IEnumerable<string> rootFiles = Directory.EnumerateFiles(
                absoluteRoot,
                "*.cs",
                SearchOption.AllDirectories);

            foreach (string file in rootFiles)
            {
                if (IsGeneratedPath(file))
                {
                    continue;
                }

                files.Add(file);
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>
    /// Determines whether a file belongs to a generated build-output directory.
    /// </summary>
    /// <param name="path">Absolute file path to inspect.</param>
    /// <returns><see langword="true"/> for generated build output; otherwise <see langword="false"/>.</returns>
    private static bool IsGeneratedPath(string path)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string binSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        string objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

        return normalized.Contains(binSegment, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(objSegment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks every source file for implicit typing and missing XML documentation.
    /// </summary>
    /// <param name="files">C# files to inspect.</param>
    /// <param name="repositoryRoot">Repository root used to render relative paths.</param>
    /// <returns>Zero when no violation is found; otherwise one.</returns>
    private static int Check(IReadOnlyList<string> files, string repositoryRoot)
    {
        List<string> violations = [];

        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: file);
            SyntaxNode root = tree.GetRoot();

            AddVarViolations(root, file, repositoryRoot, violations);
            AddDocumentationViolations(root, file, repositoryRoot, violations);
        }

        if (violations.Count == 0)
        {
            Console.WriteLine($"Code-style verification passed for {files.Count} C# files.");
            return 0;
        }

        Console.Error.WriteLine($"Code-style verification failed with {violations.Count} violation(s):");
        foreach (string violation in violations)
        {
            Console.Error.WriteLine($"  {violation}");
        }

        return 1;
    }

    /// <summary>
    /// Adds diagnostics for every use of the implicit <c>var</c> type.
    /// </summary>
    /// <param name="root">Parsed syntax root.</param>
    /// <param name="file">Absolute source file path.</param>
    /// <param name="repositoryRoot">Repository root used to render relative paths.</param>
    /// <param name="violations">Destination collection for diagnostics.</param>
    private static void AddVarViolations(
        SyntaxNode root,
        string file,
        string repositoryRoot,
        ICollection<string> violations)
    {
        IEnumerable<TypeSyntax> implicitTypes = root.DescendantNodes()
            .SelectMany(GetImplicitVarTypes)
            .Distinct();

        foreach (TypeSyntax type in implicitTypes)
        {
            FileLinePositionSpan lineSpan = type.GetLocation().GetLineSpan();
            string relativePath = Path.GetRelativePath(repositoryRoot, file);
            int line = lineSpan.StartLinePosition.Line + 1;
            violations.Add($"{relativePath}:{line}: explicit type required; 'var' is forbidden.");
        }
    }

    /// <summary>
    /// Returns implicit <c>var</c> type nodes owned by the supplied syntax node.
    /// </summary>
    /// <param name="node">Syntax node to inspect.</param>
    /// <returns>Zero or more implicit type nodes.</returns>
    private static IEnumerable<TypeSyntax> GetImplicitVarTypes(SyntaxNode node)
    {
        if (node is VariableDeclarationSyntax variableDeclaration
            && variableDeclaration.Type.IsVar)
        {
            yield return variableDeclaration.Type;
        }

        if (node is ForEachStatementSyntax forEachStatement
            && forEachStatement.Type.IsVar)
        {
            yield return forEachStatement.Type;
        }

        if (node is DeclarationExpressionSyntax declarationExpression
            && declarationExpression.Type.IsVar)
        {
            yield return declarationExpression.Type;
        }
    }

    /// <summary>
    /// Adds diagnostics for member functions that do not have XML documentation.
    /// </summary>
    /// <param name="root">Parsed syntax root.</param>
    /// <param name="file">Absolute source file path.</param>
    /// <param name="repositoryRoot">Repository root used to render relative paths.</param>
    /// <param name="violations">Destination collection for diagnostics.</param>
    private static void AddDocumentationViolations(
        SyntaxNode root,
        string file,
        string repositoryRoot,
        ICollection<string> violations)
    {
        IEnumerable<BaseMethodDeclarationSyntax> members = root.DescendantNodes()
            .OfType<BaseMethodDeclarationSyntax>();

        foreach (BaseMethodDeclarationSyntax member in members)
        {
            if (HasXmlDocumentation(member))
            {
                continue;
            }

            FileLinePositionSpan lineSpan = member.GetLocation().GetLineSpan();
            string relativePath = Path.GetRelativePath(repositoryRoot, file);
            int line = lineSpan.StartLinePosition.Line + 1;
            string memberName = GetMemberName(member);
            violations.Add($"{relativePath}:{line}: XML documentation required for '{memberName}'.");
        }
    }

    /// <summary>
    /// Determines whether a member already has an XML documentation comment.
    /// </summary>
    /// <param name="member">Member declaration to inspect.</param>
    /// <returns><see langword="true"/> when XML documentation is present; otherwise <see langword="false"/>.</returns>
    private static bool HasXmlDocumentation(BaseMethodDeclarationSyntax member)
    {
        SyntaxTriviaList leadingTrivia = member.GetLeadingTrivia();

        foreach (SyntaxTrivia trivia in leadingTrivia)
        {
            if (trivia.GetStructure() is DocumentationCommentTriviaSyntax)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds missing XML documentation comments without changing program behavior.
    /// </summary>
    /// <param name="files">C# files to update.</param>
    /// <param name="repositoryRoot">Repository root used for progress output.</param>
    /// <returns>Zero when the operation succeeds.</returns>
    private static int Fix(IReadOnlyList<string> files, string repositoryRoot)
    {
        int changedFiles = 0;
        int documentedMembers = 0;

        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: file);
            SyntaxNode root = tree.GetRoot();
            SourceText sourceText = tree.GetText();

            List<DocumentationInsertion> insertions = BuildDocumentationInsertions(
                root,
                sourceText,
                source);

            if (insertions.Count == 0)
            {
                continue;
            }

            string updated = ApplyInsertions(source, insertions);
            File.WriteAllText(file, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            changedFiles++;
            documentedMembers += insertions.Count;
            Console.WriteLine(
                $"Documented {insertions.Count} member(s) in {Path.GetRelativePath(repositoryRoot, file)}.");
        }

        Console.WriteLine(
            $"XML documentation cleanup complete: {documentedMembers} member(s) across {changedFiles} file(s).");
        return 0;
    }

    /// <summary>
    /// Builds source insertions for each undocumented member function.
    /// </summary>
    /// <param name="root">Parsed syntax root.</param>
    /// <param name="sourceText">Source text used for line and indentation data.</param>
    /// <param name="source">Original source string.</param>
    /// <returns>Insertions ordered independently of source mutation.</returns>
    private static List<DocumentationInsertion> BuildDocumentationInsertions(
        SyntaxNode root,
        SourceText sourceText,
        string source)
    {
        List<DocumentationInsertion> insertions = [];
        string newLine = DetectNewLine(source);

        IEnumerable<BaseMethodDeclarationSyntax> members = root.DescendantNodes()
            .OfType<BaseMethodDeclarationSyntax>();

        foreach (BaseMethodDeclarationSyntax member in members)
        {
            if (HasXmlDocumentation(member))
            {
                continue;
            }

            TextLine line = sourceText.Lines.GetLineFromPosition(member.SpanStart);
            string indentation = source.Substring(line.Start, member.SpanStart - line.Start);

            if (!IndentationPattern().IsMatch(indentation))
            {
                int column = member.GetLocation().GetLineSpan().StartLinePosition.Character;
                indentation = new string(' ', column);
            }

            string documentation = BuildDocumentation(member, indentation, newLine);
            insertions.Add(new DocumentationInsertion(member.SpanStart, documentation));
        }

        return insertions;
    }

    /// <summary>
    /// Applies source insertions from the end of the file toward the beginning.
    /// </summary>
    /// <param name="source">Original source content.</param>
    /// <param name="insertions">Insertions to apply.</param>
    /// <returns>The updated source content.</returns>
    private static string ApplyInsertions(
        string source,
        IReadOnlyCollection<DocumentationInsertion> insertions)
    {
        StringBuilder builder = new(source);

        foreach (DocumentationInsertion insertion in insertions.OrderByDescending(item => item.Position))
        {
            builder.Insert(insertion.Position, insertion.Content);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds an XML documentation block for a member declaration.
    /// </summary>
    /// <param name="member">Member declaration to document.</param>
    /// <param name="indentation">Indentation used by the declaration.</param>
    /// <param name="newLine">Newline sequence used by the source file.</param>
    /// <returns>The XML documentation text to insert before the declaration.</returns>
    private static string BuildDocumentation(
        BaseMethodDeclarationSyntax member,
        string indentation,
        string newLine)
    {
        StringBuilder builder = new();
        string memberName = GetMemberName(member);

        builder.Append("/// <summary>").Append(newLine);
        builder.Append(indentation)
            .Append("/// ")
            .Append(BuildSummary(member, memberName))
            .Append(newLine);
        builder.Append(indentation).Append("/// </summary>").Append(newLine);

        if (member is MethodDeclarationSyntax method)
        {
            foreach (TypeParameterSyntax typeParameter in method.TypeParameterList?.Parameters
                         ?? default(SeparatedSyntaxList<TypeParameterSyntax>))
            {
                string typeParameterName = typeParameter.Identifier.ValueText;
                builder.Append(indentation)
                    .Append("/// <typeparam name=\"")
                    .Append(typeParameterName)
                    .Append("\">The <c>")
                    .Append(typeParameterName)
                    .Append("</c> type.</typeparam>")
                    .Append(newLine);
            }
        }

        foreach (ParameterSyntax parameter in member.ParameterList.Parameters)
        {
            string parameterName = parameter.Identifier.ValueText;
            builder.Append(indentation)
                .Append("/// <param name=\"")
                .Append(parameterName)
                .Append("\">The <c>")
                .Append(parameterName)
                .Append("</c> value.</param>")
                .Append(newLine);
        }

        if (member is MethodDeclarationSyntax returnableMethod
            && !returnableMethod.ReturnType.IsKind(SyntaxKind.VoidKeyword))
        {
            builder.Append(indentation)
                .Append("/// <returns>The result of the operation.</returns>")
                .Append(newLine);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Creates a concise summary sentence for a member declaration.
    /// </summary>
    /// <param name="member">Member declaration being documented.</param>
    /// <param name="memberName">Display name of the member.</param>
    /// <returns>A safe behavioral summary sentence.</returns>
    private static string BuildSummary(
        BaseMethodDeclarationSyntax member,
        string memberName)
    {
        return member switch
        {
            ConstructorDeclarationSyntax constructor =>
                $"Initializes a new instance of <see cref=\"{constructor.Identifier.ValueText}\"/>.",
            DestructorDeclarationSyntax =>
                "Finalizes the current instance.",
            OperatorDeclarationSyntax operatorDeclaration =>
                $"Implements the <c>{operatorDeclaration.OperatorToken.ValueText}</c> operator.",
            ConversionOperatorDeclarationSyntax =>
                "Converts the current value to the declared target type.",
            _ => $"Performs the <c>{memberName}</c> operation.",
        };
    }

    /// <summary>
    /// Gets a stable display name for a member declaration.
    /// </summary>
    /// <param name="member">Member declaration to name.</param>
    /// <returns>The member display name.</returns>
    private static string GetMemberName(BaseMethodDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => $"~{destructor.Identifier.ValueText}",
            OperatorDeclarationSyntax operatorDeclaration =>
                $"operator {operatorDeclaration.OperatorToken.ValueText}",
            ConversionOperatorDeclarationSyntax conversion =>
                $"operator {conversion.Type}",
            _ => member.Kind().ToString(),
        };
    }

    /// <summary>
    /// Detects the newline convention already used by a source file.
    /// </summary>
    /// <param name="source">Source text to inspect.</param>
    /// <returns><c>\r\n</c> for CRLF files; otherwise <c>\n</c>.</returns>
    private static string DetectNewLine(string source)
    {
        return source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    /// <summary>
    /// Prints command-line usage information.
    /// </summary>
    /// <returns>Exit code two for invalid command-line usage.</returns>
    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage: Nodalis.CodeStyle --check|--fix");
        return 2;
    }

    [GeneratedRegex(@"^[\t ]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IndentationPattern();

    private sealed record DocumentationInsertion(int Position, string Content);
}
