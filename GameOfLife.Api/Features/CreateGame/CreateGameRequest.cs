using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using GameOfLife.Api.Contracts;
using GameOfLife.Api.Game;
using GameOfLife.Core;

namespace GameOfLife.Api.Features.CreateGame;

/// <summary>
/// The <c>POST /game</c> request. Every field is hard-required with no server-side defaults, so
/// the world a caller gets is exactly and only what they specified. Unknown/extra properties are
/// rejected (<see cref="JsonUnmappedMemberHandlingAttribute"/>), so a malformed or mis-versioned
/// request fails loudly. Fields are nullable purely so a <em>missing</em> field is distinguishable
/// from a supplied value and caught by <see cref="RequiredAttribute"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateGameRequest : IValidatableObject
{
    /// <summary>Base64 of exactly 1250 bytes = 100×100 bits, row-major, MSB-first, 1 = alive. All-dead allowed.</summary>
    [Required]
    public string? Seed { get; init; }

    /// <summary>Torus coordinate of the grid's top-left cell (row 0, col 0).</summary>
    [Required]
    public CellDto? Origin { get; init; }

    /// <summary>true → created directly Running; false → held at generation 0 as Created.</summary>
    [Required]
    public bool? AutoStart { get; init; }

    /// <summary>B/S rulestring, e.g. "B3/S23". B0 is rejected.</summary>
    [Required]
    public string? Rule { get; init; }

    /// <summary>Generations per second, inclusive range 0.1 .. 200.</summary>
    [Required]
    public double? TickRate { get; init; }

    /// <summary>Minimum accepted <see cref="TickRate"/> (gen/sec).</summary>
    public const double MinTickRate = 0.1;

    /// <summary>Maximum accepted <see cref="TickRate"/> (gen/sec). Raised to 200 for high-speed testing.</summary>
    public const double MaxTickRate = 200.0;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] already flags missing fields; here we validate the format/range of present ones.
        if (Seed is not null && !SeedGrid.TryDecode(Seed, out _))
            yield return new ValidationResult(
                $"Seed must be base64 that decodes to exactly {SeedGrid.ByteLength} bytes.", [nameof(Seed)]);

        if (Origin is not null && !TryParseOrigin(Origin, out _, out _))
            yield return new ValidationResult(
                "Origin X and Y must each be a ulong decimal string (0 .. 18446744073709551615).", [nameof(Origin)]);

        if (Rule is not null && !Core.Rule.TryParse(Rule, out _))
            yield return new ValidationResult(
                "Rule must match B[0-8]*/S[0-8]* with unique digits per group and must not contain B0.", [nameof(Rule)]);

        if (TickRate is { } rate && (rate < MinTickRate || rate > MaxTickRate))
            yield return new ValidationResult(
                $"TickRate must be within {MinTickRate} .. {MaxTickRate} generations per second.", [nameof(TickRate)]);
    }

    /// <summary>
    /// Projects a request that has already passed validation into the domain values the engine needs.
    /// Throws if called on an unvalidated/invalid request.
    /// </summary>
    public GameParameters ToParameters()
    {
        if (!SeedGrid.TryDecode(Seed!, out var seedBytes))
            throw new InvalidOperationException("ToParameters called on an invalid request (seed).");
        if (!TryParseOrigin(Origin!, out var originX, out var originY))
            throw new InvalidOperationException("ToParameters called on an invalid request (origin).");

        var cells = SeedGrid.ToCells(seedBytes, originX, originY);
        var rule = Core.Rule.Parse(Rule!);
        return new GameParameters(cells, rule, TickRate!.Value, AutoStart!.Value);
    }

    private static bool TryParseOrigin(CellDto origin, out ulong x, out ulong y)
    {
        x = 0;
        y = 0;
        return origin.X is not null
               && origin.Y is not null
               && ulong.TryParse(origin.X, NumberStyles.None, CultureInfo.InvariantCulture, out x)
               && ulong.TryParse(origin.Y, NumberStyles.None, CultureInfo.InvariantCulture, out y);
    }
}
