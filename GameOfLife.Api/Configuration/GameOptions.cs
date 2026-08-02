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

    /// <summary>
    /// The unsigned integer coordinate type that fixes the torus size (2^width per axis): e.g.
    /// <c>UInt64</c> for the default 2^64 world, or <c>Byte</c>/<c>UInt16</c>/<c>UInt32</c> for a
    /// smaller, wrap-testable one. Validated at startup against the wrap-capable types
    /// (<see cref="GameOfLife.Core.Universe.TryParseCoordinateType"/>): only unsigned integer types
    /// wrap with a single mask like <see cref="ulong"/> does, so anything else fails the host at boot.
    /// </summary>
    public string CoordinateType { get; init; } = "UInt64";
}
