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

    private static readonly Dictionary<string, string> PrettyNames = new()
    {
        ["Left"] = "←", ["Right"] = "→", ["Up"] = "↑", ["Down"] = "↓", ["Space"] = "Espace",
        ["Return"] = "Entrée", ["Back"] = "Retour arrière", ["Delete"] = "Suppr", ["Insert"] = "Inser",
        ["Home"] = "Début", ["End"] = "Fin", ["PageUp"] = "Page préc.", ["Next"] = "Page suiv.",
    };

    /// <summary>Nom lisible d'une touche selon la disposition du clavier (ex. « Alt + ; » plutôt que « OemSemicolon »).</summary>
    public static string KeyName(KeyBinding key)
    {
        uint vk = key.Extended ? 0 : MapVirtualKey(key.ScanCode, 1);
        uint ch = vk == 0 ? 0 : MapVirtualKey(vk, 2) & 0x7FFF;
        string name = ch > ' ' ? char.ToUpperInvariant((char)ch).ToString()
                    : PrettyNames.GetValueOrDefault(key.Name, key.Name);
        if (key.Shift) name = "Maj + " + name;
        if (key.Alt) name = "Alt + " + name;
        if (key.Ctrl) name = "Ctrl + " + name;
        return name;
    }

    private const ushort ScanCtrl = 0x1D, ScanShift = 0x2A, ScanAlt = 0x38;

    public static void TapKey(KeyBinding key, int holdMs = 40)
    {
        var modifiers = new List<ushort>();
        if (key.Ctrl) modifiers.Add(ScanCtrl);
        if (key.Shift) modifiers.Add(ScanShift);
        if (key.Alt) modifiers.Add(ScanAlt);
        foreach (var m in modifiers) Send(m, KeyScanCode);
        uint flags = KeyScanCode | (key.Extended ? KeyExtended : 0);
        Send(key.ScanCode, flags);
        Thread.Sleep(holdMs);
        Send(key.ScanCode, flags | KeyUp);
        for (int i = modifiers.Count - 1; i >= 0; i--) Send(modifiers[i], KeyScanCode | KeyUp);
    }

    private static void Send(ushort scan, uint flags)
    {
        var input = new[] { new Input { Type = InputKeyboard, Key = new KeybdInput { Scan = scan, Flags = flags } } };
        SendInput(1, input, Marshal.SizeOf<Input>());
    }

    private static IntPtr _lastHwnd;
    private static string? _lastProcess;

    /// <summary>Nom du processus de la fenêtre active (sans « .exe »), ou null.</summary>
    public static string? ForegroundProcess()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == _lastHwnd) return _lastProcess;
        _lastHwnd = hwnd;
        _lastProcess = null;
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            _lastProcess = p.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
        return _lastProcess;
    }

    /// <summary>Processus ouverts qui ont une fenêtre, pour ajouter un jeu à la liste.</summary>
    public static List<(string Process, string Title)> WindowedProcesses()
    {
        var own = Environment.ProcessId;
        var result = new List<(string, string)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == own || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle))
                        continue;
                    result.Add((p.ProcessName, p.MainWindowTitle));
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return result.OrderBy(r => r.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
