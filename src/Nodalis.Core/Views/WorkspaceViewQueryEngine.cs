using Nodalis.Core.Links;

namespace Nodalis.Core.Views;

/// <summary>Logical combination for safe, typed filter groups.</summary>
public enum WorkspaceViewLogic { All, Any }

/// <summary>Supported operations; no scripting or SQL expressions are evaluated.</summary>
public enum WorkspaceViewOperator
{
    Equals, NotEquals, Contains, Before, After, Between, HasTag, IsEmpty, IsNotEmpty
}

/// <summary>One typed comparison against a known projected field.</summary>
public sealed record WorkspaceViewPredicate
{
    public required string Field { get; init; }
    public required WorkspaceViewOperator Operator { get; init; }
    public string? Text { get; init; }
    public DateOnly? Date { get; init; }
    public DateOnly? EndDate { get; init; }
    public bool? Boolean { get; init; }
}

/// <summary>A recursively bounded AND/OR filter with no executable content.</summary>
public sealed record WorkspaceViewFilter
{
    public WorkspaceViewLogic Logic { get; init; } = WorkspaceViewLogic.All;
    public IReadOnlyList<WorkspaceViewPredicate> Predicates { get; init; } = [];
    public IReadOnlyList<WorkspaceViewFilter> Groups { get; init; } = [];
}

/// <summary>Whitelisted sorting, filtering, grouping and pagination options.</summary>
public sealed record WorkspaceViewQuery
{
    public IReadOnlyList<WorkspaceViewItemKind> Kinds { get; init; } = [];
    public string? ScopeIdentity { get; init; }
    public bool IncludeNestedScopes { get; init; }
    public WorkspaceViewFilter? Filter { get; init; }
    public string SortBy { get; init; } = "title";
    public bool Descending { get; init; }
    public string? GroupBy { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 100;
    public int MaxScannedItems { get; init; } = 25000;
}

/// <summary>One deterministic group and its visible count.</summary>
public sealed record WorkspaceViewGroup(string Label, int Count);

/// <summary>A paginated read result with explicit truncation and diagnostics.</summary>
public sealed record WorkspaceViewQueryResult(
    IReadOnlyList<WorkspaceViewItem> Items,
    IReadOnlyList<WorkspaceViewGroup> Groups,
    int TotalMatches,
    int ScannedItems,
    bool IsTruncated);

/// <summary>
/// Executes bounded, deterministic queries on projections; it never
/// mutates Markdown, the identity registry or any projection's source.
/// </summary>
public static class WorkspaceViewQueryEngine
{
    private const int MaxFilterDepth = 8;
    private const int MaxFilterNodes = 80;
    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "kind", "scope", "tags", "date", "status", "priority", "owner",
        "project", "application", "completed", "category", "incident", "lifecycle",
        "incomplete", "description"
    };

    /// <summary>
    /// Filters, orders, groups and pages the supplied read-only workspace items.
    /// Values with missing fields cannot satisfy ordinary comparisons, including NotEquals.
    /// </summary>
    /// <param name="items">Existing projected records.</param>
    /// <param name="query">Validated query controls.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Bounded page and counts, with a truncation signal if scans were capped.</returns>
    public static WorkspaceViewQueryResult Execute(
        IEnumerable<WorkspaceViewItem> items, WorkspaceViewQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 500 ||
            query.Offset is < 0 or > 100000 ||
            query.MaxScannedItems is < 1 or > 100000)
        {
            throw new ArgumentOutOfRangeException(nameof(query),
                "Limites attendues : 1–500 lignes, offset 0–100000, maximum 100000 éléments consultés.");
        }

        EnsureField(query.SortBy);
        if (query.GroupBy is string group)
        {
            EnsureField(group);
        }

        int complexity = 0;
        if (query.Filter is WorkspaceViewFilter filter)
        {
            ValidateFilter(filter, 1, ref complexity);
        }

        List<WorkspaceViewItem> matches = [];
        int scanned = 0;
        bool truncated = false;
        HashSet<WorkspaceViewItemKind> kinds = query.Kinds.ToHashSet();
        foreach (WorkspaceViewItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scanned >= query.MaxScannedItems)
            {
                truncated = true;
                break;
            }

