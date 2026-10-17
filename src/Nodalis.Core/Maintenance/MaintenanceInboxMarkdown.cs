using System.Text;
using Nodalis.Core.Inbox;

namespace Nodalis.Core.Maintenance;

/// <summary>Supported Inbox-to-maintenance workflows.</summary>
public enum MaintenanceInboxMode
{
    Incident,
    Intervention,
    IncidentAndIntervention
}

/// <summary>Explicit conversion choices, retained for safe interruption recovery.</summary>
public sealed record MaintenanceInboxRequest
{
    public required Guid EntryId { get; init; }
    public required string ProjectRelativePath { get; init; }
    public required MaintenanceInboxMode Mode { get; init; }
    public required string Summary { get; init; }
    public string? ExistingIncidentId { get; init; }
}

/// <summary>Outcome of a confirmed maintenance conversion.</summary>
public sealed record MaintenanceInboxResult(
    string TargetRelativePath, string RecordId, bool AlreadyConverted);

/// <summary>Pure generation of structured incident/intervention Markdown from an Inbox capture.</summary>
public static class MaintenanceInboxMarkdown
{
    /// <summary>Canonical origin marker for idempotent retries.</summary>
    /// <param name="id">Inbox capture identity.</param>
    /// <returns>Safe Markdown comment.</returns>
    public static string Marker(Guid id) =>
        "<!-- nodalis-inbox-maintenance:" + id.ToString("N") + " -->";

    /// <summary>Creates stable record IDs for retry even after interruption.</summary>
    /// <param name="id">Inbox ID.</param>
    /// <param name="kind">Target record type.</param>
    /// <returns>INC or INT GUID derived from the capture and kind.</returns>
    public static string StableId(Guid id, MaintenanceRecordKind kind)
    {
        byte[] bytes = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes("nodalis-inbox-v1:" + id.ToString("N") + ":" + kind));
        string prefix = kind == MaintenanceRecordKind.Incident ? "INC-" : "INT-";
        return prefix + Convert.ToHexString(bytes.AsSpan(0, 16)).ToLowerInvariant();
    }

    /// <summary>
    /// Appends one or two valid structured records and quoted capture content.
    /// Existing Markdown is retained; no I/O occurs.
    /// </summary>
    /// <param name="markdown">Latest target Markdown.</param>
    /// <param name="entry">Source capture.</param>
    /// <param name="request">User-confirmed conversion configuration.</param>
    /// <param name="incident">Optional existing incident for intervention mode.</param>
    /// <returns>Document with provenance and source text.</returns>
    public static string Append(
        string markdown, InboxEntry entry, MaintenanceInboxRequest request,
        MaintenanceRecord? incident = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(request);
        if (entry.Id != request.EntryId || !Enum.IsDefined(request.Mode) ||
            string.IsNullOrWhiteSpace(request.Summary))
        {
            throw new ArgumentException("Capture, type ou résumé de maintenance invalide.");
        }

        string marker = Marker(entry.Id);
        if (markdown.Contains(marker, StringComparison.Ordinal))
        {
            return markdown;
        }

        string headline = request.Summary.Trim();
        if (headline.Contains('\n') || headline.Contains('\r') ||
            headline.Length > 220)
        {
            throw new ArgumentException("Résumé de maintenance sur une ligne (220 caractères max).");
        }

        DateOnly date = DateOnly.FromDateTime(entry.CreatedUtc.ToLocalTime().DateTime);
        Dictionary<string, string> details = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Provenance Inbox"] = entry.Id.ToString("N")
        };
        if (!string.IsNullOrWhiteSpace(entry.Origin))
        {
            details["Origine"] = CleanLine(entry.Origin);
        }

        bool newIncident = request.Mode is MaintenanceInboxMode.Incident or
            MaintenanceInboxMode.IncidentAndIntervention;
        bool intervention = request.Mode is MaintenanceInboxMode.Intervention or
            MaintenanceInboxMode.IncidentAndIntervention;
        string? incidentId = null;
        if (newIncident)
        {
            MaintenanceRecord created = new MaintenanceRecord
            {
                Kind = MaintenanceRecordKind.Incident,
                Id = StableId(entry.Id, MaintenanceRecordKind.Incident),
                Date = date,
                Summary = headline,
                Details = details
            };
            markdown = MaintenanceMarkdownCodec.Append(markdown, created);
            incidentId = created.Id;
        }
        else if (intervention && incident is not null)
        {
            if (incident.Kind != MaintenanceRecordKind.Incident ||
                !string.Equals(incident.Id, request.ExistingIncidentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("L'incident lié ne correspond pas à la sélection.");
            }

            incidentId = incident.Id;
        }
        else if (intervention && request.ExistingIncidentId is not null)
        {
            throw new InvalidDataException("Incident existant introuvable.");
        }

        string recordId;
        if (intervention)
        {
            MaintenanceRecord created = new MaintenanceRecord
            {
                Kind = MaintenanceRecordKind.Intervention,
                Id = StableId(entry.Id, MaintenanceRecordKind.Intervention),
                Date = date,
                Summary = headline,
                IncidentId = incidentId,
                Details = details
            };
            markdown = MaintenanceMarkdownCodec.Append(markdown, created);
            recordId = created.Id;
        }
        else
        {
            recordId = incidentId!;
        }

        string nl = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        int field = markdown.IndexOf("- **Identifiant :** " + recordId,
            StringComparison.OrdinalIgnoreCase);
        if (field < 0)
        {
            throw new InvalidDataException("Bloc Markdown nouvellement ajouté introuvable.");
        }

        int blockEnd = markdown.IndexOf(nl + nl, field, StringComparison.Ordinal);
        if (blockEnd < 0)
        {
            throw new InvalidDataException("Terminaison du bloc d'historique introuvable.");
        }

        StringBuilder appendix = new StringBuilder();
        appendix.Append(nl).Append("#### Capture Inbox").Append(nl).Append(nl);
        appendix.Append(marker).Append(nl);
        foreach (string line in entry.ContentMarkdown.Replace("\r\n", "\n")
                     .Replace('\r', '\n').Split('\n'))
        {
            appendix.Append("> ").Append(line).Append(nl);
        }

        if (entry.Attachments.Count > 0)
        {
            appendix.Append(nl).Append("Pièces jointes conservées :").Append(nl);
            foreach (InboxAttachment attachment in entry.Attachments)
            {
                string name = Path.GetFileName(attachment.RelativePath);
                if (string.IsNullOrWhiteSpace(name) ||
                    name is "." or ".." || name.Contains('\\') || name.Contains('/') ||
                    name.Contains(':'))
                {
                    throw new InvalidDataException("Pièce jointe non sûre : " + attachment.RelativePath);
                }

                string relative = "Attachments/Inbox-" + entry.Id.ToString("N") +
                    "/" + Uri.EscapeDataString(name);
                appendix.Append("- [")
                    .Append(attachment.OriginalFileName.Replace("[", "\\[")
                        .Replace("]", "\\]"))
                    .Append("](").Append(relative).Append(')').Append(nl);
            }
        }

        return markdown.Insert(blockEnd + nl.Length, appendix.ToString());
    }

    /// <summary>Creates one safe single-line descriptive property.</summary>
    /// <param name="value">Original human provenance.</param>
    /// <returns>Flattened, truncated display value.</returns>
    private static string CleanLine(string value)
    {
        string flat = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length > 240 ? flat[..240] : flat;
    }
}
