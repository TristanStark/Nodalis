using System.IO;
using System.Text;
using System.Windows;
using Nodalis.Core.Sfd;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit per-field choices for one reviewed 3-way SFD update. This window
/// is only an approval UI; it does not touch the filesystem or external AI.
/// </summary>
public partial class SfdThreeWayApprovalDialog : Window
{
    /// <summary>Creates a conflict-aware human field approval dialog.</summary>
    /// <param name="item">Existing canonical SFD row with full source values.</param>
    public SfdThreeWayApprovalDialog(SfdImportReviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ThreeWay is not { HasBaseline: true, HasConflict: false } preview)
        {
            throw new InvalidDataException(
                "Comparaison 3-way non disponible ou conflictuelle.");
        }

        InitializeComponent();
        StringBuilder details = new();
        details.Append("Objet : ").AppendLine(item.Kind + " / " + item.Title)
            .Append("GUID : ").AppendLine(preview.TargetId.ToString("D"))
            .Append("SHA base : ").AppendLine(preview.BaselineSha256)
            .Append("SHA actuel : ").AppendLine(preview.CurrentSha256)
            .AppendLine();
        foreach (SfdThreeWayField field in preview.Fields)
        {
            details.Append("CHAMP : ").Append(field.Name).Append(" | ")
                .AppendLine(field.Status.ToString())
                .AppendLine("--- Base figée ---").AppendLine(field.BaseValue)
                .AppendLine("--- Fichier actuel ---").AppendLine(field.CurrentValue)
                .AppendLine("--- Proposition IA ---").AppendLine(field.ProposedValue)
                .AppendLine();
        }

        DifferencesBox.Text = details.ToString();
        AcceptTitleCheck.IsEnabled = preview.Fields.Any(field =>
            field.Name == "title" &&
            field.Status == SfdThreeWayFieldStatus.ProposedChange);
        AcceptDescriptionCheck.IsEnabled = preview.Fields.Any(field =>
            field.Name == "description" &&
            field.Status == SfdThreeWayFieldStatus.ProposedChange);
        UpdateButtons();
    }

    /// <summary>Whether the user explicitly chose to apply the title.</summary>
    public bool AcceptTitle => AcceptTitleCheck.IsChecked == true;

    /// <summary>Whether the user explicitly chose to apply the AI draft description.</summary>
    public bool AcceptDescription => AcceptDescriptionCheck.IsChecked == true;

    /// <summary>Enables confirmation only after a real field selection.</summary>
    /// <param name="sender">Checkbox.</param>
    /// <param name="e">Selection event.</param>
    private void Choices_Changed(object sender, RoutedEventArgs e) =>
        UpdateButtons();

    /// <summary>Captures the user's explicit choice without writing a file.</summary>
    /// <param name="sender">Confirmation button.</param>
    /// <param name="e">Click event.</param>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!AcceptTitle && !AcceptDescription)
        {
            return;
        }

        DialogResult = true;
    }

    /// <summary>Disables an accidental empty or ineligible approval.</summary>
    private void UpdateButtons()
    {
        if (ConfirmButton is not null)
        {
            ConfirmButton.IsEnabled = AcceptTitle || AcceptDescription;
        }
    }
}
