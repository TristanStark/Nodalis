using System.Text;
using Nodalis.Core.Glossary;
using Nodalis.Infrastructure.Glossary;

/// <summary>
/// Covers line ending, offset and cancellation compatibility of the
/// allocation-bounded, streaming glossary Markdown parser.
/// </summary>
internal static class GlossaryStreamingParseSmokeTests
{
    /// <summary>Verifies parsing with mixed line endings and synthetic terms.</summary>
    public static void Run()
    {
        GlossaryScope scope = new()
        {
            Kind = GlossaryScopeKind.Project,
            DisplayName = "Projet fictif",
            DirectoryPath = "synthetic/project",
            FilePath = "synthetic/project/Glossaire.md"
        };

        string[] endings = ["\n", "\r\n", "\r"];
        StringBuilder markdown = new("# Glossaire\r\n");
        for (int index = 0; index < 250; index++)
        {
            string ending = endings[index % endings.Length];
            markdown.Append($"## Terme {index:D4}").Append(ending);
            markdown.Append($"**Définition :** Définition {index:D4}").Append(ending);
            markdown.Append($"**Synonymes :** Alias {index:D4}").Append(ending);
        }

        IReadOnlyList<GlossaryEntry> parsed =
            GlossaryService.Parse(markdown.ToString(), scope);
        Assert(parsed.Count == 250,
            "All synthetic terms must survive mixed CR, LF and CRLF parsing.");

        for (int index = 0; index < parsed.Count; index++)
        {
            GlossaryEntry entry = parsed[index];
            Assert(entry.Term == $"Terme {index:D4}" &&
                   entry.Definition == $"Définition {index:D4}" &&
                   entry.LineNumber == 2 + index * 3 &&
                   entry.Synonyms.SequenceEqual([$"Alias {index:D4}"]),
                "The streaming parser must preserve heading offsets and aliases.");
        }

        string multiline = "## Élément\r\n" +
                           "**Définition :** Première ligne\r" +
                           "Deuxième ligne\n" +
                           "\r\n" +
                           "**Acronymes :** EL\r\n" +
                           "## Suivant\n" +
                           "**Définition :** Suite\r";
        IReadOnlyList<GlossaryEntry> multilineEntries =
            GlossaryService.Parse(multiline, scope);
        Assert(multilineEntries.Count == 2 &&
               multilineEntries[0].LineNumber == 1 &&
               multilineEntries[0].Definition ==
                   "Première ligne" + Environment.NewLine + "Deuxième ligne" &&
               multilineEntries[0].Acronyms.SequenceEqual(["EL"]) &&
               multilineEntries[1].LineNumber == 6 &&
               multilineEntries[1].Definition == "Suite",
            "Mixed newline styles cannot change multiline definitions or source lines.");

        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        bool interrupted = false;
        try
        {
            GlossaryService.Parse(markdown.ToString(), scope, canceled.Token);
        }
        catch (OperationCanceledException)
        {
            interrupted = true;
        }

        Assert(interrupted,
            "A canceled parse must not scan or publish its glossary.");
    }

    /// <summary>Fails a regression without exposing document content.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Synthetic failure description.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
