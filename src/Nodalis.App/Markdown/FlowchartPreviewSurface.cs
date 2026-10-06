using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nodalis.Core.Flowcharts;

namespace Nodalis.App.Markdown;

/// <summary>
/// Displays a native WPF flowchart with local zoom and pan controls.
/// </summary>
public sealed class FlowchartPreviewSurface : Grid
{
    private const double MinimumScale = 0.5;
    private const double MaximumScale = 2.5;
    private const double ScaleStep = 0.15;

    private readonly ScrollViewer _scrollViewer;
    private readonly ScaleTransform _scaleTransform;
    private readonly TextBlock _scaleText;

    private Point? _dragStart;
    private double _dragHorizontalOffset;
    private double _dragVerticalOffset;

    /// <summary>
    /// Initializes a native preview surface for one laid-out flowchart.
    /// </summary>
    /// <param name="layout">The framework-agnostic flowchart layout.</param>
    /// <param name="diagnostics">Non-fatal parser diagnostics to surface.</param>
    public FlowchartPreviewSurface(
            FlowchartLayout layout,
            IReadOnlyList<FlowchartDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(
            layout);
        ArgumentNullException.ThrowIfNull(
            diagnostics);

        MinHeight =
            320;
        MaxHeight =
            720;
        Margin =
            new Thickness(
                0,
                8,
                0,
                10);

        RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        if (diagnostics.Count >
            0)
        {
            RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });
        }

        RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        DockPanel toolbar =
            new DockPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        8)
            };

        TextBlock title =
            new TextBlock
            {
                Text =
                    "FLOWCHART",
                FontSize =
                    11,
                FontWeight =
                    FontWeights.SemiBold,
                Foreground =
                    GetBrush(
                        "SecondaryTextBrush",
                        Brushes.LightGray),
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        DockPanel.SetDock(
            title,
            Dock.Left);
        toolbar.Children.Add(
            title);

        StackPanel zoomPanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };
        DockPanel.SetDock(
            zoomPanel,
            Dock.Right);

        Button zoomOut =
            CreateToolbarButton(
                "−",
                "Réduire le diagramme");
        zoomOut.Click +=
            (_, _) =>
                ChangeScale(
                    -ScaleStep);

        _scaleText =
            new TextBlock
            {
                Width =
                    58,
                Text =
                    "100 %",
                TextAlignment =
                    TextAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Foreground =
                    GetBrush(
                        "SecondaryTextBrush",
                        Brushes.LightGray)
            };

        Button resetZoom =
            CreateToolbarButton(
                "100 %",
                "Revenir à l'échelle 100 %");
        resetZoom.MinWidth =
            58;
        resetZoom.Click +=
            (_, _) =>
                SetScale(
                    1);

        Button zoomIn =
            CreateToolbarButton(
                "+",
                "Agrandir le diagramme");
        zoomIn.Click +=
            (_, _) =>
                ChangeScale(
                    ScaleStep);

        zoomPanel.Children.Add(
            zoomOut);
        zoomPanel.Children.Add(
            _scaleText);
        zoomPanel.Children.Add(
            resetZoom);
        zoomPanel.Children.Add(
            zoomIn);

        toolbar.Children.Add(
            zoomPanel);
        Children.Add(
            toolbar);
        Grid.SetRow(
            toolbar,
            0);

        int scrollRow =
            1;

        if (diagnostics.Count >
            0)
        {
            TextBlock warning =
                new TextBlock
                {
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            8),
                    Padding =
                        new Thickness(
                            8,
                            5,
                            8,
                            5),
                    Text =
                        string.Join(
                            Environment.NewLine,
                            diagnostics.Select(diagnostic =>
                                $"Ligne {diagnostic.LineNumber} : {diagnostic.Message}")),
                    TextWrapping =
                        TextWrapping.Wrap,
                    Foreground =
                        GetBrush(
                            "SecondaryTextBrush",
                            Brushes.LightGray),
                    Background =
                        GetBrush(
                            "PanelElevatedBrush",
                            Brushes.DimGray)
                };

            Children.Add(
                warning);
            Grid.SetRow(
                warning,
                1);
            scrollRow =
                2;
        }

        Canvas canvas =
            CreateCanvas(
                layout);

        Border scaledContent =
            new Border
            {
                Width =
                    layout.Width,
                Height =
                    layout.Height,
                Background =
                    GetBrush(
                        "WindowBackgroundBrush",
                        Brushes.Transparent),
                BorderBrush =
                    GetBrush(
                        "BorderBrush",
                        Brushes.Gray),
                BorderThickness =
                    new Thickness(
                        1),
                Child =
                    canvas,
                HorizontalAlignment =
                    HorizontalAlignment.Left,
                VerticalAlignment =
                    VerticalAlignment.Top
            };

        _scaleTransform =
            new ScaleTransform(
                1,
                1);
        scaledContent.LayoutTransform =
            _scaleTransform;

        _scrollViewer =
            new ScrollViewer
            {
                Content =
                    scaledContent,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                PanningMode =
                    PanningMode.Both,
                Background =
                    GetBrush(
                        "WindowBackgroundBrush",
                        Brushes.Transparent),
                Cursor =
                    Cursors.Hand
            };

        _scrollViewer.PreviewMouseLeftButtonDown +=
            ScrollViewer_PreviewMouseLeftButtonDown;
        _scrollViewer.PreviewMouseMove +=
            ScrollViewer_PreviewMouseMove;
        _scrollViewer.PreviewMouseLeftButtonUp +=
            ScrollViewer_PreviewMouseLeftButtonUp;
        _scrollViewer.LostMouseCapture +=
            ScrollViewer_LostMouseCapture;

        Children.Add(
            _scrollViewer);
        Grid.SetRow(
            _scrollViewer,
            scrollRow);
    }

    /// <summary>
    /// Creates the drawing canvas for nodes, routed edges and labels.
    /// </summary>
    /// <param name="layout">The calculated flowchart layout.</param>
    /// <returns>The populated WPF canvas.</returns>
    private static Canvas CreateCanvas(
            FlowchartLayout layout)
    {
        Canvas canvas =
            new Canvas
            {
                Width =
                    layout.Width,
                Height =
                    layout.Height,
                Background =
                    Brushes.Transparent
            };

        foreach (FlowchartLayoutEdge edge in
                 layout.Edges)
        {
            AddEdge(
                canvas,
                edge);
        }

        foreach (FlowchartLayoutNode node in
                 layout.Nodes)
        {
            AddNode(
                canvas,
                node);
        }

        return canvas;
    }

    /// <summary>
    /// Adds one routed edge, arrowhead and optional label.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="layoutEdge">The routed edge.</param>
    private static void AddEdge(
            Canvas canvas,
            FlowchartLayoutEdge layoutEdge)
    {
        if (layoutEdge.Points.Count <
            2)
        {
            return;
        }

        Brush stroke =
            GetBrush(
                "SecondaryTextBrush",
                Brushes.LightGray);

        Polyline line =
            new Polyline
            {
                Stroke =
                    stroke,
                StrokeThickness =
                    1.8,
                SnapsToDevicePixels =
                    true
            };

        foreach (FlowchartLayoutPoint point in
                 layoutEdge.Points)
        {
            line.Points.Add(
                new Point(
                    point.X,
                    point.Y));
        }

        canvas.Children.Add(
            line);

        if (layoutEdge.Edge.HasArrow)
        {
            Polygon arrow =
                CreateArrowHead(
                    layoutEdge.Points[^2],
                    layoutEdge.Points[^1],
                    stroke);
            canvas.Children.Add(
                arrow);
        }

        if (!string.IsNullOrWhiteSpace(
                layoutEdge.Edge.Label))
        {
            Border label =
                new Border
                {
                    Padding =
                        new Thickness(
                            5,
                            2,
                            5,
                            2),
                    Background =
                        GetBrush(
                            "PanelBackgroundBrush",
                            Brushes.DimGray),
                    BorderBrush =
                        GetBrush(
                            "BorderBrush",
                            Brushes.Gray),
                    BorderThickness =
                        new Thickness(
                            1),
                    CornerRadius =
                        new CornerRadius(
                            3),
                    Child =
                        new TextBlock
                        {
                            Text =
                                layoutEdge.Edge.Label,
                            FontSize =
                                11,
                            Foreground =
                                GetBrush(
                                    "PrimaryTextBrush",
                                    Brushes.White)
                        }
                };

            canvas.Children.Add(
                label);
            Canvas.SetLeft(
                label,
                layoutEdge.LabelPoint.X -
                28);
            Canvas.SetTop(
                label,
                layoutEdge.LabelPoint.Y -
                14);
        }
    }

    /// <summary>
    /// Adds one flowchart node with a native shape and centered label.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="layoutNode">The positioned node.</param>
    private static void AddNode(
            Canvas canvas,
            FlowchartLayoutNode layoutNode)
    {
        Brush border =
            GetBrush(
                "AccentBrush",
                Brushes.CornflowerBlue);
        Brush background =
            GetBrush(
                "PanelElevatedBrush",
                Brushes.DimGray);
        Brush foreground =
            GetBrush(
                "PrimaryTextBrush",
                Brushes.White);

        switch (layoutNode.Node.Shape)
        {
            case FlowchartNodeShape.Decision:
                Polygon diamond =
                    new Polygon
                    {
                        Fill =
                            background,
                        Stroke =
                            border,
                        StrokeThickness =
                            1.7,
                        Points =
                        [
                            new Point(
                                layoutNode.Width / 2,
                                0),
                            new Point(
                                layoutNode.Width,
                                layoutNode.Height / 2),
                            new Point(
                                layoutNode.Width / 2,
                                layoutNode.Height),
                            new Point(
                                0,
                                layoutNode.Height / 2)
                        ]
                    };

                canvas.Children.Add(
                    diamond);
                Canvas.SetLeft(
                    diamond,
                    layoutNode.X);
                Canvas.SetTop(
                    diamond,
                    layoutNode.Y);
                break;

            case FlowchartNodeShape.Circle:
                Ellipse circle =
                    new Ellipse
                    {
                        Width =
                            layoutNode.Width,
                        Height =
                            layoutNode.Height,
                        Fill =
                            background,
                        Stroke =
                            border,
                        StrokeThickness =
                            1.7
                    };

                canvas.Children.Add(
                    circle);
                Canvas.SetLeft(
                    circle,
                    layoutNode.X);
                Canvas.SetTop(
                    circle,
                    layoutNode.Y);
                break;

            case FlowchartNodeShape.Hexagon:
                Polygon hexagon =
                    new Polygon
                    {
                        Fill =
                            background,
                        Stroke =
                            border,
                        StrokeThickness =
                            1.7,
                        Points =
                        [
                            new Point(
                                layoutNode.Width * 0.18,
                                0),
                            new Point(
                                layoutNode.Width * 0.82,
                                0),
                            new Point(
                                layoutNode.Width,
                                layoutNode.Height / 2),
                            new Point(
                                layoutNode.Width * 0.82,
                                layoutNode.Height),
                            new Point(
                                layoutNode.Width * 0.18,
                                layoutNode.Height),
                            new Point(
                                0,
                                layoutNode.Height / 2)
                        ]
                    };

                canvas.Children.Add(
                    hexagon);
                Canvas.SetLeft(
                    hexagon,
                    layoutNode.X);
                Canvas.SetTop(
                    hexagon,
                    layoutNode.Y);
                break;

            default:
                CornerRadius radius =
                    layoutNode.Node.Shape switch
                    {
                        FlowchartNodeShape.Rounded =>
                            new CornerRadius(
                                18),
                        FlowchartNodeShape.Stadium =>
                            new CornerRadius(
                                layoutNode.Height / 2),
                        FlowchartNodeShape.Database =>
                            new CornerRadius(
                                18),
                        _ =>
                            new CornerRadius(
                                4)
                    };

                Border rectangle =
                    new Border
                    {
                        Width =
                            layoutNode.Width,
                        Height =
                            layoutNode.Height,
                        Background =
                            background,
                        BorderBrush =
                            border,
                        BorderThickness =
                            new Thickness(
                                1.7),
                        CornerRadius =
                            radius
                    };

                canvas.Children.Add(
                    rectangle);
                Canvas.SetLeft(
                    rectangle,
                    layoutNode.X);
                Canvas.SetTop(
                    rectangle,
                    layoutNode.Y);

                if (layoutNode.Node.Shape ==
                    FlowchartNodeShape.Subroutine)
                {
                    double[] offsets =
                    [
                        14,
                        layoutNode.Width - 14
                    ];

                    foreach (double offset in
                             offsets)
                    {
                        Line separator =
                            new Line
                            {
                                X1 =
                                    layoutNode.X + offset,
                                X2 =
                                    layoutNode.X + offset,
                                Y1 =
                                    layoutNode.Y,
                                Y2 =
                                    layoutNode.Y + layoutNode.Height,
                                Stroke =
                                    border,
                                StrokeThickness =
                                    1.2
                            };

                        canvas.Children.Add(
                            separator);
                    }
                }
                else if (layoutNode.Node.Shape ==
                         FlowchartNodeShape.Database)
                {
                    Line cylinderLine =
                        new Line
                        {
                            X1 =
                                layoutNode.X + 10,
                            X2 =
                                layoutNode.X + layoutNode.Width - 10,
                            Y1 =
                                layoutNode.Y + 18,
                            Y2 =
                                layoutNode.Y + 18,
                            Stroke =
                                border,
                            StrokeThickness =
                                1.2
                        };

                    canvas.Children.Add(
                        cylinderLine);
                }

                break;
        }

        double horizontalInset =
            layoutNode.Node.Shape is
                FlowchartNodeShape.Decision or
                FlowchartNodeShape.Hexagon
                ? 30
                : 14;

        TextBlock label =
            new TextBlock
            {
                Width =
                    Math.Max(
                        40,
                        layoutNode.Width -
                        horizontalInset * 2),
                Height =
                    Math.Max(
                        30,
                        layoutNode.Height -
                        20),
                Text =
                    layoutNode.Node.Label,
                TextWrapping =
                    TextWrapping.Wrap,
                TextAlignment =
                    TextAlignment.Center,
                Foreground =
                    foreground,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        canvas.Children.Add(
            label);
        Canvas.SetLeft(
            label,
            layoutNode.X +
            horizontalInset);
        Canvas.SetTop(
            label,
            layoutNode.Y +
            10);
    }

    /// <summary>
    /// Creates a triangular arrowhead aligned with the final edge segment.
    /// </summary>
    /// <param name="previous">The previous route point.</param>
    /// <param name="end">The arrow endpoint.</param>
    /// <param name="brush">The arrow brush.</param>
    /// <returns>The arrowhead polygon.</returns>
    private static Polygon CreateArrowHead(
            FlowchartLayoutPoint previous,
            FlowchartLayoutPoint end,
            Brush brush)
    {
        double deltaX =
            end.X -
            previous.X;
        double deltaY =
            end.Y -
            previous.Y;
        double length =
            Math.Sqrt(
                deltaX * deltaX +
                deltaY * deltaY);

        if (length <
            0.01)
        {
            length =
                1;
        }

        double unitX =
            deltaX /
            length;
        double unitY =
            deltaY /
            length;
        double perpendicularX =
            -unitY;
        double perpendicularY =
            unitX;
        double arrowLength =
            10;
        double arrowWidth =
            5;

        Point tip =
            new Point(
                end.X,
                end.Y);
        Point left =
            new Point(
                end.X -
                    unitX * arrowLength +
                    perpendicularX * arrowWidth,
                end.Y -
                    unitY * arrowLength +
                    perpendicularY * arrowWidth);
        Point right =
            new Point(
                end.X -
                    unitX * arrowLength -
                    perpendicularX * arrowWidth,
                end.Y -
                    unitY * arrowLength -
                    perpendicularY * arrowWidth);

        return new Polygon
        {
            Fill =
                brush,
            Stroke =
                brush,
            Points =
            [
                tip,
                left,
                right
            ]
        };
    }

    /// <summary>
    /// Creates a compact toolbar button.
    /// </summary>
    /// <param name="content">The button caption.</param>
    /// <param name="toolTip">The tooltip.</param>
    /// <returns>The configured button.</returns>
    private static Button CreateToolbarButton(
            string content,
            string toolTip) =>
        new Button
        {
            MinWidth =
                32,
            Height =
                26,
            Margin =
                new Thickness(
                    3,
                    0,
                    0,
                    0),
            Padding =
                new Thickness(
                    6,
                    1,
                    6,
                    1),
            Content =
                content,
            ToolTip =
                toolTip
        };

    /// <summary>
    /// Changes the zoom factor by one configured step.
    /// </summary>
    /// <param name="delta">The requested scale delta.</param>
    private void ChangeScale(
            double delta) =>
        SetScale(
            _scaleTransform.ScaleX +
            delta);

    /// <summary>
    /// Applies a clamped zoom factor and updates the visible percentage.
    /// </summary>
    /// <param name="scale">The requested scale.</param>
    private void SetScale(
            double scale)
    {
        double clamped =
            Math.Clamp(
                scale,
                MinimumScale,
                MaximumScale);

        _scaleTransform.ScaleX =
            clamped;
        _scaleTransform.ScaleY =
            clamped;
        _scaleText.Text =
            $"{Math.Round(clamped * 100):0} %";
    }

    /// <summary>
    /// Starts mouse-drag panning inside the flowchart viewport.
    /// </summary>
    /// <param name="sender">The scroll viewer.</param>
    /// <param name="e">The mouse event.</param>
    private void ScrollViewer_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        _dragStart =
            e.GetPosition(
                _scrollViewer);
        _dragHorizontalOffset =
            _scrollViewer.HorizontalOffset;
        _dragVerticalOffset =
            _scrollViewer.VerticalOffset;

        _scrollViewer.CaptureMouse();
        _scrollViewer.Cursor =
            Cursors.SizeAll;
        e.Handled =
            true;
    }

    /// <summary>
    /// Pans the viewport while the primary mouse button is held.
    /// </summary>
    /// <param name="sender">The scroll viewer.</param>
    /// <param name="e">The mouse event.</param>
    private void ScrollViewer_PreviewMouseMove(
            object sender,
            MouseEventArgs e)
    {
        if (_dragStart is null ||
            e.LeftButton !=
            MouseButtonState.Pressed)
        {
            return;
        }

        Point current =
            e.GetPosition(
                _scrollViewer);
        Vector delta =
            current -
            _dragStart.Value;

        _scrollViewer.ScrollToHorizontalOffset(
            _dragHorizontalOffset -
            delta.X);
        _scrollViewer.ScrollToVerticalOffset(
            _dragVerticalOffset -
            delta.Y);
    }

    /// <summary>
    /// Ends mouse-drag panning.
    /// </summary>
    /// <param name="sender">The scroll viewer.</param>
    /// <param name="e">The mouse event.</param>
    private void ScrollViewer_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e) =>
        EndDrag();

    /// <summary>
    /// Clears drag state if mouse capture is lost unexpectedly.
    /// </summary>
    /// <param name="sender">The scroll viewer.</param>
    /// <param name="e">The mouse event.</param>
    private void ScrollViewer_LostMouseCapture(
            object sender,
            MouseEventArgs e)
    {
        _dragStart =
            null;
        _scrollViewer.Cursor =
            Cursors.Hand;
    }

    /// <summary>
    /// Releases mouse capture and restores the normal cursor.
    /// </summary>
    private void EndDrag()
    {
        _dragStart =
            null;

        if (_scrollViewer.IsMouseCaptured)
        {
            _scrollViewer.ReleaseMouseCapture();
        }

        _scrollViewer.Cursor =
            Cursors.Hand;
    }

    /// <summary>
    /// Resolves a dark-theme brush while keeping a safe fallback.
    /// </summary>
    /// <param name="resourceKey">The WPF resource key.</param>
    /// <param name="fallback">The fallback brush.</param>
    /// <returns>The resolved brush.</returns>
    private static Brush GetBrush(
            string resourceKey,
            Brush fallback) =>
        Application.Current.TryFindResource(
            resourceKey) as Brush ??
        fallback;
}
