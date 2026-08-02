using GameOfLife.Core;
using GameOfLife.Api.Errors;

namespace GameOfLife.Api.Tests;

/// <summary>
/// Unit tests for the one dynamic error message, <see cref="ErrorMessages.InvalidStateForVerb"/>. The
/// Created/Running arms are covered through the HTTP control matrix; this pins the Paused arm and the
/// defensive fallback (NoGame is unreachable through the real state machine, so it can't be driven over
/// HTTP — but the switch still has to name it).
/// </summary>
public class ErrorMessagesTests
{
    [Theory]
    [InlineData(GameStatus.Created, "waiting to start")]
    [InlineData(GameStatus.Running, "running")]
    [InlineData(GameStatus.Paused, "paused")]
    [InlineData(GameStatus.NoGame, "not running")] // defensive fallback for the "no live game" case
    public void Given_a_game_status_When_InvalidStateForVerb_is_formatted_Then_it_names_the_current_state_in_friendly_words(GameStatus status, string expected)
    {
        var message = ErrorMessages.InvalidStateForVerb(status);

        Assert.Contains(expected, message);
    }
}
