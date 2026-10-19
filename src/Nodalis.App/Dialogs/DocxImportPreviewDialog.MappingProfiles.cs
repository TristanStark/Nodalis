using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nodalis.App.Reliability;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

namespace Nodalis.App.Dialogs;

public partial class DocxImportPreviewDialog
{
    private DocxMappingProfileStore _mappingProfileStore = null!;
    private DocxMappingProfileCatalog _mappingProfileCatalog = new DocxMappingProfileCatalog();
    private readonly HashSet<int> _manuallyMappedSectionIndexes = [];
    private readonly HashSet<int> _unresolvedProfileConflictIndexes = [];
    private bool _applyingMappingProfile;

    public sealed record ProfileRuleRow(DocxMappingProfileRule Rule, string Label);

    /// <summary>Configures drop-down choices; no mapping is applied without a user action.</summary>
    private void InitializeMappingProfileControls()
    {
        ProfileRuleActionCombo.ItemsSource = Enum.GetValues<DocxMappingAction>();
        ProfileRuleActionCombo.SelectedItem = DocxMappingAction.Map;
        RefreshMappingProfiles(null);
    }

    /// <summary>Loads workspace-local profiles without writing to the workspace.</summary>
    private async void MappingProfilesDialog_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MappingProfilesDialog_Loaded;
        await UiActionGuard.RunAsync(this, "Profils DOCX · chargement", async () =>
        {
            _mappingProfileCatalog = await _mappingProfileStore.LoadAsync();
            RefreshMappingProfiles(null);
            MappingProfileStatusText.Text = _mappingProfileCatalog.Profiles.Count +
                " profil(s) local(aux). Aucun n'est appliqué automatiquement.";
        });
    }

    /// <summary>Refreshes the profile selector after add, duplicate or import.</summary>
    private void RefreshMappingProfiles(Guid? selectId)
    {
        Guid? chosen = selectId ??
            (MappingProfileComboBox.SelectedItem as DocxMappingProfile)?.Id;
        MappingProfileComboBox.ItemsSource = null;
        MappingProfileComboBox.ItemsSource = _mappingProfileCatalog.Profiles;
        MappingProfileComboBox.SelectedItem = _mappingProfileCatalog.Profiles
            .FirstOrDefault(profile => profile.Id == chosen);
    }

    /// <summary>Loads the selected profile into the editable, nonautomatic rules view.</summary>
    private void MappingProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        DocxMappingProfile? profile = SelectedMappingProfile();
        if (ProfileNameBox is null)
        {
            return;
        }

        ProfileNameBox.Text = profile?.Name ?? string.Empty;
        MappingProfileEnabledCheckBox.IsChecked = profile?.Enabled == true;
        RefreshMappingRules();
    }

    /// <summary>Gets the profile currently selected by the user.</summary>
    private DocxMappingProfile? SelectedMappingProfile() =>
        MappingProfileComboBox.SelectedItem as DocxMappingProfile;

    /// <summary>Refreshes rules with human-readable predicates and their status.</summary>
    private void RefreshMappingRules(Guid? selectId = null)
    {
        if (ProfileRulesList is null)
        {
            return;
        }

        List<ProfileRuleRow> rows = SelectedMappingProfile()?.Rules
            .Select((rule, index) => new ProfileRuleRow(rule,
                (index + 1) + ". " +
                (rule.Enabled ? "" : "[DÉSACTIVÉE] ") +
                DocxMappingProfileEngine.DescribeRule(rule)))
            .ToList() ?? [];
        ProfileRulesList.ItemsSource = rows;
        ProfileRulesList.SelectedItem = rows.FirstOrDefault(row => row.Rule.Id == selectId);
    }

    /// <summary>Creates a new named profile in memory, without applying it.</summary>
    private void MappingProfileCreate_Click(object sender, RoutedEventArgs e)
    {
        string name = ProfileNameBox.Text.Trim();
        if (name.Length == 0 || name.Length > 100)
        {
            MappingProfileStatusText.Text = "Saisissez un nom de profil (1 à 100 caractères).";
            return;
        }

        DocxMappingProfile profile = new DocxMappingProfile { Id = Guid.NewGuid(), Name = name };
        if (!TryAcceptProfile(profile, replaceId: null))
        {
            return;
        }

        MappingProfileStatusText.Text = "Profil créé en mémoire. Cliquez sur Enregistrer.";
    }

    /// <summary>Duplicates a profile, giving every rule a new independent identity.</summary>
    private void MappingProfileDuplicate_Click(object sender, RoutedEventArgs e)
    {
        DocxMappingProfile? selected = SelectedMappingProfile();
        if (selected is null)
        {
            return;
        }

        DocxMappingProfile duplicate = selected with
        {
            Id = Guid.NewGuid(),
            Name = selected.Name + " (copie)",
            Rules = selected.Rules.Select(rule => rule with { Id = Guid.NewGuid() }).ToList(),
            ManualOverrides = selected.ManualOverrides.ToList()
        };

        if (TryAcceptProfile(duplicate, replaceId: null))
        {
            MappingProfileStatusText.Text = "Profil dupliqué en mémoire. Cliquez sur Enregistrer.";
        }
    }

    /// <summary>Replaces or adds a validated profile in the current catalog.</summary>
    private bool TryAcceptProfile(DocxMappingProfile profile, Guid? replaceId)
    {
        try
        {
            DocxMappingProfileEngine.Validate(profile);
            List<DocxMappingProfile> profiles = _mappingProfileCatalog.Profiles
                .Where(existing => existing.Id != replaceId)
                .ToList();
            profiles.Add(profile);
            DocxMappingProfileCatalog updated = _mappingProfileCatalog with
            {
                Profiles = profiles
            };
            DocxMappingProfileStore.ValidateCatalog(updated);
            _mappingProfileCatalog = updated;
            RefreshMappingProfiles(profile.Id);
            return true;
        }
        catch (InvalidDataException exception)
        {
            MappingProfileStatusText.Text = "Profil refusé : " + exception.Message;
            return false;
        }
    }

    /// <summary>Persists the selected profile and all profiles atomically.</summary>
    private async void MappingProfileSave_Click(object sender, RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(this, "Profils DOCX · enregistrer", async () =>
        {
            DocxMappingProfile? selected = SelectedMappingProfile();
            if (selected is not null)
            {
                DocxMappingProfile renamed = selected with
                {
                    Name = ProfileNameBox.Text.Trim(),
                    Enabled = MappingProfileEnabledCheckBox.IsChecked == true
                };
                if (!TryAcceptProfile(renamed, selected.Id))
                {
                    return;
                }
            }

            await _mappingProfileStore.SaveAsync(_mappingProfileCatalog);
            MappingProfileStatusText.Text =
                "Profils enregistrés dans Templates/" + DocxMappingProfileStore.FileName;
        });
    }

    /// <summary>Imports one validated portable JSON profile with a new local identity.</summary>
    private async void MappingProfileImport_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog picker = new OpenFileDialog
        {
            Filter = "Profil DOCX JSON (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        await UiActionGuard.RunAsync(this, "Profils DOCX · importer", async () =>
        {
            DocxMappingProfile incoming =
                await DocxMappingProfileStore.ImportProfileAsync(picker.FileName);
            DocxMappingProfile copy = incoming with
            {
                Id = Guid.NewGuid(),
                Rules = incoming.Rules.Select(rule => rule with { Id = Guid.NewGuid() }).ToList()
            };
            if (TryAcceptProfile(copy, replaceId: null))
            {
                MappingProfileStatusText.Text = "Profil JSON importé. Enregistrez pour le conserver.";
            }
        });
    }

    /// <summary>Exports only the selected portable profile after explicit user choice.</summary>
    private async void MappingProfileExport_Click(object sender, RoutedEventArgs e)
    {
        DocxMappingProfile? selected = SelectedMappingProfile();
        if (selected is null)
        {
            return;
        }

        SaveFileDialog picker = new SaveFileDialog
        {
            Filter = "Profil DOCX JSON (*.json)|*.json",
            FileName = "profil-docx.json",
            OverwritePrompt = true
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        await UiActionGuard.RunAsync(this, "Profils DOCX · exporter", async () =>
        {
            await DocxMappingProfileStore.ExportProfileAsync(picker.FileName, selected);
            MappingProfileStatusText.Text = "Profil exporté vers " + picker.FileName;
        });
    }

    /// <summary>Fills the rule form from the selected rule row.</summary>
    private void MappingProfileRule_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileRulesList?.SelectedItem is not ProfileRuleRow entry)
        {
            return;
        }

        DocxMappingProfileRule rule = entry.Rule;
        ProfileRuleHeadingBox.Text = rule.HeadingText ?? string.Empty;
        ProfileRuleStyleBox.Text = rule.WordStyleId ?? string.Empty;
        ProfileRuleLevelBox.Text = rule.HeadingLevel?.ToString() ?? string.Empty;
        ProfileRuleParentBox.Text = rule.ParentPathContains ?? string.Empty;
        ProfileRulePatternBox.Text = rule.TextPattern ?? string.Empty;
        ProfileRuleDestinationBox.Text = rule.TargetSection ?? string.Empty;
        ProfileRuleActionCombo.SelectedItem = rule.Action;
        ProfileRuleEnabledCheckBox.IsChecked = rule.Enabled;
    }

    /// <summary>Creates a bounded rule from the visible editor fields.</summary>
    private DocxMappingProfileRule ReadRule(Guid id)
    {
        string levelText = ProfileRuleLevelBox.Text.Trim();
        if (levelText.Length > 0 &&
            (!int.TryParse(levelText, out int checkLevel) || checkLevel is < 1 or > 9))
        {
            throw new InvalidDataException("Le niveau Word doit être compris entre 1 et 9.");
        }

        DocxMappingAction action = ProfileRuleActionCombo.SelectedItem is DocxMappingAction selected
            ? selected
            : DocxMappingAction.Map;

        return new DocxMappingProfileRule
        {
            Id = id,
            Enabled = ProfileRuleEnabledCheckBox.IsChecked == true,
            HeadingText = NullableText(ProfileRuleHeadingBox.Text),
            WordStyleId = NullableText(ProfileRuleStyleBox.Text),
            HeadingLevel = levelText.Length == 0 ? null : int.Parse(levelText),
            ParentPathContains = NullableText(ProfileRuleParentBox.Text),
            TextPattern = NullableText(ProfileRulePatternBox.Text),
            Action = action,
            TargetSection = action == DocxMappingAction.Ignore
                ? null
                : NullableText(ProfileRuleDestinationBox.Text)
        };
    }

    private static string? NullableText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Adds a new rule at the end of the ordered profile.</summary>
    private void MappingProfileRuleAdd_Click(object sender, RoutedEventArgs e) =>
        UpdateRuleSelection(add: true);

    /// <summary>Updates the currently selected rule without changing its priority.</summary>
    private void MappingProfileRuleUpdate_Click(object sender, RoutedEventArgs e) =>
        UpdateRuleSelection(add: false);

    /// <summary>Validates and replaces an edited profile in memory.</summary>
    private void UpdateRuleSelection(bool add)
    {
        DocxMappingProfile? profile = SelectedMappingProfile();
        if (profile is null)
        {
            MappingProfileStatusText.Text = "Créez ou sélectionnez un profil.";
            return;
        }

        Guid? existing = (ProfileRulesList.SelectedItem as ProfileRuleRow)?.Rule.Id;
        if (!add && existing is null)
        {
            MappingProfileStatusText.Text = "Sélectionnez une règle à modifier.";
            return;
        }

        try
        {
            DocxMappingProfileRule rule = ReadRule(add ? Guid.NewGuid() : existing!.Value);
            List<DocxMappingProfileRule> rules = profile.Rules.ToList();
            if (add)
            {
                rules.Add(rule);
            }
            else
            {
                int index = rules.FindIndex(candidate => candidate.Id == existing);
                rules[index] = rule;
            }

            if (TryAcceptProfile(profile with { Rules = rules }, profile.Id))
            {
                RefreshMappingRules(rule.Id);
                MappingProfileStatusText.Text = "Règle actualisée. Enregistrez le profil.";
            }
        }
        catch (InvalidDataException exception)
        {
            MappingProfileStatusText.Text = "Règle refusée : " + exception.Message;
        }
    }

    /// <summary>Switches the enabled state of the selected rule.</summary>
    private void MappingProfileRuleToggle_Click(object sender, RoutedEventArgs e)
    {
        DocxMappingProfile? profile = SelectedMappingProfile();
        if (profile is null || ProfileRulesList.SelectedItem is not ProfileRuleRow selected)
        {
            return;
        }

        DocxMappingProfileRule updated = selected.Rule with { Enabled = !selected.Rule.Enabled };
        List<DocxMappingProfileRule> rules = profile.Rules
            .Select(rule => rule.Id == updated.Id ? updated : rule).ToList();
        if (TryAcceptProfile(profile with { Rules = rules }, profile.Id))
        {
            RefreshMappingRules(updated.Id);
            MappingProfileStatusText.Text = "Règle activée/désactivée. Enregistrez.";
        }
    }

    /// <summary>Removes the selected rule after an explicit action.</summary>
    private void MappingProfileRuleRemove_Click(object sender, RoutedEventArgs e)
    {
        DocxMappingProfile? profile = SelectedMappingProfile();
        if (profile is null || ProfileRulesList.SelectedItem is not ProfileRuleRow selected)
        {
            return;
        }

        List<DocxMappingProfileRule> rules = profile.Rules
            .Where(rule => rule.Id != selected.Rule.Id).ToList();
        if (TryAcceptProfile(profile with { Rules = rules }, profile.Id))
        {
            MappingProfileStatusText.Text = "Règle supprimée en mémoire. Enregistrez.";
        }
    }

    /// <summary>Gets the source heading, style and ancestor path for matching.</summary>
    private DocxMappingSourceSection DescribeSourceSection(SectionRow row)
    {
        int? sourceIndex = row.Preview.HeadingBlockIndex;
        if (sourceIndex is not int current ||
            current < 0 || current >= _preview.StagedImport.Document.Blocks.Count)
        {
            return new DocxMappingSourceSection { Heading = row.SourceHeading };
        }

        IReadOnlyList<DocxBlock> blocks = _preview.StagedImport.Document.Blocks;
        DocxParagraph? heading = blocks[current].Paragraph;
        if (heading is null)
        {
            return new DocxMappingSourceSection { Heading = row.SourceHeading };
        }

        List<(int Level, string Text)> ancestry = [];
        for (int index = 0; index < current; index++)
        {
            DocxParagraph? earlier = blocks[index].Paragraph;
            if (earlier?.HeadingLevel is not int level || string.IsNullOrWhiteSpace(earlier.Text))
            {
                continue;
            }

            while (ancestry.Count > 0 && ancestry[^1].Level >= level)
            {
                ancestry.RemoveAt(ancestry.Count - 1);
            }

            ancestry.Add((level, earlier.Text));
        }

        if (heading.HeadingLevel is int ownLevel)
        {
            ancestry.RemoveAll(entry => entry.Level >= ownLevel);
        }

        return new DocxMappingSourceSection
        {
            Heading = row.SourceHeading,
            WordStyleId = heading.StyleId,
            HeadingLevel = heading.HeadingLevel,
            ParentPath = string.Join(" / ", ancestry.Select(entry => entry.Text))
        };
    }

    /// <summary>Evaluates and displays every source section with explainable reasons.</summary>
    private List<(SectionRow Row, DocxMappingDecision Decision)> EvaluateMappingProfile()
    {
        DocxMappingProfile? profile = SelectedMappingProfile();
        if (profile is null)
        {
            MappingProfileReportBox.Text = "Sélectionnez d'abord un profil.";
            return [];
        }

        List<(SectionRow Row, DocxMappingDecision Decision)> results = [];
        List<string> lines = [];
        foreach (SectionRow section in _sections)
        {
            DocxMappingDecision decision = DocxMappingProfileEngine.Evaluate(
                profile, DescribeSourceSection(section));
            results.Add((section, decision));
            string action = decision.HasConflict
                ? "CONFLIT (résolution obligatoire)"
                : !decision.Matched
                    ? "Aucune règle"
                    : decision.Action == DocxMappingAction.Ignore
                        ? "Ignorer"
                        : decision.TargetSection ?? "Sans destination";
            lines.Add(section.SourceHeading + " → " + action +
                (_manuallyMappedSectionIndexes.Contains(section.Index)
                    ? " (correction locale conservée)" : "") +
                (decision.Reasons.Count > 0 ? "\n  " + string.Join("; ", decision.Reasons) : ""));
        }

        MappingProfileReportBox.Text = string.Join("\n", lines);
        return results;
    }

    /// <summary>Previews mapping without changing any section.</summary>
    private void MappingProfilePreview_Click(object sender, RoutedEventArgs e) =>
        EvaluateMappingProfile();

    /// <summary>Applies only conflict-free suggestions, preserving local manual decisions.</summary>
    private void MappingProfileApply_Click(object sender, RoutedEventArgs e)
    {
        List<(SectionRow Row, DocxMappingDecision Decision)> results = EvaluateMappingProfile();
        _unresolvedProfileConflictIndexes.Clear();
        _applyingMappingProfile = true;
        try
        {
            foreach ((SectionRow row, DocxMappingDecision decision) in results)
            {
                if (_manuallyMappedSectionIndexes.Contains(row.Index) || !row.Include)
                {
                    continue;
                }

                if (decision.HasConflict)
                {
                    _unresolvedProfileConflictIndexes.Add(row.Index);
                    continue;
                }

                if (!decision.Matched)
                {
                    continue;
                }

                if (decision.Action == DocxMappingAction.Ignore)
                {
                    row.SelectionRoot.IsSelected = false;
                }
                else if (decision.TargetSection is string target)
                {
                    row.TargetSection = target;
                    _customMappingSections.Add(target);
                }
            }
        }
        finally
        {
            _applyingMappingProfile = false;
        }

        RefreshMappingDestinations();
        InvalidatePlan();
        MappingProfileStatusText.Text = _unresolvedProfileConflictIndexes.Count == 0
            ? "Propositions appliquées à l'aperçu ; vérifiez le plan avant import."
            : "Règles en conflit sur " + _unresolvedProfileConflictIndexes.Count +
              " section(s) : aucun import possible sans correction manuelle.";
    }

    /// <summary>Persists only explicit manual choices into the selected portable profile.</summary>
    private async void MappingProfileRemember_Click(object sender, RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(this, "Profils DOCX · mémoriser", async () =>
        {
            DocxMappingProfile? profile = SelectedMappingProfile();
            if (profile is null)
            {
                MappingProfileStatusText.Text = "Sélectionnez un profil.";
                return;
            }

            DocxMappingProfile updated = profile;
            foreach (SectionRow row in _sections.Where(row =>
                _manuallyMappedSectionIndexes.Contains(row.Index)))
            {
                updated = DocxMappingProfileEngine.SaveOverride(
                    updated, DescribeSourceSection(row),
                    row.Include ? DocxMappingAction.Map : DocxMappingAction.Ignore,
                    row.Include ? row.TargetSection : null);
            }

            if (!TryAcceptProfile(updated, profile.Id))
            {
                return;
            }

            await _mappingProfileStore.SaveAsync(_mappingProfileCatalog);
            MappingProfileStatusText.Text =
                "Arbitrages manuels mémorisés dans le profil local.";
        });
    }

    /// <summary>Disables unresolved profile proposals and returns to purely manual mapping.</summary>
    private void MappingProfileClear_Click(object sender, RoutedEventArgs e)
    {
        _unresolvedProfileConflictIndexes.Clear();
        MappingProfileReportBox.Text = "Profil désactivé pour ce document. Affectations courantes à vérifier.";
        MappingProfileStatusText.Text = "Aucune règle de profil ne bloque désormais le plan.";
        InvalidatePlan();
    }
}
