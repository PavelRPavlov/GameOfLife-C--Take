namespace GameOfLife.WebClient.Communication.Seeding;

/// <summary>The cells parsed from a text pattern, and whether any fell outside the 100×100 board.</summary>
public sealed record SeedParse(IReadOnlyList<(int X, int Y)> Cells, bool Clamped);

/// <summary>
/// Decodes the two interchange formats the seed editor accepts (#16): <b>RLE</b> (<c>bo$2bo$3o!</c>)
/// and <b>plaintext</b> (rows of <c>.</c>/<c>O</c>). Coordinates land in board space <c>(x = col,
/// y = row)</c>; anything past the 100×100 window is dropped and flagged via
/// <see cref="SeedParse.Clamped"/> so the UI can warn. Never throws — malformed input yields whatever
/// cells were recoverable (possibly none).
/// </summary>
public static class SeedText
{
    private const int Size = SeedBoard.Size;

    public static SeedParse Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new SeedParse([], false);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // RLE header/comment lines start with '#' or carry an 'x = …, y = …' dimension line.
        var bodyLines = lines.Where(l => !l.TrimStart().StartsWith('#') && !l.Contains('='));
        var body = string.Concat(bodyLines).Where(c => !char.IsWhiteSpace(c)).ToArray();
        var bodyText = new string(body);

        // RLE is exactly the token grammar [0-9bo$!] with at least one row/terminator marker. Plaintext
        // (rows of '.'/'O', optional '!'-comment lines) fails the all-tokens test, so it falls through.
        var looksRle = bodyText.Length > 0
            && bodyText.All(c => c is 'b' or 'o' or '$' or '!' || char.IsDigit(c))
            && (bodyText.Contains('$') || bodyText.Contains('!'));

        return looksRle ? ParseRle(bodyText) : ParsePlain(lines);
    }

    private static SeedParse ParseRle(string body)
    {
        var cells = new List<(int, int)>();
        var clamped = false;
        int x = 0, y = 0, count = 0;
        var hasCount = false;

        foreach (var ch in body)
        {
            if (char.IsDigit(ch))
            {
                count = count * 10 + (ch - '0');
                hasCount = true;
                continue;
            }

            var n = hasCount ? count : 1;
            count = 0;
            hasCount = false;

            switch (ch)
            {
                case 'b':
                    x += n;
                    break;
                case 'o':
                    for (var k = 0; k < n; k++)
                    {
                        if (Add(cells, x, y)) { } else clamped = true;
                        x++;
                    }
                    break;
                case '$':
                    y += n;
                    x = 0;
                    break;
                case '!':
                    return new SeedParse(cells, clamped);
            }
        }

        return new SeedParse(cells, clamped);
    }

    private static SeedParse ParsePlain(IReadOnlyList<string> lines)
    {
        var cells = new List<(int, int)>();
        var clamped = false;
        var row = 0;

        foreach (var line in lines)
        {
            // Plaintext (.cells) comment lines start with '!'.
            if (line.StartsWith('!')) continue;

            for (var col = 0; col < line.Length; col++)
            {
                if (line[col] is 'O' or 'o' or '*' or '1' or 'X' or 'x')
                    if (!Add(cells, col, row)) clamped = true;
            }
            row++;
        }

        return new SeedParse(cells, clamped);
    }

    private static bool Add(List<(int, int)> cells, int x, int y)
    {
        if ((uint)x >= Size || (uint)y >= Size) return false;
        cells.Add((x, y));
        return true;
    }
}
