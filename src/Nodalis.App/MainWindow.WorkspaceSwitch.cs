using System.IO;
using System.Windows;
using Microsoft.Win32;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.App;

/// <summary>
/// Offers an explicit, non-destructive workspace selection for the next launch.
/// Workspace services are bound to the window and are never replaced in-place.
/// </summary>
public partial class MainWindow
{
    private bool _workspaceSwitchPending;

    /// <summary>Opens the workspace picker from the System menu.</summary>
    /// <param name="sender">The triggering menu item.</param>
    /// <param name="e">The routed event.</param>
    private async void ChangeWorkspace_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(
            "Système · changer de workspace",
            ChangeWorkspaceAsync);
    }

    /// <summary>
    /// Selects and validates an existing workspace without modifying either
    /// workspace. A restart is required to rebuild the application services.
    /// </summary>
    /// <returns>A task representing the explicit selection.</returns>
    private async Task ChangeWorkspaceAsync()
    {
        OpenFolderDialog picker = new()
        {
            Title = "Choisir un workspace Nodalis existant",
            Multiselect = false
        };

        string? currentPath = _preferences.WorkspaceRootPath;
        if (!string.IsNullOrWhiteSpace(currentPath) &&
            Directory.Exists(currentPath))
        {
            picker.InitialDirectory = currentPath;
        }

        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        string targetPath = Path.GetFullPath(picker.FolderName);
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(targetPath),
                Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(currentPath ?? _root.FullPath)),
                StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Ce workspace est déjà ouvert.";
            return;
        }

        string manifestPath = Path.Combine(
            targetPath,
            WorkspaceLayout.WorkspaceManifestFileName);
        if (!Directory.Exists(targetPath) || !File.Exists(manifestPath))
        {
            MessageBox.Show(
                this,
                "Le dossier sélectionné n'est pas un workspace Nodalis. " +
                "Choisissez directement le dossier contenant .workspace.json. " +
                "Aucun fichier n'a été modifié.",
                "Workspace introuvable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // This path only selects an existing, compatible workspace.
        // It must not initialize or migrate a folder implicitly.
        FileSystemWorkspaceStore workspaceStore = new(targetPath);
        WorkspaceManifest selected = await workspaceStore.LoadAsync();

        MessageBoxResult confirm = MessageBox.Show(
            this,
            "Workspace sélectionné : " + selected.Name +
            "\n\n" + targetPath +
            "\n\nLe changement sera appliqué au prochain démarrage de Nodalis. " +
            "Enregistrez et fermez normalement l'application, puis relancez-la. " +
            "Aucun document du workspace actuel ne sera supprimé." +
            "\n\nConfirmer le changement ?",
            "Changer de workspace",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        // Persist before closing; the old window remains fully bound to its
        // original workspace until the ordinary shutdown path completes.
        var previousPreferences = _preferences;
        var nextPreferences = _preferences with { WorkspaceRootPath = targetPath };
        await _preferencesStore.SaveAsync(nextPreferences);
        _preferences = nextPreferences;
        _workspaceSwitchPending = true;

        StatusText.Text = "Nouveau workspace enregistré : fermer et relancer Nodalis.";
        MessageBox.Show(
            this,
            "Le workspace sera chargé au prochain démarrage. " +
            "Vous pouvez maintenant fermer Nodalis normalement.",
            "Workspace sélectionné",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
