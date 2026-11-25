using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace EveFocus;

internal static class Program
{
    public const string UpdatedArgument = "--updated";

    [STAThread]
    private static void Main(string[] args)
    {
        bool afterUpdate = args.Any(a => string.Equals(a, UpdatedArgument, StringComparison.OrdinalIgnoreCase));
        using var mutex = new Mutex(false, @"Local\EveFocus.SingleInstance");
        if (!Acquire(mutex, afterUpdate ? 15000 : 0)) return;
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool firstStart = !ConfigStore.Exists;
            if (firstStart && !RunFirstSetup()) return;
            Application.Run(new TrayApp(firstStart, afterUpdate));
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static bool Acquire(Mutex mutex, int timeoutMs)
    {
        try
        {
            return mutex.WaitOne(timeoutMs);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static bool RunFirstSetup()
    {
        var tracker = new ClientTracker();
        tracker.Refresh();
        var config = new Config();
        foreach (string name in tracker.Clients.Where(c => c.IsLoggedIn).Select(c => c.Name)
                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            config.Characters.Add(new CharacterEntry { Name = name });

        using var form = new SettingsForm(config, firstRun: true);
        if (form.ShowDialog() != DialogResult.OK) return false;
        if (ConfigStore.TrySave(form.Result, out string error)) return true;

        MessageBox.Show("Could not save " + ConfigStore.FilePath + ":\n" + error, "EVE Focus",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }
}
