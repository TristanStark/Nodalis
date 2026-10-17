using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Meetings;

/// <summary>
/// Builds a copy-ready project update prompt from one meeting's AI summary and its project's presentation.
/// </summary>
public sealed class ProjectMeetingPromptService
{
    private readonly string _workspaceRoot;

    /// <summary>
    /// Initializes a prompt generator restricted to one Nodalis workspace.
    /// </summary>
    /// <param name="workspaceRoot">Root of the current workspace.</param>
    public ProjectMeetingPromptService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>
    /// Reads a project's presentation and a selected meeting's AI summary, then writes a disposable text file outside the workspace.
    /// </summary>
    /// <param name="meetingPath">Absolute path of a Markdown report within the project's Réunions directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The temporary file path and its copy-ready content.</returns>
    public async Task<(string FilePath, string Content)> GenerateAsync(
        string meetingPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meetingPath);

        string fullMeetingPath = Path.GetFullPath(meetingPath);
        EnsureWorkspacePath(fullMeetingPath);

        if (!string.Equals(Path.GetExtension(fullMeetingPath), ".md", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullMeetingPath))
        {
            throw new FileNotFoundException(
                "Sélectionnez un compte-rendu de réunion Markdown existant.",
                fullMeetingPath);
        }

        string projectDirectory = ResolveProjectDirectory(fullMeetingPath);
        string presentationPath = Path.Combine(projectDirectory, "Présentation.md");
        if (!File.Exists(presentationPath))
        {
            throw new FileNotFoundException(
                "Le fichier Présentation.md du projet est introuvable.",
                presentationPath);
        }

