using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Fills the original five SFD master prompts with verified business Markdown
/// in their own "Entrées à coller" section; does not call or import AI.
/// </summary>
public sealed class SfdMasterPromptService
{
    private const int MaxSourceBytes = 256000;
    private const int MaxSourceCharacters = 96000;
    private const int MaxAdditionalCharacters = 120000;
    private const int MaxPromptCharacters = 560000;
    private const int MaxAdditionalSources = 64;

    private readonly SfdJourneyService _journeys;
    private readonly SfdDocumentRegistryService _documents;
    private readonly SfdActivityFunctionalityService _links;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Restricts all business data to one Nodalis workspace.</summary>
    /// <param name="workspaceRoot">Canonical active workspace.</param>
    public SfdMasterPromptService(string workspaceRoot)
    {
        _journeys = new SfdJourneyService(workspaceRoot);
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _links = new SfdActivityFunctionalityService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>
    /// Prepares a single original master prompt with the right user-provided
    /// business content IN its input slots. No source is silently synthesized.
    /// </summary>
    /// <param name="projectDirectory">One existing project with specifications.</param>
    /// <param name="promptKey">sfd-master-01 through sfd-master-05.</param>
    /// <param name="activityId">Explicit ACT target, mandatory for 02 and 04.</param>
    /// <param name="functionalityId">Explicit FCT target, mandatory for 04 and 05.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Copy-ready original template and separate data quality warnings.</returns>
    public async Task<SfdPreparedPrompt> PrepareAsync(
        string projectDirectory, string promptKey, Guid? activityId = null,
        Guid? functionalityId = null,
        CancellationToken cancellationToken = default)
    {
        string template = LoadMasterTemplate(promptKey);
        ProjectJourneySnapshot? journey = await _journeys.ReadAsync(
            projectDirectory, cancellationToken);
        if (journey is null)
        {
            throw new InvalidOperationException(
                "Initialisez le parcours SFD avant de préparer un prompt.");
        }

        ProjectJourneyOperation operation = journey.Document.Steps
            .SelectMany(step => step.Operations)
            .FirstOrDefault(item => item.PromptKey == promptKey) ??
            throw new InvalidOperationException(
                "Le parcours SFD ne contient pas " + promptKey +
                ". Actualisez les cinq prompts maîtres.");

        SfdDocumentCatalog catalog = await _documents.ListAsync(
            projectDirectory, cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Identités SFD ambiguës : " +
                string.Join("; ", catalog.Diagnostics.Take(3)));
        }

        SfdDocumentEntry? activity = activityId.HasValue
            ? catalog.Entries.SingleOrDefault(item =>
                item.Card.Id == activityId.Value &&
                item.Card.Kind == SfdDocumentKind.Activity)
            : null;
        SfdDocumentEntry? functionality = functionalityId.HasValue
            ? catalog.Entries.SingleOrDefault(item =>
                item.Card.Id == functionalityId.Value &&
                item.Card.Kind == SfdDocumentKind.Functionality)
            : null;
        if ((promptKey is "sfd-master-02" or "sfd-master-04") &&
            activity is null)
        {
            throw new InvalidOperationException(
                "Sélectionnez explicitement l'activité cible avant de générer.");
        }

        if ((promptKey is "sfd-master-04" or "sfd-master-05") &&
            functionality is null)
        {
            throw new InvalidOperationException(
                "Sélectionnez explicitement la fonctionnalité cible.");
        }

        SfdActivityFunctionalitySnapshot links = await _links.ReadAsync(
            projectDirectory, cancellationToken);
        if (promptKey == "sfd-master-04" &&
            !links.Registry.Links.Any(link =>
                link.ActivityId == activity!.Card.Id &&
                link.FunctionalityId == functionality!.Card.Id))
        {
            throw new InvalidOperationException(
                "L'activité cible n'est pas liée à la fonctionnalité sélectionnée.");
        }

        List<string> warnings = [];
        HashSet<string> readPaths = new(StringComparer.OrdinalIgnoreCase);
        string root = Path.GetFullPath(projectDirectory);
        string projectPresentation = Path.Combine(root, "Présentation.md");
        if (!File.Exists(projectPresentation))
        {
            projectPresentation = Path.Combine(root, "presentation.md");
        }

        string projectText = await ReadRequiredBusinessTextAsync(
            projectPresentation, "Présentation du projet (racine)",
            readPaths, warnings, cancellationToken);
        if (LooksLikeUnfilledTemplate(projectText))
        {
            warnings.Add("La présentation racine semble encore contenir " +
                "des rubriques vides. Complétez-la avant de partager le prompt.");
        }

        string additional = await ReadLinkedNotesAsync(
            operation, readPaths, warnings, cancellationToken);
        SfdDocumentEntry[] processes = catalog.Entries.Where(item =>
            item.Card.Kind == SfdDocumentKind.Process)
            .OrderBy(item => item.Card.Reference, StringComparer.Ordinal)
            .ToArray();
        SfdDocumentEntry[] activities = catalog.Entries.Where(item =>
            item.Card.Kind == SfdDocumentKind.Activity)
            .OrderBy(item => item.Card.Reference, StringComparer.Ordinal)
            .ToArray();

        List<string> inputValues = [];
        switch (promptKey)
        {
            case "sfd-master-01":
                inputValues.Add(CombineSections(projectText, additional));
                inputValues.Add(await ComposeProcessIndexAsync(
                    processes, activities, readPaths, warnings,
                    cancellationToken));
                inputValues.Add(
                    "Aucune décision distincte fournie au-delà des notes ci-dessus.");
                break;

            case "sfd-master-02":
            {
                SfdDocumentEntry process = RequireParent(
                    catalog, activity!);
                string fullParent = await ComposeProcessAsync(
                    process, activities, readPaths, warnings,
                    cancellationToken);
                string priorActivity = await ReadRequiredBusinessTextAsync(
                    activity!.AbsolutePath, "Activité cible",
                    readPaths, warnings, cancellationToken);
                if (activity.Card.Title == activity.Card.Reference)
                {
                    warnings.Add("L'activité " + activity.Card.Reference +
                        " n'a pas encore d'intitulé métier : renommez sa fiche.");
                }

                if (LooksLikeUnfilledTemplate(fullParent))
                {
                    warnings.Add("La présentation du PM parent comporte des " +
                        "rubriques vides. Renseignez son objectif et ses activités.");
                }

                inputValues.Add(projectText);
                inputValues.Add(fullParent);
                inputValues.Add(activity.Card.Reference + " — " +
                    activity.Card.Title);
                inputValues.Add(NotProvidedIfEmpty(additional));
                inputValues.Add(HasAuthoredBusinessDetails(priorActivity)
                    ? priorActivity : "Aucune fiche d'activité détaillée antérieure.");
                break;
            }

            case "sfd-master-03":
            {
                string index = await ComposeProcessIndexAsync(
                    processes, activities, readPaths, warnings,
                    cancellationToken);
                string summaries = await ComposeActivitySummariesAsync(
                    activities, readPaths, warnings, cancellationToken);
                string existingFeatures = await ComposeFeatureIndexAsync(
                    catalog, readPaths, warnings, cancellationToken);
                inputValues.Add(projectText);
                inputValues.Add(index);
                inputValues.Add(NotProvidedIfEmpty(summaries));
                inputValues.Add(NotProvidedIfEmpty(additional));
                inputValues.Add(NotProvidedIfEmpty(existingFeatures));
                break;
            }

            case "sfd-master-04":
            case "sfd-master-05":
            {
                Guid[] linkedIds = links.Registry.Links
                    .Where(link => link.FunctionalityId == functionality!.Card.Id)
                    .Select(link => link.ActivityId).Distinct().ToArray();
                if (linkedIds.Length == 0)
                {
                    warnings.Add("La FCT n'a aucune ACT liée : " +
                        "aucune couverture ne peut être attestée.");
                }

                string featureBody = await ComposeFunctionalityAsync(
                    functionality!, processes, activities, linkedIds,
                    readPaths, warnings, cancellationToken);
                string existingRequirements = await ComposeRequirementsAsync(
                    catalog, linkedIds, readPaths, warnings,
                    cancellationToken);
                string rules = await ReadOptionalBusinessTextAsync(
                    Path.Combine(root, "DOCUMENTATION",
                        "liste des règles métiers.md"),
                    "Catalogue partagé des RM", readPaths, warnings,
                    cancellationToken) ?? "";
                warnings.Add("Le stockage historique des EXU/EXS/EXR est par ACT " +
                    "et ne prouve pas la portée exacte FCT × ACT. " +
                    "Vérifiez manuellement leur périmètre.");

                if (promptKey == "sfd-master-04")
                {
                    string activityBody = await ReadRequiredBusinessTextAsync(
                        activity!.AbsolutePath, "Fiche ACT cible",
                        readPaths, warnings, cancellationToken);
                    inputValues.Add(projectText);
                    inputValues.Add(featureBody);
                    inputValues.Add(activity!.Card.Reference + " — " +
                        activity.Card.Title + "\n\n" + activityBody);
                    inputValues.Add(NotProvidedIfEmpty(existingRequirements,
                        "Aucun registre d'exigences vérifiable fourni."));
                    inputValues.Add(NotProvidedIfEmpty(rules,
                        "Catalogue RM non fourni."));
                    inputValues.Add(NotProvidedIfEmpty(additional));
                }
                else
                {
                    if (linkedIds.Length == 0 ||
                        string.IsNullOrWhiteSpace(existingRequirements))
                    {
                        warnings.Add("Le prompt 05 requiert normalement un " +
                            "index exhaustif des exigences de toutes les ACT " +
                            "de la FCT. Complétez les sources avant validation.");
                    }

                    inputValues.Add(CombineSections(projectText, additional));
                    inputValues.Add(featureBody);
                    inputValues.Add(NotProvidedIfEmpty(existingRequirements,
                        "Exigences de la FCT non fournies — couverture indéterminée."));
                    inputValues.Add(string.IsNullOrWhiteSpace(rules)
                        ? "Catalogue des règles utilisées non fourni."
                        : "Catalogue RM global (pertinence pour cette FCT à " +
                          "vérifier ; aucune association inventée) :\n\n" + rules);
                    if (!string.IsNullOrWhiteSpace(rules))
                    {
                        warnings.Add("Le registre RM fourni est global : " +
                            "les règles réellement applicables à cette FCT " +
                            "doivent être qualifiées avant validation.");
                    }
                    inputValues.Add("Aucune EMO ou décision complémentaire " +
                        "confirmée dans les sources sélectionnées.");
                    inputValues.Add("Non fourni — mode MONOBLOC.");
                }

                break;
            }
            default:
                throw new InvalidDataException("Prompt maître SFD inconnu.");
        }

        string generated = SfdMasterPromptTemplateAssembler.Assemble(
            template, promptKey, inputValues);
        if (generated.Length > MaxPromptCharacters)
        {
            throw new InvalidDataException(
                "Le prompt complet dépasse la taille maximale. " +
                "Réduisez les sources optionnelles ou découpez le travail.");
        }

        return new SfdPreparedPrompt(
            promptKey + " — " + operation.Name, generated,
            warnings, readPaths.Count);
    }

