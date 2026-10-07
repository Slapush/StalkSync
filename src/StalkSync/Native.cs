using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StalkSync;

/// <summary>Envoi de touches clavier simulées et détection de la fenêtre du jeu.</summary>
static class Native
{
    private const uint InputKeyboard = 1, KeyExtended = 0x1, KeyUp = 0x2, KeyScanCode = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    // L'union INPUT est dimensionnée par MOUSEINPUT ; le remplissage garantit la bonne taille en 64 bits.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeybdInput Key;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Barre de titre sombre (Windows 10 20H1 et plus récent ; ignoré ailleurs).</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
    }

    /// <summary>Nom lisible d'une touche selon la disposition du clavier (ex. « ; » plutôt que « OemSemicolon »).</summary>
    public static string KeyName(KeyBinding key)
    {
        uint vk = MapVirtualKey(key.ScanCode, 1);
        uint ch = vk == 0 ? 0 : MapVirtualKey(vk, 2) & 0x7FFF;
        if (ch > ' ') return char.ToUpperInvariant((char)ch).ToString();
        return key.Name;
    }

    public static void TapKey(KeyBinding key, int holdMs = 40)
    {
        uint flags = KeyScanCode | (key.Extended ? KeyExtended : 0);
        Send(key.ScanCode, flags);
        Thread.Sleep(holdMs);
        Send(key.ScanCode, flags | KeyUp);
    }

    private static void Send(ushort scan, uint flags)
    {
        var input = new[] { new Input { Type = InputKeyboard, Key = new KeybdInput { Scan = scan, Flags = flags } } };
        SendInput(1, input, Marshal.SizeOf<Input>());
    }

    private static IntPtr _lastHwnd;
    private static bool _lastIsGame;

    /// <summary>Vrai si la fenêtre active est ETS2 ou ATS : on n'envoie jamais de touche ailleurs.</summary>
    public static bool GameHasFocus()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == _lastHwnd) return _lastIsGame;
        _lastHwnd = hwnd;
        _lastIsGame = false;
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            _lastIsGame = p.ProcessName.Equals("eurotrucks2", StringComparison.OrdinalIgnoreCase)
                       || p.ProcessName.Equals("amtrucks", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
        return _lastIsGame;
    }
}