        string meetingContent = await File.ReadAllTextAsync(fullMeetingPath, cancellationToken);
        string presentationContent = await File.ReadAllTextAsync(presentationPath, cancellationToken);
        string summary = ExtractSection(meetingContent, "Résumé IA");
        string presentation = ExtractSection(presentationContent, "Présentation");

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidDataException(
                "La section ## Résumé IA du compte-rendu est introuvable ou vide.");
        }

        if (string.IsNullOrWhiteSpace(presentation))
        {
            throw new InvalidDataException(
                "La section ## Présentation du projet est introuvable ou vide.");
        }

        DateOnly meetingDate = ResolveMeetingDate(meetingContent, fullMeetingPath);
        string prompt = BuildPrompt(presentation, summary, meetingDate);
        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            "Nodalis-Prompt-Projet-" + Guid.NewGuid().ToString("N") + ".txt");

        await File.WriteAllTextAsync(
            temporaryPath,
            prompt,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        return (temporaryPath, prompt);
    }

    /// <summary>
    /// Resolves the project immediately owning the selected meeting directory, without crossing workspace boundaries.
    /// </summary>
    /// <param name="meetingPath">Validated meeting document path.</param>
    /// <returns>The owning project directory.</returns>
    private string ResolveProjectDirectory(string meetingPath)
    {
        DirectoryInfo? cursor = Directory.GetParent(meetingPath);

        while (cursor is not null && IsWithinWorkspace(cursor.FullName))
        {
            if (string.Equals(
                    cursor.Name,
                    WorkspaceMeetingService.MeetingsDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                DirectoryInfo? project = cursor.Parent;
                if (project is not null &&
                    IsWithinWorkspace(project.FullName) &&
                    File.Exists(Path.Combine(project.FullName, WorkspaceLayout.ProjectManifestFileName)))
                {
                    return project.FullName;
                }
            }

            cursor = cursor.Parent;
        }

        throw new InvalidOperationException(
            "Le compte-rendu doit se trouver dans le dossier Réunions d'un projet Nodalis.");
    }

    /// <summary>
    /// Extracts the Markdown body of a named level-two section, preserving nested headings and stopping before the next peer section.
    /// </summary>
    /// <param name="markdown">Markdown source text.</param>
    /// <param name="headingName">Expected level-two heading.</param>
    /// <returns>Section body, or an empty string when absent.</returns>
    private static string ExtractSection(string markdown, string headingName)
    {
        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        StringBuilder body = new StringBuilder();
        bool inside = false;
        bool fenced = false;
        char fenceCharacter = '\0';
        int fenceLength = 0;

        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            Match fence = Regex.Match(trimmed, @"^(?<marker>`{3,}|~{3,})");
            if (fence.Success)
            {
                string marker = fence.Groups["marker"].Value;
                if (!fenced)
                {
                    fenced = true;
                    fenceCharacter = marker[0];
                    fenceLength = marker.Length;
                }
                else if (marker[0] == fenceCharacter && marker.Length >= fenceLength)
                {
                    fenced = false;
                }
            }

            if (!fenced && !fence.Success)
            {
                Match heading = Regex.Match(line, @"^\s{0,3}(?<hashes>#{1,6})[ \t]+(?<title>.*?)[ \t]*#*[ \t]*$");
                if (heading.Success)
                {
                    int level = heading.Groups["hashes"].Length;
                    string title = heading.Groups["title"].Value.Trim().TrimEnd('#').TrimEnd();
                    if (inside && level <= 2)
                    {
                        break;
                    }

                    if (!inside && level == 2 &&
                        string.Equals(title, headingName, StringComparison.OrdinalIgnoreCase))
                    {
                        inside = true;
                        continue;
                    }
                }
            }

            if (inside)
            {
                body.AppendLine(line);
            }
        }

        return body.ToString().Trim();
    }

    /// <summary>
    /// Obtains the source meeting date from its Markdown metadata or its ISO-prefixed filename.
    /// </summary>
    /// <param name="meetingContent">Markdown meeting document.</param>
    /// <param name="meetingPath">Source file path.</param>
    /// <returns>The actual date stated by the meeting, without silently substituting today's date.</returns>
    private static DateOnly ResolveMeetingDate(string meetingContent, string meetingPath)
    {
        Match metadata = Regex.Match(
            meetingContent,
            @"^\s*\*\*Date\s*:\*\*\s*(?<date>\d{4}-\d{2}-\d{2})\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Match fileName = Regex.Match(
            Path.GetFileNameWithoutExtension(meetingPath),
            @"^(?<date>\d{4}-\d{2}-\d{2})\b",
            RegexOptions.CultureInvariant);

        foreach (Match match in new[] { metadata, fileName })
        {
            if (match.Success &&
                DateOnly.TryParseExact(
                    match.Groups["date"].Value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly date))
            {
                return date;
            }
        }

        throw new InvalidDataException(
            "Date de réunion introuvable : renseignez **Date :** AAAA-MM-JJ ou préfixez le fichier par AAAA-MM-JJ.");
    }

    /// <summary>
    /// Formats the copy-ready user prompt exactly from the selected source sections.
    /// </summary>
    /// <param name="presentation">Project presentation section body.</param>
    /// <param name="summary">Meeting AI summary section body.</param>
    /// <param name="meetingDate">Meeting date.</param>
    /// <returns>Plain text intended for copying into an external assistant.</returns>
    private static string BuildPrompt(string presentation, string summary, DateOnly meetingDate)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine("Examine le contexte du projet et le compte rendu de la réunion pour déterminer ce qu'il faut mettre à jour dans la présentation du projet ainsi que les décisions actées et les tâches à effectuer.");
        builder.AppendLine();
        builder.AppendLine("CONTEXTE DU PROJET:");
        builder.AppendLine("\"\"\"");
        builder.AppendLine(presentation);
        builder.AppendLine("\"\"\"");
        builder.AppendLine();
        builder.AppendLine("RESUME DE LA REUNION DU " + meetingDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR")) + ":");
        builder.AppendLine("\"\"\"");
        builder.AppendLine(summary);
        builder.AppendLine("\"\"\"");
        builder.AppendLine("----");
        return builder.ToString();
    }

    /// <summary>
    /// Rejects documents outside the current workspace.
    /// </summary>
    /// <param name="path">Candidate absolute file path.</param>
    private void EnsureWorkspacePath(string path)
    {
        if (!IsWithinWorkspace(path))
        {
            throw new InvalidDataException("Le fichier sélectionné se trouve hors du workspace.");
        }
    }

    /// <summary>
    /// Checks whether a full path is the workspace itself or one of its descendants.
    /// </summary>
    /// <param name="path">Candidate path.</param>
    /// <returns>True when the path belongs to the workspace.</returns>
    private bool IsWithinWorkspace(string path)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, path);
        return !Path.IsPathRooted(relative) &&
               !string.Equals(relative, "..", StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
