using StalkSync.Core;
using Xunit;

public class BlinkerControllerTests
{
    private static GameState Game(Side active = Side.Off, bool lit = false, float steer = 0, bool paused = false, bool hazard = false) =>
        new(true, paused, active == Side.Left, active == Side.Right,
            lit && active == Side.Left, lit && active == Side.Right, hazard, steer);

    private static readonly GameState NoTelemetry = default;

    private static BlinkerController Make(StalkMode mode, Action<BlinkerSettings>? tweak = null)
    {
        var s = new BlinkerSettings { Mode = mode };
        tweak?.Invoke(s);
        return new BlinkerController(s);
    }

    [Fact]
    public void Latching_follows_stalk_position()
    {
        var c = Make(StalkMode.Latching);
        Assert.Equal(BlinkerAction.TapLeft, c.Update(0, true, false, Game(), true));
        Assert.Equal(BlinkerAction.None, c.Update(500, true, false, Game(Side.Left), true));
        Assert.Equal(BlinkerAction.TapLeft, c.Update(2000, false, false, Game(Side.Left), true));
        Assert.Equal(BlinkerAction.None, c.Update(2500, false, false, Game(), true));
    }

    [Fact]
    public void Does_not_tap_twice_while_telemetry_lags()
    {
        var c = Make(StalkMode.Latching);
        Assert.Equal(BlinkerAction.TapLeft, c.Update(0, true, false, Game(), true));
        Assert.Equal(BlinkerAction.None, c.Update(100, true, false, Game(), true));
        Assert.Equal(BlinkerAction.TapLeft, c.Update(400, true, false, Game(), true));
    }

    [Fact]
    public void Latching_reasserts_when_game_cancels()
    {
        var c = Make(StalkMode.Latching);
        c.Update(0, true, false, Game(), true);
        c.Update(500, true, false, Game(Side.Left), true);
        Assert.Equal(BlinkerAction.TapLeft, c.Update(5000, true, false, Game(), true));
    }

    [Fact]
    public void Does_not_fight_keyboard_when_stalk_is_centered()
    {
        var c = Make(StalkMode.Latching);
        Assert.Equal(BlinkerAction.None, c.Update(0, false, false, Game(Side.Right), true));
        Assert.Equal(BlinkerAction.None, c.Update(1000, false, false, Game(Side.Right), true));
    }

    [Fact]
    public void Switches_side_directly()
    {
        var c = Make(StalkMode.Latching);
        c.Update(0, true, false, Game(), true);
        c.Update(1000, true, false, Game(Side.Left), true);
        Assert.Equal(BlinkerAction.TapRight, c.Update(2000, false, true, Game(Side.Left), true));
    }

    [Theory]
    [InlineData(StalkMode.Latching)]
    [InlineData(StalkMode.Momentary)]
    public void Short_press_gives_three_blinks(StalkMode mode)
    {
        var c = Make(mode);
        Assert.Equal(BlinkerAction.TapLeft, c.Update(0, true, false, Game(), true));
        Assert.Equal(BlinkerAction.None, c.Update(200, false, false, Game(Side.Left), true));
        long t = 300;
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(BlinkerAction.None, c.Update(t, false, false, Game(Side.Left, lit: true), true));
            t += 350;
            var a = c.Update(t, false, false, Game(Side.Left), true);
            t += 350;
            Assert.Equal(i == 2 ? BlinkerAction.TapLeft : BlinkerAction.None, a);
        }
        Assert.Equal(Side.Off, c.Desired);
    }

    [Fact]
    public void Momentary_long_press_latches_then_second_press_cancels()
    {
        var c = Make(StalkMode.Momentary);
        Assert.Equal(BlinkerAction.TapRight, c.Update(0, false, true, Game(), true));
        c.Update(800, false, false, Game(Side.Right), true);
        Assert.Equal(BlinkerAction.None, c.Update(10_000, false, false, Game(Side.Right), true));
        Assert.Equal(BlinkerAction.TapRight, c.Update(11_000, false, true, Game(Side.Right), true));
        Assert.Equal(BlinkerAction.None, c.Update(11_100, false, false, Game(Side.Right), true));
        Assert.Equal(BlinkerAction.None, c.Update(12_000, false, false, Game(), true));
        Assert.Equal(Side.Off, c.Desired);
    }

    [Fact]
    public void Momentary_cancels_after_the_turn()
    {
        var c = Make(StalkMode.Momentary);
        c.Update(0, true, false, Game(), true);
        c.Update(800, false, false, Game(Side.Left), true);
        Assert.Equal(BlinkerAction.None, c.Update(2000, false, false, Game(Side.Left, steer: 0.5f), true));
        Assert.Equal(BlinkerAction.TapLeft, c.Update(4000, false, false, Game(Side.Left, steer: 0.01f), true));
    }

    [Fact]
    public void Momentary_steering_the_other_way_does_not_cancel()
    {
        var c = Make(StalkMode.Momentary);
        c.Update(0, true, false, Game(), true);
        c.Update(800, false, false, Game(Side.Left), true);
        c.Update(2000, false, false, Game(Side.Left, steer: -0.5f), true);
        Assert.Equal(BlinkerAction.None, c.Update(4000, false, false, Game(Side.Left), true));
        Assert.Equal(Side.Left, c.Desired);
    }

    [Fact]
    public void Momentary_follows_game_side_cancel()
    {
        var c = Make(StalkMode.Momentary, s => s.AutoCancelEnabled = false);
        c.Update(0, true, false, Game(), true);
        c.Update(800, false, false, Game(Side.Left), true);
        Assert.Equal(BlinkerAction.None, c.Update(5000, false, false, Game(), true));
        Assert.Equal(Side.Off, c.Desired);
    }

    [Fact]
    public void Works_without_telemetry()
    {
        var c = Make(StalkMode.Latching);
        Assert.Equal(BlinkerAction.TapLeft, c.Update(0, true, false, NoTelemetry, true));
        Assert.Equal(BlinkerAction.None, c.Update(1000, true, false, NoTelemetry, true));
        Assert.Equal(BlinkerAction.TapLeft, c.Update(2000, false, false, NoTelemetry, true));
        Assert.Equal(BlinkerAction.None, c.Update(3000, false, false, NoTelemetry, true));
    }

    [Fact]
    public void Sends_nothing_when_paused_hazard_or_game_not_focused()
    {
        var c = Make(StalkMode.Latching);
        Assert.Equal(BlinkerAction.None, c.Update(0, true, false, Game(paused: true), true));
        Assert.Equal(BlinkerAction.None, c.Update(10, true, false, Game(hazard: true), true));
        Assert.Equal(BlinkerAction.None, c.Update(20, true, false, Game(), false));
        Assert.Equal(BlinkerAction.TapLeft, c.Update(30, true, false, Game(), true));
    }
}
