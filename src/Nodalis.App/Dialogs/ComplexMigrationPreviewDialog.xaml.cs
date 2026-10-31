using System.Windows;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only preview of Complex v2 folder moves. No accept/commit action
/// is present: an executable migration requires its own approved workflow.
/// </summary>
public partial class ComplexMigrationPreviewDialog : Window
{
    /// <summary>Shows all proposed moves, collisions, and remaining folders.</summary>
    /// <param name="preview">Immutable read-only migration inventory.</param>
    public ComplexMigrationPreviewDialog(ComplexProjectMigrationPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        InitializeComponent();
        MovesList.ItemsSource = preview.Moves;
        SummaryText.Text =
            $"{preview.Moves.Count(move => move.State == ComplexMigrationMoveState.Candidate)} déplacement(s) candidat(s), " +
            $"{preview.Moves.Count(move => move.State == ComplexMigrationMoveState.AlreadyPlaced)} déjà en place, " +
            $"{preview.Moves.Count(move => move.State == ComplexMigrationMoveState.Blocked)} conflit(s). " +
            "Aucune écriture possible dans cet écran.";

        MissingText.Text = preview.MissingDirectories.Count == 0
            ? "Aucun dossier manquant dans la structure cible."
            : string.Join("  ·  ", preview.MissingDirectories);

        WarningsText.Text = string.Join(
            Environment.NewLine, preview.Warnings);
    }
}