    /// <summary>Resolves an ACT's canonical process, never by filename prefix alone.</summary>
    /// <param name="catalog">Verified identities for the current project.</param>
    /// <param name="activity">Selected ACT.</param>
    /// <returns>Exactly one matching parent process.</returns>
    private static SfdDocumentEntry RequireParent(
        SfdDocumentCatalog catalog, SfdDocumentEntry activity) =>
        catalog.Entries.SingleOrDefault(item =>
            item.Card.Id == activity.Card.ParentId &&
            item.Card.Kind == SfdDocumentKind.Process) ??
        throw new InvalidDataException("PM parent de l'activité introuvable.");

    /// <summary>Builds one complete PM input including authored text and known ACT IDs.</summary>
    /// <param name="process">Verified PM.</param>
    /// <param name="activities">Project ACT index.</param>
    /// <param name="readPaths">Unique verified source paths.</param>
    /// <param name="warnings">Warnings outside the final prompt.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Existing PM document plus actual children, without fabricated descriptions.</returns>
    private async Task<string> ComposeProcessAsync(
        SfdDocumentEntry process, IReadOnlyList<SfdDocumentEntry> activities,
        HashSet<string> readPaths, List<string> warnings, CancellationToken ct)
    {
        string body = await ReadRequiredBusinessTextAsync(
            process.AbsolutePath, "Présentation du PM " + process.Card.Reference,
            readPaths, warnings, ct);
        StringBuilder result = new();
        if (!body.Contains(process.Card.Reference + " — " +
                process.Card.Title, StringComparison.Ordinal))
        {
            result.Append("### ").Append(process.Card.Reference)
                .Append(" — ").AppendLine(process.Card.Title).AppendLine();
        }

        result.AppendLine(body).AppendLine()
            .AppendLine("### Activités référencées sous ce processus");
        SfdDocumentEntry[] children = activities.Where(item =>
            item.Card.ParentId == process.Card.Id).ToArray();
        if (children.Length == 0)
        {
            result.AppendLine("Aucune activité enregistrée dans ce PM.");
        }
        else
        {
            foreach (SfdDocumentEntry item in children)
            {
                result.Append("- ").Append(item.Card.Reference).Append(" — ")
                    .AppendLine(item.Card.Title);
                if (item.Card.Title == item.Card.Reference)
                {
                    warnings.Add("ACT " + item.Card.Reference +
                        " sans intitulé métier documenté.");
                }
            }
        }

        return result.ToString().Trim();
    }

