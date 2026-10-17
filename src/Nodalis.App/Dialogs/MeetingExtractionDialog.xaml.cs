using System.Windows;
using Nodalis.Core.Meetings;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Lets the user explicitly select deterministic meeting extraction candidates before any write occurs.
/// </summary>
public partial class MeetingExtractionDialog : Window
{
    private readonly IReadOnlyList<CandidateViewModel> _actions;
    private readonly IReadOnlyList<CandidateViewModel> _decisions;

    /// <summary>
    /// Initializes a new instance of <see cref="MeetingExtractionDialog"/>.
    /// </summary>
    /// <param name="preview">Read-only extraction preview.</param>
    public MeetingExtractionDialog(
            MeetingExtractionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(
            preview);

        InitializeComponent();

        _actions =
            preview.Actions
                .Select(candidate =>
                    new CandidateViewModel(
                        candidate.Task.Id,
                        candidate.Task.Text,
                        candidate.IsDuplicate,
                        "Ligne " +
                        candidate.Task.LineNumber))
                .ToArray();

        _decisions =
            preview.Decisions
                .Select(candidate =>
                    new CandidateViewModel(
                        candidate.Id,
                        candidate.Text,
                        candidate.IsDuplicate,
                        "Ligne " +
                        candidate.LineNumber))
                .ToArray();

        ActionsList.ItemsSource =
            _actions;
        DecisionsList.ItemsSource =
            _decisions;

        SummaryText.Text =
            _actions.Count +
            " action(s) · " +
            _decisions.Count +
            " décision(s)";
    }

    /// <summary>
    /// Gets the explicit selection after validation.
    /// </summary>
    public MeetingExtractionRequest? Request { get; private set; }

    /// <summary>
    /// Cancels the workflow without creating anything.
    /// </summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="e">Event arguments.</param>
    private void Cancel_Click(
            object sender,
            RoutedEventArgs e)
    {
        DialogResult =
            false;
    }

    /// <summary>
    /// Materializes only the candidates selected by the user.
    /// </summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="e">Event arguments.</param>
    private void Apply_Click(
            object sender,
            RoutedEventArgs e)
    {
        HashSet<Guid> actionIds =
            _actions
                .Where(candidate =>
                    candidate.IsSelected &&
                    candidate.CanSelect)
                .Select(candidate =>
                    candidate.Id)
                .ToHashSet();

        HashSet<Guid> decisionIds =
            _decisions
                .Where(candidate =>
                    candidate.IsSelected &&
                    candidate.CanSelect)
                .Select(candidate =>
                    candidate.Id)
                .ToHashSet();

        Request =
            new MeetingExtractionRequest
            {
                ActionIds =
                    actionIds,
                DecisionIds =
                    decisionIds
            };

        DialogResult =
            true;
    }

    /// <summary>
    /// Mutable UI projection for one immutable extraction candidate.
    /// </summary>
    private sealed class CandidateViewModel
    {
        /// <summary>
        /// Initializes a new candidate row.
        /// </summary>
        /// <param name="id">Candidate identifier.</param>
        /// <param name="text">Candidate text.</param>
        /// <param name="duplicate">Whether the candidate is a duplicate.</param>
        /// <param name="detail">Secondary display detail.</param>
        public CandidateViewModel(
                Guid id,
                string text,
                bool duplicate,
                string detail)
        {
            Id =
                id;
            Text =
                text;
            CanSelect =
                !duplicate;
            IsSelected =
                !duplicate;
            Detail =
                duplicate
                    ? detail + " · doublon détecté"
                    : detail;
        }

        /// <summary>Gets the candidate identifier.</summary>
        public Guid Id { get; }

        /// <summary>Gets the display text.</summary>
        public string Text { get; }

        /// <summary>Gets detail text.</summary>
        public string Detail { get; }

        /// <summary>Gets a value indicating whether this row can be selected.</summary>
        public bool CanSelect { get; }

        /// <summary>Gets or sets a value indicating whether this row is selected.</summary>
        public bool IsSelected { get; set; }
    }
}
