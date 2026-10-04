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

        var sourcesDirectory = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportSourcesDirectoryName);

        Directory.CreateDirectory(
            sourcesDirectory);

        var copyPath = WindowsPathRules.GetUniqueFilePath(
            sourcesDirectory,
            Path.GetFileName(fullSourcePath));

        try
        {
            await CopyAsync(
                fullSourcePath,
                copyPath,
                cancellationToken);

            var document = await _parser.ParseAsync(
                copyPath,
                cancellationToken);

            return new DocxImportResult
            {
                SourceCopyPath = copyPath,
                Document = document
            };
        }
        catch
        {
            try
            {
                if (File.Exists(copyPath))
                {
                    File.Delete(copyPath);
                }
            }
            catch
            {
                // Preserve the original import error. A stale copy is harmless
                // and remains ordinary local workspace data.
            }

            throw;
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
