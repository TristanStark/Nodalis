using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nodalis.Core.Importing;

namespace Nodalis.App.Dialogs;

public partial class DocxImportPreviewDialog
{
    private Point? _mappingDragOrigin;

    private readonly HashSet<string> _customMappingSections =
        new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>
    /// Rebuilds a read-only hierarchy of application, selected project and its sections.
    /// The chosen project is shared by all sections in this first #158 increment.
    /// </summary>
    private void RefreshMappingDestinations()
    {
        if (MappingDestinationTree is null ||
            ApplicationComboBox?.SelectedItem is not DocxImportTargetOption application)
        {
            return;
        }

        bool newProject = CreateNewProjectCheckBox?.IsChecked == true;
        DocxImportTargetOption? project = newProject
            ? null
            : ProjectComboBox?.SelectedItem as DocxImportTargetOption;

        DocxImportDestinationNode root = new DocxImportDestinationNode
        {
            Label = application.DisplayName
        };
        DocxImportDestinationNode selectedProject = new DocxImportDestinationNode
        {
            Label = newProject
                ? "Nouveau projet (à créer)"
                : project?.DisplayName ?? "Choisissez un projet"
        };
        root.Children.Add(selectedProject);

        // Suggestions do not silently create any section, including for Ultra-simple projects.
        List<string> choices = newProject
            ? ["Présentation", "Technique", "Tests", "Recette", "Documentation", "Jalons", "Glossaire"]
            : project?.ExistingSections.ToList() ?? [];

        foreach (string value in _sections.Select(section => section.TargetSection)
                     .Concat(_customMappingSections)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            if (!choices.Contains(value, StringComparer.CurrentCultureIgnoreCase))
            {
                choices.Add(value);
            }
        }

        foreach (string name in choices)
        {
            selectedProject.Children.Add(new DocxImportDestinationNode
            {
                Label = name,
                TargetSection = name
            });
        }

        MappingDestinationTree.ItemsSource = new[] { root };
    }

    /// <summary>
    /// Records the starting position so an ordinary mouse click never starts a drag.
    /// </summary>
    private void MappingSource_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _mappingDragOrigin = e.GetPosition(MappingSourceList);
    }

    /// <summary>
    /// Starts a drag only after the Windows movement threshold is crossed.
    /// </summary>
    private void MappingSource_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _mappingDragOrigin is not Point origin ||
            MappingSourceList.SelectedItem is not SectionRow row)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _mappingDragOrigin = null;
            }

            return;
        }

        Point current = e.GetPosition(MappingSourceList);
        if (Math.Abs(current.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _mappingDragOrigin = null;
        DataObject dragData = new DataObject(typeof(SectionRow), row);
        DragDrop.DoDragDrop(MappingSourceList, dragData, DragDropEffects.Move);
    }

    /// <summary>
    /// Allows drops only onto actual section nodes.
    /// </summary>
    private void MappingDestination_DragOver(object sender, DragEventArgs e)
    {
        DocxImportDestinationNode? destination = FindDropDestination(e.OriginalSource);
        e.Effects = e.Data.GetDataPresent(typeof(SectionRow)) &&
                    destination?.TargetSection is not null
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Maps a dragged Word section using exactly the keyboard assignment logic.
    /// </summary>
    private void MappingDestination_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(SectionRow)) is SectionRow source &&
            FindDropDestination(e.OriginalSource)?.TargetSection is string target)
        {
            AssignMapping(source, target);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Finds the destination represented by the actual hit-tested tree item.
    /// </summary>
    private static DocxImportDestinationNode? FindDropDestination(object original)
    {
        DependencyObject? current = original as DependencyObject;
        while (current is not null)
        {
            if (current is TreeViewItem item &&
                item.DataContext is DocxImportDestinationNode node)
            {
                return node;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    /// <summary>
    /// Assigns by keyboard or mouse button.
    /// </summary>
    private void MappingAssign_Click(object sender, RoutedEventArgs e)
    {
        if (MappingSourceList.SelectedItem is SectionRow row &&
            MappingDestinationTree.SelectedItem is DocxImportDestinationNode target &&
            target.TargetSection is string destination)
        {
            AssignMapping(row, destination);
        }
    }

    /// <summary>
    /// Maps a Word section into a new, explicitly named project section.
    /// </summary>
    private void MappingAssignNew_Click(object sender, RoutedEventArgs e)
    {
        if (MappingSourceList.SelectedItem is SectionRow row)
        {
            string sectionName = MappingNewSectionBox.Text.Trim();
            if (AssignMapping(row, sectionName))
            {
                _customMappingSections.Add(sectionName);
                RefreshMappingDestinations();
            }
        }
    }

    /// <summary>
    /// Excludes the selected source branch and all its descendant blocks.
    /// </summary>
    private void MappingIgnore_Click(object sender, RoutedEventArgs e)
    {
        if (MappingSourceList.SelectedItem is SectionRow row)
        {
            row.SelectionRoot.IsSelected = false;
            MappingFeedbackText.Text = "Section ignorée : " + row.SourceHeading;
        }
    }

    /// <summary>
    /// Moves the selected branch earlier in the deterministic destination merge order.
    /// </summary>
    private void MappingUp_Click(object sender, RoutedEventArgs e) => MoveMapping(-1);

    /// <summary>
    /// Moves the selected branch later in the deterministic destination merge order.
    /// </summary>
    private void MappingDown_Click(object sender, RoutedEventArgs e) => MoveMapping(1);

    /// <summary>
    /// Changes the order of complete Word sections, not block identities or source order.
    /// </summary>
    private void MoveMapping(int delta)
    {
        if (MappingSourceList.SelectedItem is not SectionRow row)
        {
            return;
        }

        int index = _sections.IndexOf(row);
        int target = index + delta;
        if (target < 0 || target >= _sections.Count)
        {
            return;
        }

        _sections.RemoveAt(index);
        _sections.Insert(target, row);
        MappingSourceList.Items.Refresh();
        SectionsListBox.Items.Refresh();
        MappingSourceList.SelectedItem = row;
        InvalidatePlan();
        MappingFeedbackText.Text = "Ordre de fusion modifié : aperçu à recalculer.";
    }

    /// <summary>
    /// Assigns a single branch using the same validation for drag and keyboard routes.
    /// </summary>
    private bool AssignMapping(SectionRow source, string destination)
    {
        try
        {
            string name = destination.Trim();
            if (string.IsNullOrWhiteSpace(name) ||
                name.Length > 120 ||
                name.StartsWith('.') ||
                name.Contains('/') ||
                name.Contains('\\'))
            {
                throw new InvalidDataException("Choisissez un nom de section simple et non vide.");
            }

            ImportDocumentPlanBuilder.ValidateDestination(name + ".md");
            source.TargetSection = name;
            source.SelectionRoot.IsSelected = true;
            MappingFeedbackText.Text = source.SourceHeading + " → " + name;
            RefreshMappingDestinations();
            return true;
        }
        catch (InvalidDataException exception)
        {
            MappingFeedbackText.Text = "Destination refusée : " + exception.Message;
            return false;
        }
    }
}
