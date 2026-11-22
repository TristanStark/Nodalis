using System.Text;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Fills exactly the input section of the user's original five SFD prompts.
/// The analyst instructions are never wrapped, moved, repeated or reformatted.
/// </summary>
public static class SfdMasterPromptTemplateAssembler
{
    /// <summary>Returns the expected input slots in their original order.</summary>
    /// <param name="promptKey">One of the five SFD master prompt identities.</param>
    /// <returns>Strict, immutable placeholder sequence.</returns>
    public static IReadOnlyList<string> RequiredPlaceholders(string promptKey) =>
        promptKey switch
        {
            "sfd-master-01" =>
            [
                "[COLLER LES NOTES, MÊME DÉSORDONNÉES]",
                "[COLLER SI DISPONIBLE]",
                "[COLLER SI DISPONIBLE]"
            ],
            "sfd-master-02" =>
            [
                "[COLLER LE PROJET]",
                "[COLLER LE PM CIBLE]",
                "[PM-XXX-ACT-YYY — NOM]",
                "[COLLER SI DISPONIBLE]",
                "[COLLER SI DISPONIBLE]"
            ],
            "sfd-master-03" =>
            [
                "[COLLER LE PROJET]",
                "[COLLER TOUS LES PM ET ACT, IDS INCLUS]",
                "[COLLER DES SYNTHÈSES COURTES SI DISPONIBLES — PAS OBLIGATOIRE]",
                "[COLLER SI DISPONIBLE]",
                "[COLLER SI DISPONIBLE]"
            ],
            "sfd-master-04" =>
            [
                "[COLLER LE PROJET]",
                "[COLLER LA FCT]",
                "[COLLER LA FICHE ACT]",
                "[COLLER LE REGISTRE CONNU, SINON « AUCUN »]",
                "[COLLER OU « NON FOURNI »]",
                "[COLLER SI DISPONIBLE]"
            ],
            "sfd-master-05" =>
            [
                "[COLLER LE PROJET — VERSION SYNTHÉTIQUE SI LONGUE, MAIS AVEC SES CONTRAINTES UTILES]",
                "[COLLER LA FCT]",
                "[COLLER LE DOSSIER EXIGENCES DE LA FCT]",
                "[COLLER LA LISTE DES RM CONCERNÉES — UNE DÉFINITION PAR ID]",
                "[COLLER OU « AUCUN »]",
                "[COLLER LA MATRICE / LE LOT / LES LIVRABLES SELON LE MODE]"
            ],
            _ => throw new ArgumentException("Prompt maître SFD inconnu.", nameof(promptKey))
        };

    /// <summary>
    /// Fills each placeholder once, in position, without touching the instructions
    /// before the Entrées à coller heading. Refuses mismatched master templates.
    /// </summary>
    /// <param name="template">Exact user-authored embedded master prompt.</param>
    /// <param name="promptKey">Template identity.</param>
    /// <param name="values">Complete content for each ordered slot.</param>
    /// <returns>One copy-ready master prompt with populated inputs at the end.</returns>
    public static string Assemble(
        string template, string promptKey, IReadOnlyList<string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentNullException.ThrowIfNull(values);
        IReadOnlyList<string> markers = RequiredPlaceholders(promptKey);
        if (markers.Count != values.Count ||
            values.Any(value => string.IsNullOrWhiteSpace(value)))
        {
            throw new InvalidDataException(
                "Le nombre ou le contenu des entrées du prompt SFD est invalide.");
        }

        int start = template.LastIndexOf("## Entrées à coller",
            StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidDataException(
                "Le prompt maître n'a pas sa section d'entrées canonique.");
        }

        string inputs = template[start..];
        StringBuilder assembled = new(template.Length +
            values.Sum(value => value.Length));
        assembled.Append(template.AsSpan(0, start));
        int position = 0;
        for (int i = 0; i < markers.Count; i++)
        {
            int index = inputs.IndexOf(markers[i], position,
                StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidDataException(
                    "Le placeholder attendu a changé dans le prompt maître : " +
                    markers[i]);
            }

            assembled.Append(inputs.AsSpan(position, index - position))
                .Append(values[i]);
            position = index + markers[i].Length;
        }

        string remaining = inputs[position..];
        if (remaining.Contains("[COLLER", StringComparison.Ordinal) ||
            remaining.Contains("[PM-XXX-ACT-YYY", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Certaines entrées du prompt maître ne sont pas renseignées.");
        }

        assembled.Append(remaining);
        return assembled.ToString();
    }
}
