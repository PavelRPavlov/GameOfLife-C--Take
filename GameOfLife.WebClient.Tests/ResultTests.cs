using GameOfLife.WebClient.Communication;

namespace GameOfLife.WebClient.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Given_an_Ok_result_When_accessed_and_matched_Then_it_carries_the_value_and_runs_the_success_arm()
    {
        var result = Result<int, GameError>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal("ok:42", result.Match(v => $"ok:{v}", _ => "err"));
    }

    [Fact]
    public void Given_an_Err_result_When_accessed_and_matched_Then_it_carries_the_error_and_runs_the_failure_arm()
    {
        var result = Result<int, GameError>.Err(new GameError.NoGame("no game"));

        Assert.True(result.IsError);
        Assert.IsType<GameError.NoGame>(result.Error);
        Assert.Equal("err", result.Match(v => $"ok:{v}", _ => "err"));
    }

    [Fact]
    public void Given_a_mismatched_result_When_accessing_Value_on_a_failure_or_Error_on_a_success_Then_it_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Result<int, GameError>.Err(new GameError.NoGame("no game")).Value);
        Assert.Throws<InvalidOperationException>(() => Result<int, GameError>.Ok(1).Error);
    }

    [Fact]
    public void Given_a_result_When_mapped_Then_success_is_transformed_and_failure_passes_through()
    {
        Assert.Equal(10, Result<int, GameError>.Ok(5).Map(v => v * 2).Value);
        Assert.IsType<GameError.Forbidden>(
            Result<int, GameError>.Err(new GameError.Forbidden("forbidden")).Map(v => v * 2).Error);
    }

    [Fact]
    public void Given_a_result_When_bound_Then_success_chains_and_failure_short_circuits()
    {
        var chained = Result<int, GameError>.Ok(5).Bind(v => Result<string, GameError>.Ok($"n{v}"));
        Assert.Equal("n5", chained.Value);

        var shorted = Result<int, GameError>.Err(new GameError.InvalidState("invalid state"))
            .Bind(v => Result<string, GameError>.Ok($"n{v}"));
        Assert.IsType<GameError.InvalidState>(shorted.Error);
    }

    [Fact]
    public void Given_a_result_When_matched_with_side_effects_Then_the_matching_action_runs_for_each_case()
    {
        string? ranWith = null;

        Result<int, GameError>.Ok(7).Match(v => ranWith = $"ok:{v}", _ => ranWith = "err");
        Assert.Equal("ok:7", ranWith);

        Result<int, GameError>.Err(new GameError.NoGame("no game")).Match(_ => ranWith = "ok", e => ranWith = $"err:{e.Message}");
        Assert.Equal("err:no game", ranWith);
    }
}