    /// <summary>Produces the documented project-wide PM/ACT map for prompts 01 and 03.</summary>
    /// <param name="processes">All current PMs.</param>
    /// <param name="activities">All current ACTs.</param>
    /// <param name="readPaths">Unique source list.</param>
    /// <param name="warnings">Incomplete inputs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exact project PM documents with existing child identities.</returns>
    private async Task<string> ComposeProcessIndexAsync(
        IReadOnlyList<SfdDocumentEntry> processes,
        IReadOnlyList<SfdDocumentEntry> activities,
        HashSet<string> readPaths, List<string> warnings, CancellationToken ct)
    {
        if (processes.Count == 0)
        {
            return "Aucun processus métier existant dans le référentiel SFD.";
        }

        StringBuilder index = new();
        foreach (SfdDocumentEntry process in processes)
        {
            index.AppendLine(await ComposeProcessAsync(process,
                activities, readPaths, warnings, ct)).AppendLine();
        }

        return index.ToString().Trim();
    }

    /// <summary>Extracts existing written ACT summaries without inventing missing descriptions.</summary>
    /// <param name="activities">All current ACTs.</param>
    /// <param name="readPaths">Unique source paths.</param>
    /// <param name="warnings">Incomplete source information.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Short authored summaries or empty when none is documented.</returns>
    private async Task<string> ComposeActivitySummariesAsync(
        IReadOnlyList<SfdDocumentEntry> activities,
        HashSet<string> readPaths, List<string> warnings, CancellationToken ct)
    {
        StringBuilder summaries = new();
        foreach (SfdDocumentEntry activity in activities)
        {
            string? text = await ReadOptionalBusinessTextAsync(
                activity.AbsolutePath, "Résumé " + activity.Card.Reference,
                readPaths, warnings, ct);
            if (text is null)
            {
                continue;
            }

            string? summary = ExtractNamedSection(
                text, "## Synthèse réutilisable pour les fonctionnalités");
            if (summary is null || !HasAuthoredBusinessDetails(summary))
            {
                continue;
            }

            summaries.AppendLine("### " + activity.Card.Reference + " — " +
                activity.Card.Title).AppendLine(summary).AppendLine();
        }

        return summaries.ToString().Trim();
    }

