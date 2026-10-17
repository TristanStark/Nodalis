using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Nodalis.App.Reliability;
using Nodalis.Core.Inbox;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Maintenance;
using Nodalis.Infrastructure.Navigation;

namespace Nodalis.App.Dialogs;

/// <summary>Human-confirmed bridge from a recoverable Inbox capture to a maintenance sheet.</summary>
public partial class InboxMaintenanceConversionDialog : Window
{
    private sealed record ProjectChoice(string Label, string RelativePath, string FullPath);
    private sealed record IncidentChoice(string Label, string? Id);

    private readonly string _root;
    private readonly InboxEntry _capture;
    private readonly string? _preferredContextPath;
    private readonly Func<string, Task<bool>>? _prepareDocument;
    private readonly WorkspaceInboxMaintenanceService _service;
    private readonly WorkspaceMaintenanceService _maintenance;
    private bool _ready;
    private int _previewRevision;
    private MaintenanceInboxRequest? _resuming;

    /// <summary>Opens a safe explicit maintenance conversion from the triage window.</summary>
    /// <param name="workspaceRoot">Current workspace directory.</param>
    /// <param name="capture">Active Inbox capture.</param>
    /// <param name="preferredContextPath">Preferred project or file.</param>
    /// <param name="prepareDocument">Optional guard to flush in-memory editor changes.</param>
    public InboxMaintenanceConversionDialog(
        string workspaceRoot, InboxEntry capture,
        string? preferredContextPath = null,
        Func<string, Task<bool>>? prepareDocument = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(capture);
        _root = Path.GetFullPath(workspaceRoot);
        _capture = capture;
        _preferredContextPath = preferredContextPath;
        _prepareDocument = prepareDocument;
        _service = new WorkspaceInboxMaintenanceService(_root);
        _maintenance = new WorkspaceMaintenanceService();

        InitializeComponent();
        string firstLine = capture.ContentMarkdown.Split(
                ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimStart('#', '-', '*', ' '))
            .FirstOrDefault(line => line.Length > 0) ?? string.Empty;
        SummaryTextBox.Text = firstLine.Length == 0
            ? "Capture Inbox " + capture.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy")
            : firstLine[..Math.Min(firstLine.Length, 180)];
        SourceSummaryText.Text = "Capture " + capture.Id.ToString("N") +
            " · " + capture.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
            " · " + capture.Kind + (string.IsNullOrWhiteSpace(capture.Origin)
                ? "" : " · Origine : " + capture.Origin);
        AttachmentText.Text = capture.Attachments.Count == 0
            ? "Aucune pièce jointe."
            : "Pièces jointes (" + capture.Attachments.Count + ") :\n" +
                string.Join("\n", capture.Attachments.Select(attachment =>
                    "• " + attachment.OriginalFileName + " (" +
                    attachment.SizeBytes + " octets)"));
        ModeComboBox.SelectedIndex = 0;
        LoadImagePreview();
        Loaded += async (_, _) =>
            await UiActionGuard.RunAsync(this, "Maintenance · destinations",
                LoadChoicesAsync, _root);
    }

