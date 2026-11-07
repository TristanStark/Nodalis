using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Core.Tasks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Two exact replacement Markdown contents for one task handover.</summary>
/// <param name="Source">New original project task document.</param>
/// <param name="Target">New recipient project task document.</param>
public sealed record ProjectHandoverTaskRewrites(string Source, string Target);

/// <summary>
/// Pure, deliberately narrow task-only transform. No nested subtasks,
/// milestones, linked tasks or noncanonical checkbox sources are transferred.
/// </summary>
public static class ProjectHandoverTaskCodec
{
    /// <summary>
    /// Keeps the original stable task identity for A; creates a new task
    /// identity and provenance on both sides for B. The original source line
    /// is never silently erased without a readable trace.
    /// </summary>
    /// <param name="source">Exact existing UTF-8 task Markdown.</param>
    /// <param name="target">Exact existing recipient task Markdown.</param>
    /// <param name="taskId">Current single-task ID.</param>
    /// <param name="successorId">Original GUID (A) or proposed new GUID (B).</param>
    /// <param name="intentId">Human-approved plan event.</param>
    /// <param name="sourceProjectId">Original owner GUID.</param>
    /// <param name="targetProjectId">Recipient owner GUID.</param>
    /// <param name="method">User-selected A/B mode.</param>
    /// <returns>Two in-memory documents, with no filesystem changes.</returns>
    public static ProjectHandoverTaskRewrites Prepare(
        string source, string target, Guid taskId, Guid successorId,
        Guid intentId, Guid sourceProjectId, Guid targetProjectId,
        ProjectHandoverMethod method)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (taskId == Guid.Empty || successorId == Guid.Empty ||
            intentId == Guid.Empty || sourceProjectId == Guid.Empty ||
            targetProjectId == Guid.Empty ||
            sourceProjectId == targetProjectId || !Enum.IsDefined(method) ||
            (method == ProjectHandoverMethod.MoveOriginal &&
             successorId != taskId) ||
            (method == ProjectHandoverMethod.LinkedSuccessor &&
             successorId == taskId))
        {
            throw new InvalidDataException("Identités de passation incohérentes.");
        }

        string[] lines = source.Split('\n');
        TaskPersistentIdentityScan scan = TaskPersistentIdentityCodec.Inspect(lines);
        if (scan.Issues.Count > 0)
        {
            throw new InvalidDataException(
                "Marqueurs de tâches ambigus dans le document source.");
        }

        TaskPersistentIdentityEntry[] matches = scan.Entries.Where(item =>
            item.Id == taskId).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                "La tâche source doit posséder un GUID unique et inchangé.");
        }

        TaskPersistentIdentityEntry match = matches[0];
        int index = match.LineNumber - 1;
        string raw = lines[index].TrimEnd('\r');
        if (!raw.StartsWith("- [ ] ", StringComparison.Ordinal) ||
            !raw.EndsWith(TaskPersistentIdentityCodec.FormatMarker(taskId),
                StringComparison.Ordinal) ||
            raw.Contains("](", StringComparison.Ordinal) ||
            raw.Contains("[[", StringComparison.Ordinal) ||
            raw.Contains("Jalon :", StringComparison.OrdinalIgnoreCase) ||
            (index + 1 < lines.Length &&
             (lines[index + 1].StartsWith("  ", StringComparison.Ordinal) ||
              lines[index + 1].StartsWith("\t", StringComparison.Ordinal))))
        {
            throw new InvalidDataException(
                "Seules les tâches simples, non imbriquées et non liées, " +
                "sont prises en charge par ce premier transfert.");
        }

        TaskPersistentIdentityScan targetScan =
            TaskPersistentIdentityCodec.Inspect(target.Split('\n'));
        if (targetScan.Issues.Count > 0 ||
            targetScan.Entries.Any(item =>
                item.Id == taskId || item.Id == successorId))
        {
            throw new InvalidDataException(
                "Le destinataire contient une tâche conflictuelle.");
        }

        string currentMarker = TaskPersistentIdentityCodec.FormatMarker(taskId);
        string newMarker = TaskPersistentIdentityCodec.FormatMarker(successorId);
        string transferred = method == ProjectHandoverMethod.MoveOriginal
            ? raw : raw[..^currentMarker.Length] + newMarker;
        string trace = method == ProjectHandoverMethod.MoveOriginal
            ? "> Passation A · tâche " + taskId.ToString("D") +
              " transférée au projet " + targetProjectId.ToString("D") +
              " · intention " + intentId.ToString("D")
            : "> Passation B · tâche source " + taskId.ToString("D") +
              " conservée ; successeur " + successorId.ToString("D") +
              " dans le projet " + targetProjectId.ToString("D") +
              " · intention " + intentId.ToString("D");
        string provenance = "> Provenance : projet " +
            sourceProjectId.ToString("D") + " · tâche " + taskId.ToString("D") +
            " · intention " + intentId.ToString("D");

        string newline = source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string original = method == ProjectHandoverMethod.MoveOriginal
            ? trace : raw + newline + trace;
        lines[index] = original + (lines[index].EndsWith('\r') ? "\r" : "");
        string resultingSource = string.Join("\n", lines);

        string targetNl = target.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        StringBuilder result = new(target);
        if (result.Length > 0 && !target.EndsWith("\n", StringComparison.Ordinal))
        {
            result.Append(targetNl);
        }

        result.Append(transferred).Append(targetNl)
            .Append(provenance).Append(targetNl);
        return new ProjectHandoverTaskRewrites(
            resultingSource, result.ToString());
    }
}