            scanned++;
            if (kinds.Count > 0 && !kinds.Contains(item.Kind))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(query.ScopeIdentity) &&
                !ScopeMatches(item.ScopeIdentity, query.ScopeIdentity,
                    query.IncludeNestedScopes))
            {
                continue;
            }

            if (query.Filter is not null && !Matches(item, query.Filter, cancellationToken))
            {
                continue;
            }

            matches.Add(item);
        }

        List<WorkspaceViewGroup> groups = [];
        if (query.GroupBy is string groupField)
        {
            groups.AddRange(matches.GroupBy(item => GroupLabel(item, groupField),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new WorkspaceViewGroup(group.Key, group.Count()))
                .OrderBy(group => group.Label, StringComparer.OrdinalIgnoreCase));
        }

        matches.Sort((left, right) =>
        {
            int result = CompareFields(left, right, query.SortBy);
            if (query.Descending)
            {
                result = -result;
            }

            return result != 0 ? result : string.Compare(
                left.Key, right.Key, StringComparison.Ordinal);
        });
        return new WorkspaceViewQueryResult(
            matches.Skip(query.Offset).Take(query.Limit).ToArray(),
            groups,
            matches.Count,
            scanned,
            truncated);
    }

    /// <summary>Checks stable-scoped results without using ambiguous substring matches.</summary>
    /// <param name="source">The indexed source scope.</param>
    /// <param name="target">Requested scope.</param>
    /// <param name="includeNested">Whether hierarchical child identities are allowed.</param>
    /// <returns>True when the scope is an exact/explicit hierarchical match.</returns>
    private static bool ScopeMatches(string source, string target, bool includeNested)
    {
        return string.Equals(source, target, StringComparison.OrdinalIgnoreCase) ||
            (includeNested && source.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Rejects unrecognized fields and executable expressions before processing.</summary>
    /// <param name="field">Field name.</param>
    private static void EnsureField(string field)
    {
        if (!KnownFields.Contains(field))
        {
            throw new ArgumentException("Champ de vue inconnu : " + field);
        }
    }

    /// <summary>Recursively validates bounded query complexity before execution.</summary>
    /// <param name="filter">Current AND/OR group.</param>
    /// <param name="depth">Current nesting depth.</param>
    /// <param name="count">Total node budget, passed by reference.</param>
    private static void ValidateFilter(WorkspaceViewFilter filter, int depth, ref int count)
    {
        count++;
        if (depth > MaxFilterDepth || count > MaxFilterNodes)
        {
            throw new ArgumentException("Filtre trop complexe : profondeur ou nombre de conditions dépassé.");
        }

        foreach (WorkspaceViewPredicate predicate in filter.Predicates)
        {
            count++;
            if (count > MaxFilterNodes)
            {
                throw new ArgumentException("Trop de prédicats de vue.");
            }

            EnsureField(predicate.Field);
            if (predicate.Operator == WorkspaceViewOperator.Between &&
                (predicate.Date is null || predicate.EndDate is null ||
                    predicate.Date > predicate.EndDate))
            {
                throw new ArgumentException("Intervalle de dates invalide.");
            }

            if (predicate.Operator is WorkspaceViewOperator.Before or WorkspaceViewOperator.After &&
                predicate.Date is null)
            {
                throw new ArgumentException("Une comparaison de date exige une date.");
            }

            if (predicate.Operator == WorkspaceViewOperator.HasTag &&
                !predicate.Field.Equals("tags", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("HasTag est réservé au champ tags.");
            }
        }

        foreach (WorkspaceViewFilter child in filter.Groups)
        {
            ValidateFilter(child, depth + 1, ref count);
        }
    }

    /// <summary>Evaluates a Boolean group with cancellation and short-circuiting.</summary>
    /// <param name="item">Projected item.</param>
    /// <param name="filter">Safe validated group.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Whether the projected item matches.</returns>
    private static bool Matches(
        WorkspaceViewItem item, WorkspaceViewFilter filter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<bool> checks = filter.Predicates.Select(predicate => Match(item, predicate))
            .Concat(filter.Groups.Select(child => Matches(item, child, cancellationToken)));
        return filter.Logic == WorkspaceViewLogic.All
            ? checks.All(value => value)
            : checks.Any(value => value);
    }

    /// <summary>Compares one typed field, with absent values remaining absent.</summary>
    /// <param name="item">Projected item.</param>
    /// <param name="predicate">Validated filter condition.</param>
    /// <returns>Match result.</returns>
    private static bool Match(WorkspaceViewItem item, WorkspaceViewPredicate predicate)
    {
        if (predicate.Operator == WorkspaceViewOperator.HasTag)
        {
            return predicate.Text is not null && item.Tags.Contains(
                predicate.Text, StringComparer.OrdinalIgnoreCase);
        }

        WorkspaceViewField? field = GetField(item, predicate.Field);
        if (predicate.Operator == WorkspaceViewOperator.IsEmpty)
        {
            return field is null;
        }

        if (predicate.Operator == WorkspaceViewOperator.IsNotEmpty)
        {
            return field is not null;
        }

        if (field is null)
        {
            return false;
        }

        if (predicate.Operator is WorkspaceViewOperator.Before or
            WorkspaceViewOperator.After or WorkspaceViewOperator.Between)
        {
            if (field.Kind != WorkspaceViewFieldKind.Date || field.Date is not DateOnly date ||
                predicate.Date is not DateOnly target)
            {
                return false;
            }

            return predicate.Operator switch
            {
                WorkspaceViewOperator.Before => date < target,
                WorkspaceViewOperator.After => date > target,
                _ => predicate.EndDate is DateOnly end && date >= target && date <= end
            };
        }

        if (field.Kind == WorkspaceViewFieldKind.Boolean)
        {
            if (predicate.Boolean is not bool expected || field.Boolean is not bool actual)
            {
                return false;
            }

            return predicate.Operator switch
            {
                WorkspaceViewOperator.Equals => actual == expected,
                WorkspaceViewOperator.NotEquals => actual != expected,
                _ => false
            };
        }

        if (field.Kind == WorkspaceViewFieldKind.Date)
        {
            return predicate.Date is DateOnly expected && field.Date is DateOnly actual &&
                (predicate.Operator == WorkspaceViewOperator.Equals && actual == expected ||
                 predicate.Operator == WorkspaceViewOperator.NotEquals && actual != expected);
        }

        if (field.Kind != WorkspaceViewFieldKind.Text ||
            field.Text is not string text || predicate.Text is not string value)
        {
            return false;
        }

        return predicate.Operator switch
        {
            WorkspaceViewOperator.Equals =>
                string.Equals(text, value, StringComparison.OrdinalIgnoreCase),
            WorkspaceViewOperator.NotEquals =>
                !string.Equals(text, value, StringComparison.OrdinalIgnoreCase),
            WorkspaceViewOperator.Contains =>
                text.Contains(value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>Gets a built-in or custom field without injecting placeholder values.</summary>
    /// <param name="item">Projected item.</param>
    /// <param name="name">Case-insensitive field.</param>
    /// <returns>Present field or null.</returns>
    private static WorkspaceViewField? GetField(WorkspaceViewItem item, string name)
    {
        if (name.Equals("title", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceViewField.OfText(item.Title);
        }

        if (name.Equals("kind", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceViewField.OfText(item.Kind.ToString());
        }

        if (name.Equals("scope", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceViewField.OfText(item.ScopeIdentity);
        }

        if (name.Equals("tags", StringComparison.OrdinalIgnoreCase))
        {
            return item.Tags.Count > 0 ? WorkspaceViewField.OfText(string.Join(", ", item.Tags)) : null;
        }

        return item.Fields.TryGetValue(name, out WorkspaceViewField? value)
            ? value : null;
    }

    /// <summary>Compares typed fields consistently, putting missing values last.</summary>
    /// <param name="left">First result.</param>
    /// <param name="right">Second result.</param>
    /// <param name="name">Sort field.</param>
    /// <returns>Stable typed comparison.</returns>
    private static int CompareFields(WorkspaceViewItem left, WorkspaceViewItem right, string name)
    {
        WorkspaceViewField? first = GetField(left, name);
        WorkspaceViewField? second = GetField(right, name);
        if (first is null) return second is null ? 0 : 1;
        if (second is null) return -1;
        if (first.Kind == second.Kind)
        {
            return first.Kind switch
            {
                WorkspaceViewFieldKind.Date => Nullable.Compare(first.Date, second.Date),
                WorkspaceViewFieldKind.Boolean => Nullable.Compare(first.Boolean, second.Boolean),
                WorkspaceViewFieldKind.Number => Nullable.Compare(first.Number, second.Number),
                _ => string.Compare(first.Text, second.Text, StringComparison.OrdinalIgnoreCase)
            };
        }

        return first.Kind.CompareTo(second.Kind);
    }

    /// <summary>Groups an absent value under a visible, explicit unknown label.</summary>
    /// <param name="item">Projected item.</param>
    /// <param name="field">Grouping field.</param>
    /// <returns>Stable group label.</returns>
    private static string GroupLabel(WorkspaceViewItem item, string field)
    {
        WorkspaceViewField? value = GetField(item, field);
        return value is null ? "(Non renseigné)" : value.Kind switch
        {
            WorkspaceViewFieldKind.Date => value.Date?.ToString("yyyy-MM-dd") ?? "(Non renseigné)",
            WorkspaceViewFieldKind.Boolean => value.Boolean == true ? "Oui" : "Non",
            WorkspaceViewFieldKind.Number => value.Number?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "(Non renseigné)",
            _ => value.Text ?? "(Non renseigné)"
        };
    }
}
