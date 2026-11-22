using System.Windows;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>User-reviewed creation of one canonical SFD draft.</summary>
public partial class SfdNewDocumentDialog : Window
{
    private readonly IReadOnlyList<SfdDocumentEntry> _entries;

    private sealed record KindChoice(SfdDocumentKind Kind, string Label);
    private sealed record ParentChoice(Guid Id, string Label);
    private sealed record ModeChoice(SfdFunctionalityMode Mode, string Label);

    /// <summary>Builds a selector over already resolved, unambiguous SFD parents.</summary>
    /// <param name="entries">Current project-only business objects.</param>
    public SfdNewDocumentDialog(IReadOnlyList<SfdDocumentEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;
        InitializeComponent();

        KindComboBox.ItemsSource = new[]
        {
            new KindChoice(SfdDocumentKind.Process, "Processus métier (PM)"),
            new KindChoice(SfdDocumentKind.Activity, "Activité (ACT)"),
            new KindChoice(SfdDocumentKind.UserRequirement, "Exigence utilisateur (EXU)"),
            new KindChoice(SfdDocumentKind.SystemRequirement, "Exigence système (EXS)"),
            new KindChoice(SfdDocumentKind.RegulatoryRequirement, "Exigence réglementaire (EXR)"),
            new KindChoice(SfdDocumentKind.Functionality, "Fonctionnalité (FCT)")
        };
        ModeComboBox.ItemsSource = new[]
        {
            new ModeChoice(SfdFunctionalityMode.WithInterface, "Avec IHM"),
            new ModeChoice(SfdFunctionalityMode.WithoutInterface, "Sans IHM")
        };
        ModeComboBox.SelectedIndex = 0;
        KindComboBox.SelectionChanged += (_, _) => UpdateKind();
        KindComboBox.SelectedIndex = 0;
        Loaded += (_, _) => TitleTextBox.Focus();
    }

    /// <summary>Selected role, used only after the user validates the dialog.</summary>
    public SfdDocumentKind SelectedKind =>
        ((KindChoice)KindComboBox.SelectedItem).Kind;

    /// <summary>Title explicitly entered by the user.</summary>
    public string DocumentTitle => TitleTextBox.Text.Trim();

    /// <summary>GUID of the existing required parent, never inferred from a folder.</summary>
    public Guid? SelectedParentId => ParentPanel.Visibility == Visibility.Visible
        ? (ParentComboBox.SelectedItem as ParentChoice)?.Id
        : null;

    /// <summary>Exclusive FCT mode, or null for non-functionality objects.</summary>
    public SfdFunctionalityMode? SelectedMode => ModePanel.Visibility == Visibility.Visible
        ? (ModeComboBox.SelectedItem as ModeChoice)?.Mode
        : null;

    /// <summary>Updates parent and interface selectors for the selected role.</summary>
    private void UpdateKind()
    {
        if (KindComboBox.SelectedItem is not KindChoice choice)
        {
            return;
        }

        SfdDocumentKind? parentKind = choice.Kind switch
        {
            SfdDocumentKind.Activity => SfdDocumentKind.Process,
            SfdDocumentKind.UserRequirement or SfdDocumentKind.SystemRequirement or
                SfdDocumentKind.RegulatoryRequirement => SfdDocumentKind.Activity,
            _ => null
        };
        ParentPanel.Visibility = parentKind.HasValue
            ? Visibility.Visible : Visibility.Collapsed;
        ModePanel.Visibility = choice.Kind == SfdDocumentKind.Functionality
            ? Visibility.Visible : Visibility.Collapsed;

        ParentComboBox.ItemsSource = parentKind.HasValue
            ? _entries.Where(item => item.Card.Kind == parentKind.Value)
                .OrderBy(item => item.Card.Reference, StringComparer.Ordinal)
                .Select(item => new ParentChoice(item.Card.Id,
                    item.Card.Reference + " — " + item.Card.Title))
                .ToArray()
            : Array.Empty<ParentChoice>();
        if (parentKind.HasValue && ParentComboBox.Items.Count > 0)
        {
            ParentComboBox.SelectedIndex = 0;
        }

        HelpText.Text = parentKind.HasValue && ParentComboBox.Items.Count == 0
            ? "Aucun parent compatible : créez d'abord le processus ou l'activité."
            : "Les conditions, sous-conditions et références RM seront ajoutées " +
              "lors de la rédaction, sans contenu fictif.";
    }

    /// <summary>Checks the requested identity before closing the review dialog.</summary>
    /// <param name="sender">Create button.</param>
    /// <param name="e">Click event.</param>
    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DocumentTitle) ||
            (ParentPanel.Visibility == Visibility.Visible && SelectedParentId is null))
        {
            MessageBox.Show(this,
                "Renseignez un titre et, si nécessaire, sélectionnez le parent métier.",
                "Nouvel objet SFD", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
