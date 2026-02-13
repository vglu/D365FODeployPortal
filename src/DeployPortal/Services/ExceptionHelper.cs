using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace DeployPortal.Services;

/// <summary>
/// Builds a user-friendly message from exceptions (including EF Core DbUpdateException).
/// </summary>
public static class ExceptionHelper
{
    /// <summary>
    /// Returns a detailed error message including inner exception and, for DbUpdateException,
    /// the underlying database error (e.g. constraint name).
    /// </summary>
    public static string GetDisplayMessage(Exception ex)
    {
        if (ex == null) return "Unknown error.";

        var msg = ex.Message;
        var inner = ex.InnerException;

        if (ex is DbUpdateException dbEx)
        {
            // EF Core wraps the real DB error in InnerException (e.g. SqliteException).
            if (inner != null)
                msg = inner.Message;
            // Optionally append which entities failed (useful for debug).
            if (dbEx.Entries.Count > 0)
            {
                var entities = string.Join(", ", dbEx.Entries.Select(e => e.Entity.GetType().Name));
                msg = $"{msg} (entities: {entities})";
            }
        }
        else if (inner != null)
        {
            msg = $"{msg} → {inner.Message}";
            if (inner.InnerException != null)
                msg += $" → {inner.InnerException.Message}";
        }

        return msg;
    }
}
