using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Maintenance;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Captures or edits an intervention without changing the Markdown until the user confirms.
/// </summary>
public partial class MaintenanceInterventionDialog : Window
{
    private readonly MaintenanceRecord? _existing;
    private readonly MaintenanceRecordKind _kind;
    private bool _initialized;
    private bool _dirty;

    private sealed record IncidentChoice(string? Id, string DisplayLabel);

    /// <summary>Builds the intervention editor and its optional incident list.</summary>
    /// <param name="incidents">Incidents already available in this maintenance file.</param>
    /// <param name="existing">The intervention to edit, or null when creating one.</param>
    public MaintenanceInterventionDialog(
        IReadOnlyList<MaintenanceRecord> incidents,
        MaintenanceRecord? existing = null,
        MaintenanceRecordKind kind = MaintenanceRecordKind.Intervention)
    {
        ArgumentNullException.ThrowIfNull(incidents);
        if (existing is not null && existing.Kind != MaintenanceRecordKind.Intervention)
        {
            throw new ArgumentException("Seule une intervention peut être éditée.", nameof(existing));
        }

        _existing = existing;
        _kind = kind;
        InitializeComponent();
        if (kind == MaintenanceRecordKind.Incident)
        {
            if (existing is not null)
            {
                throw new ArgumentException("La création d'incident ne modifie pas une intervention.", nameof(existing));
            }

            Title = "Nouvel incident";
            FormHeadingTextBlock.Text = "Incident de maintenance";
            IncidentRelationLabel.Visibility = Visibility.Collapsed;
            IncidentComboBox.Visibility = Visibility.Collapsed;
        }
        List<IncidentChoice> choices =
        [
            new IncidentChoice(null, "Aucun incident lié")
        ];
        choices.AddRange(incidents
            .Where(item => item.Kind == MaintenanceRecordKind.Incident)
            .OrderByDescending(item => item.Date)
            .Select(item => new IncidentChoice(item.Id,
                $"{item.Date:yyyy-MM-dd} · {item.Summary} ({item.Id})")));
        IncidentComboBox.ItemsSource = choices;
        IncidentComboBox.SelectedIndex = 0;
        DatePicker.SelectedDate = DateTime.Today;

        if (existing is not null)
        {
            Title = "Modifier une intervention";
            SummaryTextBox.Text = existing.Summary;
            DatePicker.SelectedDate = existing.Date.ToDateTime(TimeOnly.MinValue);
            IncidentComboBox.SelectedItem = choices.FirstOrDefault(item =>
                string.Equals(item.Id, existing.IncidentId, StringComparison.OrdinalIgnoreCase))
                ?? choices[0];
            SymptomsTextBox.Text = ReadDetail(existing, "Symptômes");
            CauseTextBox.Text = ReadDetail(existing, "Cause");
            DiagnosisTextBox.Text = ReadDetail(existing, "Diagnostic");
            ActionTextBox.Text = ReadDetail(existing, "Action");
            ResultTextBox.Text = ReadDetail(existing, "Résultat");
            StatusTextBox.Text = ReadDetail(existing, "Statut");
            CategoryTextBox.Text = ReadDetail(existing, "Catégorie");
            TagsTextBox.Text = ReadDetail(existing, "Tags");
            MoreFieldsExpander.IsExpanded =
                existing.IncidentId is not null || existing.Details.Count > 0;
        }

        Loaded += (_, _) =>
        {
            _initialized = true;
            SummaryTextBox.Focus();
            SummaryTextBox.SelectAll();
        };
        Closing += ConfirmCancel;
    }

    /// <summary>Gets the fully validated entry once the user accepts the dialog.</summary>
    public MaintenanceRecord? Result { get; private set; }

    /// <summary>Reads an optional Markdown field without discarding unknown fields.</summary>
    /// <param name="entry">Original intervention.</param>
    /// <param name="name">Visible Markdown field name.</param>
    /// <returns>The saved value, if any.</returns>
    private static string ReadDetail(MaintenanceRecord entry, string name)
    {
        return entry.Details.TryGetValue(name, out string? value) ? value : string.Empty;
    }

    /// <summary>Tracks user edits to warn before discarding an incomplete form.</summary>
    /// <param name="sender">Changed input.</param>
    /// <param name="e">WPF text change.</param>
    private void FieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized)
        {
            _dirty = true;
        }
    }

    /// <summary>Tracks selection changes made after initialization.</summary>
    /// <param name="sender">Changed selector.</param>
    /// <param name="e">WPF selection change.</param>
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized)
        {
            _dirty = true;
        }
    }

    /// <summary>Tracks date edits made after initialization.</summary>
    /// <param name="sender">Date control.</param>
    /// <param name="e">WPF date selection change.</param>
    private void DateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized)
        {
            _dirty = true;
        }
    }

    /// <summary>Warns before closing a form with uncommitted edits.</summary>
    /// <param name="sender">This window.</param>
    /// <param name="e">Cancelable close request.</param>
    private void ConfirmCancel(object? sender, CancelEventArgs e)
    {
        if (DialogResult == true || !_dirty)
        {
            return;
        }

        MessageBoxResult choice = MessageBox.Show(this,
            "Les modifications de cette entrée de maintenance ne sont pas enregistrées. Les abandonner ?",
            "Maintenance non enregistrée", MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        e.Cancel = choice != MessageBoxResult.Yes;
    }

    /// <summary>Validates the required summary and date, returning an editable Markdown record.</summary>
    /// <param name="sender">Save button.</param>
    /// <param name="e">Click event.</param>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string summary = SummaryTextBox.Text.Trim();
        if (summary.Length == 0 || summary.Contains('\n') || summary.Contains('\r') ||
            DatePicker.SelectedDate is not DateTime selected)
        {
            MessageBox.Show(this, "Un résumé sur une ligne et une date valide sont requis.",
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
            SummaryTextBox.Focus();
            return;
        }

        Dictionary<string, string> details = new Dictionary<string, string>(
            _existing?.Details ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        SetDetail(details, "Symptômes", SymptomsTextBox.Text);
        SetDetail(details, "Cause", CauseTextBox.Text);
        SetDetail(details, "Diagnostic", DiagnosisTextBox.Text);
        SetDetail(details, "Action", ActionTextBox.Text);
        SetDetail(details, "Résultat", ResultTextBox.Text);
        SetDetail(details, "Statut", StatusTextBox.Text);
        SetDetail(details, "Catégorie", CategoryTextBox.Text);
        SetDetail(details, "Tags", TagsTextBox.Text);

        Result = new MaintenanceRecord
        {
            Kind = _kind,
            Id = _existing?.Id ?? MaintenanceMarkdownCodec.NewId(_kind),
            Date = DateOnly.FromDateTime(selected),
            Summary = summary,
            IncidentId = _kind == MaintenanceRecordKind.Intervention
                ? (IncidentComboBox.SelectedItem as IncidentChoice)?.Id
                : null,
            Details = details
        };

        // Validate through the same core contract before leaving the form.
        try
        {
            MaintenanceMarkdownCodec.Append(string.Empty,
                Result with { IncidentId = null });
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, exception.Message, Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    /// <summary>Writes or removes one optional single-line property.</summary>
    /// <param name="details">All optional fields, including unknown ones.</param>
    /// <param name="name">Field name.</param>
    /// <param name="value">Edited text.</param>
    private static void SetDetail(Dictionary<string, string> details, string name, string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            details.Remove(name);
        }
        else
        {
            details[name] = trimmed;
        }
    }
}