    /// <summary>Lists previously written FCTs without generating fictitious associations.</summary>
    /// <param name="catalog">Current SFD catalog.</param>
    /// <param name="readPaths">Unique files read.</param>
    /// <param name="warnings">Incomplete file warnings.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Existing FCT titles and their canonical Markdown.</returns>
    private async Task<string> ComposeFeatureIndexAsync(
        SfdDocumentCatalog catalog, HashSet<string> readPaths,
        List<string> warnings, CancellationToken ct)
    {
        StringBuilder features = new();
        foreach (SfdDocumentEntry feature in catalog.Entries.Where(item =>
            item.Card.Kind == SfdDocumentKind.Functionality))
        {
            string? body = await ReadOptionalBusinessTextAsync(
                feature.AbsolutePath, "FCT " + feature.Card.Reference,
                readPaths, warnings, ct);
            features.AppendLine("### " + feature.Card.Reference + " — " +
                feature.Card.Title);
            if (body is not null)
            {
                features.AppendLine(body);
            }

            features.AppendLine();
        }

        return features.ToString().Trim();
    }

    /// <summary>Provides the complete FCT and every real PM/ACT association.</summary>
    /// <param name="feature">The exact target FCT.</param>
    /// <param name="processes">Existing PM index.</param>
    /// <param name="activities">Existing ACT index.</param>
    /// <param name="linkedIds">User-recorded ACT links for this FCT only.</param>
    /// <param name="readPaths">Unique files read.</param>
    /// <param name="warnings">Missing or partial business sources.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Authored FCT body and all ACTs grouped by real parent PM.</returns>
    private async Task<string> ComposeFunctionalityAsync(
        SfdDocumentEntry feature, IReadOnlyList<SfdDocumentEntry> processes,
        IReadOnlyList<SfdDocumentEntry> activities,
        IReadOnlyCollection<Guid> linkedIds, HashSet<string> readPaths,
        List<string> warnings, CancellationToken ct)
    {
        string body = await ReadRequiredBusinessTextAsync(feature.AbsolutePath,
            "FCT cible " + feature.Card.Reference, readPaths, warnings, ct);
        StringBuilder result = new();
        string interfaceMode = feature.Card.FunctionalityMode ==
            SfdFunctionalityMode.WithInterface ? "Avec IHM" : "Sans IHM";
        result.AppendLine(feature.Card.Reference + " — " + feature.Card.Title)
            .AppendLine("Type : " + interfaceMode)
            .AppendLine(body).AppendLine()
            .AppendLine("### Activités réellement liées à cette fonctionnalité");
        foreach (SfdDocumentEntry process in processes)
        {
            SfdDocumentEntry[] perPm = activities.Where(item =>
                item.Card.ParentId == process.Card.Id &&
                linkedIds.Contains(item.Card.Id)).ToArray();
            if (perPm.Length == 0)
            {
                continue;
            }

            result.AppendLine("#### " + process.Card.Reference + " — " +
                process.Card.Title);
            foreach (SfdDocumentEntry activity in perPm)
            {
                result.AppendLine("- " + activity.Card.Reference +
                    " — " + activity.Card.Title);
            }
        }

        if (linkedIds.Count == 0)
        {
            result.AppendLine("Aucune activité liée dans le registre.");
        }

        if (linkedIds.Any(id => !activities.Any(item => item.Card.Id == id)))
        {
            warnings.Add("Certaines associations FCT/ACT pointent sur une ACT absente.");
        }

        return result.ToString().Trim();
    }

