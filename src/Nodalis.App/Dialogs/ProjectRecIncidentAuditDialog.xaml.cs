using System.Windows;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Collects an explicit, distinct human decision to associate a newly
/// created maintenance incident with a historical REC failure event.
/// </summary>
public partial class ProjectRecIncidentAuditDialog : Window
{
    /// <summary>Shows the frozen test context without changing any file.</summary>
    /// <param name="context">Human readable project-local source metadata.</param>
    public ProjectRecIncidentAuditDialog(string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        InitializeComponent();
        ContextText.Text = context;
        ActorBox.Focus();
    }

    /// <summary>Actor entered by the user, never guessed from historic tests.</summary>
    public string? Actor { get; private set; }

    /// <summary>Human rationale to persist in both source audit records.</summary>
    public string? Reason { get; private set; }

    /// <summary>Requires a real explicit actor and a useful written reason.</summary>
    /// <param name="sender">Human approval button.</param>
    /// <param name="e">Click.</param>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        string actor = ActorBox.Text.Trim();
        string reason = ReasonBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(actor) ||
            actor.Length > 160 || actor.Any(char.IsControl) ||
            reason.Length < 10 || reason.Length > 2000 ||
            reason.Any(char.IsControl))
        {
            MessageBox.Show(this,
                "Indiquez un auteur et un motif sur une seule ligne " +
                "d'au moins 10 caractères. Les retours à la ligne " +
                "ne sont pas autorisés dans cette justification.",
                "Provenance REC", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Actor = actor;
        Reason = reason;
        DialogResult = true;
    }
}
