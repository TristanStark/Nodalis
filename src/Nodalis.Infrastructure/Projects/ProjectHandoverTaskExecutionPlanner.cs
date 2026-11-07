using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Projects;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// On-demand, bounded dry-run for one already signed task handover intention.
/// This is not a generic Markdown-object transfer planner.
/// </summary>
public sealed class ProjectHandoverTaskExecutionPlanner
{
    private const int MaxTaskBytes = 2 * 1024 * 1024;
    private const int MaxReferenceFiles = 6000;
    private const long MaxReferenceBytes = 96 * 1024 * 1024;
    private readonly string _root;
    private readonly ProjectHandoverPreflightService _preflight;
    private readonly ProjectHandoverIntentService _intents;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a planner limited to an existing workspace.</summary>
    /// <param name="workspaceRoot">Single Nodalis workspace.</param>
    public ProjectHandoverTaskExecutionPlanner(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _preflight = new ProjectHandoverPreflightService(_root);
        _intents = new ProjectHandoverIntentService(_root);
        _sources = new ProjectJourneySourceService(_root);
    }

    /// <summary>
    /// Rechecks an existing human intention and verifies both exact top-level
    /// Tâches.md files. An unsupported source, unknown link or source drift
    /// blocks publication without creating files.
    /// </summary>
    /// <param name="sourceProjectDirectory">Selected original owner.</param>
    /// <param name="intentId">Persisted human-approved intention.</param>
    /// <param name="cancellationToken">Explicit read cancellation.</param>
    /// <returns>Two prepared in-memory documents and collision warnings.</returns>
    public async Task<ProjectHandoverTaskExecutionPlan> PrepareAsync(
        string sourceProjectDirectory, Guid intentId,
        CancellationToken cancellationToken = default)
    {
        if (intentId == Guid.Empty)
        {
            throw new InvalidDataException("Intention de passation absente.");
        }

        (string source, string sourceRelative,
            Nodalis.Core.Domain.ProjectManifest owner) =
            await _preflight.ResolveActiveProjectAsync(
                sourceProjectDirectory, cancellationToken);
        ProjectHandoverIntentSnapshot history =
            await _intents.ReadAsync(source, cancellationToken);
        ProjectHandoverIntent intent = history.Ledger.Intents.SingleOrDefault(
            item => item.Id == intentId) ??
            throw new InvalidDataException(
                "Intention non retrouvée dans le projet source.");
        if (intent.SourceProjectId != owner.Id ||
            !string.Equals(intent.SourceRelativePath,
                sourceRelative + "/Tâches.md", StringComparison.OrdinalIgnoreCase) ||
            intent.SourceObjectKind != "Tâches ouvertes")
        {
            throw new InvalidDataException(
                "Cet exécuteur prend seulement en charge les tâches simples " +
                "du fichier Tâches.md du projet source.");
        }

        string targetProject = Path.GetFullPath(Path.Combine(_root,
            intent.TargetProjectRelativePath.Replace('/',
                Path.DirectorySeparatorChar)));
        ProjectHandoverPreview preview = await _preflight.PrepareAsync(
            source, targetProject, intent.SourceObjectId, intent.Method,
            intent.ProposedTargetObjectId, cancellationToken);
        if (!preview.CanRecord ||
            preview.SourceProjectId != intent.SourceProjectId ||
            preview.TargetProjectId != intent.TargetProjectId ||
            preview.SourceObjectId != intent.SourceObjectId ||
            preview.SourceObjectKind != intent.SourceObjectKind ||
            preview.SourceReference != intent.SourceReference ||
            preview.SourceRelativePath != intent.SourceRelativePath ||
            !string.Equals(preview.SourceProjectRelativePath,
                sourceRelative, StringComparison.Ordinal) ||
            preview.TargetProjectRelativePath !=
                intent.TargetProjectRelativePath ||
            preview.SourceFingerprint != intent.SourceFingerprint ||
            preview.TargetFingerprint != intent.TargetFingerprint ||
            preview.ProposedTargetObjectId != intent.ProposedTargetObjectId)
        {
            throw new InvalidDataException(
                "L'intention ne correspond plus aux sources originales et " +
                "au destinataire : créez une nouvelle préparation.");
        }

        string targetRelative = preview.TargetProjectRelativePath +
            "/Tâches.md";
        string sourcePath = _sources.ResolveExistingFile(
            intent.SourceRelativePath);
        string targetPath = _sources.ResolveExistingFile(targetRelative);
        (string sourceText, string sourceHash) =
            await ReadExactTaskDocumentAsync(sourcePath, cancellationToken);
        (string targetText, string targetHash) =
            await ReadExactTaskDocumentAsync(targetPath, cancellationToken);

        ProjectHandoverTaskRewrites proposed = ProjectHandoverTaskCodec.Prepare(
            sourceText, targetText, intent.SourceObjectId,
            intent.ProposedTargetObjectId, intent.Id, intent.SourceProjectId,
            intent.TargetProjectId, intent.Method);
        List<string> warnings = preview.Warnings.ToList();
        if (intent.Method == ProjectHandoverMethod.MoveOriginal)
        {
            IReadOnlyList<string> references = await FindOtherReferencesAsync(
                intent.SourceObjectId, sourcePath, targetPath,
                cancellationToken);
            if (references.Count > 0)
            {
                warnings.Add(references.Count + " référence(s) externes " +
                    "mentionnent ce GUID ; elles nécessitent une revue " +
                    "et empêchent le déplacement automatique : " +
                    string.Join(", ", references.Take(3)));
                return new ProjectHandoverTaskExecutionPlan
                {
                    Intent = intent,
                    SourceDocumentRelativePath = intent.SourceRelativePath,
                    TargetDocumentRelativePath = targetRelative,
                    SourceHash = sourceHash,
                    TargetHash = targetHash,
                    ProposedSource = proposed.Source,
                    ProposedTarget = proposed.Target,
                    Warnings = warnings,
                    CanExecute = false
                };
            }
        }

        return new ProjectHandoverTaskExecutionPlan
        {
            Intent = intent,
            SourceDocumentRelativePath = intent.SourceRelativePath,
            TargetDocumentRelativePath = targetRelative,
            SourceHash = sourceHash,
            TargetHash = targetHash,
            ProposedSource = proposed.Source,
            ProposedTarget = proposed.Target,
            Warnings = warnings,
            CanExecute = true
        };
    }