    /// <summary>
    /// Reads candidate historical EXU/EXS/EXR without asserting that their
    /// ACT storage alone proves attachment to the selected FCT.
    /// </summary>
    /// <param name="catalog">Verified SFD catalog.</param>
    /// <param name="linkedIds">Only ACT IDs linked to the target FCT.</param>
    /// <param name="readPaths">Unique verified source paths.</param>
    /// <param name="warnings">Warnings about missing inputs.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Known requirements, labeled as candidates needing review.</returns>
    private async Task<string> ComposeRequirementsAsync(
        SfdDocumentCatalog catalog, IReadOnlyCollection<Guid> linkedIds,
        HashSet<string> readPaths, List<string> warnings, CancellationToken ct)
    {
        StringBuilder result = new();
        foreach (SfdDocumentEntry item in catalog.Entries.Where(item =>
            (item.Card.Kind is SfdDocumentKind.UserRequirement or
                SfdDocumentKind.SystemRequirement or
                SfdDocumentKind.RegulatoryRequirement) &&
            item.Card.ParentId.HasValue &&
            linkedIds.Contains(item.Card.ParentId.Value)))
        {
            string? body = await ReadOptionalBusinessTextAsync(item.AbsolutePath,
                "EXU/EXS/EXR " + item.Card.Reference, readPaths, warnings, ct);
            if (body is null)
            {
                continue;
            }

            SfdDocumentEntry? parentActivity = catalog.Entries
                .SingleOrDefault(candidate =>
                    candidate.Card.Id == item.Card.ParentId &&
                    candidate.Card.Kind == SfdDocumentKind.Activity);
            result.AppendLine("### Exigence candidate " + item.Card.Reference)
                .AppendLine("ACT parent : " +
                    (parentActivity?.Card.Reference ?? "Non documentée"))
                .AppendLine("Portée FCT/ACT à confirmer.")
                .AppendLine(body).AppendLine();
        }

        return result.ToString().Trim();
    }

