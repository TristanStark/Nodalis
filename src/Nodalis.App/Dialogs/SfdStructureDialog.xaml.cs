using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Dialogs;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human-controlled C/SC/RM authoring and a read-only completion dashboard.
/// Never imports external files or proposes invented conditions/rules.
/// </summary>
public partial class SfdStructureDialog : Window
{
    private readonly SfdStructuredAuthoringService _service;
    private readonly SfdCompletionReviewService _review;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private SfdRequirementBlockSnapshot? _current;
    private SfdBusinessRuleSnapshot? _rules;
    private bool _loading = true;

    private sealed record Choice(Guid Id, string Label);
    private sealed record FindingChoice(string Label);

    /// <summary>Builds the editable dashboard for one validated SFD project.</summary>
    /// <param name="workspaceRoot">Active Nodalis workspace.</param>
    /// <param name="projectDirectory">Selected project SFD.</param>
    /// <param name="catalog">Previously validated source identities.</param>
    /// <param name="readOnly">Whether human authoring is disabled.</param>
    public SfdStructureDialog(string workspaceRoot, string projectDirectory,
        SfdDocumentCatalog catalog, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _service = new SfdStructuredAuthoringService(workspaceRoot);
        _review = new SfdCompletionReviewService(workspaceRoot);
        _projectDirectory = projectDirectory;
        _readOnly = readOnly;
        InitializeComponent();
        RequirementComboBox.ItemsSource = catalog.Entries
            .Where(entry => entry.Card.Kind is SfdDocumentKind.UserRequirement or
                SfdDocumentKind.SystemRequirement or
                SfdDocumentKind.RegulatoryRequirement)
            .OrderBy(entry => entry.Card.Reference, StringComparer.Ordinal)
            .Select(entry => new Choice(entry.Card.Id,
                entry.Card.Reference + " — " + entry.Card.Title)).ToArray();
        if (RequirementComboBox.Items.Count > 0)
        {
            RequirementComboBox.SelectedIndex = 0;
        }

        _loading = false;
        Loaded += async (_, _) => await RunSafeAsync(async () =>
        {
            await ReloadRulesAsync();
            await ReloadRequirementAsync();
            await ReloadFindingsAsync();
        });
        UpdateButtons();
    }

    /// <summary>Whether at least one explicit, human-approved document edit was saved.</summary>
    public bool Changed { get; private set; }

