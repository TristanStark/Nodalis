using Nodalis.Core.Semantic;

/// <summary>Validates deterministic canonical Markdown chunks and privacy exclusions.</summary>
internal static class SemanticChunkingSmokeTests
{
    /// <summary>Exercises headings, duplicates, code, composite references and long inputs.</summary>
    public static void Run()
    {
        Guid document = Guid.NewGuid();
        string markdown = "---\nsecret: ne_pas_indexer\n---\n" +
            "# Dossier\nTexte initial\n\n## Tests\nLes commandes vérifiées\n\n" +
            "~~~md\ninformation_confidentielle_dans_code\n~~~\n" +
            "![[autre_document#Tests]]\n\n## Tests\nAutre sous-section\n";
        IReadOnlyList<SemanticChunk> chunks = SemanticMarkdownChunker.Split(
            document, "Applications/Projet/notes.md", markdown);
        Assert(chunks.Count == 3 && chunks.All(chunk =>
                !chunk.Content.Contains("ne_pas_indexer") &&
                !chunk.Content.Contains("information_confidentielle_dans_code") &&
                !chunk.Content.Contains("![[")),
            "Exclude YAML metadata, code examples and composite virtual content.");
        Assert(chunks[1].Heading == "Tests" && chunks[1].HeadingLine == 7 &&
            chunks[2].Heading == "Tests" && chunks[2].HeadingLine == 15,
            "Duplicated headings retain unambiguous numeric provenance.");
        IReadOnlyList<SemanticChunk> again = SemanticMarkdownChunker.Split(
            document, "Applications/Projet/notes.md", markdown);
        Assert(chunks.Select(chunk => chunk.ChunkId).SequenceEqual(
                again.Select(chunk => chunk.ChunkId)),
            "Chunk IDs must be stable on identical canonical inputs.");
        Assert(SemanticMarkdownChunker.Split(document, ".nodalis/cache.md", markdown).Count == 0 &&
            SemanticMarkdownChunker.Split(document, "Secrets/password.md", markdown).Count == 0 &&
            SemanticMarkdownChunker.Split(document, "docs/private.md", markdown).Count == 0 &&
            SemanticMarkdownChunker.Split(document, "../escape.md", markdown).Count == 0 &&
            SemanticMarkdownChunker.Split(document, "image.png", markdown).Count == 0,
            "Technical, hidden, sensitive and non-Markdown paths are not eligible.");
        Assert(!SemanticMarkdownChunker.IsEligiblePath("docs/meeting.md",
                new SemanticChunkingOptions
                {
                    ExcludedPathPatterns = ["docs/*"]
                }), "User-defined exclusions must work.");
        string longBody = "Un passage avec contexte à conserver. " +
            new string('x', 12400);
        IReadOnlyList<SemanticChunk> longChunks = SemanticMarkdownChunker.Split(
            document, "project/report.md", "# Grand document\n" + longBody,
            new SemanticChunkingOptions
            {
                MaxCharacters = 800,
                OverlapCharacters = 100
            });
        Assert(longChunks.Count > 10 && longChunks.All(chunk => chunk.Content.Length <= 800) &&
            longChunks.All(chunk => chunk.SourceLine >= 2),
            "Long canonical sources must create bounded overlapping passages.");
        Console.WriteLine("[Semantic 1.7] Markdown chunking, deterministic IDs and exclusions passed.");
    }

    /// <summary>Raises a regression error on failed eligibility or provenance.</summary>
    /// <param name="condition">Assertion outcome.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
