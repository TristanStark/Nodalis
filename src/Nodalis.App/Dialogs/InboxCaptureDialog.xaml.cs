using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Nodalis.Core.Inbox;
using Nodalis.Infrastructure.Inbox;

namespace Nodalis.App.Dialogs;

/// <summary>
/// User-controlled capture from manually entered text, selected files or
/// clipboard formats; never reads the clipboard when the dialog opens.
/// </summary>
public partial class InboxCaptureDialog : Window
{
    private string[] _selectedFiles = [];
    private string? _temporaryImagePath;
    private bool _imageSelected;
    private bool _saveInProgress;
    private readonly WorkspaceInboxService? _duplicateStore;
    private readonly string _origin;

    /// <summary>Initializes a keyboard-accessible explicit capture dialog.</summary>
    /// <param name="workspaceRoot">Optional workspace for explicit persistence and duplicate checks.</param>
    /// <param name="origin">Voluntary capture entry point, for provenance.</param>
    public InboxCaptureDialog(
        string? workspaceRoot = null,
        string origin = "explicit-user-capture")
    {
        _origin = origin;
        _duplicateStore = string.IsNullOrWhiteSpace(workspaceRoot)
            ? null : new WorkspaceInboxService(workspaceRoot);
        InitializeComponent();
        Loaded += (_, _) => CaptureTextBox.Focus();
    }

    public string CapturedMarkdown => CaptureTextBox.Text;

    public InboxCaptureKind CapturedKind { get; private set; } =
        InboxCaptureKind.Text;

    public IReadOnlyList<string> CapturedFilePaths => _selectedFiles;

    /// <summary>Fully persisted capture, or null until explicit Save succeeds.</summary>
    public InboxEntry? SavedEntry { get; private set; }

    /// <summary>Reads the clipboard only after the user presses Import.</summary>
    /// <param name="sender">Import button.</param>
    /// <param name="e">Routed click event.</param>
    private void ImportClipboard_Click(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                StringCollection paths = Clipboard.GetFileDropList();
                ApplyFiles(paths.Cast<string>().ToArray(), isImage: false);
                return;
            }

            if (Clipboard.ContainsImage())
            {
                BitmapSource? image = Clipboard.GetImage();
                if (image is null)
                {
                    throw new InvalidDataException("Le bitmap du presse-papiers est inaccessible.");
                }

                if (image.PixelWidth <= 0 || image.PixelHeight <= 0 ||
                    (long)image.PixelWidth * image.PixelHeight > 40_000_000)
                {
                    throw new InvalidDataException(
                        "L'image est trop grande pour être importée (40 millions de pixels maximum).");
                }

                string temporaryImage = Path.Combine(
                    Path.GetTempPath(),
                    "Nodalis-Inbox-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using (FileStream stream = new FileStream(
                        temporaryImage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        encoder.Save(stream);
                    }

                    ApplyFiles([temporaryImage], isImage: true);
                    CaptureTextBox.Text = "Image importée depuis le presse-papiers.";
                }
                catch
                {
                    if (File.Exists(temporaryImage))
                    {
                        File.Delete(temporaryImage);
                    }

                    throw;
                }

