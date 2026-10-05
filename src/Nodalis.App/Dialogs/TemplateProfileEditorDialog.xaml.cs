using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Markdown;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Domain;
using Nodalis.Core.Templates;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Provides a workspace-level editor for Markdown templates and initial
/// project profiles while keeping the files in Templates as canonical data.
/// </summary>
public partial class TemplateProfileEditorDialog : Window
{
    private readonly ITemplateStore _templateStore;

    private List<MarkdownTemplateDefinition> _templates = [];
    private ProjectProfileCatalog? _profileCatalog;
    private List<ProfileSectionRow> _profileSections = [];
    private bool _loadingTemplate;
    private bool _loadingProfile;
    private bool _updatingPreviewVariables;

    /// <summary>
    /// Initializes a new instance of <see cref="TemplateProfileEditorDialog"/>.
    /// </summary>
    /// <param name="templateStore">The canonical workspace template store.</param>
    public TemplateProfileEditorDialog(
            ITemplateStore templateStore)
    {
        ArgumentNullException.ThrowIfNull(
            templateStore);

        _templateStore =
            templateStore;

        InitializeComponent();

        Loaded += async (_, _) =>
            await InitializeAsync();
    }

    /// <summary>
    /// Gets a value indicating whether canonical template/profile files changed.
    /// </summary>
    public bool Changed { get; private set; }

