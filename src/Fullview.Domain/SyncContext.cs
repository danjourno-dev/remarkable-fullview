namespace Fullview.Domain;

/// <summary>
/// Every entity belongs to exactly one context; the device filters screens to whichever
/// mode it's currently in (B3). Meals/Shopping/Recipes are implicitly Personal.
/// </summary>
public enum SyncContext
{
    Personal,
    Work
}
