using System.Windows;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Pure read-only presentation of one exact attempted MEP preflight report.
/// This dialog cannot approve or mutate a project or deployment.
/// </summary>
public partial class ProjectDeploymentPreflightDialog : Window
{
    /// <summary>Shows the read-only transient diagnosis for one attempt.</summary>
    /// <param name="report">Source-verified project-local preflight snapshot.</param>
    public ProjectDeploymentPreflightDialog(ProjectDeploymentPreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        InitializeComponent();
        SummaryText.Text = report.OperationReference + " · tentative " +
            report.AttemptId.ToString("D") + "\n" +
            "Contrôle du " +
            report.CheckedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
            " · GO historique : " +
            (report.HistoricalGateConditionsMatch
                ? "conditions connues concordantes, contrôle humain requis"
                : "écart ou preuve indisponible, nouvelle revue requise") +
            "\nRisques : " + report.RisksFound +
            " · incidents canoniques : " + report.IncidentsFound +
            " · autorisation créée : NON";
        List<string> lines =
        [
            "Constats de la revue (lecture seule)",
            "Aucun résultat ci-dessous n'autorise automatiquement une MEP.",
            string.Empty
        ];
        if (report.Findings.Count == 0)
        {
            lines.Add("Aucun détail disponible : reprendre une revue humaine.");
        }

        foreach (ProjectDeploymentPreflightFinding finding in report.Findings)
        {
            lines.Add("[" + finding.Area + "] " +
                (finding.NeedsNewDecision ? "[Nouvelle décision] " : "") +
                finding.Detail);
            lines.Add(string.Empty);
        }

        FindingsBox.Text = string.Join(Environment.NewLine, lines);
    }
}
