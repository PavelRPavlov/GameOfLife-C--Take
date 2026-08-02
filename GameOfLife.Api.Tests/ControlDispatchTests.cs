using GameOfLife.Core;
using GameOfLife.Api.Features.GameControl;
using GameOfLife.Api.Game;
using Microsoft.AspNetCore.Http;

namespace GameOfLife.Api.Tests;

/// <summary>
/// Unit tests for the control-verb HTTP edge (<see cref="ControlDispatch"/>). The four defined outcomes
/// are covered end-to-end by <see cref="ControlVerbTests"/>; this pins the defensive fallback for an
/// out-of-contract <see cref="ControlResult"/>, which the kernel never emits but the edge must not drop.
/// </summary>
public class ControlDispatchTests
{
    [Fact]
    public async Task Given_an_out_of_contract_control_result_When_the_dispatch_runs_Then_it_maps_to_a_500()
    {
        var context = new DefaultHttpContext();

        // Feed the edge an outcome outside the ControlResult contract — the switch's default arm.
        var result = await ControlDispatch.Run(
            _ => Task.FromResult(new ControlOutcome((ControlResult)999, GameStatus.NoGame, 0)),
            context);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }
}
