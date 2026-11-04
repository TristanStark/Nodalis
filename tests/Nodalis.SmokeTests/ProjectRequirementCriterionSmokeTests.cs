using Nodalis.Core.Markdown;
using Nodalis.Core.Requirements;

/// <summary>
/// Synthetic acceptance-criterion tests: stable block IDs, duplicate CA codes,
/// missing markers and fenced-code exclusions without modifying the Markdown.
/// </summary>
internal static class ProjectRequirementCriterionSmokeTests
{
    /// <summary>Checks local CA inventory and deliberately conservative identity.</summary>
    public static void Run()
    {
        Guid firstId = Guid.NewGuid();
        Guid duplicateId = Guid.NewGuid();
        string original = "# EXI-0001 — Fiche fictive\n" +
            "## Contexte\n" +
            "### CA-90 — Hors section\n" +
            "## Critères d'acceptation\n" +
            "### CA-01 — Afficher un résultat\n" +
            MarkdownBlockIdentityCodec.FormatMarker(firstId) + "\n" +
            "### CA-02 — Exporter un fichier\n" +
            "~~~md\n" +
            "### CA-99 — Faux critère dans du code\n" +
            "~~~\n" +
            "## Autre section\n" +
            "### CA-77 — Après la section\n";
        ProjectRequirementCriterionScan scan =
            ProjectRequirementCriterionCodec.Inspect(original);

        Assert(scan.Criteria.Count == 2 &&
               scan.Criteria[0].Reference == "CA-01" &&
               scan.Criteria[0].Id == firstId &&
               scan.Criteria[0].LocallyIdentified &&
               scan.Criteria[0].HeadingLine == 5 &&
               scan.Criteria[1].Reference == "CA-02" &&
               scan.Criteria[1].Id is null &&
               !scan.Criteria[1].LocallyIdentified &&
               scan.Diagnostics.Any(item => item.Contains("sans GUID",
                   StringComparison.Ordinal)),
            "Only the acceptance section may expose locally identified CA blocks.");

        string renamed = original.Replace(
            "CA-01 — Afficher un résultat",
            "CA-01 — Afficher un résultat renommé",
            StringComparison.Ordinal);
        ProjectRequirementCriterionScan afterRename =
            ProjectRequirementCriterionCodec.Inspect(renamed);
        Assert(afterRename.Criteria[0].Id == firstId &&
               afterRename.Criteria[0].Title == "Afficher un résultat renommé",
            "Renaming a criterion must preserve its marker identity.");

        string repeatedCodes = original.Replace(
            "CA-02 — Exporter un fichier",
            "CA-01 — Exporter un fichier",
            StringComparison.Ordinal);
        ProjectRequirementCriterionScan repeated =
            ProjectRequirementCriterionCodec.Inspect(repeatedCodes);
        Assert(repeated.Criteria.Count == 2 &&
               repeated.Criteria.All(item => !item.LocallyIdentified) &&
               repeated.Diagnostics.Any(item => item.Contains(
                   "dupliquée", StringComparison.Ordinal)),
            "Repeated local CA codes must invalidate ambiguous candidates.");

        string reusedId = original.Replace(
            "### CA-02 — Exporter un fichier\n",
            "### CA-02 — Exporter un fichier\n" +
                MarkdownBlockIdentityCodec.FormatMarker(firstId) + "\n",
            StringComparison.Ordinal);
        ProjectRequirementCriterionScan collisions =
            ProjectRequirementCriterionCodec.Inspect(reusedId);
        Assert(collisions.Criteria.Count == 2 &&
               collisions.Criteria.All(item => !item.LocallyIdentified),
            "Two criteria cannot reuse the same block marker.");

        string malformed = original.Replace(
            MarkdownBlockIdentityCodec.FormatMarker(firstId),
            "<!-- nodalis:block:invalid-guid -->",
            StringComparison.Ordinal);
        ProjectRequirementCriterionScan damaged =
            ProjectRequirementCriterionCodec.Inspect(malformed);
        Assert(damaged.Criteria[0].Id is null &&
               !damaged.Criteria[0].LocallyIdentified,
            "Malformed IDs cannot be published as stable links.");

        Assert(ProjectRequirementCriterionCodec.IsReference("CA-01") &&
               ProjectRequirementCriterionCodec.IsReference("CA-99") &&
               !ProjectRequirementCriterionCodec.IsReference("CA-00") &&
               !ProjectRequirementCriterionCodec.IsReference("CA-1") &&
               !ProjectRequirementCriterionCodec.IsReference("ca-01"),
            "Local criterion references require two nonzero canonical digits.");

        string fencedOnly = "## Critères d'acceptation\n" +
            "~~~markdown\n" +
            "### CA-01 — Fausse entrée\n" +
            MarkdownBlockIdentityCodec.FormatMarker(duplicateId) + "\n" +
            "~~~\n";
        Assert(ProjectRequirementCriterionCodec.Inspect(fencedOnly)
                   .Criteria.Count == 0,
            "Code fences cannot declare real acceptance criteria.");

        Assert(ProjectRequirementCriterionCodec.Inspect(
                   "## Critères d'acceptation\n").Criteria.Count == 0,
            "An empty section must not invent fake acceptance criteria.");

        ProjectRequirementCard card = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Reference = "EXI-0001",
            Title = "Exigence de démonstration",
            CreatedUtc = DateTimeOffset.UtcNow
        };
        string lineEnding = Environment.NewLine;
        string canonical = ProjectRequirementMarkdownCodec.Format(card);
        string source = canonical.Replace(
            "## Critères d'acceptation" + lineEnding + lineEnding,
            "## Critères d'acceptation" + lineEnding + lineEnding +
            "### CA-01 — Condition initiale" + lineEnding +
            MarkdownBlockIdentityCodec.FormatMarker(firstId) + lineEnding +
            lineEnding, StringComparison.Ordinal);
        Assert(source != canonical, "Synthetic CA fixture must contain a heading.");
        string published = ProjectRequirementCriterionTitleCodec.RewriteTitle(
            source, card, firstId, "Condition initiale", "Condition retenue");
        Assert(ProjectRequirementMarkdownCodec.Parse(published) == card &&
            published == source.Replace(
                "### CA-01 — Condition initiale",
                "### CA-01 — Condition retenue", StringComparison.Ordinal) &&
            ProjectRequirementCriterionCodec.Inspect(published).Criteria.Single().Id ==
                firstId,
            "Only the human-reviewed CA heading may change, never its GUID.");
        string unixSource = source.Replace("\r\n", "\n",
            StringComparison.Ordinal);
        string unixPublished = ProjectRequirementCriterionTitleCodec.RewriteTitle(
            unixSource, card, firstId, "Condition initiale",
            "Condition retenue");
        Assert(!unixPublished.Contains('\r') &&
            ProjectRequirementCriterionCodec.Inspect(unixPublished).Criteria
                .Single().Title == "Condition retenue",
            "Pure CA rewriting must support LF without CRLF normalization.");

