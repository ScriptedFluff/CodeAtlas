namespace CodeAtlas.Core.Plugins;

/// <summary>
/// Security override policy controlling whether user-tier parsers can override app/system baseline parsers.
/// </summary>
public enum ParserOverridePolicy
{
    /// <summary>
    /// Strict (Default): App-level parsers are immutable; user directory parsers cannot shadow existing IDs/extensions.
    /// </summary>
    Strict,

    /// <summary>
    /// AllowAll: Primary (User) parsers take precedence over Fallback (App) parsers.
    /// </summary>
    AllowAll,

    /// <summary>
    /// AllowList: Only parser IDs explicitly listed in <see cref="ParserSecuritySettings.AllowedOverrides"/> can override.
    /// </summary>
    AllowList
}

/// <summary>
/// Status of a discovered parser candidate after security policy evaluation.
/// </summary>
public enum ParserRegistrationStatus
{
    Active,
    ActiveOverride,
    BlockedShadowing,
    Disabled,
    Failed
}

/// <summary>
/// Configuration settings for parser security and anti-hijacking protection.
/// </summary>
public sealed record ParserSecuritySettings
{
    public ParserOverridePolicy OverridePolicy { get; init; } = ParserOverridePolicy.Strict;
    public HashSet<string> AllowedOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool WarnOnShadowAttempt { get; init; } = true;
}

/// <summary>
/// Represents a discovered parser candidate before or during registration.
/// </summary>
public sealed record DiscoveredParserCandidate(
    string Id,
    string Language,
    IReadOnlyList<string> FileExtensions,
    string AssemblyPath,
    string DirectoryPath,
    ParserSourceTier SourceTier,
    bool IsDisabledFolder = false,
    string? Version = null);

/// <summary>
/// Result of evaluating a parser candidate against security policy.
/// </summary>
public sealed record ParserSecurityEvaluation(
    DiscoveredParserCandidate Candidate,
    ParserRegistrationStatus Status,
    string? Reason = null);

/// <summary>
/// Evaluates parser registration candidates against anti-shadowing security policies.
/// </summary>
public static class ParserSecurityEvaluator
{
    /// <summary>
    /// Evaluates whether a new candidate parser should be registered, allowed as an override, or blocked for shadowing.
    /// </summary>
    public static ParserSecurityEvaluation Evaluate(
        DiscoveredParserCandidate candidate,
        IReadOnlyCollection<DiscoveredParserCandidate> existingActiveCandidates,
        ParserSecuritySettings settings)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existingActiveCandidates);
        ArgumentNullException.ThrowIfNull(settings);

        if (candidate.IsDisabledFolder)
        {
            return new ParserSecurityEvaluation(candidate, ParserRegistrationStatus.Disabled, "Parser is located in a DisabledParsers folder");
        }

        // Check if there is any collision with an existing higher-precedence / App-tier parser
        var collision = existingActiveCandidates.FirstOrDefault(existing =>
            existing.SourceTier == ParserSourceTier.App &&
            (string.Equals(existing.Id, candidate.Id, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(existing.Language, candidate.Language, StringComparison.OrdinalIgnoreCase) ||
             existing.FileExtensions.Intersect(candidate.FileExtensions, StringComparer.OrdinalIgnoreCase).Any()));

        if (collision == null || candidate.SourceTier == ParserSourceTier.App)
        {
            return new ParserSecurityEvaluation(candidate, ParserRegistrationStatus.Active);
        }

        // A user/env parser is colliding with an App-tier parser
        switch (settings.OverridePolicy)
        {
            case ParserOverridePolicy.AllowAll:
                return new ParserSecurityEvaluation(
                    candidate,
                    ParserRegistrationStatus.ActiveOverride,
                    $"Overriding app-level parser '{collision.Id}' under AllowAll policy");

            case ParserOverridePolicy.AllowList:
                if (settings.AllowedOverrides.Contains(candidate.Id) || settings.AllowedOverrides.Contains(candidate.Language))
                {
                    return new ParserSecurityEvaluation(
                        candidate,
                        ParserRegistrationStatus.ActiveOverride,
                        $"Overriding app-level parser '{collision.Id}' under AllowList policy");
                }
                return new ParserSecurityEvaluation(
                    candidate,
                    ParserRegistrationStatus.BlockedShadowing,
                    $"Parser '{candidate.Id}' blocked by AllowList policy (not present in AllowedOverrides list)");

            case ParserOverridePolicy.Strict:
            default:
                return new ParserSecurityEvaluation(
                    candidate,
                    ParserRegistrationStatus.BlockedShadowing,
                    $"Parser '{candidate.Id}' blocked by Strict security policy (App-level parser '{collision.Id}' takes precedence)");
        }
    }
}
