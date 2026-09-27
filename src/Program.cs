namespace LightsaberCursor;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--render-sheet")
        {
            ContactSheet.Render(args[1]);
            return 0;
        }
        if (args.Contains("--restore-cursors"))
        {
            SystemCursors.Restore();
            return 0;
        }
        Log.Enabled = args.Contains("--log");
        Log.Write("start " + string.Join(" ", args));

        using var mutex = new Mutex(true, "LightsaberCursor.Windows.SingleInstance", out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "LightsaberCursor.Windows.ShowCustomizer");
        if (!first)
        {
            showSignal.Set();
            return 0;
        }

        // The pointer must never stay blank: undo a crashed run, and restore on every exit path.
        SystemCursors.RecoverFromCrash();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { if (SystemCursors.IsApplied) SystemCursors.Restore(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { SystemCursors.Restore(); Log.Write("unhandled " + e.ExceptionObject); };
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => SystemCursors.Restore();
        Application.ThreadException += (_, e) =>
        {
            SystemCursors.Restore();
            Log.Write("thread exception " + e.Exception);
            MessageBox.Show(e.Exception.ToString(), "Lightsaber Cursor error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        ApplicationConfiguration.Initialize();
        var app = new TrayApp(openCustomizer: args.Contains("--customize"));
        var ui = SynchronizationContext.Current;
        ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => ui?.Post(_ => app.ShowCustomizer(), null), null, -1, false);
        Application.Run(app);
        SystemCursors.Restore();
        return 0;
    }
}
