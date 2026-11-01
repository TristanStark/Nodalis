using System.IO;
using System.Windows;
using Microsoft.Win32;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Gathers an explicit human action on one historical reservation.
/// No disk or registry write occurs inside this modal review dialog.
/// </summary>
public partial class ProjectGateReservationActionDialog : Window
{
    private readonly ProjectJourneySourceService _sources;
    private readonly string _projectDirectory;
    private readonly ProjectGateReservationEventKind _kind;

    /// <summary>Creates a non-executing qualification form.</summary>
    /// <param name="workspaceRoot">Current workspace root.</param>
    /// <param name="projectDirectory">Owning project.</param>
    /// <param name="reservation">Original immutable reservation.</param>
    /// <param name="kind">Human lift or reopen.</param>
    public ProjectGateReservationActionDialog(
        string workspaceRoot, string projectDirectory,
        ProjectGateReservation reservation, ProjectGateReservationEventKind kind)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        _sources = new ProjectJourneySourceService(workspaceRoot);
        _projectDirectory = projectDirectory;
        _kind = kind;
        InitializeComponent();
        bool lifting = kind == ProjectGateReservationEventKind.Lifted;
        HeadingText.Text = lifting
            ? "Qualifier la levée d'une réserve"
            : "Réouvrir une réserve";
        Title = HeadingText.Text + " · Gate";
        DescriptionText.Text = reservation.Description +
            "\n\nIdentifiant stable : " + reservation.Id.ToString("D");
        EvidenceHeadingText.Text = lifting
            ? "Preuve locale obligatoire pour la levée (SHA-256)"
            : "Preuve locale facultative pour la réouverture (SHA-256)";
        ConfirmButton.Content = lifting
            ? "Proposer la levée"
            : "Proposer la réouverture";
        StatusText.Text = "Saisie manuelle requise.";
    }

    /// <summary>Human author approved in this dialog.</summary>
    public string? Actor { get; private set; }

    /// <summary>Human qualification text approved in this dialog.</summary>
    public string? Rationale { get; private set; }

    /// <summary>Existing local proof to fingerprint before saving.</summary>
    public string? EvidencePath { get; private set; }

    /// <summary>Selects a local proof without copying or reading its contents.</summary>
    /// <param name="sender">Select button.</param>
    /// <param name="e">Click event.</param>
    private void ChooseEvidence_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog picker = new()
        {
            Title = "Sélectionner une preuve de qualification existante",
            InitialDirectory = _projectDirectory,
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            string source = _sources.GetReferenceForExistingFile(picker.FileName);
            EvidencePath = source;
            EvidenceText.Text = source;
            StatusText.Text = "Preuve retenue, empreinte calculée après validation.";
        }
        catch (Exception exception) when (exception is ArgumentException or
            IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message,
                "Preuve non admissible", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>Validates explicit human input; no registry write.</summary>
    /// <param name="sender">Confirm button.</param>
    /// <param name="e">Click event.</param>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string actor = ProjectGateValidation.RequireText(
                ActorBox.Text, 120, nameof(Actor));
            string rationale = ProjectGateValidation.RequireText(
                RationaleBox.Text, 4000, nameof(Rationale));
            if (rationale.Length < 10)
            {
                throw new InvalidDataException(
                    "Un motif explicite d'au moins 10 caractères est requis.");
            }

            if (_kind == ProjectGateReservationEventKind.Lifted &&
                EvidencePath is null)
            {
                throw new InvalidDataException(
                    "Une levée exige une preuve vérifiable du workspace.");
            }

            Actor = actor;
            Rationale = rationale;
            DialogResult = true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidDataException)
        {
            StatusText.Text = exception.Message;
        }
    }

    /// <summary>Cancels the action without creating an event.</summary>
    /// <param name="sender">Cancel button.</param>
    /// <param name="e">Click event.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
