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
    public void Given_a_result_When_matched_with_side_effects_Then_the_matching_action_runs_for_each_case()
    {
        string? ranWith = null;

        Result<int, GameError>.Ok(7).Match(v => ranWith = $"ok:{v}", _ => ranWith = "err");
        Assert.Equal("ok:7", ranWith);

        Result<int, GameError>.Err(new GameError.NoGame("no game")).Match(_ => ranWith = "ok", e => ranWith = $"err:{e.Message}");
        Assert.Equal("err:no game", ranWith);
    }
}