    /// <summary>Loads explicitly linked Markdown/TXT notes as optional source inputs.</summary>
    /// <param name="operation">Selected journey operation and linked paths.</param>
    /// <param name="readPaths">Unique source paths read.</param>
    /// <param name="warnings">Source warnings for the preview UI.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Human-readable business content without paths or file hashes.</returns>
    private async Task<string> ReadLinkedNotesAsync(
        ProjectJourneyOperation operation, HashSet<string> readPaths,
        List<string> warnings, CancellationToken ct)
    {
        StringBuilder notes = new();
        foreach (string reference in operation.SourcePaths.Distinct(
            StringComparer.OrdinalIgnoreCase).Take(MaxAdditionalSources))
        {
            ct.ThrowIfCancellationRequested();
            if (!IsSupportedTextFile(reference))
            {
                warnings.Add("Source non autorisée (Markdown/TXT uniquement) : " +
                    reference);
                continue;
            }

            string path;
            try
            {
                path = _sources.ResolveExistingFile(reference);
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or ArgumentException or
                InvalidDataException)
            {
                warnings.Add("Source liée absente/inaccessible : " + reference +
                    " (" + exception.Message + ")");
                continue;
            }

            if (readPaths.Contains(Path.GetFullPath(path)))
            {
                continue;
            }

            string? body = await ReadOptionalBusinessTextAsync(path,
                Path.GetFileName(path), readPaths, warnings, ct);
            if (body is null)
            {
                continue;
            }

            if (notes.Length + body.Length > MaxAdditionalCharacters)
            {
                warnings.Add("Notes additionnelles trop volumineuses : " +
                    Path.GetFileName(path) + " a été ignorée.");
                continue;
            }

            notes.AppendLine("### " + Path.GetFileNameWithoutExtension(path))
                .AppendLine(body).AppendLine();
        }

        if (operation.SourcePaths.Count > MaxAdditionalSources)
        {
            warnings.Add("Sources additionnelles au-delà de 64 ignorées.");
        }

        return notes.ToString().Trim();
    }