        Reject(() => ProjectRequirementCriterionTitleCodec.RewriteTitle(
            source, card, firstId, "Ancienne valeur obsolète",
            "Condition retenue"), "Stale CA title must never publish.");
        Reject(() => ProjectRequirementCriterionTitleCodec.RewriteTitle(
            source, card, firstId, "Condition initiale",
            "Texte\n### CA-02 — Nouvelle section"),
            "AI cannot inject another heading as a CA title.");
        Reject(() => ProjectRequirementCriterionTitleCodec.RewriteTitle(
            source, card with { Title = "Autre EXI" }, firstId,
            "Condition initiale", "Condition retenue"),
            "A foreign EXI identity cannot be modified.");
        Reject(() => ProjectRequirementCriterionTitleCodec.RewriteTitle(
            source, card, Guid.NewGuid(),
            "Condition initiale", "Condition retenue"),
            "A missing block marker cannot be selected by title alone.");
        string reused = source.Replace(
            "## Dépendances et preuves",
            "### CA-02 — Autre section" + lineEnding +
                MarkdownBlockIdentityCodec.FormatMarker(firstId) + lineEnding +
                "## Dépendances et preuves", StringComparison.Ordinal);
        Reject(() => ProjectRequirementCriterionTitleCodec.RewriteTitle(
            reused, card, firstId, "Condition initiale", "Condition retenue"),
            "The same block GUID in an unrelated heading blocks rewriting.");
        string noncanonical = source.Replace(
            "CA-01 — Condition initiale", "CA-01: Condition initiale",
            StringComparison.Ordinal);
        Reject(() => ProjectRequirementCriterionTitleCodec.RewriteTitle(
            noncanonical, card, firstId, "Condition initiale",
            "Condition retenue"), "Noncanonical CA line must fail closed.");

