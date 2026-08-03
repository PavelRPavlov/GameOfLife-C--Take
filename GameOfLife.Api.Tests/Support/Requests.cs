using System.Text;

namespace GameOfLife.Api.Tests.Support;

/// <summary>Builds <c>POST /game</c> request bodies as raw JSON so tests control the exact shape.</summary>
public static class Requests
{
    /// <summary>A fully valid create body (all-dead seed, origin 0,0, held Created, B3/S23, 100 gen/s).</summary>
    public static string ValidCreate(
        string? seed = null,
        string originX = "0",
        string originY = "0",
        bool autoStart = false,
        string rule = "B3/S23",
        double tickRate = 100) =>
        $$"""
        {
          "seed": "{{seed ?? TestSeeds.AllDead()}}",
          "origin": { "x": "{{originX}}", "y": "{{originY}}" },
          "autoStart": {{(autoStart ? "true" : "false")}},
          "rule": "{{rule}}",
          "tickRate": {{tickRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
        }
        """;

    /// <summary>A valid create body with the <c>rule</c> field omitted, so the server applies its configured default.</summary>
    public static string ValidCreateWithoutRule(
        string? seed = null,
        string originX = "0",
        string originY = "0",
        bool autoStart = false,
        double tickRate = 100) =>
        $$"""
        {
          "seed": "{{seed ?? TestSeeds.AllDead()}}",
          "origin": { "x": "{{originX}}", "y": "{{originY}}" },
          "autoStart": {{(autoStart ? "true" : "false")}},
          "tickRate": {{tickRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
        }
        """;

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