    /// <summary>Loads mandatory UTF-8 business content or fails without producing a misleading prompt.</summary>
    /// <param name="path">Required canonical Markdown file.</param>
    /// <param name="caption">Readable business role for diagnostics.</param>
    /// <param name="readPaths">Unique verified files read.</param>
    /// <param name="warnings">Quality warnings.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Entire authored business body without Nodalis YAML metadata.</returns>
    private async Task<string> ReadRequiredBusinessTextAsync(
        string path, string caption, HashSet<string> readPaths,
        List<string> warnings, CancellationToken ct)
    {
        string? body = await ReadBusinessTextAsync(
            path, caption, readPaths, warnings, required: true, ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidDataException(
                "La source obligatoire est vide : " + caption +
                ". Complétez le document avant de préparer le prompt.");
        }

        return body;
    }

    /// <summary>Loads optional UTF-8 Markdown/TXT; omissions are explicitly reported.</summary>
    /// <param name="path">Optional existing workspace file.</param>
    /// <param name="caption">Readable role.</param>
    /// <param name="readPaths">Unique files read.</param>
    /// <param name="warnings">Quality warnings.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Business body or null when optional evidence is unusable.</returns>
    private Task<string?> ReadOptionalBusinessTextAsync(
        string path, string caption, HashSet<string> readPaths,
        List<string> warnings, CancellationToken ct) =>
        ReadBusinessTextAsync(path, caption, readPaths, warnings,
            required: false, ct);

    /// <summary>
    /// Reads bounded, strictly decoded UTF-8 source text and removes only
    /// Nodalis identity front matter, leaving every authored sentence intact.
    /// </summary>
    /// <param name="path">Workspace-confined path.</param>
    /// <param name="caption">Readable role.</param>
    /// <param name="readPaths">Unique file names counted as evidence.</param>
    /// <param name="warnings">Quality notices.</param>
    /// <param name="required">Whether a failure must prevent prompt generation.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Complete evidence text, or null for an optional omitted source.</returns>
    private async Task<string?> ReadBusinessTextAsync(
        string path, string caption, HashSet<string> readPaths,
        List<string> warnings, bool required, CancellationToken ct)
    {
        if (!IsSupportedTextFile(path))
        {
            throw new InvalidDataException("Source DOCX ou binaire interdite : " + caption);
        }

        if (!File.Exists(path))
        {
            if (required)
            {
                throw new InvalidDataException(
                    "Source obligatoire introuvable : " + caption);
            }

            warnings.Add("Source optionnelle introuvable : " + caption);
            return null;
        }

        _sources.GetReferenceForExistingFile(path);
        FileInfo info = new(path);
        if (info.Length > MaxSourceBytes)
        {
            if (required)
            {
                throw new InvalidDataException("Source obligatoire trop volumineuse : " +
                    caption + ". Découpez-la sans en perdre le contenu.");
            }

            warnings.Add("Source trop volumineuse ignorée : " + caption);
            return null;
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, ct);
        string raw;
        try
        {
            raw = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "La source n'est pas du texte UTF-8 lisible : " + caption,
                exception);
        }

        string business = RemoveNodalisFrontMatter(raw).Trim();
        if (business.Length > MaxSourceCharacters)
        {
            if (required)
            {
                throw new InvalidDataException(
                    "Source obligatoire trop longue : " + caption +
                    ". La préparation ne tronque pas les données métier.");
            }

            warnings.Add("Source optionnelle trop longue ignorée : " + caption);
            return null;
        }

        if (business.Length == 0)
        {
            if (required)
            {
                throw new InvalidDataException("Source obligatoire vide : " + caption);
            }

            warnings.Add("Source optionnelle vide : " + caption);
            return null;
        }