                return;
            }

            if (Clipboard.ContainsText())
            {
                string clipboardText = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    throw new InvalidDataException("Le presse-papiers ne contient pas de texte.");
                }

                ResetFileSelection();
                CaptureTextBox.Text = clipboardText;
                CaptureTextBox.CaretIndex = clipboardText.Length;
                SelectionStatusText.Text = InboxCaptureClassifier.ClassifyText(
                    clipboardText) == InboxCaptureKind.Link
                    ? "Lien HTTP(S) sélectionné pour capture."
                    : "Texte récupéré depuis le presse-papiers.";
                CaptureTextBox.Focus();
                return;
            }

            ShowValidation("Aucun texte, lien, bitmap ou fichier compatible dans le presse-papiers.");
        }
        catch (Exception exception) when (
            exception is IOException or ExternalException or InvalidDataException
                or ArgumentException or UnauthorizedAccessException
                or NotSupportedException)
        {
            LogRecoverableImportError("Inbox · import presse-papiers", exception);
            ShowValidation("Import impossible : " + exception.Message);
        }
    }

    /// <summary>Lets the user explicitly select up to ten files.</summary>
    /// <param name="sender">File picker button.</param>
    /// <param name="e">Routed click event.</param>
    private void ChooseFiles_Click(
            object sender,
            RoutedEventArgs e)
    {
        OpenFileDialog picker = new OpenFileDialog
        {
            Title = "Fichiers à capturer dans Inbox",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Tous les fichiers (*.*)|*.*"
        };

        try
        {
            if (picker.ShowDialog(this) != true)
            {
                return;
            }

            ApplyFiles(picker.FileNames, isImage: false);
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or InvalidDataException
                or UnauthorizedAccessException or ExternalException)
        {
            LogRecoverableImportError("Inbox · sélection de fichiers", exception);
            ShowValidation("Fichier non importé : " + exception.Message);
        }
    }

    /// <summary>Validates the selection and retains only file paths until Save.</summary>
    /// <param name="paths">Paths explicitly selected by the user.</param>
    /// <param name="isImage">Whether this is a staged clipboard bitmap.</param>
    private void ApplyFiles(
            IEnumerable<string> paths,
            bool isImage)
    {
        IReadOnlyList<string> selected = InboxCaptureClassifier.ValidateFiles(paths);
        string[] safeSelection = selected.ToArray();
        string summary = isImage
            ? "Image importée depuis le presse-papiers."
            : InboxCaptureClassifier.FormatFileSummary(safeSelection);

        ResetFileSelection();
        _selectedFiles = safeSelection;
        _imageSelected = isImage;
        _temporaryImagePath = isImage ? safeSelection[0] : null;
        CaptureTextBox.Text = summary;
        SelectionStatusText.Text = isImage
            ? "Image prête à être copiée localement dans Inbox lors de l'enregistrement."
            : safeSelection.Length + " fichier(s) sélectionné(s), copiés localement uniquement à l'enregistrement.";
        CaptureTextBox.Focus();
    }

    /// <summary>Ensures a former clipboard bitmap cannot be mistaken for a new one.</summary>
    private void ResetFileSelection()
    {
        _selectedFiles = [];
        _imageSelected = false;
        CleanupTemporaryCapture();
    }

    /// <summary>
    /// Deletes only the uniquely generated temporary PNG, never user files.
    /// Called after successful persistence or cancellation by the owner.
    /// </summary>
    public void CleanupTemporaryCapture()
    {
        if (_temporaryImagePath is null)
        {
            return;
        }

        string temp = _temporaryImagePath;
        _temporaryImagePath = null;
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }
    }

    /// <summary>
    /// On explicit Save, validates content and compares probable duplicates.
    /// The user decides whether to keep an intentional duplicate. A rejected
    /// suggestion never persists the clipboard or attachment content.
    /// </summary>
    /// <param name="sender">Save button.</param>
    /// <param name="e">Routed click event.</param>
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saveInProgress)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CaptureTextBox.Text))
        {
            ShowValidation("Saisissez du contenu à capturer.");
            CaptureTextBox.Focus();
            return;
        }

        _saveInProgress = true;
        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        try
        {
            if (_selectedFiles.Length > 0)
            {
                _selectedFiles = InboxCaptureClassifier.ValidateFiles(_selectedFiles)
                    .ToArray();
                CapturedKind = _imageSelected
                    ? InboxCaptureKind.Image
                    : InboxCaptureKind.File;
            }
            else
            {
                CapturedKind = InboxCaptureClassifier.ClassifyText(CaptureTextBox.Text);
            }

            if (_duplicateStore is not null)
            {
                InboxCaptureRequest request = new InboxCaptureRequest
                {
                    Kind = CapturedKind,
                    ContentMarkdown = CaptureTextBox.Text,
                    AttachmentSourcePaths = _selectedFiles,
                    Origin = _origin
                };
                IReadOnlyList<InboxDuplicateCandidate> matches =
                    await _duplicateStore.FindProbableDuplicatesAsync(request);
                if (matches.Count > 0)
                {
                    InboxDuplicateCandidate recent = matches[0];
                    string location = recent.InTrash
                        ? "en corbeille"
                        : recent.State == InboxItemState.Archived
                            ? "archivée" : "présente dans l'Inbox";
                    MessageBoxResult confirmation = MessageBox.Show(
                        this,
                        "Une capture très similaire existe déjà (" +
                        matches.Count + " correspondance(s) sur 7 jours).\n" +
                        "Plus récente : " +
                        recent.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
                        ", " + location + ".\n\n" +
                        "Enregistrer volontairement ce doublon ?",
                        "Inbox · doublon probable",
                        MessageBoxButton.YesNo, MessageBoxImage.Question,
                        MessageBoxResult.No);
                    if (confirmation != MessageBoxResult.Yes)
                    {
                        SelectionStatusText.Text =
                            "Doublon non enregistré. Modifiez la capture ou annulez.";
                        return;
                    }
                }
            }

            // Keep this dialog open on disk/permission/attachment errors: no
            // captured text disappears if the workspace write fails.
            if (_duplicateStore is not null)
            {
                SavedEntry = await _duplicateStore.CaptureAsync(
                    new InboxCaptureRequest
                    {
                        Kind = CapturedKind,
                        ContentMarkdown = CaptureTextBox.Text,
                        AttachmentSourcePaths = _selectedFiles,
                        Origin = _origin
                    });
            }

            // No duplicate watcher or automatic clipboard polling: the only
            // path to persistence is this explicit user confirmation.
            DialogResult = true;
        }
        catch (Exception exception) when (
            exception is IOException or ExternalException or InvalidDataException
                or ArgumentException or UnauthorizedAccessException
                or InvalidOperationException or NotSupportedException)
        {
            if (Application.Current is App application)
            {
                application.DiagnosticsService.LogRecoverableException(
                    "Inbox · validation et détection de doublon", exception);
            }

            ShowValidation("Capture impossible : " + exception.Message);
        }
        finally
        {
            _saveInProgress = false;
            if (DialogResult != true)
            {
                SaveButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
            }
        }
    }

    /// <summary>Records a recoverable import failure with its explicit action context.</summary>
    /// <param name="context">User action that failed.</param>
    /// <param name="exception">Original exception.</param>
    private static void LogRecoverableImportError(
        string context, Exception exception)
    {
        if (Application.Current is App application)
        {
            application.DiagnosticsService.LogRecoverableException(
                context, exception);
        }
    }

    /// <summary>Shows an accessible recovery message instead of closing the dialog.</summary>
    /// <param name="message">User-facing validation error.</param>
    private void ShowValidation(string message)
    {
        MessageBox.Show(
            this, message, "Capture Inbox", MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
