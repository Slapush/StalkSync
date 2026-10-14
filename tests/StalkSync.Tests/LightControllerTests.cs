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
    public void Flash_follows_the_held_button_and_never_sticks()
    {
        var s = new LightStalk();
        s.Update(false, false, false, false, true, false);
        Assert.Equal(BeamPosition.Flash, s.Beam);
        s.Update(false, false, false, false, true, false);
        Assert.Equal(BeamPosition.Flash, s.Beam);
        // Bouton d'appel relâché sans impulsion de retour (manquée) : on revient quand même au neutre.
        s.Update(false, false, false, false, false, false);
        Assert.Equal(BeamPosition.Neutral, s.Beam);
    }

    [Fact]
    public void Moza_flash_then_return_pulse_in_same_poll()
    {
        // Rapport du 14/10 : bouton 6 relâché et bouton 5 appuyé au même instant.
        var s = new LightStalk();
        s.Update(false, false, false, false, true, false);
        s.Update(false, false, false, false, false, true);
        Assert.Equal(BeamPosition.Neutral, s.Beam);
    }

    [Fact]
    public void Flash_from_high_beam_returns_to_neutral()
    {
        var s = new LightStalk();
        s.Update(false, false, false, true, false, false);
        s.Update(false, false, false, false, false, false);
        Assert.Equal(BeamPosition.High, s.Beam);
        s.Update(false, false, false, false, true, false);
        Assert.Equal(BeamPosition.Flash, s.Beam);
        s.Update(false, false, false, false, false, true);
        Assert.Equal(BeamPosition.Neutral, s.Beam);
    }

    [Fact]
    public void Light_key_waits_after_flash_release()
    {
        // Rapport du 14/10 : N appuyé 15 ms après la fin de l'appel, BeamNG l'avait perdu.
        var c = new LightController(assumedCycle: LightState.Low);
        Assert.Equal(LightAction.FlashPress, c.Update(0, LightState.Low, BeamPosition.Flash, BeamNg, true));
        Assert.Equal(LightAction.FlashRelease, c.Update(5000, LightState.Low, BeamPosition.High, BeamNg, true));
        Assert.Equal(LightAction.None, c.Update(5015, LightState.Low, BeamPosition.High, BeamNg, true));
        Assert.Equal(LightAction.None, c.Update(5200, LightState.Low, BeamPosition.High, BeamNg, true));
        Assert.Equal(LightAction.Cycle, c.Update(5300, LightState.Low, BeamPosition.High, BeamNg, true));
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

    /// <summary>Envoie la même valeur de télémétrie toutes les 50 ms, de <paramref name="from"/> à <paramref name="to"/>.</summary>
    private static void Feed(LightController c, LightKeys keys, long from, long to, bool high, bool? low = null)
    {
        for (long t = from; t <= to; t += 50) c.Observe(t, keys.Cycle, high, low);
    }

    [Fact]
    public void Telemetry_high_beam_corrects_beamng_cycle()
    {
        var c = new LightController();
        Feed(c, BeamNg, 0, 1000, high: true);
        Assert.Equal(LightState.High, c.AssumedCycle);
        Feed(c, BeamNg, 1050, 2000, high: false);
        Assert.Equal(LightState.Low, c.AssumedCycle);
        Feed(c, BeamNg, 2050, 3000, high: false, low: false);
        Assert.Equal(LightState.Off, c.AssumedCycle);
    }

    [Fact]
    public void BeamNg_high_to_low_is_not_undone_by_slow_telemetry()
    {
        // Pleins phares, levier ramené au neutre : deux appuis (pleins phares → éteint → croisement).
        var c = new LightController(assumedCycle: LightState.High);
        Feed(c, BeamNg, 0, 1000, high: true);
        long t = 1000;
        Assert.Equal(LightAction.Cycle, c.Update(t, LightState.Low, BeamPosition.Neutral, BeamNg, true));
        Assert.Equal(LightAction.Cycle, c.Update(t + 150, LightState.Low, BeamPosition.Neutral, BeamNg, true));
        // Télémétrie lente : elle annonce encore les pleins phares pendant 1,2 s après les appuis.
        for (long u = t + 150; u <= t + 1350; u += 50)
        {
            c.Observe(u, BeamNg.Cycle, true, null);
            Assert.Equal(LightAction.None, c.Update(u, LightState.Low, BeamPosition.Neutral, BeamNg, true));
        }
        Feed(c, BeamNg, t + 1400, t + 3000, high: false);
        Assert.Equal(LightAction.None, c.Update(t + 3000, LightState.Low, BeamPosition.Neutral, BeamNg, true));
        Assert.Equal(LightState.Low, c.AssumedCycle);
    }

    [Fact]
    public void Telemetry_contradiction_wins_when_a_press_was_lost()
    {
        var c = new LightController(assumedCycle: LightState.Low);
        Assert.Equal(LightAction.Cycle, c.Update(0, LightState.Low, BeamPosition.High, BeamNg, true));
        // Le jeu n'a pas vu l'appui : les pleins phares restent éteints bien au-delà du délai.
        Feed(c, BeamNg, 0, 3000, high: false);
        Assert.Equal(LightState.Low, c.AssumedCycle);
        Assert.Equal(LightAction.Cycle, c.Update(3000, LightState.Low, BeamPosition.High, BeamNg, true));
    }

    [Fact]
    public void Telemetry_ignored_during_flash()
    {
        var c = new LightController(assumedCycle: LightState.Low);
        Assert.Equal(LightAction.FlashPress, c.Update(0, LightState.Low, BeamPosition.Flash, BeamNg, true));
        Feed(c, BeamNg, 0, 3000, high: true);
        Assert.Equal(LightState.Low, c.AssumedCycle);
    }

    [Fact]
    public void Telemetry_sets_separate_high_beam_state()
    {
        var c = new LightController();
        Feed(c, Trucks, 0, 1000, high: true);
        Assert.True(c.AssumedHigh);
    }
}
