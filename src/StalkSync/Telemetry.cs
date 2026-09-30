using System.IO.MemoryMappedFiles;
using StalkSync.Core;

namespace StalkSync;

/// <summary>
/// Lit la mémoire partagée du plugin de télémétrie scs-sdk-plugin (RenCloud), révision 12.
/// Les décalages viennent de scs-telemetry-common.hpp ; lecture seule, rien n'est écrit vers le jeu.
/// </summary>
public sealed class TelemetryReader : IDisposable
{
    private const string MapName = @"Local\SCSTelemetry";
    private const int OffSdkActive = 0, OffPaused = 4, OffGame = 52;
    private const int OffSpeed = 948, OffUserSteer = 956;
    private const int OffBlinkerLeftActive = 1578, OffBlinkerRightActive = 1579;
    private const int OffBlinkerLeftOn = 1580, OffBlinkerRightOn = 1581, OffHazard = 1588;

    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private long _nextTry;

    /// <summary>0 = inconnu, 1 = ETS2, 2 = ATS.</summary>
    public uint Game { get; private set; }
    public float SpeedKmh { get; private set; }

    public GameState Read(long nowMs)
    {
        if (_view == null)
        {
            if (nowMs < _nextTry) return default;
            _nextTry = nowMs + 2000;
            try
            {
                _map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
                _view = _map.CreateViewAccessor(0, 2048, MemoryMappedFileAccess.Read);
            }
            catch (Exception e) when (e is FileNotFoundException or UnauthorizedAccessException or IOException)
            {
                Close();
                return default;
            }
        }

        if (!_view.ReadBoolean(OffSdkActive))
        {
            // Jeu fermé : on relâche la mémoire pour pouvoir la rouvrir proprement au prochain lancement.
            Close();
            return default;
        }

        Game = _view.ReadUInt32(OffGame);
        SpeedKmh = _view.ReadSingle(OffSpeed) * 3.6f;
        return new GameState(
            Connected: true,
            Paused: _view.ReadBoolean(OffPaused),
            LeftActive: _view.ReadBoolean(OffBlinkerLeftActive),
            RightActive: _view.ReadBoolean(OffBlinkerRightActive),
            LeftLit: _view.ReadBoolean(OffBlinkerLeftOn),
            RightLit: _view.ReadBoolean(OffBlinkerRightOn),
            Hazard: _view.ReadBoolean(OffHazard),
            Steer: _view.ReadSingle(OffUserSteer));
    }

    private void Close()
    {
        _view?.Dispose();
        _map?.Dispose();
        _view = null;
        _map = null;
    }

    public void Dispose() => Close();
}
