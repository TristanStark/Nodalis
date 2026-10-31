using System.Windows;
using System.Windows.Controls;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit human approval for one fingerprinted, safe-subset migration plan.
/// This dialog cannot execute a migration and never changes a workspace.
/// </summary>
public partial class ComplexMigrationApprovalDialog : Window
{
    private readonly bool _canExecute;

    /// <summary>Displays the immutable preflight and blockers.</summary>
    /// <param name="plan">Approved-source candidate prepared without writes.</param>
    public ComplexMigrationApprovalDialog(ComplexProjectMigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _canExecute = plan.CanExecute;
        InitializeComponent();
        MovesList.ItemsSource = plan.Moves;
        SummaryText.Text = plan.CanExecute
            ? $"{plan.Moves.Count(move => move.State == ComplexMigrationMoveState.Candidate)} déplacement(s) et {plan.LinkRewrites.Count} lien(s) locaux et {plan.ExternalLinkRewrites.Count} backlink(s) externes à réécrire · sauvegarde et journal avant publication."
            : "Migration indisponible : corrigez les blocages ci-dessous. Aucun fichier ne sera déplacé.";
        FingerprintText.Text = "Empreinte du projet : " + plan.SourceFingerprint;
        ComplexMigrationLinkRewrite[] allRewrites = plan.LinkRewrites
            .Concat(plan.ExternalLinkRewrites).ToArray();
        RewritesText.Text = allRewrites.Length == 0
            ? "Aucune modification de lien nécessaire."
            : string.Join(Environment.NewLine, allRewrites.Take(60)
                .Select(item => item.WorkspaceRelativePath + ":" + item.Line +
                    " · " + item.OriginalTarget + " → " + item.ReplacementTarget)) +
                (allRewrites.Length > 60 ? Environment.NewLine +
                    "… et " + (allRewrites.Length - 60) + " autre(s)." : string.Empty);
        BlockersText.Text = plan.Blockers.Count == 0
            ? "Aucun blocage identifié dans le périmètre vérifié."
            : string.Join(Environment.NewLine, plan.Blockers);
        ApprovalText.IsEnabled = _canExecute;
    }

    /// <summary>Requires an explicit, exact confirmation word.</summary>
    /// <param name="sender">Confirmation input.</param>
    /// <param name="e">Changed text event.</param>
    private void ApprovalText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ApplyButton is not null)
        {
            ApplyButton.IsEnabled = _canExecute &&
                string.Equals(ApprovalText.Text, "MIGRER",
                    StringComparison.Ordinal);
        }
    }

    /// <summary>Returns approval to the owner without changing any file.</summary>
    /// <param name="sender">Confirmation button.</param>
    /// <param name="e">Button click event.</param>
    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canExecute ||
            !string.Equals(ApprovalText.Text, "MIGRER",
                StringComparison.Ordinal))
        {
            return;
        }

        DialogResult = true;
    }
}
