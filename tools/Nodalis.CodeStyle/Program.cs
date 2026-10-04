using System.Runtime.InteropServices;
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

    private static readonly SymbolDisplayFormat ExplicitTypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

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
    /// Adds diagnostics for every implicit <c>var</c> construct.
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
        IEnumerable<SyntaxNode> implicitNodes = GetImplicitVarNodes(root);

        foreach (SyntaxNode implicitNode in implicitNodes)
        {
            FileLinePositionSpan lineSpan = implicitNode.GetLocation().GetLineSpan();
            string relativePath = Path.GetRelativePath(repositoryRoot, file);
            int line = lineSpan.StartLinePosition.Line + 1;
            violations.Add($"{relativePath}:{line}: explicit type required; 'var' is forbidden.");
        }
    }

    /// <summary>
    /// Returns every syntax node that represents an implicit <c>var</c> construct.
    /// </summary>
    /// <param name="root">Parsed syntax root.</param>
    /// <returns>The implicit typing nodes found in the tree.</returns>
    private static IEnumerable<SyntaxNode> GetImplicitVarNodes(SyntaxNode root)
    {
        foreach (VariableDeclarationSyntax declaration in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            if (declaration.Type.IsVar)
            {
                yield return declaration.Type;
            }
        }

        foreach (ForEachStatementSyntax forEachStatement in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (forEachStatement.Type.IsVar)
            {
                yield return forEachStatement.Type;
            }
        }

        foreach (DeclarationExpressionSyntax declarationExpression in root.DescendantNodes().OfType<DeclarationExpressionSyntax>())
        {
            if (declarationExpression.Type.IsVar)
            {
                yield return declarationExpression.Type;
            }
        }

        foreach (VarPatternSyntax varPattern in root.DescendantNodes().OfType<VarPatternSyntax>())
        {
            yield return varPattern;
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
    /// Applies explicit typing and XML documentation fixes without changing program behavior.
    /// </summary>
    /// <param name="files">C# files to update.</param>
    /// <param name="repositoryRoot">Repository root used for progress output.</param>
    /// <returns>Zero when every fixable construct was migrated; otherwise one.</returns>
    private static int Fix(IReadOnlyList<string> files, string repositoryRoot)
    {
        int unresolvedTypes = FixExplicitTypes(files, repositoryRoot);
        FixDocumentation(files, repositoryRoot);
        return unresolvedTypes == 0 ? 0 : 1;
    }

    /// <summary>
    /// Replaces implicit <c>var</c> constructs with their Roslyn-resolved static types.
    /// </summary>
    /// <param name="files">C# files to update.</param>
    /// <param name="repositoryRoot">Repository root used for progress output.</param>
    /// <returns>The number of implicit types that could not be resolved safely.</returns>
    private static int FixExplicitTypes(IReadOnlyList<string> files, string repositoryRoot)
    {
        CSharpParseOptions parseOptions = new(languageVersion: LanguageVersion.Latest);
        List<SyntaxTree> syntaxTrees = [CreateImplicitUsingsTree(parseOptions)];
        Dictionary<string, SyntaxTree> treeByFile = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions, file);
            syntaxTrees.Add(tree);
            treeByFile[file] = tree;
        }

        IReadOnlyList<MetadataReference> references = BuildMetadataReferences();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Nodalis.CodeStyle.Analysis",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        int changedFiles = 0;
        int replacedTypes = 0;
        int unresolvedTypes = 0;

        foreach (string file in files)
        {
            SyntaxTree tree = treeByFile[file];
            SyntaxNode root = tree.GetRoot();
            SemanticModel semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            List<TextReplacement> replacements = BuildExplicitTypeReplacements(root, semanticModel, file);

            int fileUnresolved = replacements.Count(item => !item.IsResolved);
            unresolvedTypes += fileUnresolved;

            List<TextReplacement> resolved = replacements
                .Where(item => item.IsResolved)
                .OrderByDescending(item => item.Start)
                .ToList();

            if (resolved.Count == 0)
            {
                continue;
            }

            string source = File.ReadAllText(file);
            StringBuilder builder = new(source);

            foreach (TextReplacement replacement in resolved)
            {
                builder.Remove(replacement.Start, replacement.Length);
                builder.Insert(replacement.Start, replacement.Content);
            }

            File.WriteAllText(
                file,
                builder.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            changedFiles++;
            replacedTypes += resolved.Count;
            Console.WriteLine(
                $"Replaced {resolved.Count} implicit type(s) in {Path.GetRelativePath(repositoryRoot, file)}.");
        }

        Console.WriteLine(
            $"Explicit-type cleanup complete: {replacedTypes} replacement(s) across {changedFiles} file(s); "
            + $"{unresolvedTypes} unresolved construct(s).");

        return unresolvedTypes;
    }

    /// <summary>
    /// Builds text replacements for implicit typing constructs in one syntax tree.
    /// </summary>
    /// <param name="root">Parsed syntax root.</param>
    /// <param name="semanticModel">Semantic model used to resolve static types.</param>
    /// <param name="file">Source file path used for diagnostics.</param>
    /// <returns>Resolved and unresolved text replacements.</returns>
    private static List<TextReplacement> BuildExplicitTypeReplacements(
        SyntaxNode root,
        SemanticModel semanticModel,
        string file)
    {
        List<TextReplacement> replacements = [];

        foreach (VariableDeclarationSyntax declaration in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            if (!declaration.Type.IsVar)
            {
                continue;
            }

            ITypeSymbol? type = ResolveVariableDeclarationType(declaration, semanticModel);
            replacements.Add(CreateTypeReplacement(declaration.Type, type, file));
        }

        foreach (ForEachStatementSyntax forEachStatement in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (!forEachStatement.Type.IsVar)
            {
                continue;
            }

            ForEachStatementInfo info = semanticModel.GetForEachStatementInfo(forEachStatement);
            replacements.Add(CreateTypeReplacement(forEachStatement.Type, info.ElementType, file));
        }

        foreach (DeclarationExpressionSyntax declarationExpression in root.DescendantNodes().OfType<DeclarationExpressionSyntax>())
        {
            if (!declarationExpression.Type.IsVar)
            {
                continue;
            }

            ITypeSymbol? type = ResolveDeclarationExpressionType(declarationExpression, semanticModel);
            replacements.Add(CreateTypeReplacement(declarationExpression.Type, type, file));
        }

        foreach (VarPatternSyntax varPattern in root.DescendantNodes().OfType<VarPatternSyntax>())
        {
            ITypeSymbol? type = ResolveVarPatternType(varPattern, semanticModel);
            replacements.Add(CreatePatternReplacement(varPattern, type, file));
        }

        return replacements;
    }

    /// <summary>
    /// Resolves the static type for a local variable declaration.
    /// </summary>
    /// <param name="declaration">Implicitly typed declaration.</param>
    /// <param name="semanticModel">Semantic model used for type resolution.</param>
    /// <returns>The resolved type, or <see langword="null"/> when it cannot be named safely.</returns>
    private static ITypeSymbol? ResolveVariableDeclarationType(
        VariableDeclarationSyntax declaration,
        SemanticModel semanticModel)
    {
        VariableDeclaratorSyntax? declarator = declaration.Variables.FirstOrDefault();
        if (declarator?.Initializer is null)
        {
            return null;
        }

        TypeInfo typeInfo = semanticModel.GetTypeInfo(declarator.Initializer.Value);
        ITypeSymbol? type = typeInfo.ConvertedType ?? typeInfo.Type;
        return IsNameableType(type) ? type : null;
    }

    /// <summary>
    /// Resolves the static type for an <c>out var</c> or similar declaration expression.
    /// </summary>
    /// <param name="declaration">Implicit declaration expression.</param>
    /// <param name="semanticModel">Semantic model used for type resolution.</param>
    /// <returns>The resolved type, or <see langword="null"/> when it cannot be named safely.</returns>
    private static ITypeSymbol? ResolveDeclarationExpressionType(
        DeclarationExpressionSyntax declaration,
        SemanticModel semanticModel)
    {
        if (declaration.Designation is not SingleVariableDesignationSyntax designation)
        {
            return null;
        }

        ILocalSymbol? local = semanticModel.GetDeclaredSymbol(designation) as ILocalSymbol;
        if (local is null)
        {
            return null;
        }

        return IsNameableType(local.Type) ? local.Type : null;
    }

    /// <summary>
    /// Resolves the static type captured by a <c>var</c> pattern.
    /// </summary>
    /// <param name="varPattern">Implicit pattern declaration.</param>
    /// <param name="semanticModel">Semantic model used for type resolution.</param>
    /// <returns>The resolved type, or <see langword="null"/> when it cannot be named safely.</returns>
    private static ITypeSymbol? ResolveVarPatternType(
        VarPatternSyntax varPattern,
        SemanticModel semanticModel)
    {
        if (varPattern.Designation is not SingleVariableDesignationSyntax designation)
        {
            return null;
        }

        ILocalSymbol? local = semanticModel.GetDeclaredSymbol(designation) as ILocalSymbol;
        if (local is null)
        {
            return null;
        }

        return IsNameableType(local.Type) ? local.Type : null;
    }

    /// <summary>
    /// Creates a replacement for a syntax type currently written as <c>var</c>.
    /// </summary>
    /// <param name="syntax">Implicit type syntax to replace.</param>
    /// <param name="type">Resolved static type.</param>
    /// <param name="file">Source file path used for unresolved diagnostics.</param>
    /// <returns>A resolved replacement when possible; otherwise an unresolved marker.</returns>
    private static TextReplacement CreateTypeReplacement(
        TypeSyntax syntax,
        ITypeSymbol? type,
        string file)
    {
        if (!IsNameableType(type))
        {
            PrintUnresolved(file, syntax.GetLocation());
            return TextReplacement.Unresolved;
        }

        string typeName = type!.ToDisplayString(ExplicitTypeFormat);
        return new TextReplacement(syntax.SpanStart, syntax.Span.Length, typeName, true);
    }

    /// <summary>
    /// Creates a replacement for a <c>var</c> pattern.
    /// </summary>
    /// <param name="varPattern">Pattern syntax to replace.</param>
    /// <param name="type">Resolved static type.</param>
    /// <param name="file">Source file path used for unresolved diagnostics.</param>
    /// <returns>A resolved replacement when possible; otherwise an unresolved marker.</returns>
    private static TextReplacement CreatePatternReplacement(
        VarPatternSyntax varPattern,
        ITypeSymbol? type,
        string file)
    {
        if (!IsNameableType(type))
        {
            PrintUnresolved(file, varPattern.GetLocation());
            return TextReplacement.Unresolved;
        }

        string typeName = type!.ToDisplayString(ExplicitTypeFormat);
        string designation = varPattern.Designation.ToString();
        string replacement = $"{typeName} {designation}";
        return new TextReplacement(varPattern.SpanStart, varPattern.Span.Length, replacement, true);
    }

    /// <summary>
    /// Determines whether a Roslyn type symbol can be represented explicitly in C# source.
    /// </summary>
    /// <param name="type">Type symbol to inspect.</param>
    /// <returns><see langword="true"/> when the type is safe to name explicitly.</returns>
    private static bool IsNameableType(ITypeSymbol? type)
    {
        if (type is null || type is IErrorTypeSymbol)
        {
            return false;
        }

        return type switch
        {
            INamedTypeSymbol namedType =>
                !namedType.IsAnonymousType
                && namedType.TypeArguments.All(IsNameableType),
            IArrayTypeSymbol arrayType => IsNameableType(arrayType.ElementType),
            IPointerTypeSymbol pointerType => IsNameableType(pointerType.PointedAtType),
            IFunctionPointerTypeSymbol => true,
            IDynamicTypeSymbol => true,
            ITypeParameterSymbol => true,
            _ => true,
        };
    }

    /// <summary>
    /// Writes a diagnostic for an implicit type that cannot be converted automatically.
    /// </summary>
    /// <param name="file">Source file path.</param>
    /// <param name="location">Location of the unresolved syntax.</param>
    private static void PrintUnresolved(string file, Location location)
    {
        FileLinePositionSpan lineSpan = location.GetLineSpan();
        int line = lineSpan.StartLinePosition.Line + 1;
        Console.Error.WriteLine(
            $"Unable to resolve an explicit type safely at {file}:{line}; manual cleanup required.");
    }

    /// <summary>
    /// Creates the global usings normally emitted by the .NET SDK when ImplicitUsings is enabled.
    /// </summary>
    /// <param name="parseOptions">Parse options shared with the analyzed source files.</param>
    /// <returns>A synthetic syntax tree containing SDK-compatible global usings.</returns>
    private static SyntaxTree CreateImplicitUsingsTree(CSharpParseOptions parseOptions)
    {
        const string implicitUsings = """
            global using System;
            global using System.Collections.Generic;
            global using System.IO;
            global using System.Linq;
            global using System.Net.Http;
            global using System.Threading;
            global using System.Threading.Tasks;
            """;

        return CSharpSyntaxTree.ParseText(
            implicitUsings,
            parseOptions,
            "__NodalisImplicitUsings.g.cs");
    }

    /// <summary>
    /// Builds metadata references from the active runtime and Windows Desktop shared framework.
    /// </summary>
    /// <returns>Metadata references used by the semantic cleanup compilation.</returns>
    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        string? trustedPlatformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            {
                if (File.Exists(path))
                {
                    paths.Add(path);
                }
            }
        }

        AddWindowsDesktopReferences(paths);

        List<MetadataReference> references = [];
        foreach (string path in paths)
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        return references;
    }

    /// <summary>
    /// Adds WPF and Windows Desktop reference assemblies when the shared framework is installed.
    /// </summary>
    /// <param name="paths">Destination set for assembly paths.</param>
    private static void AddWindowsDesktopReferences(ISet<string> paths)
    {
        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        string sharedRoot = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", ".."));
        string desktopRoot = Path.Combine(sharedRoot, "Microsoft.WindowsDesktop.App");

        if (!Directory.Exists(desktopRoot))
        {
            return;
        }

        string? latestVersionDirectory = Directory.EnumerateDirectories(desktopRoot)
            .OrderByDescending(path => ParseFrameworkVersion(Path.GetFileName(path)))
            .FirstOrDefault();

        if (latestVersionDirectory is null)
        {
            return;
        }

        foreach (string assemblyPath in Directory.EnumerateFiles(
                     latestVersionDirectory,
                     "*.dll",
                     SearchOption.TopDirectoryOnly))
        {
            paths.Add(assemblyPath);
        }
    }

    /// <summary>
    /// Parses a shared-framework folder name into a sortable version.
    /// </summary>
    /// <param name="value">Folder name to parse.</param>
    /// <returns>The parsed version, or <c>0.0</c> when parsing fails.</returns>
    private static Version ParseFrameworkVersion(string value)
    {
        return Version.TryParse(value, out Version? version)
            ? version
            : new Version(0, 0);
    }

    /// <summary>
    /// Adds missing XML documentation comments without changing program behavior.
    /// </summary>
    /// <param name="files">C# files to update.</param>
    /// <param name="repositoryRoot">Repository root used for progress output.</param>
    private static void FixDocumentation(IReadOnlyList<string> files, string repositoryRoot)
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
            SeparatedSyntaxList<TypeParameterSyntax> typeParameters =
                method.TypeParameterList?.Parameters
                ?? default;

            foreach (TypeParameterSyntax typeParameter in typeParameters)
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

        builder.Append(indentation);
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

    private sealed record TextReplacement(
        int Start,
        int Length,
        string Content,
        bool IsResolved)
    {
        public static TextReplacement Unresolved { get; } = new(0, 0, string.Empty, false);
    }
}
