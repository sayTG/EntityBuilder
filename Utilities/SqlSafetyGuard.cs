using System.Text.RegularExpressions;

namespace EntityBuilder.Utilities;

// Defence-in-depth guard for any code path that accepts SQL from the client (Send Report,
// Schedule Report, Update Scheduled Report). The query builder itself emits parameterised,
// server-side SQL — this guard exists to reject anything a compromised/tampered client posts.
// Not a substitute for parameterisation of WHERE values (already in place); this just
// prevents an authenticated user from turning a "run this SELECT" pipeline into a write path.
public static class SqlSafetyGuard
{
    // Word-boundary match on anything that mutates state or opens execution paths.
    // INTO catches SELECT ... INTO <table> which materialises a new table.
    private static readonly Regex DisallowedKeywords = new(
        @"\b(?:INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|INTO|xp_[a-z0-9_]+|sp_[a-z0-9_]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (bool Ok, string? Reason) EnsureSafeSelect(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return (false, "SQL is empty.");

        var trimmed = sql.Trim().TrimEnd(';').Trim();

        // Statement stacking → refuse.
        if (trimmed.Contains(';', StringComparison.Ordinal))
            return (false, "Multiple SQL statements are not allowed.");

        // SQL comments are a common obfuscation vector for hiding payload from naive scans.
        if (trimmed.Contains("--", StringComparison.Ordinal) || trimmed.Contains("/*", StringComparison.Ordinal))
            return (false, "SQL comments (-- and /* */) are not allowed.");

        // Must be a read query.
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            return (false, "Only SELECT or WITH ... SELECT queries are allowed.");

        var match = DisallowedKeywords.Match(trimmed);
        if (match.Success)
            return (false, $"Disallowed keyword: {match.Value.ToUpperInvariant()}.");

        return (true, null);
    }
}
