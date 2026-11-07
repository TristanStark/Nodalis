using System.Windows;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only EXI/CA/test/REC/incident relationship worklist at the moment of
/// an explicit project handover. It never applies any relation decision.
/// </summary>
public partial class ProjectRequirementHandoverRelationsDialog : Window
{
    private readonly ProjectRequirementRelationReview _review;

    /// <summary>Displays immutable inspected relations and action suggestions.</summary>
    /// <param name="review">SHA-bound, independently calculated review.</param>
    public ProjectRequirementHandoverRelationsDialog(
        ProjectRequirementRelationReview review)
    {
        _review = review ?? throw new ArgumentNullException(nameof(review));
        InitializeComponent();
        SummaryText.Text = _review.Items.Count +
            " élément(s) à examiner · " + _review.Diagnostics.Count +
            " diagnostic(s) · " + (_review.IsComplete
                ? "inventaire établi sur les sources disponibles"
                : "inventaire incomplet : nouvelles vérifications nécessaires");
        ChecklistText.Text = _review.FormatChecklist();
        ChecklistText.CaretIndex = 0;
        ChecklistText.ScrollToHome();
    }

    /// <summary>Copies actionable text without writing any project source.</summary>
    /// <param name="sender">Copy button.</param>
    /// <param name="e">Routed event.</param>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_review.FormatChecklist());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this,
                "Le presse-papiers est occupé. Sélectionnez le texte " +
                "pour le copier manuellement.",
                "Copie indisponible", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Acknowledges reading the report, NOT requalification or evidence.
    /// Existing transaction guards still apply independently.
    /// </summary>
    /// <param name="sender">Continue button.</param>
    /// <param name="e">Routed event.</param>
    private void Continue_Click(object sender, RoutedEventArgs e) =>
        DialogResult = true;
}
