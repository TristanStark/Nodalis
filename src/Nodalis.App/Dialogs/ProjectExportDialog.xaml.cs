using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using Nodalis.Core.Exporting;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Collects the destination, formats, section selection, section order, and attachment preference for a project export.
/// </summary>
public partial class ProjectExportDialog : Window
{
    private readonly ObservableCollection<ProjectExportSectionRow> _sections;

    /// <summary>
    /// Initializes a project export dialog from a read-only preview.
    /// </summary>
    /// <param name="preview">The project export preview.</param>
    public ProjectExportDialog(
            ProjectExportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(
            preview);

        InitializeComponent();

        ProjectNameText.Text =
            "Exporter « " +
            preview.ProjectName +
            " »";
        ProjectSummaryText.Text =
            preview.MarkdownDocumentCount +
            " document(s) Markdown · " +
            preview.AttachmentCount +
            " pièce(s) jointe(s) locale(s) référencée(s)";
        BaseNameTextBox.Text =
            preview.SuggestedBaseName;

        _sections =
            new ObservableCollection<ProjectExportSectionRow>(
                preview.Sections
                    .OrderBy(section =>
                        section.SourceOrder)
                    .Select(section =>
                        new ProjectExportSectionRow(
                            section)));

        DataContext =
            this;
    }

    /// <summary>
    /// Gets the mutable section rows displayed by the dialog.
    /// </summary>
    public ObservableCollection<ProjectExportSectionRow> Sections =>
        _sections;

    /// <summary>
    /// Gets the export request accepted by the user.
    /// </summary>
    public ProjectExportRequest? ExportRequest { get; private set; }

    /// <summary>
    /// Opens the native folder picker for the external export destination.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void BrowseDestination_Click(
            object sender,
            RoutedEventArgs e)
    {
        OpenFolderDialog picker =
            new OpenFolderDialog
            {
                Title =
                    "Sélectionner le dossier de destination de l'export",
                Multiselect =
                    false
            };

        if (!string.IsNullOrWhiteSpace(
                DestinationTextBox.Text) &&
            Directory.Exists(
                DestinationTextBox.Text))
        {
            picker.InitialDirectory =
                DestinationTextBox.Text;
        }

        if (picker.ShowDialog(
                this) ==
            true)
        {
            DestinationTextBox.Text =
                picker.FolderName;
        }
    }

    /// <summary>
    /// Moves the selected section one position earlier in the export.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void MoveSectionUp_Click(
            object sender,
            RoutedEventArgs e)
    {
        int index =
            SectionList.SelectedIndex;

        if (index <=
            0)
        {
            return;
        }

        _sections.Move(
            index,
            index -
            1);
        SectionList.SelectedIndex =
            index -
            1;
    }

    /// <summary>
    /// Moves the selected section one position later in the export.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void MoveSectionDown_Click(
            object sender,
            RoutedEventArgs e)
    {
        int index =
            SectionList.SelectedIndex;

        if (index <
                0 ||
            index >=
                _sections.Count -
                1)
        {
            return;
        }

        _sections.Move(
            index,
            index +
            1);
        SectionList.SelectedIndex =
            index +
            1;
    }

    /// <summary>
    /// Validates the dialog choices and returns a complete export request.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Export_Click(
            object sender,
            RoutedEventArgs e)
    {
        string destination =
            DestinationTextBox.Text.Trim();
        string baseName =
            BaseNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                destination))
        {
            ShowValidation(
                "Sélectionnez un dossier de destination.",
                DestinationTextBox);
            return;
        }

        if (string.IsNullOrWhiteSpace(
                baseName))
        {
            ShowValidation(
                "Indiquez un nom de base pour l'export.",
                BaseNameTextBox);
            return;
        }

        List<ProjectExportFormat> formats =
            new List<ProjectExportFormat>();

        if (MarkdownCheckBox.IsChecked ==
            true)
        {
            formats.Add(
                ProjectExportFormat.PortableMarkdown);
        }

        if (HtmlCheckBox.IsChecked ==
            true)
        {
            formats.Add(
                ProjectExportFormat.StandaloneHtml);
        }

        if (DocxCheckBox.IsChecked ==
            true)
        {
            formats.Add(
                ProjectExportFormat.NativeDocx);
        }

        if (formats.Count ==
            0)
        {
            MessageBox.Show(
                this,
                "Sélectionnez au moins un format d'export.",
                "Export projet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        List<ProjectExportSectionSelection> sections =
            _sections
                .Select(
                    (section, index) =>
                        new
                        {
                            Section =
                                section,
                            Order =
                                index
                        })
                .Where(item =>
                    item.Section.IsIncluded)
                .Select(item =>
                    new ProjectExportSectionSelection
                    {
                        SectionId =
                            item.Section.SectionId,
                        Order =
                            item.Order
                    })
                .ToList();

        if (sections.Count ==
            0)
        {
            MessageBox.Show(
                this,
                "Sélectionnez au moins une section à exporter.",
                "Export projet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ExportRequest =
            new ProjectExportRequest
            {
                DestinationDirectory =
                    destination,
                BaseName =
                    baseName,
                IncludeAttachments =
                    AttachmentsCheckBox.IsChecked ==
                    true,
                Formats =
                    formats,
                Sections =
                    sections
            };

        DialogResult =
            true;
    }

    /// <summary>
    /// Shows one validation message and focuses its associated control.
    /// </summary>
    /// <param name="message">The validation message.</param>
    /// <param name="control">The control to focus.</param>
    private void ShowValidation(
            string message,
            FrameworkElement control)
    {
        MessageBox.Show(
            this,
            message,
            "Export projet",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        control.Focus();
    }

    /// <summary>
    /// Mutable UI row used to select and reorder one project section.
    /// </summary>
    public sealed class ProjectExportSectionRow : INotifyPropertyChanged
    {
        private bool _isIncluded = true;

        /// <summary>
        /// Initializes a section row from one preview section.
        /// </summary>
        /// <param name="section">The source section.</param>
        public ProjectExportSectionRow(
                ProjectExportSectionOption section)
        {
            ArgumentNullException.ThrowIfNull(
                section);

            SectionId =
                section.SectionId;
            Name =
                section.Name;
            DocumentCount =
                section.DocumentCount;
        }

        /// <summary>
        /// Raised when a mutable row property changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Gets the immutable section identifier.
        /// </summary>
        public Guid SectionId { get; }

        /// <summary>
        /// Gets the section display name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the number of Markdown documents discovered in the section.
        /// </summary>
        public int DocumentCount { get; }

        /// <summary>
        /// Gets or sets whether the section will be exported.
        /// </summary>
        public bool IsIncluded
        {
            get =>
                _isIncluded;
            set
            {
                if (_isIncluded ==
                    value)
                {
                    return;
                }

                _isIncluded =
                    value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Raises <see cref="PropertyChanged"/> for one row property.
        /// </summary>
        /// <param name="propertyName">The changed property name.</param>
        private void OnPropertyChanged(
                [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    propertyName));
        }
    }
}
