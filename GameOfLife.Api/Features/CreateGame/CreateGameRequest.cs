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

    /// <summary>Generations per second, inclusive range 60 .. 250.</summary>
    [Required(ErrorMessage = ErrorMessages.TickRateRequired)]
    public double? TickRate { get; init; }

    /// <summary>Minimum accepted <see cref="TickRate"/> (gen/sec).</summary>
    public const double MinTickRate = 60.0;

    /// <summary>
    /// Maximum accepted <see cref="TickRate"/> (gen/sec). Delivery is advance-driven — one broadcast per
    /// generation — so the broadcast rate simply equals the tick rate and this ceiling is, in effect, the
    /// cap on messages/sec pushed to every observer. The tick rate is the only such lever; there is no
    /// separate broadcast cadence to configure.
    /// </summary>
    public const double MaxTickRate = 250.0;

    // Decoded once by Validate() on the way through the DataAnnotations pipeline and consumed by
    // ToParameters(), so the seed and origin are parsed a single time per request rather than twice.
    // Populated only on a successful decode; null otherwise, which is how ToParameters() detects a
    // request that never passed validation.
    private byte[]? _decodedSeed;
    private (ulong X, ulong Y)? _decodedOrigin;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] already flags missing fields; here we validate the format/range of present ones and,
        // on success, memoise the decoded seed/origin so ToParameters need not decode them a second time.
        // Member names are the C# property names; the endpoint projects them to camelCase field keys.
        var results = new List<ValidationResult>();

        if (Seed is not null)
        {
            if (SeedGrid.TryDecode(Seed, out var seedBytes))
                _decodedSeed = seedBytes;
            else
                results.Add(new ValidationResult(ErrorMessages.SeedInvalid, [nameof(Seed)]));
        }

        if (Origin is not null)
        {
            if (TryParseOrigin(Origin, out var originX, out var originY))
                _decodedOrigin = (originX, originY);
            else
                results.Add(new ValidationResult(ErrorMessages.OriginInvalid, [nameof(Origin)]));
        }

        if (Rule is not null && !Core.Rule.TryParse(Rule, out _))
            results.Add(new ValidationResult(ErrorMessages.RuleInvalid, [nameof(Rule)]));

        if (TickRate is { } rate && (rate < MinTickRate || rate > MaxTickRate))
            results.Add(new ValidationResult(ErrorMessages.TickRateInvalid, [nameof(TickRate)]));

        return results;
    }

    /// <summary>
    /// Projects a request that has already passed validation into the domain values the engine needs,
    /// reusing the seed and origin that <see cref="Validate"/> decoded rather than decoding them again.
    /// A missing <see cref="Rule"/> falls back to <paramref name="options"/>.<see cref="GameOptions.DefaultRule"/>
    /// (itself validated at startup). Throws if called on a request that has not passed validation (the
    /// memoised seed/origin are absent), which over HTTP the endpoint's validate-first flow prevents.
    /// </summary>
    public GameParameters ToParameters(GameOptions options)
    {
        if (_decodedSeed is null || _decodedOrigin is not { } origin)
            throw new InvalidOperationException("ToParameters called on a request that has not passed validation.");

        var cells = SeedGrid.ToCells(_decodedSeed, origin.X, origin.Y);
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
