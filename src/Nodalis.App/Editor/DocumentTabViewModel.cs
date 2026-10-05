using System.ComponentModel;
using System.Runtime.CompilerServices;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.App.Editor;

/// <summary>
/// Holds the independent editing state and autosave session for one open document tab.
/// </summary>
public sealed class DocumentTabViewModel : INotifyPropertyChanged
{
    private string _displayName;
    private string _fullPath;
    private string _content;
    private bool _isDirty;
    private bool _isMissing;
    private bool _hasConflict;

    /// <summary>
    /// Initializes a new document tab.
    /// </summary>
    /// <param name="documentId">The stable navigation identifier.</param>
    /// <param name="displayName">The visible document name.</param>
    /// <param name="fullPath">The absolute document path.</param>
    /// <param name="session">The document reliability session.</param>
    /// <param name="autosave">The autosave controller dedicated to this tab.</param>
    public DocumentTabViewModel(
            Guid documentId,
            string displayName,
            string fullPath,
            TextDocumentSession session,
            DocumentAutosaveController autosave)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(autosave);

        DocumentId = documentId;
        _displayName = displayName;
        _fullPath = Path.GetFullPath(fullPath);
        Session = session;
        Autosave = autosave;
        _content = session.Content;
    }

    /// <summary>
    /// Raised when a bindable tab property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the stable navigation identifier for the document.
    /// </summary>
    public Guid DocumentId { get; }

    /// <summary>
    /// Gets the visible document name.
    /// </summary>
    public string DisplayName => _displayName;

    /// <summary>
    /// Gets the absolute document path.
    /// </summary>
    public string FullPath => _fullPath;

    /// <summary>
    /// Gets the title rendered in the tab strip.
    /// </summary>
    public string TabTitle =>
        IsMissing
            ? $"⚠ {_displayName}"
            : HasConflict
                ? $"⚠ {_displayName}"
                : IsDirty
                    ? $"● {_displayName}"
                    : _displayName;

    /// <summary>
    /// Gets or sets the in-memory editor content for this tab.
    /// </summary>
    public string Content
    {
        get => _content;
        set => _content = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets whether the editor content differs from the last saved revision.
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value)
            {
                return;
            }

            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TabTitle));
        }
    }

    /// <summary>
    /// Gets or sets whether the backing file is currently missing.
    /// </summary>
    public bool IsMissing
    {
        get => _isMissing;
        set
        {
            if (_isMissing == value)
            {
                return;
            }

            _isMissing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TabTitle));
        }
    }

    /// <summary>
    /// Gets or sets whether the tab has an unresolved external modification conflict.
    /// </summary>
    public bool HasConflict
    {
        get => _hasConflict;
        set
        {
            if (_hasConflict == value)
            {
                return;
            }

            _hasConflict = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TabTitle));
        }
    }

    /// <summary>
    /// Gets or sets whether the conflict warning has already been shown to the user.
    /// </summary>
    public bool ConflictWarningShown { get; set; }

    /// <summary>
    /// Gets or sets the caret position remembered for this tab.
    /// </summary>
    public int CaretIndex { get; set; }

    /// <summary>
    /// Gets or sets the selection start remembered for this tab.
    /// </summary>
    public int SelectionStart { get; set; }

    /// <summary>
    /// Gets or sets the selection length remembered for this tab.
    /// </summary>
    public int SelectionLength { get; set; }

    /// <summary>
    /// Gets the reliability session dedicated to this document.
    /// </summary>
    public TextDocumentSession Session { get; }

    /// <summary>
    /// Gets or sets the autosave controller dedicated to this document.
    /// </summary>
    public DocumentAutosaveController Autosave { get; set; }

    /// <summary>
    /// Updates display metadata after the navigation tree has been rebuilt.
    /// </summary>
    /// <param name="displayName">The current visible name.</param>
    /// <param name="fullPath">The current absolute path.</param>
    public void UpdateNavigationIdentity(
            string displayName,
            string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        string normalizedPath = Path.GetFullPath(fullPath);
        bool nameChanged = !string.Equals(
            _displayName,
            displayName,
            StringComparison.CurrentCulture);

        _displayName = displayName;
        _fullPath = normalizedPath;

        if (nameChanged)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(TabTitle));
        }

        OnPropertyChanged(nameof(FullPath));
    }

    /// <summary>
    /// Raises a property changed notification.
    /// </summary>
    /// <param name="propertyName">The changed property name.</param>
    private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
}
