using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nodalis.Core.Impact;

namespace Nodalis.App.Dialogs;

/// <summary>Provides a native WPF graph view as an optional complement to the accessible list.</summary>
public partial class ImpactAnalysisDialog
{
    private const int MaximumDiagramNodes = 90;
    private readonly Dictionary<Guid, Button> _graphNodeButtons = [];
    private readonly List<(ImpactDiagramEdge Edge, Line Visual)> _graphLines = [];

    /// <summary>
    /// Draws only the bounded, already filtered evidence paths; large graphs fall back to the list.
    /// </summary>
    private void RenderGraph()
    {
        if (!IsLoaded || GraphCanvas is null || GraphStatusText is null)
        {
            return;
        }

        GraphCanvas.Children.Clear();
        _graphNodeButtons.Clear();
        _graphLines.Clear();
        if (_analysis is null)
        {
            GraphStatusText.Text = "Le calcul n'est pas terminé.";
            return;
        }

        ImpactFindingRow[] rows = ResultsList.ItemsSource is IEnumerable<ImpactFindingRow> results
            ? results.ToArray() : [];
        ImpactDiagram diagram = ImpactDiagramProjection.Create(
            _origin, rows.Select(row => row.Finding), _graph, MaximumDiagramNodes);
        if (diagram.Nodes.Count == 0)
        {
            GraphStatusText.Text = "Aucun nœud à afficher.";
            return;
        }

        const double boxWidth = 210d;
        const double boxHeight = 65d;
        const double horizontalSpacing = 75d;
        const double verticalSpacing = 23d;
        const double margin = 45d;
        Dictionary<Guid, Point> coordinates = [];
        foreach (IGrouping<int, ImpactDiagramNode> column in diagram.Nodes.GroupBy(node => node.Depth))
        {
            int lane = 0;
            string grouping = GraphGroupComboBox.SelectedItem as string ?? "Par profondeur";
            IEnumerable<ImpactDiagramNode> ordered = grouping switch
            {
                "Par projet" => column.OrderBy(node => node.Group,
                    StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Title,
                    StringComparer.OrdinalIgnoreCase),
                "Par type" => column.OrderBy(node => node.Kind).ThenBy(node => node.Group,
                    StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Title,
                    StringComparer.OrdinalIgnoreCase),
                _ => column.OrderBy(node => node.Title, StringComparer.OrdinalIgnoreCase)
            };
            foreach (ImpactDiagramNode node in ordered)
            {
                double x = margin + column.Key * (boxWidth + horizontalSpacing);
                double y = margin + lane++ * (boxHeight + verticalSpacing);
                coordinates.Add(node.Id, new Point(x, y));
            }
        }

        GraphCanvas.Width = Math.Max(600, coordinates.Values.Max(point => point.X) + boxWidth + margin);
        GraphCanvas.Height = Math.Max(350, coordinates.Values.Max(point => point.Y) + boxHeight + margin);
        Brush lineBrush = (Brush)FindResource("SecondaryTextBrush");
        Brush accent = (Brush)FindResource("AccentBrush");
        Brush background = (Brush)FindResource("PanelBackgroundBrush");
        Brush border = (Brush)FindResource("BorderBrush");

        foreach (ImpactDiagramEdge edge in diagram.Edges)
        {
            if (!coordinates.TryGetValue(edge.FromId, out Point first) ||
                !coordinates.TryGetValue(edge.ToId, out Point second))
            {
                continue;
            }

            double x1 = first.X + boxWidth;
            double y1 = first.Y + boxHeight / 2;
            double x2 = second.X;
            double y2 = second.Y + boxHeight / 2;
            Brush stroke = edge.Evidence == ImpactEvidence.Explicit ? accent : lineBrush;
            Line line = new()
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = stroke,
                StrokeThickness = 2,
                StrokeDashArray = edge.Evidence == ImpactEvidence.Suggested
                    ? new DoubleCollection { 4, 4 } : null,
                ToolTip = edge.RelationType + " · " +
                    (edge.Evidence == ImpactEvidence.Explicit ? "relation explicite" : "simple contexte")
            };
            GraphCanvas.Children.Add(line);
            _graphLines.Add((edge, line));
            if (x2 > x1 + 10)
            {
                Polygon arrow = new()
                {
                    Points = new PointCollection
                    {
                        new Point(x2 - 3, y2),
                        new Point(x2 - 14, y2 - 5),
                        new Point(x2 - 14, y2 + 5)
                    },
                    Fill = stroke,
                    IsHitTestVisible = false
                };
                GraphCanvas.Children.Add(arrow);
            }
        }

        foreach (ImpactDiagramNode node in diagram.Nodes)
        {
            Point point = coordinates[node.Id];
            string kind = node.Kind.ToString();
            Button button = new()
            {
                Width = boxWidth,
                Height = boxHeight,
                Padding = new Thickness(9, 5, 9, 5),
                Tag = node.Id,
                Content = new TextBlock
                {
                    Text = node.Title + "\n" + kind + " · niveau " + node.Depth,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.Wrap
                },
                Background = background,
                Foreground = node.Id == _origin.Id || node.Evidence == ImpactEvidence.Explicit
                    ? accent : lineBrush,
                BorderBrush = node.Id == _origin.Id ? accent : border,
                BorderThickness = node.Id == _origin.Id ? new Thickness(2) : new Thickness(1),
                ToolTip = node.Group + " · " +
                    (node.Evidence == ImpactEvidence.Explicit ? "relations explicites" : "suggestion")
            };
            AutomationProperties.SetName(button,
                node.Title + " · " + node.Group + " · niveau " + node.Depth);
            button.Click += GraphNode_Click;
            Canvas.SetLeft(button, point.X);
            Canvas.SetTop(button, point.Y);
            GraphCanvas.Children.Add(button);
            _graphNodeButtons.Add(node.Id, button);
        }

