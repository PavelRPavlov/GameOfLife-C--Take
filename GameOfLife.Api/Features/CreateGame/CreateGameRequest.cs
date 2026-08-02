namespace GameOfLife.Api.Features.CreateGame;

/// <summary>
/// The <c>POST /game</c> request. Every field is hard-required <em>except</em> <see cref="Rule"/>,
/// which falls back to the configured <see cref="GameOptions.DefaultRule"/> when omitted; every other
/// field the caller must specify exactly. Unknown/extra properties are rejected
/// (<see cref="JsonUnmappedMemberHandlingAttribute"/>), so a malformed or mis-versioned request fails
/// loudly. Fields are nullable purely so a <em>missing</em> field is distinguishable from a supplied
/// value and caught by <see cref="RequiredAttribute"/> (or, for <see cref="Rule"/>, defaulted).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateGameRequest : IValidatableObject
{
    /// <summary>Base64 of exactly 1250 bytes = 100×100 bits, row-major, MSB-first, 1 = alive. All-dead allowed.</summary>
    [Required(ErrorMessage = ErrorMessages.SeedRequired)]
    public string? Seed { get; init; }

    /// <summary>Torus coordinate of the grid's top-left cell (row 0, col 0).</summary>
    [Required(ErrorMessage = ErrorMessages.OriginRequired)]
    public CellDto? Origin { get; init; }

    /// <summary>true → created directly Running; false → held at generation 0 as Created.</summary>
    [Required(ErrorMessage = ErrorMessages.AutoStartRequired)]
    public bool? AutoStart { get; init; }

    /// <summary>
    /// B/S rulestring, e.g. "B3/S23". B0 is rejected. Optional: when omitted, the server applies the
    /// configured <see cref="GameOptions.DefaultRule"/>.
    /// </summary>
    public string? Rule { get; init; }

    /// <summary>Generations per second, inclusive range 0.1 .. 200.</summary>
    [Required(ErrorMessage = ErrorMessages.TickRateRequired)]
    public double? TickRate { get; init; }

    /// <summary>Minimum accepted <see cref="TickRate"/> (gen/sec).</summary>
    public const double MinTickRate = 0.1;

    /// <summary>Maximum accepted <see cref="TickRate"/> (gen/sec). Raised to 200 for high-speed testing.</summary>
    public const double MaxTickRate = 200.0;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] already flags missing fields; here we validate the format/range of present ones.
        // Member names are the C# property names; the endpoint projects them to camelCase field keys.
        if (Seed is not null && !SeedGrid.TryDecode(Seed, out _))
            yield return new ValidationResult(ErrorMessages.SeedInvalid, [nameof(Seed)]);

        if (Origin is not null && !TryParseOrigin(Origin, out _, out _))
            yield return new ValidationResult(ErrorMessages.OriginInvalid, [nameof(Origin)]);

        if (Rule is not null && !Core.Rule.TryParse(Rule, out _))
            yield return new ValidationResult(ErrorMessages.RuleInvalid, [nameof(Rule)]);

        if (TickRate is { } rate && (rate < MinTickRate || rate > MaxTickRate))
            yield return new ValidationResult(ErrorMessages.TickRateInvalid, [nameof(TickRate)]);
    }

    /// <summary>
    /// Projects a request that has already passed validation into the domain values the engine needs.
    /// A missing <see cref="Rule"/> falls back to <paramref name="options"/>.<see cref="GameOptions.DefaultRule"/>
    /// (itself validated at startup). Throws if called on an unvalidated/invalid request.
    /// </summary>
    public GameParameters ToParameters(GameOptions options)
    {
        if (!SeedGrid.TryDecode(Seed!, out var seedBytes))
            throw new InvalidOperationException("ToParameters called on an invalid request (seed).");
        if (!TryParseOrigin(Origin!, out var originX, out var originY))
            throw new InvalidOperationException("ToParameters called on an invalid request (origin).");

        var cells = SeedGrid.ToCells(seedBytes, originX, originY);
        var rule = Core.Rule.Parse(Rule ?? options.DefaultRule);
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
