namespace Nodalis.Core.Decisions;

/// <summary>
/// Describes the lifecycle state interpreted from a Decision Record status.
/// </summary>
public enum DecisionLifecycleState
{
    /// <summary>
    /// The status is not one of the lifecycle values understood by Nodalis.
    /// </summary>
    Other,

    /// <summary>
    /// The decision is currently applicable.
    /// </summary>
    Active,

    /// <summary>
    /// The decision has been replaced by another Decision Record.
    /// </summary>
    Superseded,

    /// <summary>
    /// The decision is no longer recommended and has no automatic replacement.
    /// </summary>
    Deprecated
}