    /// <summary>
    /// Loads template and project-profile catalogs into the editor.
    /// </summary>
    /// <returns>A task representing initialization.</returns>
    private async Task InitializeAsync()
    {
        try
        {
            await ReloadTemplatesAsync(
                selectedKey: null);
            await ReloadProfilesAsync(
                selectedComplexity: null);
        }
        catch (Exception exception) when (
            IsExpectedTemplateException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Reloads template metadata and source-backed selection choices.
    /// </summary>
    /// <param name="selectedKey">The key to reselect after the reload.</param>
    /// <returns>A task representing the reload.</returns>
    private async Task ReloadTemplatesAsync(
            string? selectedKey)
    {
        TemplateCatalog catalog =
            await _templateStore.LoadTemplateCatalogAsync();

        _templates =
            catalog.Templates
                .OrderBy(template =>
                    template.Category,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(template =>
                    template.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        _loadingTemplate =
            true;

        TemplatesList.ItemsSource =
            _templates;

        List<TemplateChoice> choices =
            new List<TemplateChoice>
            {
                new TemplateChoice(
                    "Aucun template",
                    null)
            };

        choices.AddRange(
            _templates.Select(template =>
                new TemplateChoice(
                    $"{template.Category} · {template.DisplayName}",
                    template.Key)));

        ProfileSectionTemplateComboBox.ItemsSource =
            choices;

        MarkdownTemplateDefinition? selection =
            selectedKey is null
                ? _templates.FirstOrDefault()
                : _templates.FirstOrDefault(template =>
                    string.Equals(
                        template.Key,
                        selectedKey,
                        StringComparison.OrdinalIgnoreCase));

        TemplatesList.SelectedItem =
            selection ??
            _templates.FirstOrDefault();

        MarkdownTemplateDefinition? selected =
            TemplatesList.SelectedItem as
            MarkdownTemplateDefinition;

        _loadingTemplate =
            false;

        if (selected is not null)
        {
            TemplatesList.ScrollIntoView(
                selected);

            await LoadTemplateAsync(
                selected);
        }
    }

    /// <summary>
    /// Loads one template source and metadata into the edit controls.
    /// </summary>
    /// <param name="definition">The selected template definition.</param>
    /// <returns>A task representing the load.</returns>
    private async Task LoadTemplateAsync(
            MarkdownTemplateDefinition definition)
    {
        _loadingTemplate =
            true;

        try
        {
            TemplateDisplayNameTextBox.Text =
                definition.DisplayName;
            TemplateCategoryTextBox.Text =
                definition.Category;
            TemplateDefaultFileNameTextBox.Text =
                definition.DefaultFileName;
            TemplateKeyTextBox.Text =
                definition.Key;

            TemplateContentTextBox.Text =
                await _templateStore.LoadTemplateContentAsync(
                    definition.Key);

            PreviewVariablesTextBox.Text =
                BuildPreviewVariableText(
                    TemplateContentTextBox.Text,
                    TemplateDefaultFileNameTextBox.Text);

            TemplateStatusText.Text =
                $"{definition.FileName} · configuration canonique dans Templates";
        }
        finally
        {
            _loadingTemplate =
                false;
        }

        RenderTemplatePreview();
    }

    /// <summary>
    /// Reloads the project-profile catalog and reselects one complexity.
    /// </summary>
    /// <param name="selectedComplexity">The complexity to reselect.</param>
    /// <returns>A task representing the reload.</returns>
    private async Task ReloadProfilesAsync(
            ProjectComplexity? selectedComplexity)
    {
        _profileCatalog =
            await _templateStore.LoadProjectProfilesAsync();

        List<ProjectProfileDefinition> profiles =
            _profileCatalog.Profiles
                .OrderBy(profile =>
                    profile.Complexity)
                .ToList();

        _loadingProfile =
            true;

        ProfilesComboBox.ItemsSource =
            profiles;

        ProjectProfileDefinition? selection =
            selectedComplexity is null
                ? profiles.FirstOrDefault()
                : profiles.FirstOrDefault(profile =>
                    profile.Complexity ==
                    selectedComplexity.Value);

        ProfilesComboBox.SelectedItem =
            selection ??
            profiles.FirstOrDefault();

        _loadingProfile =
            false;

        LoadSelectedProfile();
    }

    /// <summary>
    /// Copies the selected profile into editable UI rows.
    /// </summary>
    private void LoadSelectedProfile()
    {
        if (ProfilesComboBox.SelectedItem is not
            ProjectProfileDefinition profile)
        {
            _profileSections =
                [];
            ProfileSectionsList.ItemsSource =
                null;
            return;
        }

        _loadingProfile =
            true;

        try
        {
            ProfileDisplayNameTextBox.Text =
                profile.DisplayName;

            _profileSections =
                profile.Sections
                    .OrderBy(section =>
                        section.Order)
                    .Select(section =>
                        new ProfileSectionRow(
                            section.Name,
                            section.TemplateKey,
                            section.IsSingleton))
                    .ToList();

            ProfileSectionsList.ItemsSource =
                _profileSections;
            ProfileSectionsList.SelectedIndex =
                _profileSections.Count > 0
                    ? 0
                    : -1;

            RefreshSelectedProfileSection();
            ProfileStatusText.Text =
                $"{profile.Complexity} · {_profileSections.Count} section(s) initiale(s)";
        }
        finally
        {
            _loadingProfile =
                false;
        }
    }

    /// <summary>
    /// Synchronizes section edit controls with the selected profile row.
    /// </summary>
    private void RefreshSelectedProfileSection()
    {
        ProfileSectionRow? row =
            ProfileSectionsList.SelectedItem as
            ProfileSectionRow;

        if (row is null)
        {
            ProfileSectionNameTextBox.Text =
                string.Empty;
            ProfileSectionSingletonCheckBox.IsChecked =
                false;
            ProfileSectionTemplateComboBox.SelectedIndex =
                0;
            return;
        }

        ProfileSectionNameTextBox.Text =
            row.Name;
        ProfileSectionSingletonCheckBox.IsChecked =
            row.IsSingleton;

        IEnumerable<TemplateChoice> choices =
            ProfileSectionTemplateComboBox.Items
                .Cast<TemplateChoice>();

        TemplateChoice? selectedChoice =
            choices.FirstOrDefault(choice =>
                string.Equals(
                    choice.Key,
                    row.TemplateKey,
                    StringComparison.OrdinalIgnoreCase));

        ProfileSectionTemplateComboBox.SelectedItem =
            selectedChoice ??
            choices.FirstOrDefault();
    }

    /// <summary>
    /// Handles a template selection and loads its Markdown source.
    /// </summary>
    /// <param name="sender">The template list.</param>
    /// <param name="e">The selection event.</param>
    private async void TemplatesList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (_loadingTemplate ||
            TemplatesList.SelectedItem is not
                MarkdownTemplateDefinition definition)
        {
            return;
        }

        try
        {
            await LoadTemplateAsync(
                definition);
        }
        catch (Exception exception) when (
            IsExpectedTemplateException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Rebuilds variable suggestions and preview after editable template fields change.
    /// </summary>
    /// <param name="sender">The changed text box.</param>
    /// <param name="e">The text change event.</param>
    private void TemplateField_TextChanged(
            object sender,
            TextChangedEventArgs e)
    {
        if (_loadingTemplate)
        {
            return;
        }

        EnsurePreviewVariables();
        RenderTemplatePreview();
    }

    /// <summary>
    /// Refreshes the preview after sample variable values change.
    /// </summary>
    /// <param name="sender">The variables text box.</param>
    /// <param name="e">The text change event.</param>
    private void PreviewVariablesTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
    {
        if (_loadingTemplate ||
            _updatingPreviewVariables)
        {
            return;
        }

        RenderTemplatePreview();
    }

    /// <summary>
    /// Saves the selected template after rendering and file-name validation.
    /// </summary>
    /// <param name="sender">The save button.</param>
    /// <param name="e">The routed event.</param>
    private async void SaveTemplate_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (TemplatesList.SelectedItem is not
            MarkdownTemplateDefinition current)
        {
            return;
        }

        try
        {
            MarkdownTemplateDefinition updated =
                BuildValidatedTemplateDefinition(
                    current);

            IReadOnlyDictionary<string, string> variables =
                ParsePreviewVariables();

            MarkdownTemplateRenderer.Render(
                TemplateContentTextBox.Text,
                variables);

            string renderedFileName =
                MarkdownTemplateRenderer.Render(
                    updated.DefaultFileName,
                    variables);

            ValidateRenderedFileName(
                renderedFileName);

            await _templateStore.SaveTemplateAsync(
                updated,
                TemplateContentTextBox.Text);

            Changed =
                true;

            await ReloadTemplatesAsync(
                updated.Key);
            TemplateStatusText.Text =
                "Template enregistré dans les fichiers canoniques.";
        }
        catch (Exception exception) when (
            IsExpectedTemplateException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Duplicates the selected template into a new canonical Markdown file.
    /// </summary>
    /// <param name="sender">The duplicate button.</param>
    /// <param name="e">The routed event.</param>
    private async void DuplicateTemplate_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (TemplatesList.SelectedItem is not
            MarkdownTemplateDefinition current)
        {
            return;
        }

        TextPromptDialog prompt =
            new TextPromptDialog(
                "Dupliquer le template",
                "Nom du nouveau template :",
                current.DisplayName + " — copie")
            {
                Owner =
                    this
            };

        if (prompt.ShowDialog() !=
                true ||
            string.IsNullOrWhiteSpace(
                prompt.Value))
        {
            return;
        }

        try
        {
            MarkdownTemplateDefinition duplicate =
                await _templateStore.DuplicateTemplateAsync(
                    current.Key,
                    prompt.Value);

            Changed =
                true;

            await ReloadTemplatesAsync(
                duplicate.Key);
            TemplateStatusText.Text =
                $"Template « {duplicate.DisplayName} » créé.";
        }
        catch (Exception exception) when (
            IsExpectedTemplateException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Restores the selected built-in template to the shipped definition and source.
    /// </summary>
    /// <param name="sender">The restore button.</param>
    /// <param name="e">The routed event.</param>
    private async void RestoreTemplate_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (TemplatesList.SelectedItem is not
            MarkdownTemplateDefinition current)
        {
            return;
        }

        MessageBoxResult answer =
            MessageBox.Show(
                this,
                $"Restaurer « {current.DisplayName} » avec son contenu livré par Nodalis ?\n\nLes modifications locales de ce template seront remplacées.",
                "Restaurer le template",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (answer !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _templateStore.RestoreTemplateDefaultAsync(
                current.Key);

            Changed =
                true;

            await ReloadTemplatesAsync(
                current.Key);
            TemplateStatusText.Text =
                "Valeurs par défaut restaurées.";
        }
        catch (KeyNotFoundException)
        {
            MessageBox.Show(
                this,
                "Ce template est personnalisé et n'a pas de valeur par défaut intégrée.",
                "Restaurer le template",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            IsExpectedTemplateException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Switches the editable section list to another project profile.
    /// </summary>
    /// <param name="sender">The profile selector.</param>
    /// <param name="e">The selection event.</param>
    private void ProfilesComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (_loadingProfile)
        {
            return;
        }

        LoadSelectedProfile();
    }

    /// <summary>
    /// Synchronizes section fields when another profile section is selected.
    /// </summary>
    /// <param name="sender">The section list.</param>
    /// <param name="e">The selection event.</param>
    private void ProfileSectionsList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (_loadingProfile)
        {
            return;
        }

        RefreshSelectedProfileSection();
    }

    /// <summary>
    /// Adds a new editable section to the current profile.
    /// </summary>
    /// <param name="sender">The add button.</param>
    /// <param name="e">The routed event.</param>
    private void AddProfileSection_Click(
            object sender,
            RoutedEventArgs e)
    {
        string baseName =
            "Nouvelle section";
        string name =
            baseName;
        int suffix =
            2;

        while (_profileSections.Any(section =>
                   string.Equals(
                       section.Name,
                       name,
                       StringComparison.OrdinalIgnoreCase)))
        {
            name =
                $"{baseName} {suffix}";
            suffix++;
        }

        ProfileSectionRow row =
            new ProfileSectionRow(
                name,
                null,
                isSingleton: false);

        _profileSections.Add(
            row);
        ProfileSectionsList.Items.Refresh();
        ProfileSectionsList.SelectedItem =
            row;
        ProfileSectionsList.ScrollIntoView(
            row);
    }

    /// <summary>
    /// Removes the selected section from the current editable profile.
    /// </summary>
    /// <param name="sender">The remove button.</param>
    /// <param name="e">The routed event.</param>
    private void RemoveProfileSection_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (ProfileSectionsList.SelectedItem is not
            ProfileSectionRow row)
        {
            return;
        }

        int index =
            _profileSections.IndexOf(
                row);

        _profileSections.Remove(
            row);
        ProfileSectionsList.Items.Refresh();

        if (_profileSections.Count > 0)
        {
            ProfileSectionsList.SelectedIndex =
                Math.Min(
                    index,
                    _profileSections.Count - 1);
        }
        else
        {
            RefreshSelectedProfileSection();
        }
    }

    /// <summary>
    /// Moves the selected profile section one position upward.
    /// </summary>
    /// <param name="sender">The move button.</param>
    /// <param name="e">The routed event.</param>
    private void MoveProfileSectionUp_Click(
            object sender,
            RoutedEventArgs e) =>
        MoveSelectedProfileSection(
            -1);

    /// <summary>
    /// Moves the selected profile section one position downward.
    /// </summary>
    /// <param name="sender">The move button.</param>
    /// <param name="e">The routed event.</param>
    private void MoveProfileSectionDown_Click(
            object sender,
            RoutedEventArgs e) =>
        MoveSelectedProfileSection(
            1);

    /// <summary>
    /// Reorders the selected profile section by one position.
    /// </summary>
    /// <param name="delta">The requested position delta.</param>
    private void MoveSelectedProfileSection(
            int delta)
    {
        if (ProfileSectionsList.SelectedItem is not
            ProfileSectionRow row)
        {
            return;
        }

        int index =
            _profileSections.IndexOf(
                row);
        int target =
            index + delta;

        if (target < 0 ||
            target >=
            _profileSections.Count)
        {
            return;
        }

        _profileSections.RemoveAt(
            index);
        _profileSections.Insert(
            target,
            row);

        ProfileSectionsList.Items.Refresh();
        ProfileSectionsList.SelectedItem =
            row;
        ProfileSectionsList.ScrollIntoView(
            row);
    }

    /// <summary>
    /// Applies section field values to the selected editable profile row.
    /// </summary>
    /// <param name="sender">The apply button.</param>
    /// <param name="e">The routed event.</param>
    private void ApplyProfileSection_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (ProfileSectionsList.SelectedItem is not
            ProfileSectionRow row)
        {
            return;
        }

        string name =
            ProfileSectionNameTextBox.Text.Trim();

        if (name.Length ==
            0)
        {
            MessageBox.Show(
                this,
                "Le nom de la section est requis.",
                "Profil projet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        TemplateChoice? template =
            ProfileSectionTemplateComboBox.SelectedItem as
            TemplateChoice;

        row.Name =
            name;
        row.TemplateKey =
            template?.Key;
        row.IsSingleton =
            ProfileSectionSingletonCheckBox.IsChecked ==
            true;

        ProfileSectionsList.Items.Refresh();
        ProfileSectionsList.SelectedItem =
            row;
    }

    /// <summary>
    /// Saves the selected project profile after local validation.
    /// </summary>
    /// <param name="sender">The save button.</param>
    /// <param name="e">The routed event.</param>
    private async void SaveProfile_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (_profileCatalog is null ||
            ProfilesComboBox.SelectedItem is not
                ProjectProfileDefinition current)
        {
            return;
        }

        try
        {
            ProjectProfileDefinition updated =
                BuildValidatedProjectProfile(
                    current.Complexity);

            List<ProjectProfileDefinition> profiles =
                _profileCatalog.Profiles
                    .Select(profile =>
                        profile.Complexity ==
                        updated.Complexity
                            ? updated
                            : profile)
                    .ToList();

            ProjectProfileCatalog updatedCatalog =
                _profileCatalog with
                {
                    Profiles =
                        profiles
                };

            await _templateStore.SaveProjectProfilesAsync(
                updatedCatalog);

            Changed =
                true;

            await ReloadProfilesAsync(
                updated.Complexity);
            ProfileStatusText.Text =
                "Profil enregistré dans project-profiles.json.";
        }
        catch (Exception exception) when (
            IsExpectedTemplateException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Restores the selected built-in Simple, Medium or Complex profile.
    /// </summary>
    /// <param name="sender">The restore button.</param>
    /// <param name="e">The routed event.</param>
    private async void RestoreProfile_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (ProfilesComboBox.SelectedItem is not
            ProjectProfileDefinition current)
        {
            return;
        }

        MessageBoxResult answer =
            MessageBox.Show(
                this,
                $"Restaurer le profil « {current.DisplayName} » avec la structure livrée par Nodalis ?",
                "Restaurer le profil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (answer !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _templateStore.RestoreProjectProfileDefaultAsync(
                current.Complexity);

            Changed =
                true;

            await ReloadProfilesAsync(
                current.Complexity);
            ProfileStatusText.Text =
                "Profil par défaut restauré.";
        }
        catch (Exception exception) when (
            IsExpectedTemplateException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Creates a validated immutable template definition from the editor.
    /// </summary>
    /// <param name="current">The currently selected canonical definition.</param>
    /// <returns>The updated template definition.</returns>
    private MarkdownTemplateDefinition BuildValidatedTemplateDefinition(
            MarkdownTemplateDefinition current)
    {
        string displayName =
            TemplateDisplayNameTextBox.Text.Trim();
        string category =
            TemplateCategoryTextBox.Text.Trim();
        string defaultFileName =
            TemplateDefaultFileNameTextBox.Text.Trim();

        if (displayName.Length ==
                0 ||
            category.Length ==
                0 ||
            defaultFileName.Length ==
                0)
        {
            throw new InvalidDataException(
                "Le nom, la catégorie et le nom de fichier généré sont requis.");
        }

        return current with
        {
            DisplayName =
                displayName,
            Category =
                category,
            DefaultFileName =
                defaultFileName
        };
    }

    /// <summary>
    /// Creates a validated project profile from the current editable rows.
    /// </summary>
    /// <param name="complexity">The stable profile complexity.</param>
    /// <returns>The updated immutable project profile.</returns>
    private ProjectProfileDefinition BuildValidatedProjectProfile(
            ProjectComplexity complexity)
    {
        string displayName =
            ProfileDisplayNameTextBox.Text.Trim();

        if (displayName.Length ==
            0)
        {
            throw new InvalidDataException(
                "Le nom affiché du profil est requis.");
        }

        if (_profileSections.Count ==
            0)
        {
            throw new InvalidDataException(
                "Un profil doit contenir au moins une section.");
        }

        if (_profileSections.Any(section =>
                string.IsNullOrWhiteSpace(
                    section.Name)))
        {
            throw new InvalidDataException(
                "Toutes les sections doivent avoir un nom.");
        }

        global::System.Linq.IGrouping<string, ProfileSectionRow>? duplicate =
            _profileSections
                .GroupBy(
                    section => section.Name.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group =>
                    group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"La section « {duplicate.Key} » apparaît plusieurs fois.");
        }

        List<ProjectSectionTemplateDefinition> sections =
            _profileSections
                .Select((section, index) =>
                    new ProjectSectionTemplateDefinition
                    {
                        Name =
                            section.Name.Trim(),
                        Order =
                            (index + 1) * 10,
                        IsSingleton =
                            section.IsSingleton,
                        TemplateKey =
                            string.IsNullOrWhiteSpace(
                                section.TemplateKey)
                                ? null
                                : section.TemplateKey
                    })
                .ToList();

        return new ProjectProfileDefinition
        {
            Complexity =
                complexity,
            DisplayName =
                displayName,
            Sections =
                sections
        };
    }

    /// <summary>
    /// Ensures every variable referenced by the current template has a preview value.
    /// </summary>
    private void EnsurePreviewVariables()
    {
        IReadOnlyDictionary<string, string> existing =
            ParsePreviewVariables(
                throwOnInvalidLine: false);

        string[] names =
            GetTemplateVariableNames(
                TemplateContentTextBox.Text,
                TemplateDefaultFileNameTextBox.Text);

        global::System.Collections.Generic.Dictionary<string, string> merged =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (string name in
                 names)
        {
            merged[name] =
                existing.TryGetValue(
                    name,
                    out string? value)
                    ? value
                    : GetDefaultPreviewValue(
                        name);
        }

        string updatedText =
            string.Join(
                Environment.NewLine,
                merged
                    .OrderBy(pair =>
                        pair.Key,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(pair =>
                        $"{pair.Key}={pair.Value}"));

        if (string.Equals(
                PreviewVariablesTextBox.Text,
                updatedText,
                StringComparison.Ordinal))
        {
            return;
        }

        _updatingPreviewVariables =
            true;

        try
        {
            PreviewVariablesTextBox.Text =
                updatedText;
        }
        finally
        {
            _updatingPreviewVariables =
                false;
        }
    }

    /// <summary>
    /// Builds initial preview values for all variables referenced by a template.
    /// </summary>
    /// <param name="content">The Markdown content.</param>
    /// <param name="defaultFileName">The generated-file-name pattern.</param>
    /// <returns>One name=value pair per line.</returns>
    private static string BuildPreviewVariableText(
            string content,
            string defaultFileName)
    {
        string[] names =
            GetTemplateVariableNames(
                content,
                defaultFileName);

        return string.Join(
            Environment.NewLine,
            names.Select(name =>
                $"{name}={GetDefaultPreviewValue(name)}"));
    }

    /// <summary>
    /// Extracts unique template variables from Markdown and file-name patterns.
    /// </summary>
    /// <param name="content">The Markdown source.</param>
    /// <param name="defaultFileName">The file-name pattern.</param>
    /// <returns>Sorted unique variable names.</returns>
    private static string[] GetTemplateVariableNames(
            string content,
            string defaultFileName) =>
        TemplateVariablePattern()
            .Matches(
                content + "\n" + defaultFileName)
            .Select(match =>
                match.Groups["name"].Value)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                name =>
                    name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Parses preview variables from the multiline name=value editor.
    /// </summary>
    /// <param name="throwOnInvalidLine">Whether malformed lines should fail validation.</param>
    /// <returns>The parsed variable values.</returns>
    private IReadOnlyDictionary<string, string> ParsePreviewVariables(
            bool throwOnInvalidLine = true)
    {
        global::System.Collections.Generic.Dictionary<string, string> result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        string[] lines =
            PreviewVariablesTextBox.Text.Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Split(
                    '\n');

        for (int index = 0;
             index < lines.Length;
             index++)
        {
            string line =
                lines[index].Trim();

            if (line.Length ==
            0)
            {
                continue;
            }

            int separator =
                line.IndexOf(
                    '=');

            if (separator <=
                0)
            {
                if (throwOnInvalidLine)
                {
                    throw new InvalidDataException(
                        $"Variable de prévisualisation invalide à la ligne {index + 1}. Utilisez nom=valeur.");
                }

                continue;
            }

            string name =
                line[..separator].Trim();
            string value =
                line[(separator + 1)..];

            if (name.Length ==
            0)
            {
                if (throwOnInvalidLine)
                {
                    throw new InvalidDataException(
                        $"Nom de variable manquant à la ligne {index + 1}.");
                }

                continue;
            }

            result[name] =
                value;
        }

        return result;
    }

    /// <summary>
    /// Renders the current Markdown locally and surfaces syntax/variable errors.
    /// </summary>
    private void RenderTemplatePreview()
    {
        if (TemplatesList.SelectedItem is not
            MarkdownTemplateDefinition)
        {
            return;
        }

        try
        {
            IReadOnlyDictionary<string, string> variables =
                ParsePreviewVariables();

            string rendered =
                MarkdownTemplateRenderer.Render(
                    TemplateContentTextBox.Text,
                    variables);

            TemplatePreview.Document =
                MarkdownFlowDocumentRenderer.Render(
                    rendered);
            PreviewStatusText.Text =
                "Aperçu valide · rendu local WPF";
        }
        catch (Exception exception) when (
            exception is TemplateRenderException or
            InvalidDataException)
        {
            TemplatePreview.Document =
                MarkdownFlowDocumentRenderer.Render(
                    "# Prévisualisation indisponible\n\nCorrigez l'erreur indiquée sous l'aperçu.");
            PreviewStatusText.Text =
                $"Erreur : {exception.Message}";
        }
    }

    /// <summary>
    /// Validates the file name produced by a template before persistence.
    /// </summary>
    /// <param name="fileName">The rendered file name.</param>
    private static void ValidateRenderedFileName(
            string fileName)
    {
        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            throw new InvalidDataException(
                "Le nom de fichier généré est vide.");
        }

        if (fileName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >=
            0 ||
            fileName.Contains(
                Path.DirectorySeparatorChar) ||
            fileName.Contains(
                Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException(
                $"Le nom de fichier généré « {fileName} » n'est pas valide.");
        }
    }

    /// <summary>
    /// Provides a stable sample value for well-known and custom variables.
    /// </summary>
    /// <param name="name">The template variable name.</param>
    /// <returns>A deterministic preview value.</returns>
    private static string GetDefaultPreviewValue(
            string name) =>
        name.ToLowerInvariant() switch
        {
            "title" =>
                "Exemple",
            "id" =>
                "11111111-1111-1111-1111-111111111111",
            "date" =>
                "2026-10-05",
            "datetime" =>
                "2026-10-05 15:00",
            "year" =>
                "2026",
            "workspace.name" =>
                "Workspace exemple",
            "context.name" =>
                "Contexte exemple",
            "context.kind" =>
                "Projet",
            "project.name" =>
                "Projet exemple",
            "project.id" =>
                "22222222-2222-2222-2222-222222222222",
            "application.name" =>
                "Application exemple",
            "module.name" =>
                "Module exemple",
            _ =>
                "Exemple"
        };

    /// <summary>
    /// Tests whether an exception is expected from editable template/profile data.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><see langword="true"/> for a user-actionable data or I/O error.</returns>
    private static bool IsExpectedTemplateException(
            Exception exception) =>
        exception is IOException or
        UnauthorizedAccessException or
        InvalidDataException or
        InvalidOperationException or
        TemplateRenderException or
        KeyNotFoundException;

    /// <summary>
    /// Shows one consistent editor error dialog.
    /// </summary>
    /// <param name="message">The user-actionable error message.</param>
    private void ShowError(
            string message)
    {
        MessageBox.Show(
            this,
            message,
            "Templates et profils",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// Matches portable Nodalis template variables.
    /// </summary>
    /// <returns>The generated variable regular expression.</returns>
    [GeneratedRegex(
            @"{{\s*(?<name>[A-Za-z0-9_.-]+)\s*}}",
            RegexOptions.CultureInvariant)]
    private static partial Regex TemplateVariablePattern();

    private sealed record TemplateChoice(
        string DisplayName,
        string? Key);

    private sealed class ProfileSectionRow
    {
        /// <summary>
        /// Initializes a mutable section row for profile editing.
        /// </summary>
        /// <param name="name">The section name.</param>
        /// <param name="templateKey">The optional initial template key.</param>
        /// <param name="isSingleton">Whether the initial template is a singleton document.</param>
        public ProfileSectionRow(
                string name,
                string? templateKey,
                bool isSingleton)
        {
            Name =
                name;
            TemplateKey =
                templateKey;
            IsSingleton =
                isSingleton;
        }

        public string Name { get; set; }

        public string? TemplateKey { get; set; }

        public bool IsSingleton { get; set; }

        public string Detail =>
            (TemplateKey is null
                ? "Aucun template"
                : $"Template : {TemplateKey}") +
            (IsSingleton
                ? " · document unique"
                : string.Empty);
    }
}
