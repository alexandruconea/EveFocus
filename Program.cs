using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace EveFocus;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\EveFocus.SingleInstance", out bool firstInstance);
        if (!firstInstance) return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool firstStart = !ConfigStore.Exists;
        if (firstStart && !RunFirstSetup()) return;
        Application.Run(new TrayApp(firstStart));
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
