using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Importing;

public sealed class WorkspaceDocxImportService
{
    private readonly string _workspaceRoot;
    private readonly DocxParser _parser = new();

    public WorkspaceDocxImportService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task<DocxImportResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var staged = await StageAsync(
            sourcePath,
            cancellationToken);

        try
        {
            var sourceCopyPath = await CommitStagedCopyAsync(
                staged,
                cancellationToken);

            return new DocxImportResult
            {
                SourceCopyPath = sourceCopyPath,
                Document = staged.Document
            };
        }
        catch
        {
            DiscardStagedCopy(
                staged);
            throw;
        }
    }

    public async Task<DocxStagedImport> StageAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException(
                "Le fichier DOCX source est introuvable.",
                fullSourcePath);
        }

        if (!string.Equals(
                Path.GetExtension(fullSourcePath),
                ".docx",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Seuls les fichiers .docx sont acceptés par l'import Word.");
        }

        var stagingDirectory = GetStagingDirectory();

        Directory.CreateDirectory(
            stagingDirectory);

        var stagedPath = Path.Combine(
            stagingDirectory,
            $"{Guid.NewGuid():N}-{WindowsPathRules.SanitizeSegment(Path.GetFileNameWithoutExtension(fullSourcePath))}.docx");

        try
        {
            await CopyAsync(
                fullSourcePath,
                stagedPath,
                cancellationToken);

            var document = await _parser.ParseAsync(
                stagedPath,
                cancellationToken);

            return new DocxStagedImport
            {
                OriginalSourcePath = fullSourcePath,
                StagedCopyPath = stagedPath,
                Document = document
            };
        }
        catch
        {
            TryDelete(
                stagedPath);
            throw;
        }
    }

    public Task<string> CommitStagedCopyAsync(
        DocxStagedImport staged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staged);
        cancellationToken.ThrowIfCancellationRequested();

        var stagedPath = Path.GetFullPath(
            staged.StagedCopyPath);

        EnsureIsStagingPath(
            stagedPath);

        if (!File.Exists(stagedPath))
        {
            throw new FileNotFoundException(
                "La copie temporaire DOCX est introuvable.",
                stagedPath);
        }

        var sourcesDirectory = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportSourcesDirectoryName);

        Directory.CreateDirectory(
            sourcesDirectory);

        var destination = WindowsPathRules.GetUniqueFilePath(
            sourcesDirectory,
            Path.GetFileName(staged.OriginalSourcePath));

        File.Move(
            stagedPath,
            destination);

        return Task.FromResult(
            destination);
    }

    public void DiscardStagedCopy(
        DocxStagedImport staged)
    {
        ArgumentNullException.ThrowIfNull(staged);

        var stagedPath = Path.GetFullPath(
            staged.StagedCopyPath);

        EnsureIsStagingPath(
            stagedPath);

        TryDelete(
            stagedPath);
    }

    private string GetStagingDirectory() =>
        Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportStagingDirectoryName);

    private void EnsureIsStagingPath(string path)
    {
        var root = Path.GetFullPath(
                GetStagingDirectory())
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        if (!path.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "La copie DOCX indiquée ne se trouve pas dans la zone temporaire d'import.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup is best-effort. Import operations keep their original error.
        }
    }

    private static async Task CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await source.CopyToAsync(
            destination,
            cancellationToken);

        await destination.FlushAsync(
            cancellationToken);
    }
}
