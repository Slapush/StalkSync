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
/// Un fil dédié lit chaque paquet dès son arrivée : à 400 paquets/s, aucun n'est perdu ni lu en retard.
/// </summary>
public sealed class OutGauge : IDisposable
{
    public const int OffDashLights = 40, OffShowLights = 44, MinSize = 92;
    private const uint DlFullBeam = 1 << 1, DlSignalL = 1 << 5, DlSignalR = 1 << 6, DlSpare = 1 << 11;

    // Les clignotants arrivent en clignotant : un côté est actif s'il s'est allumé récemment.
    private const int BlinkWindowMs = 1200;
    private const int StaleMs = 1000;

    private readonly int _port;
    private readonly Func<long> _clock;
    private readonly Action<byte[]>? _onPacket;
    private readonly object _lock = new();
    private readonly Queue<long> _arrivals = new();
    private readonly Thread _thread;
    private volatile bool _running = true;
    private UdpClient? _udp;
    private long _lastPacket = -1_000_000, _lastLeft = -1_000_000, _lastRight = -1_000_000;
    private uint _show, _dash;

    /// <param name="clock">Horloge en millisecondes, la même que celle du moteur.</param>
    /// <param name="onPacket">Appelé pour chaque paquet reçu (relais vers l'outil de debug).</param>
    public OutGauge(int port, Func<long> clock, Action<byte[]>? onPacket = null)
    {
        _port = port;
        _clock = clock;
        _onPacket = onPacket;
        _thread = new Thread(Receive) { IsBackground = true, Name = "OutGauge" };
        _thread.Start();
    }

    public int Port => _port;
    public string? Error { get; private set; }

    private void Receive()
    {
        while (_running)
        {
            try
            {
                if (_udp == null)
                {
                    _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, _port));
                    _udp.Client.ReceiveBufferSize = 1 << 20;
                    Error = null;
                }
                IPEndPoint? from = null;
                var data = _udp.Receive(ref from);
                if (data.Length < MinSize) continue;
                long now = _clock();
                uint dash = BitConverter.ToUInt32(data, OffDashLights), show = BitConverter.ToUInt32(data, OffShowLights);
                lock (_lock)
                {
                    _dash = dash;
                    _show = show;
                    _lastPacket = now;
                    _arrivals.Enqueue(now);
                    while (_arrivals.Count > 0 && now - _arrivals.Peek() > 1000) _arrivals.Dequeue();
                    if ((show & DlSignalL) != 0) _lastLeft = now;
                    if ((show & DlSignalR) != 0) _lastRight = now;
                }
                _onPacket?.Invoke(data);
            }
            catch (SocketException e) when (_running)
            {
                // Port pris ou socket fermée : on réessaie un peu plus tard.
                if (_udp == null) Error = $"port {_port} indisponible ({e.SocketErrorCode})";
                _udp?.Dispose();
                _udp = null;
                Thread.Sleep(3000);
            }
            catch (ObjectDisposedException) { return; }
        }
    }

    /// <summary>Dernier état du véhicule, ou null si BeamNG n'envoie rien.</summary>
    public BeamNgState? Read(long nowMs)
    {
        lock (_lock)
        {
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
    }

    public void Dispose()
    {
        _running = false;
        _udp?.Dispose();
        _thread.Join(500);
    }
}
