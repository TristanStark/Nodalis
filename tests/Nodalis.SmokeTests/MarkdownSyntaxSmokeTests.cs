using Nodalis.Core.Markdown;

internal static class MarkdownSyntaxSmokeTests
{
    public static void Run()
    {
        AssertKind("# Titre majeur", 3, MarkdownSyntaxKind.Heading);
        AssertKind("### Sous-titre", 1, MarkdownSyntaxKind.Muted);
        AssertKind("### Sous-titre", 5, MarkdownSyntaxKind.Heading);
        AssertKind("- [ ] PERF-01", 3, MarkdownSyntaxKind.TaskOpen);
        AssertKind("- [x] PERF-01", 3, MarkdownSyntaxKind.TaskDone);
        AssertKind("> Note utile", 4, MarkdownSyntaxKind.Quote);
        AssertKind("**important**", 4, MarkdownSyntaxKind.Strong);
        AssertKind("*italique*", 4, MarkdownSyntaxKind.Emphasis);
        AssertKind("[lien](notes.md)", 3, MarkdownSyntaxKind.Link);
        AssertKind("[[Tests|Recette]]", 6, MarkdownSyntaxKind.Link);
        AssertKind("texte " + (char)96 + "code" + (char)96, 8, MarkdownSyntaxKind.Code);
        AssertKind("- [ ] Ticket | Statut : À faire", 19, MarkdownSyntaxKind.Metadata);
        AssertKind("- [ ] Ticket | Priorité : Haute", 22, MarkdownSyntaxKind.Priority);
        AssertKind("sans syntaxe", 3, MarkdownSyntaxKind.Text);

        string[] examples =
        [
            "# Titre",
            "texte ordinaire",
            "une **mise en forme** et [un lien](document.md)",
            "- [x] terminé | Responsable : Validation",
            "> Citation et *mise en évidence*",
            new string((char)96, 3) + "mermaid",
            ""
        ];

        foreach (string example in examples)
        {
            IReadOnlyList<MarkdownSyntaxSpan> spans =
                MarkdownSyntaxTokenizer.Tokenize(example);
            int next = 0;
            foreach (MarkdownSyntaxSpan span in spans)
            {
                if (span.Start != next || span.Length <= 0)
                {
                    throw new InvalidOperationException(
                        "Les spans Markdown doivent être contigus et non vides.");
                }

                next += span.Length;
            }

            if (next != example.Length)
            {
                throw new InvalidOperationException(
                    "La coloration ne doit perdre aucun caractère Markdown.");
            }
        }
    }

    private static void AssertKind(
        string source,
        int position,
        MarkdownSyntaxKind expected)
    {
        IReadOnlyList<MarkdownSyntaxSpan> spans =
            MarkdownSyntaxTokenizer.Tokenize(source);

        MarkdownSyntaxSpan match = spans.FirstOrDefault(
            span => position >= span.Start &&
                    position < span.Start + span.Length);

        if (match.Kind != expected)
        {
            throw new InvalidOperationException(
                "Coloration Markdown inattendue pour " +
                source + " à la position " +
                position + ": " + match.Kind +
                ", attendu " + expected + ".");
        }
    }
}
