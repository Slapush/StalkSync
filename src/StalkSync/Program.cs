namespace StalkSync;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "StalkSync.SingleInstance", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