        Guid assignedId = Guid.NewGuid();
        Assert(ProjectRequirementCriterionCreationCodec.NextReference(canonical) ==
            "CA-01" &&
            ProjectRequirementCriterionCreationCodec.NextReference(source) ==
                "CA-02",
            "CA creation must allocate only the first unused local reference.");
        string inserted = ProjectRequirementCriterionCreationCodec.Insert(
            source, card, "CA-02", "Nouveau critère confirmé", assignedId);
        ProjectRequirementCriterionScan created =
            ProjectRequirementCriterionCodec.Inspect(inserted);
        Assert(created.Criteria.Count == 2 &&
            created.Criteria[1].Reference == "CA-02" &&
            created.Criteria[1].Id == assignedId &&
            created.Criteria[1].LocallyIdentified &&
            created.Criteria[0].Id == firstId &&
            ProjectRequirementMarkdownCodec.Parse(inserted) == card &&
            inserted.Contains("## Dépendances et preuves", StringComparison.Ordinal),
            "New CA must have its own stable GUID and preserve the existing EXI.");
        string unixInserted = ProjectRequirementCriterionCreationCodec.Insert(
            unixSource, card, "CA-02", "Texte humain", Guid.NewGuid());
        Assert(!unixInserted.Contains('\r') &&
            ProjectRequirementCriterionCodec.Inspect(unixInserted)
                .Criteria.Count == 2,
            "CA creation must preserve the existing LF file style.");
        Reject(() => ProjectRequirementCriterionCreationCodec.Insert(
            source, card, "CA-01", "Code déjà pris", assignedId),
            "An AI must never reuse an occupied CA reference.");
        Reject(() => ProjectRequirementCriterionCreationCodec.Insert(
            source, card, "CA-02", "Titre\n### CA-03 — Injection", assignedId),
            "CA creation cannot add extra headings through a title.");
        Reject(() => ProjectRequirementCriterionCreationCodec.Insert(
            source, card, "CA-02", "Titre", firstId),
            "A new block cannot recycle an existing CA GUID.");
        Reject(() => ProjectRequirementCriterionCreationCodec.Insert(
            source, card with { Title = "Autre fiche" }, "CA-02", "Titre",
            assignedId), "A changed EXI identity must block CA creation.");
        string ambiguousSection = source.Replace(
            "## Dépendances et preuves",
            "## Critères d'acceptation" + lineEnding + lineEnding +
            "## Dépendances et preuves", StringComparison.Ordinal);
        Reject(() => ProjectRequirementCriterionCreationCodec.NextReference(
            ambiguousSection), "Repeated CA sections must require manual repair.");
        string duplicateCode = source.Replace("## Dépendances et preuves",
            "### CA-01 — Seconde condition" + lineEnding +
            "## Dépendances et preuves", StringComparison.Ordinal);
        Reject(() => ProjectRequirementCriterionCreationCodec.NextReference(
            duplicateCode), "Duplicated CA references block automatic allocation.");

