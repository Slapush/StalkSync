using System.Net;
using System.Net.Sockets;
using StalkSync.Core;

namespace StalkSync;

/// <summary>État du véhicule lu dans BeamNG.drive via le protocole OutGauge.</summary>
/// <param name="Rate">Paquets reçus par seconde.</param>
public readonly record struct BeamNgState(Side Signal, bool Hazard, bool LeftLit, bool RightLit, bool High, bool? Low, uint ShowLights, int Rate);

/// <summary>
/// Reçoit les paquets OutGauge que BeamNG.drive envoie en UDP (Options → Autres → Protocoles → OutGauge,
/// adresse 127.0.0.1, port 4444). Lecture seule : rien n'est envoyé au jeu.
/// </summary>
public sealed class OutGauge : IDisposable
{
    private const int OffDashLights = 40, OffShowLights = 44, MinSize = 92;
    private const uint DlFullBeam = 1 << 1, DlSignalL = 1 << 5, DlSignalR = 1 << 6, DlSpare = 1 << 11;

    // Les clignotants arrivent en clignotant : un côté est actif s'il s'est allumé récemment.
    private const int BlinkWindowMs = 1200;
    private const int StaleMs = 1000;

    private readonly int _port;
    private UdpClient? _udp;
    private long _nextTry, _lastPacket = -1_000_000, _lastLeft = -1_000_000, _lastRight = -1_000_000;
    private uint _show, _dash;
    private readonly Queue<long> _arrivals = new();

    public OutGauge(int port) => _port = port;

    public int Port => _port;
    public string? Error { get; private set; }

    /// <summary>Lit les paquets arrivés ; renvoie l'état du véhicule, ou null si BeamNG n'envoie rien.</summary>
    public BeamNgState? Read(long nowMs)
    {
        if (_udp == null)
        {
            if (nowMs < _nextTry) return null;
            _nextTry = nowMs + 3000;
            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, _port));
                Error = null;
            }
            catch (SocketException e)
            {
                Error = $"port {_port} indisponible ({e.SocketErrorCode})";
                return null;
            }
        }

        try
        {
            while (_udp.Available > 0)
            {
                IPEndPoint? from = null;
                var data = _udp.Receive(ref from);
                if (data.Length < MinSize) continue;
                _dash = BitConverter.ToUInt32(data, OffDashLights);
                _show = BitConverter.ToUInt32(data, OffShowLights);
                _lastPacket = nowMs;
                _arrivals.Enqueue(nowMs);
                if ((_show & DlSignalL) != 0) _lastLeft = nowMs;
                if ((_show & DlSignalR) != 0) _lastRight = nowMs;
            }
        }
        catch (SocketException) { }

        while (_arrivals.Count > 0 && nowMs - _arrivals.Peek() > 1000) _arrivals.Dequeue();
        if (nowMs - _lastPacket > StaleMs) return null;
        bool left = nowMs - _lastLeft < BlinkWindowMs, right = nowMs - _lastRight < BlinkWindowMs;
        // Croisement : seulement si le jeu annonce le voyant (bit « spare »), sinon inconnu.
        bool? low = (_dash & DlSpare) != 0 ? (_show & DlSpare) != 0 : null;
        return new BeamNgState(
            Signal: left && !right ? Side.Left : right && !left ? Side.Right : Side.Off,
            Hazard: left && right,
            LeftLit: (_show & DlSignalL) != 0,
            RightLit: (_show & DlSignalR) != 0,
            High: (_show & DlFullBeam) != 0,
            Low: low,
            ShowLights: _show,
            Rate: _arrivals.Count);
    }

    public void Dispose()
    {
        _udp?.Dispose();
        _udp = null;
    }
}
