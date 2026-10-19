using System.Security.Cryptography;
using System.Text;

namespace Nodalis.Core.Importing;

/// <summary>
/// Computes an ordered, unambiguous fingerprint of all planned workspace changes.
/// A fingerprint is a drift guard, not an authorization token or transaction lock.
/// </summary>
public static class DocxImportPlanFingerprint
{
    /// <summary>Hashes the exact ordered file plan, metadata, warnings and projected content.</summary>
    /// <param name="plan">Completed dry-run without its own fingerprint.</param>
    /// <returns>Lowercase SHA256 text of the plan snapshot.</returns>
    public static string Compute(DocxImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        StringBuilder buffer = new StringBuilder();
        Append(buffer, plan.CreatesProject ? "NOUVEAU" : "EXISTANT");
        Append(buffer, plan.TargetProjectDisplayName);
        Append(buffer, plan.TargetProjectRelativePath);
        Append(buffer, plan.ProjectedContentSha256);
        foreach (DocxImportPlannedChange change in plan.Changes)
        {
            Append(buffer, change.Action);
            Append(buffer, change.RelativePath);
            Append(buffer, change.Description);
        }

        Append(buffer, "#WARNINGS");
        foreach (string warning in plan.Warnings)
        {
            Append(buffer, warning);
        }

        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(buffer.ToString()))).ToLowerInvariant();
    }

    /// <summary>Encodes each string with a length prefix to disambiguate concatenations.</summary>
    /// <param name="buffer">Fingerprint input builder.</param>
    /// <param name="text">One exact field.</param>
    private static void Append(StringBuilder buffer, string text)
    {
        buffer.Append(text.Length).Append(':').Append(text);
    }
}
