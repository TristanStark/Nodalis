using Nodalis.Core.Risks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Pure RIS mode-A transformation. Changes only three verified canonical
/// declarations; the risk GUID, reviews, treatments, links, body and all
/// human-authored history are preserved byte-for-byte in their original lines.
/// No file is touched and the target is never silently allocated.
/// </summary>
public static class ProjectRiskMoveDraft
{
    /// <summary>Prepares a deterministic historical RIS relocation in memory.</summary>
    /// <param name="original">Exact original UTF-8 Markdown, no BOM.</param>
    /// <param name="targetProjectId">Different active receiving project.</param>
    /// <param name="targetReference">Available RIS-xxxx in recipient.</param>
    /// <returns>New owner/reference with unchanged GUID and history.</returns>
    public static string Prepare(string original, Guid targetProjectId,
        string targetReference)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Length > 1024 * 1024 ||
            original.StartsWith('\uFEFF') ||
            targetProjectId == Guid.Empty ||
            !ProjectRiskMarkdownCodec.IsReference(targetReference))
        {
            throw new InvalidDataException(
                "Source RIS A, projet ou référence de destination invalide.");
        }

        ProjectRiskCard source = ProjectRiskMarkdownCodec.Parse(original);
        if (source.ProjectId == targetProjectId)
        {
            throw new InvalidDataException(
                "Déplacement RIS A vers le même projet interdit.");
        }

        // Split/join retains the original LF/CRLF sequence and final newline,
        // including every byte of the human-owned historical body.
        string[] lines = original.Split('\n');
        if (lines.Length < 5 ||
            Clean(lines[0]) != "---")
        {
            throw new InvalidDataException("En-tête RIS A non canonique.");
        }

        int closing = -1;
        for (int index = 1; index < Math.Min(lines.Length, 32); index++)
        {
            if (Clean(lines[index]) == "---")
            {
                closing = index;
                break;
            }
        }

        if (closing < 0 || closing + 1 >= lines.Length)
        {
            throw new InvalidDataException(
                "Front-matter RIS A incomplet : historique conservé.");
        }

        string projectLine = "project-id: " + source.ProjectId.ToString("D");
        string referenceLine = "reference: " + source.Reference;
        int ownerIndex = -1;
        int referenceIndex = -1;
        for (int index = 1; index < closing; index++)
        {
            string line = Clean(lines[index]);
            if (line.StartsWith("project-id:", StringComparison.Ordinal))
            {
                if (ownerIndex >= 0 || line != projectLine)
                {
                    throw new InvalidDataException(
                        "Propriétaire RIS A ambigu dans les métadonnées.");
                }

                ownerIndex = index;
            }

            if (line.StartsWith("reference:", StringComparison.Ordinal))
            {
                if (referenceIndex >= 0 || line != referenceLine)
                {
                    throw new InvalidDataException(
                        "Référence RIS A ambiguë dans les métadonnées.");
                }

                referenceIndex = index;
            }
        }

        string title = "# " + source.Reference + " — " + source.Title;
        if (ownerIndex < 0 || referenceIndex < 0 ||
            Clean(lines[closing + 1]) != title)
        {
            throw new InvalidDataException(
                "En-tête ou titre RIS modifié : une revue humaine est requise.");
        }

        lines[ownerIndex] = WithOriginalLineEnding(lines[ownerIndex],
            "project-id: " + targetProjectId.ToString("D"));
        lines[referenceIndex] = WithOriginalLineEnding(lines[referenceIndex],
            "reference: " + targetReference);
        lines[closing + 1] = WithOriginalLineEnding(lines[closing + 1],
            "# " + targetReference + " — " + source.Title);
        string destination = string.Join("\n", lines);

        ProjectRiskCard moved = ProjectRiskMarkdownCodec.Parse(destination);
        if (moved.Id != source.Id ||
            moved.ProjectId != targetProjectId ||
            moved.Reference != targetReference ||
            moved.Title != source.Title ||
            moved.CreatedUtc != source.CreatedUtc ||
            moved.Qualification != source.Qualification ||
            moved.Lifecycle != source.Lifecycle ||
            moved.Initial != source.Initial ||
            moved.Residual != source.Residual ||
            moved.TreatmentHeadId != source.TreatmentHeadId)
        {
            throw new InvalidDataException(
                "Déplacement RIS A : une propriété métier a changé.");
        }

        return destination;
    }

    /// <summary>Excludes only the original CR of a CRLF line.</summary>
    /// <param name="line">Original line including optional CR.</param>
    /// <returns>Original text without its line-ending CR.</returns>
    private static string Clean(string line) =>
        line.EndsWith('\r') ? line[..^1] : line;

    /// <summary>Preserves line ending when substituting a declaration.</summary>
    /// <param name="oldLine">Original line including optional CR.</param>
    /// <param name="replacement">Verified new declaration.</param>
    /// <returns>Replaced line with original CR if present.</returns>
    private static string WithOriginalLineEnding(
        string oldLine, string replacement) =>
        oldLine.EndsWith('\r') ? replacement + "\r" : replacement;
}
