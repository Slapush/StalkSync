using StalkSync.Core;
using Xunit;

public class BlinkerControllerTests
{
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
        Assert.Equal(BlinkerAction.TapLeft, c.Update(0, true, false, true));
        Assert.Equal(BlinkerAction.None, c.Update(1000, true, false, true));
        Assert.Equal(Side.Left, c.Assumed);
        Assert.Equal(BlinkerAction.TapLeft, c.Update(2000, false, false, true));
        Assert.Equal(BlinkerAction.None, c.Update(3000, false, false, true));
        Assert.Equal(Side.Off, c.Assumed);
    }

    [Fact]
    public void Switches_side_directly()
    {
        var c = Make(StalkMode.Latching);
        c.Update(0, true, false, true);
        Assert.Equal(BlinkerAction.TapRight, c.Update(2000, false, true, true));
        Assert.Equal(Side.Right, c.Assumed);
    }

    [Fact]
    public void Waits_for_game_focus_then_catches_up()
    {
        var c = Make(StalkMode.Latching);
        Assert.Equal(BlinkerAction.None, c.Update(0, true, false, false));
        Assert.Equal(Side.Off, c.Assumed);
        Assert.Equal(BlinkerAction.TapLeft, c.Update(100, true, false, true));
    }

    [Fact]
    public void Respects_cooldown_between_taps()
    {
        var c = Make(StalkMode.Latching);
        Assert.Equal(BlinkerAction.TapLeft, c.Update(0, true, false, true));
        Assert.Equal(BlinkerAction.None, c.Update(100, false, true, true));
        Assert.Equal(BlinkerAction.TapRight, c.Update(400, false, true, true));
    }

    [Theory]
    [InlineData(StalkMode.Latching)]
    [InlineData(StalkMode.Momentary)]
    public void Short_press_gives_comfort_blinks(StalkMode mode)
    {
        var c = Make(mode);
        Assert.Equal(BlinkerAction.TapLeft, c.Update(0, true, false, true));
        Assert.Equal(BlinkerAction.None, c.Update(200, false, false, true));
        Assert.True(c.ComfortActive);
        Assert.Equal(BlinkerAction.None, c.Update(2000, false, false, true));
        Assert.Equal(BlinkerAction.TapLeft, c.Update(2200, false, false, true));
        Assert.Equal(Side.Off, c.Desired);
        Assert.Equal(Side.Off, c.Assumed);
    }

    [Fact]
    public void Comfort_disabled_short_press_turns_off()
    {
        var c = Make(StalkMode.Latching, s => s.ComfortEnabled = false);
        c.Update(0, true, false, true);
        c.Update(200, false, false, true);
        Assert.Equal(Side.Off, c.Desired);
    }

    [Fact]
    public void Latching_full_press_through_half_position_stays_on()
    {
        // Mi-course puis à fond : le commodo peut retomber un instant au centre.
        var c = Make(StalkMode.Latching);
        c.Update(0, true, false, true);
        c.Update(60, false, false, true);
        c.Update(90, true, false, true);
        Assert.Equal(BlinkerAction.None, c.Update(10_000, true, false, true));
        Assert.Equal(Side.Left, c.Desired);
        Assert.False(c.ComfortActive);
    }

    [Fact]
    public void Momentary_long_press_latches_then_second_press_cancels()
    {
        var c = Make(StalkMode.Momentary);
        Assert.Equal(BlinkerAction.TapRight, c.Update(0, false, true, true));
        c.Update(800, false, false, true);
        Assert.Equal(BlinkerAction.None, c.Update(10_000, false, false, true));
        Assert.Equal(BlinkerAction.TapRight, c.Update(11_000, false, true, true));
        Assert.Equal(BlinkerAction.None, c.Update(11_100, false, false, true));
        Assert.Equal(Side.Off, c.Desired);
    }

    [Fact]
    public void Keeps_assumed_state_when_recreated()
    {
        var c = new BlinkerController(new BlinkerSettings(), Side.Left);
        Assert.Equal(BlinkerAction.TapLeft, c.Update(0, false, false, true));
    }

    [Fact]
    public void Pulse_stalk_remembers_last_position()
    {
        var p = new PulseStalk();
        Assert.Equal(Side.Off, p.Update(false, false, false));
        Assert.Equal(Side.Left, p.Update(true, false, false));
        Assert.Equal(Side.Left, p.Update(false, false, false));
        Assert.Equal(Side.Right, p.Update(false, true, false));
        Assert.Equal(Side.Right, p.Update(false, false, false));
        Assert.Equal(Side.Off, p.Update(false, false, true));
        Assert.Equal(Side.Off, p.Update(false, false, false));
    }

    private static BlinkerAction Step(BlinkerController c, PulseStalk p, long t, bool left, bool center)
    {
        var pos = p.Update(left, false, center);
        return c.Update(t, pos == Side.Left, pos == Side.Right, true);
    }

    [Fact]
    public void Positions_mode_full_press_stays_until_center_pulse()
    {
        var c = Make(StalkMode.Positions);
        var p = new PulseStalk();
        Assert.Equal(BlinkerAction.TapLeft, Step(c, p, 0, true, false));
        Step(c, p, 150, false, false);
        Assert.Equal(BlinkerAction.None, Step(c, p, 10_000, false, false));
        Assert.Equal(Side.Left, c.Desired);
        Assert.Equal(BlinkerAction.TapLeft, Step(c, p, 10_100, false, true));
        Assert.Equal(Side.Off, c.Desired);
    }

    [Fact]
    public void Positions_mode_quick_return_to_center_gives_comfort()
    {
        var c = Make(StalkMode.Positions);
        var p = new PulseStalk();
        Step(c, p, 0, true, false);
        Step(c, p, 150, false, false);
        Step(c, p, 250, false, true);
        Assert.True(c.ComfortActive);
        Assert.Equal(Side.Left, c.Desired);
    }
}
