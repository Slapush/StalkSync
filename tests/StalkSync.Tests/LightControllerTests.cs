using StalkSync.Core;
using Xunit;

public class LightControllerTests
{
    private static readonly LightKeys Trucks = new([LightState.Off, LightState.Parking, LightState.Low], true, true, true);
    private static readonly LightKeys BeamNg = new([LightState.Off, LightState.Low, LightState.High], true, false, true);

    [Fact]
    public void Light_stalk_remembers_ring_and_beam_positions()
    {
        var s = new LightStalk();
        s.Update(false, false, true, false, false, false);
        s.Update(false, false, false, false, false, false);
        Assert.Equal(LightState.Low, s.Ring);
        s.Update(false, false, false, true, false, false);
        s.Update(false, false, false, false, false, false);
        Assert.Equal(BeamPosition.High, s.Beam);
        s.Update(false, false, false, false, false, true);
        Assert.Equal(BeamPosition.Neutral, s.Beam);
        Assert.Equal(LightState.Low, s.Ring);
    }

    [Fact]
    public void Trucks_cycle_key_reaches_low_beam_in_two_presses()
    {
        var c = new LightController();
        Assert.Equal(LightAction.Cycle, c.Update(0, LightState.Low, BeamPosition.Neutral, Trucks, true));
        Assert.Equal(LightAction.None, c.Update(100, LightState.Low, BeamPosition.Neutral, Trucks, true));
        Assert.Equal(LightAction.Cycle, c.Update(300, LightState.Low, BeamPosition.Neutral, Trucks, true));
        Assert.Equal(LightAction.None, c.Update(600, LightState.Low, BeamPosition.Neutral, Trucks, true));
        Assert.Equal(LightState.Low, c.AssumedCycle);
    }

    [Fact]
    public void Trucks_cycle_wraps_around_to_switch_off()
    {
        var c = new LightController(assumedCycle: LightState.Low);
        Assert.Equal(LightAction.Cycle, c.Update(0, LightState.Off, BeamPosition.Neutral, Trucks, true));
        Assert.Equal(LightState.Off, c.AssumedCycle);
    }

    [Fact]
    public void Trucks_high_beam_uses_its_own_toggle()
    {
        var c = new LightController(assumedCycle: LightState.Low);
        Assert.Equal(LightAction.HighToggle, c.Update(0, LightState.Low, BeamPosition.High, Trucks, true));
        Assert.Equal(LightAction.None, c.Update(500, LightState.Low, BeamPosition.High, Trucks, true));
        Assert.Equal(LightAction.HighToggle, c.Update(1000, LightState.Low, BeamPosition.Neutral, Trucks, true));
        Assert.False(c.AssumedHigh);
    }

    [Fact]
    public void BeamNg_high_beam_is_the_next_step_of_the_cycle()
    {
        var c = new LightController(assumedCycle: LightState.Low);
        Assert.Equal(LightAction.Cycle, c.Update(0, LightState.Low, BeamPosition.High, BeamNg, true));
        Assert.Equal(LightState.High, c.AssumedCycle);
        // Retour au neutre : pleins phares → éteint → croisement.
        Assert.Equal(LightAction.Cycle, c.Update(1000, LightState.Low, BeamPosition.Neutral, BeamNg, true));
        Assert.Equal(LightAction.Cycle, c.Update(2000, LightState.Low, BeamPosition.Neutral, BeamNg, true));
        Assert.Equal(LightState.Low, c.AssumedCycle);
    }

    [Fact]
    public void BeamNg_parking_lights_mean_off()
    {
        var c = new LightController(assumedCycle: LightState.Low);
        Assert.Equal(LightAction.Cycle, c.Update(0, LightState.Parking, BeamPosition.Neutral, BeamNg, true));
        Assert.Equal(LightAction.Cycle, c.Update(1000, LightState.Parking, BeamPosition.Neutral, BeamNg, true));
        Assert.Equal(LightState.Off, c.AssumedCycle);
    }

    [Fact]
    public void Flash_holds_key_and_releases_on_focus_loss()
    {
        var c = new LightController();
        Assert.Equal(LightAction.FlashPress, c.Update(0, LightState.Off, BeamPosition.Flash, Trucks, true));
        Assert.Equal(LightAction.None, c.Update(5000, LightState.Off, BeamPosition.Flash, Trucks, true));
        Assert.Equal(LightAction.FlashRelease, c.Update(5100, LightState.Off, BeamPosition.Flash, Trucks, false));
        Assert.Equal(LightAction.FlashPress, c.Update(5200, LightState.Off, BeamPosition.Flash, Trucks, true));
        Assert.Equal(LightAction.FlashRelease, c.Update(5300, LightState.Off, BeamPosition.Neutral, Trucks, true));
    }

    [Fact]
    public void Missing_keys_send_nothing()
    {
        var none = new LightKeys([LightState.Off, LightState.Low], false, false, false);
        var c = new LightController();
        Assert.Equal(LightAction.None, c.Update(0, LightState.Low, BeamPosition.Flash, none, true));
        Assert.Equal(LightAction.None, c.Update(1000, LightState.Low, BeamPosition.High, none, true));
    }

    [Fact]
    public void Nothing_sent_while_game_not_focused()
    {
        var c = new LightController();
        Assert.Equal(LightAction.None, c.Update(0, LightState.Low, BeamPosition.High, Trucks, false));
        Assert.Equal(LightState.Off, c.AssumedCycle);
    }
}
