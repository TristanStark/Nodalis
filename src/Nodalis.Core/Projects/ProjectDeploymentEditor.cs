namespace Nodalis.Core.Projects;

/// <summary>
/// Human-only deployment journal commands; each command creates a new
/// immutable snapshot rather than rewriting an older attempt or event.
/// </summary>
public static class ProjectDeploymentEditor
{
    /// <summary>Creates an in-memory proposal, never writes a source file.</summary>
    /// <param name="projectId">Canonical owner project.</param>
    /// <param name="reference">Reviewed MEP reference.</param>
    /// <param name="title">Human-readable title.</param>
    /// <returns>Empty proposed operation.</returns>
    public static ProjectDeploymentOperation Create(
        Guid projectId, string reference, string title)
    {
        ProjectDeploymentOperation proposed = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Reference = reference,
            Title = title,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        ProjectDeploymentMarkdownCodec.Validate(proposed);
        return proposed;
    }

    /// <summary>
    /// Freezes one explicitly reviewed attempt, including real observed
    /// pre-deployment version and human declaration of gate review.
    /// This does not independently verify or grant deployment permission.
    /// </summary>
    /// <param name="operation">Current complete operation.</param>
    /// <param name="attempt">Fully populated human-reviewed attempt.</param>
    /// <returns>Extended operation, leaving original attempts untouched.</returns>
    public static ProjectDeploymentOperation StartAttempt(
        ProjectDeploymentOperation operation, ProjectDeploymentAttempt attempt)
    {
        ProjectDeploymentMarkdownCodec.Validate(operation);
        if (operation.Closure is not null)
        {
            throw new InvalidDataException("MEP clôturée : aucune nouvelle tentative.");
        }

        ArgumentNullException.ThrowIfNull(attempt);
        ProjectDeploymentOperation changed = operation with
        {
            Attempts = [.. operation.Attempts, attempt]
        };
        ProjectDeploymentMarkdownCodec.Validate(changed);
        return changed;
    }

    /// <summary>Appends a user-declared event to its active attempt.</summary>
    /// <param name="operation">Existing event ledger.</param>
    /// <param name="action">New event with stable identity and human context.</param>
    /// <returns>Extended operation, without changing earlier events.</returns>
    public static ProjectDeploymentOperation AppendEvent(
        ProjectDeploymentOperation operation,
        ProjectDeploymentAttemptEvent action)
    {
        ProjectDeploymentMarkdownCodec.Validate(operation);
        if (operation.Closure is not null)
        {
            throw new InvalidDataException("MEP clôturée : aucun nouvel événement.");
        }

        ArgumentNullException.ThrowIfNull(action);
        ProjectDeploymentOperation changed = operation with
        {
            Events = [.. operation.Events, action]
        };
        ProjectDeploymentMarkdownCodec.Validate(changed);
        return changed;
    }

    /// <summary>
    /// Freezes an explicit human business verdict without touching history.
    /// </summary>
    /// <param name="operation">Validated, not yet closed operation.</param>
    /// <param name="closure">Reviewed closure declaration.</param>
    /// <returns>In-memory immutable proposal; no source write.</returns>
    public static ProjectDeploymentOperation Close(
        ProjectDeploymentOperation operation, ProjectDeploymentClosure closure)
    {
        ProjectDeploymentMarkdownCodec.Validate(operation);
        ArgumentNullException.ThrowIfNull(closure);
        if (operation.Closure is not null)
        {
            throw new InvalidDataException("Un bilan MEP est déjà enregistré.");
        }

        ProjectDeploymentOperation changed = operation with { Closure = closure };
        ProjectDeploymentMarkdownCodec.Validate(changed);
        return changed;
    }


    /// <summary>
    /// Appends an explicitly recorded operational handover before final
    /// business closure. Earlier reviews are never mutated or inferred.
    /// </summary>
    /// <param name="operation">Validated canonical MEP ledger.</param>
    /// <param name="review">Human review of seven operational control areas.</param>
    /// <returns>New in-memory operation, without any source file writes.</returns>
    public static ProjectDeploymentOperation ReviewHandover(
        ProjectDeploymentOperation operation, ProjectDeploymentHandoverReview review)
    {
        ProjectDeploymentMarkdownCodec.Validate(operation);
        ArgumentNullException.ThrowIfNull(review);
        if (operation.Closure is not null)
        {
            throw new InvalidDataException(
                "MEP clôturée : aucune nouvelle passation.");
        }

        ProjectDeploymentOperation proposed = operation with
        {
            HandoverReviews = [.. (operation.HandoverReviews ?? []), review]
        };
        ProjectDeploymentMarkdownCodec.Validate(proposed);
        return proposed;
    }

}