        string markerLine = MarkdownBlockIdentityCodec.FormatMarker(firstId);
        string bodySource = source.Replace(
            markerLine + lineEnding + lineEnding,
            markerLine + lineEnding + lineEnding +
                "Texte CA initial" + lineEnding + lineEnding +
                "#### Exemple détaillé" + lineEnding +
                "Preuve de conformité" + lineEnding + lineEnding,
            StringComparison.Ordinal);
        Guid neighborId = Guid.NewGuid();
        bodySource = bodySource.Replace(
            "## Dépendances et preuves",
            "### CA-02 — Critère voisin" + lineEnding +
                MarkdownBlockIdentityCodec.FormatMarker(neighborId) +
                lineEnding + lineEnding +
                "Corps du voisin" + lineEnding + lineEnding +
                "## Dépendances et preuves", StringComparison.Ordinal);
        Assert(ProjectRequirementCriterionBodyCodec.Extract(
                bodySource, firstId) ==
                "Texte CA initial\n\n#### Exemple détaillé\nPreuve de conformité" &&
            ProjectRequirementCriterionBodyCodec.Extract(
                bodySource, neighborId) == "Corps du voisin" &&
            ProjectRequirementCriterionBodyCodec.ExtractAll(bodySource).Count == 2,
            "CA detailed bodies must be isolated by stable GUID, never CA code.");
        string bodyUpdated = ProjectRequirementCriterionBodyCodec.Rewrite(
            bodySource, card, firstId,
            "Texte CA initial\n\n#### Exemple détaillé\nPreuve de conformité",
            "Nouveau texte de recette\n\n#### Justification\nFait observable");
        Assert(ProjectRequirementCriterionBodyCodec.Extract(
                bodyUpdated, firstId) ==
                "Nouveau texte de recette\n\n#### Justification\nFait observable" &&
            ProjectRequirementCriterionBodyCodec.Extract(
                bodyUpdated, neighborId) == "Corps du voisin" &&
            bodyUpdated.Contains("## Dépendances et preuves",
                StringComparison.Ordinal) &&
            ProjectRequirementMarkdownCodec.Parse(bodyUpdated) == card &&
            ProjectRequirementCriterionCodec.Inspect(bodyUpdated).Criteria.Count == 2,
            "CA body edit preserves other CA contents and EXI metadata.");
        string lfBodies = bodySource.Replace("\r\n", "\n",
            StringComparison.Ordinal);
        string lfChanged = ProjectRequirementCriterionBodyCodec.Rewrite(
            lfBodies, card, firstId,
            "Texte CA initial\n\n#### Exemple détaillé\nPreuve de conformité",
            "Version revue");
        Assert(!lfChanged.Contains('\r') &&
            ProjectRequirementCriterionBodyCodec.Extract(lfChanged, firstId) ==
                "Version revue", "CA body rewrite cannot normalize LF to CRLF.");
        string fencedBody = "~~~md\n## Pas un vrai titre\n~~~";
        string fencedUpdated = ProjectRequirementCriterionBodyCodec.Rewrite(
            bodySource, card, firstId,
            "Texte CA initial\n\n#### Exemple détaillé\nPreuve de conformité",
            fencedBody);
        Assert(ProjectRequirementCriterionBodyCodec.Extract(
                fencedUpdated, firstId) == fencedBody,
            "Fenced fake H2 titles remain literal CA body text.");
        Reject(() => ProjectRequirementCriterionBodyCodec.Rewrite(
            bodySource, card, firstId,
            "Texte CA initial\n\n#### Exemple détaillé\nPreuve de conformité",
            "## Exigence injectée"), "Accepted CA bodies may not change H2 structure.");
        Reject(() => ProjectRequirementCriterionBodyCodec.Rewrite(
            bodySource, card, firstId, "Corps obsolète", "Autre texte"),
            "Concurrent CA body edits must block a stale approval.");
        Reject(() => ProjectRequirementCriterionBodyCodec.Rewrite(
            bodySource, card, firstId,
            "Texte CA initial\n\n#### Exemple détaillé\nPreuve de conformité",
            "~~~md\nBloc non clos"),
            "An unclosed code fence could hide later headings and must fail.");
        Reject(() => ProjectRequirementCriterionBodyCodec.Rewrite(
            bodySource, card, firstId,
            "Texte CA initial\n\n#### Exemple détaillé\nPreuve de conformité",
            "Nouvelle identité <!-- nodalis:block:" + Guid.NewGuid() + " -->"),
            "An AI-provided block GUID cannot be injected into CA body.");
        Assert(ProjectRequirementCriterionBodyCodec.Extract(
                bodySource, Guid.NewGuid()) is null,
            "Unknown CA GUID has no inferred or fallback body.");
        string ambiguousBody = bodySource.Replace(
            "## Dépendances et preuves",
            "## Critères d'acceptation" + lineEnding + lineEnding +
            "## Dépendances et preuves", StringComparison.Ordinal);
        Assert(ProjectRequirementCriterionBodyCodec.ExtractAll(
                ambiguousBody).Count == 0,
            "Duplicated acceptance sections make every CA body unsafe.");
        string unclosedSource = bodySource.Replace("Texte CA initial",
            "~~~md" + lineEnding + "Texte CA initial",
            StringComparison.Ordinal);
        Assert(ProjectRequirementCriterionBodyCodec.Extract(
                unclosedSource, firstId) is null,
            "Open source fence must not swallow later CA or EXI headings.");
    }

    /// <summary>Expects an unsafe CA rewrite to be rejected.</summary>
    /// <param name="action">Pure rewrite under test.</param>
    /// <param name="message">Safety invariant.</param>
    private static void Reject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>Reports only fixed synthetic diagnostics.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Synthetic failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
