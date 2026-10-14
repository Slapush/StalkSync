using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace StalkSync;

/// <summary>
/// Copie locale pour l'outil de debug (StalkSyncDebug.exe), en UDP sur 127.0.0.1:4446 :
/// chaque paquet OutGauge reçu (préfixe « OG ») et chaque décision de StalkSync (préfixe « EV », en JSON).
/// Rien ne sort de l'ordinateur ; sans outil de debug ouvert, les envois sont simplement perdus.
/// </summary>
public sealed class DebugMirror : IDisposable
{
    public const int Port = 4446;
    private static readonly IPEndPoint Target = new(IPAddress.Loopback, Port);
    private readonly UdpClient _udp = new(AddressFamily.InterNetwork);
    private readonly object _lock = new();

    public void Packet(byte[] outGauge)
    {
        var data = new byte[outGauge.Length + 2];
        data[0] = (byte)'O';
        data[1] = (byte)'G';
        Buffer.BlockCopy(outGauge, 0, data, 2, outGauge.Length);
        Send(data);
    }

    public void Event(long timeMs, string type, object? details = null)
    {
        var json = JsonSerializer.Serialize(new { t = timeMs, type, details });
        Send(Encoding.UTF8.GetBytes("EV" + json));
    }

    private void Send(byte[] data)
    {
        try
        {
            lock (_lock) _udp.Send(data, data.Length, Target);
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose() => _udp.Dispose();
}
