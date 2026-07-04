namespace DispatchTiger.Models
{
    /// <summary>
    /// Represents the status of a job in the dispatch system.
    /// </summary>
    public enum DispatchStatus
    {
        Unassigned,
        Assigned,
        InProgress,
        Completed,
        Cancelled
    }
}