        readPaths.Add(Path.GetFullPath(path));
        return business;
    }

    /// <summary>Checks only Markdown/TXT, never DOCX or other conversion paths.</summary>
    /// <param name="path">Full path or workspace reference.</param>
    /// <returns>Whether source is already text.</returns>
    private static bool IsSupportedTextFile(string path) =>
        Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes Nodalis-only YAML identity metadata from ACT/FCT/PM cards.</summary>
    /// <param name="markdown">Raw canonical Markdown or ordinary user prose.</param>
    /// <returns>Original authored text and headings, without internal GUID headers.</returns>
    private static string RemoveNodalisFrontMatter(string markdown)
    {
        string normalized = markdown.TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return normalized;
        }

        int end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end > 0 && normalized[..end].Contains(
                "nodalis-sfd-schema:", StringComparison.Ordinal))
        {
            return normalized[(end + 5)..];
        }

        return normalized;
    }

    /// <summary>Finds one previously authored ACT synthesis by heading, if present.</summary>
    /// <param name="body">Complete authored Markdown.</param>
    /// <param name="heading">Exact expected heading.</param>
    /// <returns>Section text or null.</returns>
    private static string? ExtractNamedSection(string body, string heading)
    {
        int start = body.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += heading.Length;
        int end = body.IndexOf("\n## ", start, StringComparison.Ordinal);
        return (end < 0 ? body[start..] : body[start..end]).Trim();
    }

    /// <summary>Marks blank Nodalis draft sections as needing human completion.</summary>
    /// <param name="body">Business Markdown body.</param>
    /// <returns>Whether the body contains a recognizably empty draft template.</returns>
    private static bool LooksLikeUnfilledTemplate(string body) =>
        body.Contains("## Finalité et périmètre\n\n##", StringComparison.Ordinal) ||
        body.Contains("## Déclencheur et acteurs\n\n##", StringComparison.Ordinal) ||
        body.Contains("_Décrire ici le contexte", StringComparison.Ordinal);

    /// <summary>Detects content beyond only empty headings and generic placeholders.</summary>
    /// <param name="body">Markdown body.</param>
    /// <returns>Whether it contains authored prose or table rows.</returns>
    private static bool HasAuthoredBusinessDetails(string body) =>
        body.Split('\n').Any(line =>
            !string.IsNullOrWhiteSpace(line) &&
            !line.TrimStart().StartsWith('#') &&
            !line.TrimStart().StartsWith('_') &&
            !line.TrimStart().StartsWith("---") &&
            !line.TrimStart().StartsWith("Aucune exigence", StringComparison.Ordinal) &&
            !line.TrimStart().StartsWith("À confirmer", StringComparison.Ordinal));

    /// <summary>Joins independently authored business notes only when supplied.</summary>
    /// <param name="main">Mandatory content.</param>
    /// <param name="extra">Optional notes.</param>
    /// <returns>Markdown without duplicated master prompt wrapper.</returns>
    private static string CombineSections(string main, string extra) =>
        string.IsNullOrWhiteSpace(extra) ? main :
            main + "\n\n### Notes et décisions liées\n\n" + extra;

    /// <summary>Replaces an optional slot with an explicit non-fabricated absence.</summary>
    /// <param name="body">Existing optional evidence.</param>
    /// <param name="fallback">Visible absence statement.</param>
    /// <returns>Source content or a plain absence notice.</returns>
    private static string NotProvidedIfEmpty(
        string body, string fallback = "Non fourni.") =>
        string.IsNullOrWhiteSpace(body) ? fallback : body;

    /// <summary>Loads the original approved prompt without altering its analyst instructions.</summary>
    /// <param name="key">Master prompt identity.</param>
    /// <returns>Full embedded Markdown template.</returns>
    private static string LoadMasterTemplate(string key)
    {
        string fileName = key switch
        {
            "sfd-master-01" => "01-cartographie.md",
            "sfd-master-02" => "02-detail-activite.md",
            "sfd-master-03" => "03-assemblage-fonctionnalites.md",
            "sfd-master-04" => "04-exigences-metier.md",
            "sfd-master-05" => "05-specifications-transversales.md",
            _ => throw new ArgumentException("Prompt maître SFD inconnu.", nameof(key))
        };
        string resourceName = "Nodalis.Infrastructure.SfdPrompts." + fileName;
        using Stream stream = typeof(SfdMasterPromptService).Assembly
            .GetManifestResourceStream(resourceName) ??
            throw new InvalidDataException("Prompt SFD embarqué absent : " + fileName);
        using StreamReader reader = new(stream, new UTF8Encoding(false, true));
        return reader.ReadToEnd();
    }
}