        GraphStatusText.Text = diagram.Nodes.Count + " nœud(s), " +
            diagram.Edges.Count + " flèche(s)" +
            (diagram.IsTruncated ? " · Graphe limité à 90 nœuds : vue liste complète disponible." : "") +
            (_analysis.IsTruncated ? " · Analyse source tronquée." : "") +
            " · Clic : sélectionner ; double-clic sur la liste : ouvrir.";
        FocusGraphSelection();
    }

    /// <summary>
    /// Selects the matching accessible list item after a graph node is clicked.
    /// </summary>
    /// <param name="sender">Clicked graph button.</param>
    /// <param name="e">Routed event.</param>
    private void GraphNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id })
        {
            return;
        }

        foreach (object item in ResultsList.Items)
        {
            if (item is ImpactFindingRow row && row.Finding.Target.Id == id)
            {
                ResultsList.SelectedItem = row;
                ResultsList.ScrollIntoView(row);
                return;
            }
        }

        if (id == _origin.Id)
        {
            ExplanationText.Text = "Nœud d'origine : " + _origin.QualifiedName;
        }
    }

    /// <summary>Repositions the selected node into view when the user changes selection.</summary>
    private void FocusGraphSelection()
    {
        if (ResultsList.SelectedItem is not ImpactFindingRow row ||
            !_graphNodeButtons.TryGetValue(row.Finding.Target.Id, out Button? button))
        {
            return;
        }

        foreach (Button current in _graphNodeButtons.Values)
        {
            current.BorderThickness = current.Tag is Guid id && id == _origin.Id
                ? new Thickness(2) : new Thickness(1);
        }

        button.BorderThickness = new Thickness(3);

        // Highlight exactly the auditable dependency path to the selected node.
        // Context-only edges keep their dashed styling and are never treated as causal.
        HashSet<string> selectedArrows = new(StringComparer.Ordinal);
        Guid cursor = _origin.Id;
        foreach (ImpactGraphEdge edge in row.Finding.Path)
        {
            Guid next = row.Finding.Evidence == ImpactEvidence.Explicit ||
                edge.TargetId == _origin.Id ? edge.SourceId : edge.TargetId;
            selectedArrows.Add(cursor.ToString("D") + ":" + next.ToString("D") + ":" +
                edge.RelationType + ":" + row.Finding.Evidence);
            cursor = next;
        }

        foreach ((ImpactDiagramEdge edge, Line visual) in _graphLines)
        {
            string key = edge.FromId.ToString("D") + ":" + edge.ToId.ToString("D") + ":" +
                edge.RelationType + ":" + edge.Evidence;
            bool onPath = selectedArrows.Contains(key);
            visual.StrokeThickness = onPath ? 4 : 2;
            visual.Opacity = onPath ? 1 : 0.45;
        }

        if (GraphTab.IsSelected)
        {
            double zoom = GraphZoomSlider.Value / 100d;
            GraphScrollViewer.ScrollToHorizontalOffset(Math.Max(0,
                Canvas.GetLeft(button) * zoom - GraphScrollViewer.ViewportWidth / 2));
            GraphScrollViewer.ScrollToVerticalOffset(Math.Max(0,
                Canvas.GetTop(button) * zoom - GraphScrollViewer.ViewportHeight / 2));
        }
    }

    /// <summary>Switches graph lane ordering between level, project and indexed target type.</summary>
    /// <param name="sender">Group selector.</param>
    /// <param name="e">Selection change.</param>
    private void GraphGroup_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RenderGraph();
        }
    }

    /// <summary>Centers the selected result after switching to the diagram tab.</summary>
    /// <param name="sender">Tab header.</param>
    /// <param name="e">Routed selection event.</param>
    private void GraphTab_Selected(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && GraphTab.IsSelected)
        {
            FocusGraphSelection();
        }
    }

    /// <summary>
    /// Changes the visual zoom without changing results or recreating graph nodes.
    /// </summary>
    /// <param name="sender">Zoom slider.</param>
    /// <param name="e">New zoom level.</param>
    private void GraphZoom_Changed(object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (GraphCanvas is null)
        {
            return;
        }

        double zoom = e.NewValue / 100d;
        GraphCanvas.LayoutTransform = new ScaleTransform(zoom, zoom);
    }

    /// <summary>
    /// Selects the root of the diagram and resets the scroll to its beginning.
    /// </summary>
    /// <param name="sender">Reset button.</param>
    /// <param name="e">Routed event.</param>
    private void GraphReset_Click(object sender, RoutedEventArgs e)
    {
        GraphZoomSlider.Value = 100;
        GraphScrollViewer.ScrollToHome();
    }
}
