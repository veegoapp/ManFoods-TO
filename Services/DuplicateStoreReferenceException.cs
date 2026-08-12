namespace MvcApp.Services;

/// <summary>
/// Thrown when a parsed Store Reference upload contains more than one row for
/// the same (StoreName, Month, Year). Each row can carry a different OC/OM/
/// Head Manager email, so an in-file duplicate could otherwise grant store
/// access to more people than intended — this must be rejected before any
/// database write happens, not silently deduplicated.
/// </summary>
public class DuplicateStoreReferenceException : InvalidOperationException
{
    public DuplicateStoreReferenceException(string message) : base(message) { }
}
