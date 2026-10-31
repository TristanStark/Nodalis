using System.Windows;

namespace Nodalis.App.Dialogs;

/// <summary>Optional standalone host for the reusable project journey view.</summary>
public partial class ProjectJourneyDialog : Window
{
    private readonly ProjectJourneyView _view;

    /// <summary>Creates a standalone journey window without duplicating editor logic.</summary>
    /// <param name="workspaceRoot">Workspace root.</param>
    /// <param name="projectDirectory">Selected project directory.</param>
    /// <param name="readOnly">Read-only workspace.</param>
    public ProjectJourneyDialog(string workspaceRoot, string projectDirectory,
        bool readOnly = false)
    {
        InitializeComponent();
        _view = new ProjectJourneyView(workspaceRoot, projectDirectory, readOnly);
        _view.CloseRequested += (_, _) => Close();
        Content = _view;
    }

    /// <summary>Indicates an explicit, successful change during this window's lifetime.</summary>
    public bool Changed => _view.Changed;
}