    /// <summary>Updates current C/SC selection after changing the requirement.</summary>
    /// <param name="sender">Requirement combo.</param>
    /// <param name="e">Selection change.</param>
    private async void Requirement_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            await RunSafeAsync(async () =>
            {
                await ReloadRequirementAsync();
                await ReloadFindingsAsync();
            });
        }
    }

    /// <summary>Projects the SC children of the selected C.</summary>
    /// <param name="sender">C selector.</param>
    /// <param name="e">Selection change.</param>
    private void Condition_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateSubconditions();
        }
    }

    /// <summary>Refreshes link buttons after a SC selection.</summary>
    /// <param name="sender">SC selector.</param>
    /// <param name="e">Selection change.</param>
    private void Subcondition_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateButtons();
        }
    }

    /// <summary>Refreshes link buttons after selecting a canonical RM.</summary>
    /// <param name="sender">RM selector.</param>
    /// <param name="e">Selection change.</param>
    private void Rule_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateButtons();
        }
    }

    /// <summary>Creates one reviewed C with the mandatory EXU/EXS/EXR formulation.</summary>
    /// <param name="sender">Create condition button.</param>
    /// <param name="e">Click.</param>
    private async void AddCondition_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (_readOnly || _current is null)
            {
                return;
            }

            string pattern = _current.Card.Kind switch
            {
                SfdDocumentKind.UserRequirement =>
                    "Quand [condition], l'utilisateur doit pouvoir [action / résultat].",
                SfdDocumentKind.SystemRequirement =>
                    "Quand [condition], le système doit permettre de [action / résultat].",
                _ => "Quand [condition], le système doit respecter [obligation applicable]."
            };
            TextPromptDialog dialog = new("Nouvelle condition SFD",
                "Saisissez une condition réelle en suivant le patron :\n" + pattern)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SfdConditionBlock created = await _service.AddConditionAsync(
                _projectDirectory, _current.Card.Id, dialog.Value);
            Changed = true;
            await ReloadRequirementAsync(created.Id);
            await ReloadFindingsAsync();
            StatusText.Text = created.Reference + " créée.";
        });

    /// <summary>Creates one reviewed SC under the selected canonical C GUID.</summary>
    /// <param name="sender">Create SC button.</param>
    /// <param name="e">Click.</param>
    private async void AddSubcondition_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (_readOnly || _current is null ||
                ConditionComboBox.SelectedItem is not Choice condition)
            {
                return;
            }

            string prompt = _current.Card.Kind == SfdDocumentKind.UserRequirement
                ? "SC EXU : En tant que [RÔLE], je veux [ACTION] afin de [BUT / BÉNÉFICE] sur [CANAL]. Utilisez [CANAL À CONFIRMER] si inconnu."
                : "Décrivez un comportement précis et vérifiable. Aucun autre patron SC n'est imposé.";
            TextPromptDialog dialog = new("Nouvelle sous-condition SFD", prompt)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SfdSubconditionBlock created = await _service.AddSubconditionAsync(
                _projectDirectory, _current.Card.Id, condition.Id, dialog.Value);
            Changed = true;
            await ReloadRequirementAsync(condition.Id, created.Id);
            await ReloadFindingsAsync();
            StatusText.Text = created.Reference + " créée.";
        });

    /// <summary>Adds one source-backed or explicitly unconfirmed shared RM.</summary>
    /// <param name="sender">Create RM button.</param>
    /// <param name="e">Click.</param>
    private async void AddRule_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (_readOnly)
            {
                return;
            }

            SfdNewBusinessRuleDialog dialog = new() { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SfdBusinessRule created = await _service.AddRuleAsync(
                _projectDirectory, dialog.Statement, dialog.Source,
                dialog.Certainty);
            Changed = true;
            await ReloadRulesAsync(created.Id);
            await ReloadFindingsAsync();
            StatusText.Text = created.Reference + " ajoutée au catalogue commun.";
        });

    /// <summary>Associates an existing canonical RM by GUID; never copies its text.</summary>
    /// <param name="sender">Link button.</param>
    /// <param name="e">Click.</param>
    private async void LinkRule_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (_readOnly || _current is null ||
                SubconditionComboBox.SelectedItem is not Choice subcondition ||
                RuleComboBox.SelectedItem is not Choice rule)
            {
                return;
            }

            await _service.LinkRuleAsync(_projectDirectory, _current.Card.Id,
                subcondition.Id, rule.Id);
            Changed = true;
            Guid? conditionId = (ConditionComboBox.SelectedItem as Choice)?.Id;
            await ReloadRequirementAsync(conditionId, subcondition.Id);
            await ReloadFindingsAsync();
            StatusText.Text = "Référence RM liée à la SC.";
        });

    /// <summary>Unlinks a reference without deleting the canonical shared RM.</summary>
    /// <param name="sender">Unlink button.</param>
    /// <param name="e">Click.</param>
    private async void UnlinkRule_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (_readOnly || _current is null ||
                SubconditionComboBox.SelectedItem is not Choice subcondition ||
                RuleComboBox.SelectedItem is not Choice rule)
            {
                return;
            }

            await _service.UnlinkRuleAsync(_projectDirectory, _current.Card.Id,
                subcondition.Id, rule.Id);
            Changed = true;
            Guid? conditionId = (ConditionComboBox.SelectedItem as Choice)?.Id;
            await ReloadRequirementAsync(conditionId, subcondition.Id);
            await ReloadFindingsAsync();
            StatusText.Text = "Référence RM dissociée ; règle conservée.";
        });

    /// <summary>Refreshes current requirement from its committed Markdown GUID.</summary>
    /// <param name="preferredCondition">Condition to select after creation.</param>
    /// <param name="preferredSubcondition">Subcondition to select after creation.</param>
    /// <returns>Updated C/SC selection.</returns>
    private async Task ReloadRequirementAsync(Guid? preferredCondition = null,
        Guid? preferredSubcondition = null)
    {
        _current = RequirementComboBox.SelectedItem is Choice choice
            ? await _service.ReadRequirementAsync(_projectDirectory, choice.Id)
            : null;
        _loading = true;
        try
        {
            Guid? selected = preferredCondition ??
                (ConditionComboBox.SelectedItem as Choice)?.Id;
            Choice[] conditions = _current?.Blocks
                .Select(block => new Choice(block.Id,
                    block.Reference + " — " + block.Statement)).ToArray() ?? [];
            ConditionComboBox.ItemsSource = conditions;
            ConditionComboBox.SelectedItem = conditions.FirstOrDefault(item =>
                item.Id == selected) ?? conditions.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        UpdateSubconditions(preferredSubcondition);
    }

    /// <summary>Refreshes SC choices from the currently selected C.</summary>
    /// <param name="preferredSubcondition">Optional SC to preserve.</param>
    private void UpdateSubconditions(Guid? preferredSubcondition = null)
    {
        Guid? selected = preferredSubcondition ??
            (SubconditionComboBox.SelectedItem as Choice)?.Id;
        Guid? conditionId = (ConditionComboBox.SelectedItem as Choice)?.Id;
        SfdConditionBlock? parent = _current?.Blocks.SingleOrDefault(item =>
            item.Id == conditionId);
        Choice[] children = parent?.Subconditions
            .Select(item => new Choice(item.Id,
                item.Reference + " — " + item.Statement)).ToArray() ?? [];
        _loading = true;
        try
        {
            SubconditionComboBox.ItemsSource = children;
            SubconditionComboBox.SelectedItem = children.FirstOrDefault(item =>
                item.Id == selected) ?? children.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        UpdateButtons();
    }

    /// <summary>Loads canonical RM choices; never copies their wording to requirement files.</summary>
    /// <param name="preferredRule">Optional RM to select after creation.</param>
    /// <returns>Updated RM selector.</returns>
    private async Task ReloadRulesAsync(Guid? preferredRule = null)
    {
        _rules = await _service.ReadRulesAsync(_projectDirectory);
        Guid? selected = preferredRule ?? (RuleComboBox.SelectedItem as Choice)?.Id;
        Choice[] rules = _rules.Rules
            .Select(rule => new Choice(rule.Id,
                rule.Reference + " — " + rule.Statement)).ToArray();
        _loading = true;
        try
        {
            RuleComboBox.ItemsSource = rules;
            RuleComboBox.SelectedItem = rules.FirstOrDefault(item =>
                item.Id == selected) ?? rules.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        UpdateButtons();
    }

    /// <summary>Reads and displays the up-to-date, non-mutating SFD completion report.</summary>
    /// <returns>Latest finding list.</returns>
    private async Task ReloadFindingsAsync()
    {
        SfdCompletionSnapshot report = await Task.Run(() =>
            _review.ReviewAsync(_projectDirectory));
        FindingsListBox.ItemsSource = report.Findings.Select(item =>
            new FindingChoice(item.Severity + " · " + item.Reference +
                " · " + item.Message)).ToArray();
        StatusText.Text = report.IncompleteCount + " incomplet(s), " +
            report.InvalidCount + " invalide(s)";
    }

    /// <summary>Applies read-only and available-selection guards to authoring buttons.</summary>
    private void UpdateButtons()
    {
        if (AddRuleButton is null)
        {
            return;
        }

        bool enabled = !_readOnly;
        bool hasRequirement = _current is not null;
        bool hasCondition = ConditionComboBox.SelectedItem is Choice;
        bool hasSc = SubconditionComboBox.SelectedItem is Choice;
        bool hasRule = RuleComboBox.SelectedItem is Choice;
        AddConditionButton.IsEnabled = enabled && hasRequirement;
        AddSubconditionButton.IsEnabled = enabled && hasRequirement && hasCondition;
        AddRuleButton.IsEnabled = enabled;
        LinkRuleButton.IsEnabled = enabled && hasRequirement && hasSc && hasRule;
        Guid? selectedRule = (RuleComboBox.SelectedItem as Choice)?.Id;
        Guid? selectedSc = (SubconditionComboBox.SelectedItem as Choice)?.Id;
        bool alreadyLinked = _current?.Blocks.Any(block =>
            block.Subconditions.Any(sc => sc.Id == selectedSc &&
                sc.BusinessRuleIds.Contains(selectedRule ?? Guid.Empty))) == true;
        UnlinkRuleButton.IsEnabled = enabled && alreadyLinked;
    }

    /// <summary>Protects async WPF event handlers against data and revision conflicts.</summary>
    /// <param name="action">Reviewed operation to execute.</param>
    /// <returns>Completed action or an informative error dialog.</returns>
    private async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message,
                "Projet SFD", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