    /// <summary>
    /// Displays an optional bounded image preview from the original Inbox attachment
    /// without following a redirected path or modifying the capture.
    /// </summary>
    private void LoadImagePreview()
    {
        if (_capture.Kind != InboxCaptureKind.Image || _capture.Attachments.Count == 0)
        {
            return;
        }

        string itemDirectory = Path.Combine(_root, "Inbox", "Items",
            _capture.Id.ToString("N"));
        InboxAttachment attachment = _capture.Attachments[0];
        string source = Path.GetFullPath(Path.Combine(itemDirectory,
            attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!source.StartsWith(itemDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
        {
            AttachmentText.Text += "\nAperçu image indisponible : source manquante.";
            return;
        }

        try
        {
            string cursor = Path.GetDirectoryName(source)!;
            while (cursor.StartsWith(itemDirectory, StringComparison.OrdinalIgnoreCase))
            {
                if ((new DirectoryInfo(cursor).Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return;
                }

                if (string.Equals(cursor, itemDirectory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                cursor = Path.GetDirectoryName(cursor)!;
            }

            if ((new FileInfo(source).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            BitmapImage preview = new BitmapImage();
            preview.BeginInit();
            preview.UriSource = new Uri(source, UriKind.Absolute);
            preview.DecodePixelWidth = 450;
            preview.CacheOption = BitmapCacheOption.OnLoad;
            preview.EndInit();
            preview.Freeze();
            AttachmentImage.Source = preview;
            AttachmentImage.Visibility = Visibility.Visible;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or NotSupportedException or
            System.IO.FileFormatException)
        {
            AttachmentText.Text += "\nImpossible d'afficher l'image : " +
                exception.Message;
        }
    }

    /// <summary>Committed result, null when the user cancelled or conversion failed.</summary>
    public MaintenanceInboxResult? Result { get; private set; }

    /// <summary>Discovers projects from workspace manifests rather than guessed directories.</summary>
    /// <returns>Initial loading task.</returns>
    private async Task LoadChoicesAsync()
    {
        WorkspaceNavigationBuilder builder = new WorkspaceNavigationBuilder();
        WorkspaceNavigationNode root = await builder.BuildAsync(_root);
        List<ProjectChoice> projects = [];
        CollectProjects(root, string.Empty, projects);
        _resuming = await _service.ReadPreparedAsync(_capture.Id);
        ProjectComboBox.ItemsSource = projects;

        ProjectChoice? selected = null;
        if (_resuming is not null)
        {
            selected = projects.FirstOrDefault(candidate =>
                string.Equals(candidate.RelativePath, _resuming.ProjectRelativePath,
                    StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(_preferredContextPath))
        {
            string preferred = Path.GetFullPath(_preferredContextPath);
            selected = projects.Where(candidate =>
                    string.Equals(candidate.FullPath, preferred,
                        StringComparison.OrdinalIgnoreCase) ||
                    preferred.StartsWith(candidate.FullPath +
                        Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.FullPath.Length)
                .FirstOrDefault();
        }

        ProjectComboBox.SelectedItem = selected ?? projects.FirstOrDefault();
        ModeComboBox.SelectedIndex = _resuming?.Mode switch
        {
            MaintenanceInboxMode.Intervention => 1,
            MaintenanceInboxMode.IncidentAndIntervention => 2,
            _ => 0
        };
        if (_resuming is not null)
        {
            SummaryTextBox.Text = _resuming.Summary;
        }

        _ready = true;
        await LoadIncidentsAsync();
        if (_resuming is not null)
        {
            // A durable intent protects against a retry targeting another project.
            ProjectComboBox.IsEnabled = false;
            ModeComboBox.IsEnabled = false;
            ExistingIncidentComboBox.IsEnabled = false;
            SummaryTextBox.IsReadOnly = true;
        }

        if (projects.Count == 0)
        {
            StatusText.Text = "Aucun projet disposant d'une fiche de maintenance n'est disponible.";
            ConvertButton.IsEnabled = false;
        }
    }

    /// <summary>Collects project destinations with an informative application label.</summary>
    /// <param name="node">Navigation subtree.</param>
    /// <param name="application">Nearest application name.</param>
    /// <param name="projects">Accumulated valid destinations.</param>
    private void CollectProjects(WorkspaceNavigationNode node,
        string application, ICollection<ProjectChoice> projects)
    {
        string context = node.Kind == WorkspaceNodeKind.Application
            ? node.DisplayName : application;
        if (node.Kind == WorkspaceNodeKind.Project &&
            File.Exists(Path.Combine(node.FullPath, "Fiche de Maintenance.md")))
        {
            projects.Add(new ProjectChoice(
                (string.IsNullOrEmpty(context) ? "" : context + " · ") + node.DisplayName,
                Path.GetRelativePath(_root, node.FullPath).Replace('\\', '/'),
                node.FullPath));
        }

        foreach (WorkspaceNavigationNode child in node.Children)
        {
            CollectProjects(child, context, projects);
        }
    }

    /// <summary>Reloads incidents for the chosen project without modifying its maintenance sheet.</summary>
    /// <returns>Read task.</returns>
    private async Task LoadIncidentsAsync()
    {
        if (ProjectComboBox.SelectedItem is not ProjectChoice project)
        {
            ExistingIncidentComboBox.ItemsSource = null;
            ConvertButton.IsEnabled = false;
            return;
        }

        string path = Path.Combine(project.FullPath, "Fiche de Maintenance.md");
        MaintenanceParseResult parsed = await _maintenance.ReadAsync(path);
        List<IncidentChoice> choices =
        [
            new IncidentChoice("Aucun incident — intervention autonome", null)
        ];
        choices.AddRange(parsed.Records.Where(item =>
                item.Kind == MaintenanceRecordKind.Incident)
            .OrderByDescending(item => item.Date)
            .Select(item => new IncidentChoice(
                item.Date.ToString("dd/MM/yyyy") + " · " + item.Summary, item.Id)));
        ExistingIncidentComboBox.ItemsSource = choices;
        ExistingIncidentComboBox.SelectedItem = choices.FirstOrDefault(choice =>
            string.Equals(choice.Id, _resuming?.ExistingIncidentId,
                StringComparison.OrdinalIgnoreCase)) ?? choices[0];
        UpdateModeVisibility();
        await UpdatePreviewAsync();
    }

    /// <summary>Shows existing incidents only when the mode can link to one.</summary>
    private void UpdateModeVisibility()
    {
        bool visible = ModeComboBox.SelectedIndex == 1;
        ExistingIncidentLabel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ExistingIncidentComboBox.Visibility = ExistingIncidentLabel.Visibility;
    }

    /// <summary>Returns the request represented by the visible form.</summary>
    /// <returns>Immutable conversion choices.</returns>
    private MaintenanceInboxRequest GetRequest()
    {
        if (ProjectComboBox.SelectedItem is not ProjectChoice project)
        {
            throw new InvalidOperationException("Sélectionnez un projet contenant une fiche.");
        }

        return new MaintenanceInboxRequest
        {
            EntryId = _capture.Id,
            ProjectRelativePath = project.RelativePath,
            Mode = ModeComboBox.SelectedIndex switch
            {
                1 => MaintenanceInboxMode.Intervention,
                2 => MaintenanceInboxMode.IncidentAndIntervention,
                _ => MaintenanceInboxMode.Incident
            },
            ExistingIncidentId = ModeComboBox.SelectedIndex == 1
                ? (ExistingIncidentComboBox.SelectedItem as IncidentChoice)?.Id
                : null,
            Summary = SummaryTextBox.Text.Trim()
        };
    }

    /// <summary>Recomputes the preview when another project is selected.</summary>
    /// <param name="sender">Project selector.</param>
    /// <param name="e">Selection event.</param>
    private async void ProjectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        await UiActionGuard.RunAsync(this, "Maintenance · projet", LoadIncidentsAsync, _root);
    }

    /// <summary>Refreshes incident selector visibility and planned output.</summary>
    /// <param name="sender">Mode selector.</param>
    /// <param name="e">Selection event.</param>
    private async void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateModeVisibility();
        await UpdatePreviewAsync();
    }

    /// <summary>Refreshes proposed Markdown when the linked incident changes.</summary>
    /// <param name="sender">Incident selector.</param>
    /// <param name="e">Selection event.</param>
    private async void IncidentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) await UpdatePreviewAsync();
    }

    /// <summary>Refreshes proposed Markdown for an edited summary.</summary>
    /// <param name="sender">Summary textbox.</param>
    /// <param name="e">Text change.</param>
    private async void SummaryChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready) await UpdatePreviewAsync();
    }

    /// <summary>Shows a read-only preview; invalid documents are never edited.</summary>
    /// <returns>Preview task.</returns>
    private async Task UpdatePreviewAsync()
    {
        int revision = ++_previewRevision;
        try
        {
            MaintenanceInboxRequest request = GetRequest();
            string markdown = await _service.PreviewAsync(request);
            if (revision != _previewRevision) return;
            string marker = MaintenanceInboxMarkdown.Marker(_capture.Id);
            int at = markdown.LastIndexOf(marker, StringComparison.Ordinal);
            int heading = at >= 0
                ? markdown.LastIndexOf("### ", at, StringComparison.Ordinal) : -1;
            PreviewTextBox.Text = "Fiche : " + request.ProjectRelativePath +
                "/Fiche de Maintenance.md\n\n" +
                (heading >= 0 ? markdown[heading..] : markdown);
            StatusText.Text = _resuming is null
                ? "Prévisualisation uniquement : aucune écriture avant confirmation."
                : "Reprise d'une conversion interrompue : choix verrouillés, pas de doublon.";
            ConvertButton.IsEnabled = true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or IOException or
            UnauthorizedAccessException)
        {
            if (revision != _previewRevision) return;
            PreviewTextBox.Text = _capture.ContentMarkdown;
            StatusText.Text = exception.Message;
            ConvertButton.IsEnabled = false;
        }
    }

    /// <summary>Commits one conversion only after the owning editor flushes its tab.</summary>
    /// <param name="sender">Convert button.</param>
    /// <param name="e">WPF event.</param>
    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(this, "Inbox · ajouter à une fiche", async () =>
        {
            ConvertButton.IsEnabled = false;
            try
            {
                MaintenanceInboxRequest request = GetRequest();
                if (ProjectComboBox.SelectedItem is not ProjectChoice project)
                {
                    return;
                }

                if (_prepareDocument is not null &&
                    !await _prepareDocument(Path.Combine(
                        project.FullPath, "Fiche de Maintenance.md")))
                {
                    StatusText.Text = "Annulé : modifications non enregistrées dans la fiche.";
                    return;
                }

                Result = await _service.ConvertAsync(request);
                DialogResult = true;
            }
            finally
            {
                ConvertButton.IsEnabled = true;
            }
        }, _root);
    }
}
