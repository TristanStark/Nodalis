using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Calendar;
using Nodalis.Infrastructure.Calendar;
using Nodalis.App.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Displays a month or week projection of dated workspace sources and supports safe date drag-and-drop.
/// </summary>
public partial class WorkspaceCalendarDialog : Window
{
    private readonly WorkspaceCalendarService _service;
    private WorkspaceCalendarSnapshot _snapshot =
        new WorkspaceCalendarSnapshot();
    private DateOnly _anchorDate =
        DateOnly.FromDateTime(
            DateTime.Today);
    private Point _dragStart;

    /// <summary>
    /// Initializes a new workspace calendar dialog.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceCalendarDialog(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _service =
            new WorkspaceCalendarService(
                workspaceRoot);

        InitializeComponent();

        AnchorDatePicker.SelectedDate =
            _anchorDate.ToDateTime(
                TimeOnly.MinValue);

        Loaded +=
            WorkspaceCalendarDialog_Loaded;
    }

    /// <summary>
    /// Gets the source event selected for navigation after the dialog closes.
    /// </summary>
    public CalendarEventItem? SelectedEvent { get; private set; }

    /// <summary>
    /// Loads the initial derived calendar.
    /// </summary>
    /// <param name="sender">The dialog.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void WorkspaceCalendarDialog_Loaded(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Calendrier · chargement",
            async () =>
            {
                Loaded -=
                    WorkspaceCalendarDialog_Loaded;

                await RefreshAsync();
            });
    }

    /// <summary>
    /// Refreshes all dated source data.
    /// </summary>
    /// <param name="sender">The refresh button.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void Refresh_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Calendrier · actualiser",
            async () =>
            {
                await RefreshAsync();
            });
    }

    /// <summary>
    /// Moves the anchor to the previous month or week.
    /// </summary>
    /// <param name="sender">The previous button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Previous_Click(
            object sender,
            RoutedEventArgs e)
    {
        _anchorDate =
            IsWeekMode()
                ? _anchorDate.AddDays(
                    -7)
                : _anchorDate.AddMonths(
                    -1);

        SyncAnchorPicker();
        RebuildView();
    }

    /// <summary>
    /// Moves the anchor to the next month or week.
    /// </summary>
    /// <param name="sender">The next button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Next_Click(
            object sender,
            RoutedEventArgs e)
    {
        _anchorDate =
            IsWeekMode()
                ? _anchorDate.AddDays(
                    7)
                : _anchorDate.AddMonths(
                    1);

        SyncAnchorPicker();
        RebuildView();
    }

    /// <summary>
    /// Changes the anchor date from the date picker.
    /// </summary>
    /// <param name="sender">The date picker.</param>
    /// <param name="e">The selection arguments.</param>
    private void AnchorDatePicker_SelectedDateChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (AnchorDatePicker.SelectedDate is not DateTime date)
        {
            return;
        }

        _anchorDate =
            DateOnly.FromDateTime(
                date);

        RebuildView();
    }

    /// <summary>
    /// Rebuilds the view after any filter or mode change.
    /// </summary>
    /// <param name="sender">The changed filter.</param>
    /// <param name="e">The selection arguments.</param>
    private void Filter_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (ReferenceEquals(
                sender,
                ApplicationFilter))
        {
            RebuildProjectFilter();
        }

        RebuildView();
    }

    /// <summary>
    /// Remembers the mouse position used to start a drag gesture.
    /// </summary>
    /// <param name="sender">The source event list.</param>
    /// <param name="e">The mouse event arguments.</param>
    private void Events_PreviewMouseMove(
            object sender,
            MouseEventArgs e)
    {
        if (e.LeftButton !=
                MouseButtonState.Pressed)
        {
            _dragStart =
                e.GetPosition(
                    this);
            return;
        }

        Point current =
            e.GetPosition(
                this);

        if (Math.Abs(
                current.X -
                _dragStart.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(
                current.Y -
                _dragStart.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (sender is not ListBox listBox ||
            listBox.SelectedItem is not CalendarEventItem item)
        {
            return;
        }

        DragDrop.DoDragDrop(
            listBox,
            item,
            DragDropEffects.Move);
    }

    /// <summary>
    /// Opens the source of the double-clicked event.
    /// </summary>
    /// <param name="sender">The event list.</param>
    /// <param name="e">The mouse event arguments.</param>
    private void Events_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox ||
            listBox.SelectedItem is not CalendarEventItem item)
        {
            return;
        }

        SelectedEvent =
            item;
        DialogResult =
            true;
    }

    /// <summary>
    /// Advertises move semantics only for safely reschedulable source items.
    /// </summary>
    /// <param name="sender">The target day card.</param>
    /// <param name="e">The drag event arguments.</param>
    private void Day_DragOver(
            object sender,
            DragEventArgs e)
    {
        CalendarEventItem? item =
            e.Data.GetData(
                typeof(CalendarEventItem)) as CalendarEventItem;

        e.Effects =
            item?.CanReschedule ==
                true
                ? DragDropEffects.Move
                : DragDropEffects.None;
        e.Handled =
            true;
    }

    /// <summary>
    /// Rewrites the explicit source date when a safely reschedulable event is dropped on another day.
    /// </summary>
    /// <param name="sender">The target day card.</param>
    /// <param name="e">The drag event arguments.</param>
    private async void Day_Drop(
            object sender,
            DragEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Calendrier · déplacer un élément",
            async () =>
            {
                if (sender is not FrameworkElement element ||
                    element.DataContext is not CalendarDayViewModel day ||
                    e.Data.GetData(
                        typeof(CalendarEventItem)) is not CalendarEventItem item ||
                    !item.CanReschedule)
                {
                    return;
                }

                try
                {
                    await _service.RescheduleAsync(
                        item,
                        day.Date);

                    StatusText.Text =
                        item.KindLabel +
                        " déplacé vers le " +
                        day.Date.ToString(
                            "dd/MM/yyyy");

                    await RefreshAsync();
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    InvalidOperationException)
                {
                    StatusText.Text =
                        "Déplacement refusé : " +
                        exception.Message;
                }
            });
    }

    /// <summary>
    /// Refreshes source data, filters, and the visible time window.
    /// </summary>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshAsync()
    {
        StatusText.Text =
            "Lecture des échéances, jalons et réunions…";

        try
        {
            _snapshot =
                await _service.RefreshAsync();

            RebuildApplicationFilter();
            RebuildProjectFilter();
            RebuildView();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            DaysItemsControl.ItemsSource =
                null;
            StatusText.Text =
                exception.Message;
        }
    }

    /// <summary>
    /// Rebuilds the list of application filters while preserving the current selection when possible.
    /// </summary>
    private void RebuildApplicationFilter()
    {
        Guid? selected =
            (ApplicationFilter.SelectedItem as ScopeFilterOption)?.Id;

        List<ScopeFilterOption> options =
            new List<ScopeFilterOption>
            {
                new ScopeFilterOption(
                    null,
                    "Toutes les applications")
            };

        options.AddRange(
            _snapshot.Events
                .Where(item =>
                    item.ApplicationId is not null)
                .GroupBy(item =>
                    item.ApplicationId!.Value)
                .Select(group =>
                    new ScopeFilterOption(
                        group.Key,
                        group.Select(item =>
                                item.ApplicationName)
                            .FirstOrDefault(name =>
                                !string.IsNullOrWhiteSpace(
                                    name)) ??
                        group.Key.ToString(
                            "D")))
                .OrderBy(option =>
                    option.Label,
                    StringComparer.CurrentCultureIgnoreCase));

        ApplicationFilter.ItemsSource =
            options;
        ApplicationFilter.SelectedItem =
            options.FirstOrDefault(option =>
                option.Id ==
                    selected) ??
            options[0];
    }

    /// <summary>
    /// Rebuilds the project filter based on the selected application.
    /// </summary>
    private void RebuildProjectFilter()
    {
        Guid? selected =
            (ProjectFilter.SelectedItem as ScopeFilterOption)?.Id;
        Guid? applicationId =
            (ApplicationFilter.SelectedItem as ScopeFilterOption)?.Id;

        List<ScopeFilterOption> options =
            new List<ScopeFilterOption>
            {
                new ScopeFilterOption(
                    null,
                    "Tous les projets")
            };

        options.AddRange(
            _snapshot.Events
                .Where(item =>
                    item.ProjectId is not null &&
                    (applicationId is null ||
                     item.ApplicationId ==
                         applicationId))
                .GroupBy(item =>
                    item.ProjectId!.Value)
                .Select(group =>
                    new ScopeFilterOption(
                        group.Key,
                        group.Select(item =>
                                item.ProjectName)
                            .FirstOrDefault(name =>
                                !string.IsNullOrWhiteSpace(
                                    name)) ??
                        group.Key.ToString(
                            "D")))
                .OrderBy(option =>
                    option.Label,
                    StringComparer.CurrentCultureIgnoreCase));

        ProjectFilter.ItemsSource =
            options;
        ProjectFilter.SelectedItem =
            options.FirstOrDefault(option =>
                option.Id ==
                    selected) ??
            options[0];
    }

    /// <summary>
    /// Rebuilds month or week day cards from the selected filters.
    /// </summary>
    private void RebuildView()
    {
        if (!IsLoaded)
        {
            return;
        }

        Guid? applicationId =
            (ApplicationFilter.SelectedItem as ScopeFilterOption)?.Id;
        Guid? projectId =
            (ProjectFilter.SelectedItem as ScopeFilterOption)?.Id;
        CalendarEventKind? kind =
            ResolveTypeFilter();

        DateOnly start =
            IsWeekMode()
                ? StartOfWeek(
                    _anchorDate)
                : new DateOnly(
                    _anchorDate.Year,
                    _anchorDate.Month,
                    1);

        int dayCount =
            IsWeekMode()
                ? 7
                : DateTime.DaysInMonth(
                    _anchorDate.Year,
                    _anchorDate.Month);

        double width =
            IsWeekMode()
                ? 150
                : 150;

        List<CalendarDayViewModel> days =
            new List<CalendarDayViewModel>();

        for (int offset = 0;
             offset < dayCount;
             offset++)
        {
            DateOnly date =
                start.AddDays(
                    offset);

            IReadOnlyList<CalendarEventItem> events =
                _snapshot.Events
                    .Where(item =>
                        item.Date ==
                            date &&
                        (applicationId is null ||
                         item.ApplicationId ==
                             applicationId) &&
                        (projectId is null ||
                         item.ProjectId ==
                             projectId) &&
                        (kind is null ||
                         item.Kind ==
                             kind))
                    .ToArray();

            days.Add(
                new CalendarDayViewModel(
                    date,
                    width,
                    events));
        }

        DaysItemsControl.ItemsSource =
            days;

        int visibleEventCount =
            days.Sum(day =>
                day.Events.Count);

        StatusText.Text =
            visibleEventCount +
            " élément(s) visible(s) sur " +
            _snapshot.Events.Count +
            " daté(s) · double-cliquez pour ouvrir la source ; tâches et jalons datés sont déplaçables.";
    }

    /// <summary>
    /// Determines whether week mode is selected.
    /// </summary>
    /// <returns>Whether the current view is weekly.</returns>
    private bool IsWeekMode()
    {
        return ViewModeComboBox.SelectedItem is ComboBoxItem item &&
               string.Equals(
                   item.Tag?.ToString(),
                   "Week",
                   StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the optional type filter.
    /// </summary>
    /// <returns>The selected event kind, or null for all kinds.</returns>
    private CalendarEventKind? ResolveTypeFilter()
    {
        string? tag =
            (TypeFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        return tag switch
        {
            "Task" => CalendarEventKind.Task,
            "Milestone" => CalendarEventKind.Milestone,
            "Meeting" => CalendarEventKind.Meeting,
            _ => null
        };
    }

    /// <summary>
    /// Calculates the Monday containing one date.
    /// </summary>
    /// <param name="date">The date.</param>
    /// <returns>The Monday of its week.</returns>
    private static DateOnly StartOfWeek(
            DateOnly date)
    {
        int offset =
            ((int)date.DayOfWeek +
             6) %
            7;

        return date.AddDays(
            -offset);
    }

    /// <summary>
    /// Synchronizes the date picker without changing the semantic anchor.
    /// </summary>
    private void SyncAnchorPicker()
    {
        AnchorDatePicker.SelectedDate =
            _anchorDate.ToDateTime(
                TimeOnly.MinValue);
    }

    /// <summary>
    /// Represents one application or project filter option.
    /// </summary>
    private sealed record ScopeFilterOption
    {
        /// <summary>
        /// Initializes a new scope filter option.
        /// </summary>
        /// <param name="id">The optional scope identifier.</param>
        /// <param name="label">The display label.</param>
        public ScopeFilterOption(
                Guid? id,
                string label)
        {
            Id =
                id;
            Label =
                label;
        }

        /// <summary>Gets the optional scope identifier.</summary>
        public Guid? Id { get; }

        /// <summary>Gets the display label.</summary>
        public string Label { get; }
    }

    /// <summary>
    /// Represents one visible day card.
    /// </summary>
    private sealed record CalendarDayViewModel
    {
        /// <summary>
        /// Initializes a day card.
        /// </summary>
        /// <param name="date">The represented date.</param>
        /// <param name="cardWidth">The card width.</param>
        /// <param name="events">The filtered events.</param>
        public CalendarDayViewModel(
                DateOnly date,
                double cardWidth,
                IReadOnlyList<CalendarEventItem> events)
        {
            Date =
                date;
            CardWidth =
                cardWidth;
            Events =
                events;
        }

        /// <summary>Gets the represented date.</summary>
        public DateOnly Date { get; }

        /// <summary>Gets the card width.</summary>
        public double CardWidth { get; }

        /// <summary>Gets the localized day label.</summary>
        public string DateLabel =>
            Date.ToString(
                "ddd dd MMM",
                System.Globalization.CultureInfo.CurrentCulture);

        /// <summary>Gets the events shown on this day.</summary>
        public IReadOnlyList<CalendarEventItem> Events { get; }
    }
}