    /// <summary>
    /// Reads one exact small UTF-8 document. Unsupported BOM and large files
    /// are refused rather than silently changing their original encoding.
    /// </summary>
    /// <param name="path">Verified, existing Markdown source.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Exact text and original byte SHA.</returns>
    internal static async Task<(string Text, string Hash)>
        ReadExactTaskDocumentAsync(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > MaxTaskBytes)
        {
            throw new InvalidDataException(
                "Document de tâches trop volumineux pour ce transfert.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, token);
        string text = new UTF8Encoding(false, true).GetString(bytes);
        if (text.StartsWith('\uFEFF') ||
            !bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(text)))
        {
            throw new InvalidDataException(
                "Encodage UTF-8 non canonique : transfert refusé sans conversion.");
        }

        return (text, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Scans bounded Markdown/JSON only on deliberate A execution. Any
    /// reference outside the selected checkbox files blocks an unsafe move.
    /// No periodic or Worktree-triggered global scan is introduced.
    /// </summary>
    /// <param name="taskId">Existing stable task GUID.</param>
    /// <param name="sourcePath">Exact original task document to exclude.</param>
    /// <param name="targetPath">Exact recipient task document to exclude.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>At most five paths demonstrating potential references.</returns>
    internal async Task<IReadOnlyList<string>> FindOtherReferencesAsync(
        Guid taskId, string sourcePath, string targetPath,
        CancellationToken cancellationToken)
    {
        string applications = Path.Combine(_root,
            Nodalis.Infrastructure.Persistence.WorkspaceLayout.ApplicationsDirectoryName);
        if (!Directory.Exists(applications))
        {
            throw new InvalidDataException(
                "Dossier Applications absent : liens impossibles à vérifier.");
        }

        List<string> references = [];
        Stack<string> pending = new();
        pending.Push(applications);
        int files = 0;
        long bytesRead = 0;
        string canonical = taskId.ToString("D");
        string compact = taskId.ToString("N");
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Lien symbolique dans le périmètre de recherche : " +
                    "déplacement A non autorisé.");
            }

            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Source liée dans le workspace : références " +
                        "non vérifiables pour cette passation.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(path);
                    continue;
                }

                if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string filename = Path.GetFileName(path);
                if (string.Equals(path, sourcePath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, targetPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(filename, "Passations.md",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(filename, "Bilan de clôture.md",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                files++;
                long length = new FileInfo(path).Length;
                bytesRead += length;
                if (files > MaxReferenceFiles ||
                    bytesRead > MaxReferenceBytes ||
                    length > MaxTaskBytes)
                {
                    throw new InvalidDataException(
                        "La recherche des références dépasse le plafond : " +
                        "aucun déplacement A ne sera exécuté.");
                }

                string verified = _sources.ResolveExistingFile(
                    _sources.GetReferenceForExistingFile(path));
                string content = new UTF8Encoding(false, true).GetString(
                    await File.ReadAllBytesAsync(verified, cancellationToken));
                if (content.Contains(canonical,
                        StringComparison.OrdinalIgnoreCase) ||
                    content.Contains(compact,
                        StringComparison.OrdinalIgnoreCase))
                {
                    references.Add(Path.GetRelativePath(_root, verified)
                        .Replace(Path.DirectorySeparatorChar, '/'));
                    if (references.Count >= 5)
                    {
                        return references;
                    }
                }
            }
        }

        return references;
    }
}
