namespace GameOfLife.Api.Configuration;

/// <summary>
/// Backend behaviour bound from the <c>"Game"</c> configuration section. Every member is
/// environment-overridable via <c>appsettings.{Environment}.json</c>; values are validated once at
/// startup (see <see cref="OptionsRegistration"/>), so an invalid <c>appsettings</c> crashes the app
/// at boot rather than failing per-request.
/// </summary>
public sealed class GameOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Game";

    /// <summary>
    /// B/S rulestring applied when a <c>POST /game</c> request omits <c>rule</c> (e.g. <c>B3/S23</c>).
    /// Validated as a parseable, non-<c>B0</c> rule at startup.
    /// </summary>
    public string DefaultRule { get; init; } = "";

    /// <summary>Server-wide broadcast cadence in milliseconds (a coalesced net snapshot-diff per interval).</summary>
    public int BroadcastIntervalMs { get; init; }
}
