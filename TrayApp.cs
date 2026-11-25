using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace EveFocus;

internal sealed class TrayApp : ApplicationContext
{
    private readonly ClientTracker _tracker = new();
    private readonly HotkeyManager _hotkeys = new();
    private readonly ForegroundWatcher _foreground = new();
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1000 };
    private readonly FileSystemWatcher _configWatcher;
    private readonly NotifyIcon _tray;
    private readonly Dictionary<IntPtr, WindowLayout> _pendingLayouts = new();
    private Config _config;
    private SettingsForm? _settingsForm;
    private bool _exitRequested;

    private IntPtr _lastActive;
    private volatile bool _configChanged;
    private long _ignoreConfigChangesUntil;

    public TrayApp(bool firstStart)
    {
        string? configError = null;
        if (!ConfigStore.TryLoad(out _config, out string loadError))
            configError = ConfigStore.FileName + " could not be read, using the defaults until it is fixed:\n" + loadError;

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Load(SystemInformation.SmallIconSize),
            Text = "EVE Focus",
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true,
        };
        _tray.ContextMenuStrip.Opening += (_, e) =>
        {
            BuildMenu();
            e.Cancel = false;
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _tracker.CharacterLoggedIn += OnCharacterLoggedIn;
        _tracker.Refresh();

        _foreground.Changed += OnForegroundChanged;
        _poll.Tick += (_, _) => OnPoll();
        _poll.Start();

        _configWatcher = new FileSystemWatcher(ConfigStore.FolderPath, ConfigStore.FileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        FileSystemEventHandler onChange = (_, _) => OnConfigFileChanged();
        _configWatcher.Changed += onChange;
        _configWatcher.Created += onChange;
        _configWatcher.Renamed += (_, _) => OnConfigFileChanged();
        _configWatcher.EnableRaisingEvents = true;

        RegisterHotkeys();
        OnForegroundChanged();
        if (configError != null) Notify(configError, ToolTipIcon.Warning);
        else if (firstStart) Notify("EVE Focus is running. Double-click this icon to change the settings.");
    }

    private void ToggleMinimizeAll()
    {
        _tracker.Refresh();
        var clients = _tracker.Clients;
        if (clients.Count == 0) return;
        if (clients.Any(c => !Native.IsIconic(c.Handle))) MinimizeAll();
        else if (_config.MinimizeOthersOnSwitch) RestoreActive();
        else RestoreAll();
    }

    private void MinimizeAll()
    {
        _tracker.Refresh();
        IntPtr foreground = Native.GetForegroundWindow();
        MinimizeOthers(foreground);
        if (_tracker.Find(foreground) != null) WindowOps.Minimize(foreground);
    }

    private void RestoreActive()
    {
        var target = ClientToRestore();
        if (target != null) WindowOps.Activate(target.Handle);
    }

    private void RestoreAll()
    {
        _tracker.Refresh();
        var clients = _tracker.Clients;
        if (clients.Count == 0) return;

        var target = ClientToRestore();
        for (int i = clients.Count - 1; i >= 0; i--)
            if (Native.IsIconic(clients[i].Handle))
                WindowOps.Restore(clients[i].Handle);

        if (target != null) WindowOps.Activate(target.Handle);
    }

    private EveClient? ClientToRestore() => _tracker.Find(_lastActive) ?? OrderedClients().FirstOrDefault();

    private void MinimizeOthers(IntPtr keep)
    {
        foreach (var client in _tracker.Clients)
            if (client.Handle != keep && !Native.IsIconic(client.Handle))
                WindowOps.MinimizeNoActivate(client.Handle);
    }

    private void FocusCharacter(string name)
    {
        _tracker.Refresh();
        var client = _tracker.Find(name);
        if (client != null) Focus(client);
    }

    private void Focus(EveClient client)
    {
        WindowOps.Activate(client.Handle);
        if (_config.MinimizeOthersOnSwitch && Native.GetForegroundWindow() == client.Handle)
            MinimizeOthers(client.Handle);
    }

    private void Cycle(int direction)
    {
        _tracker.Refresh();
        var ordered = OrderedClients();
        if (ordered.Count == 0) return;

        IntPtr foreground = Native.GetForegroundWindow();
        int current = ordered.FindIndex(c => c.Handle == foreground);
        if (current < 0) current = ordered.FindIndex(c => c.Handle == _lastActive);
        int next = current >= 0
            ? (current + direction + ordered.Count) % ordered.Count
            : direction > 0 ? 0 : ordered.Count - 1;
        Focus(ordered[next]);
    }

    private List<EveClient> OrderedClients()
    {
        int Rank(EveClient client)
        {
            int index = _config.Characters.FindIndex(c =>
                string.Equals(c.Name, client.Name, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? int.MaxValue : index;
        }

        return _tracker.Clients
            .OrderBy(Rank)
            .ThenBy(c => c.IsLoggedIn ? 0 : 1)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Handle.ToInt64())
            .ToList();
    }

    private void SaveLayouts()
    {
        _tracker.Refresh();
        int saved = 0;
        foreach (var client in _tracker.Clients.Where(c => c.IsLoggedIn))
        {
            _config.Layouts[client.Name] = WindowOps.GetLayout(client.Handle);
            saved++;
        }
        if (saved == 0)
        {
            Notify("No logged-in clients to save.");
            return;
        }
        if (SaveConfig()) Notify($"Saved the window layout of {saved} character(s).");
    }

    private void ApplyLayouts()
    {
        _tracker.Refresh();
        foreach (var client in _tracker.Clients.Where(c => c.IsLoggedIn))
            if (_config.Layouts.TryGetValue(client.Name, out var layout))
                ApplyLayout(client.Handle, layout, now: !Native.IsIconic(client.Handle));
    }

    private void OnCharacterLoggedIn(EveClient client)
    {
        _pendingLayouts.Remove(client.Handle);
        if (!_config.AutoApplyLayouts || !_config.Layouts.TryGetValue(client.Name, out var layout)) return;
        bool inFront = client.Handle == Native.GetForegroundWindow() && !Native.IsIconic(client.Handle);
        ApplyLayout(client.Handle, layout, now: inFront);
    }

    private void ApplyLayout(IntPtr handle, WindowLayout layout, bool now)
    {
        if (now) WindowOps.ApplyLayout(handle, layout);
        else _pendingLayouts[handle] = layout;
    }

    private void OnForegroundChanged()
    {
        IntPtr foreground = Native.GetForegroundWindow();
        if (!_tracker.IsEveWindow(foreground) || Native.IsIconic(foreground)) return;
        _lastActive = foreground;

        if (_pendingLayouts.TryGetValue(foreground, out var layout))
        {
            _pendingLayouts.Remove(foreground);
            WindowOps.ApplyLayout(foreground, layout);
        }

        if (_config.MinimizeOthersOnSwitch)
        {
            _tracker.Refresh();
            MinimizeOthers(foreground);
        }
    }

    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var errors = new List<string>();

        void Add(string? combo, string what, Action action)
        {
            if (string.IsNullOrWhiteSpace(combo)) return;
            if (!_hotkeys.Register(combo!, action, out string error)) errors.Add($"{what} ({combo}): {error}");
        }

        var hotkeys = _config.Hotkeys;
        Add(hotkeys.MinimizeAll, "Minimize/restore all", ToggleMinimizeAll);
        Add(hotkeys.NextClient, "Next client", () => Cycle(+1));
        Add(hotkeys.PreviousClient, "Previous client", () => Cycle(-1));
        foreach (var character in _config.Characters)
        {
            string name = character.Name;
            Add(character.Hotkey, name, () => FocusCharacter(name));
        }

        if (errors.Count > 0)
            Notify("Some hotkeys could not be set:\n" + string.Join("\n", errors), ToolTipIcon.Warning);
    }

    private void ShowSettings()
    {
        if (_settingsForm != null)
        {
            _settingsForm.Activate();
            return;
        }

        _hotkeys.UnregisterAll();
        var form = new SettingsForm(_config.Clone(), firstRun: false);
        _settingsForm = form;
        try
        {
            if (form.ShowDialog() == DialogResult.OK)
            {
                _config = form.Result;
                SaveConfig(force: true);
            }
        }
        finally
        {
            _settingsForm = null;
            form.Dispose();
            if (_exitRequested) ExitThread();
            else RegisterHotkeys();
        }
    }

    private bool SaveConfig(bool force = false)
    {
        if (!force && ConfigStore.Exists && !ConfigStore.TryLoad(out _, out string loadError))
        {
            Notify(ConfigStore.FileName + " has an error; fix it first so it isn't overwritten:\n" + loadError,
                ToolTipIcon.Warning);
            return false;
        }

        Interlocked.Exchange(ref _ignoreConfigChangesUntil, DateTime.UtcNow.AddSeconds(2).Ticks);
        if (ConfigStore.TrySave(_config, out string error)) return true;
        Notify("Could not save " + ConfigStore.FileName + ":\n" + error, ToolTipIcon.Error);
        return false;
    }

    private void OnConfigFileChanged()
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _ignoreConfigChangesUntil)) return;
        _configChanged = true;
    }

    private void ReloadConfig()
    {
        if (!ConfigStore.TryLoad(out var config, out string error))
        {
            Notify(ConfigStore.FileName + " has an error, keeping the previous settings:\n" + error, ToolTipIcon.Warning);
            return;
        }
        _config = config;
        RegisterHotkeys();
        Notify("Settings reloaded.");
    }

    private void OnPoll()
    {
        if (_configChanged && _settingsForm == null)
        {
            _configChanged = false;
            ReloadConfig();
        }
        _tracker.Refresh();
        foreach (IntPtr handle in _pendingLayouts.Keys.Where(h => _tracker.Find(h) == null).ToList())
            _pendingLayouts.Remove(handle);
    }

    private void BuildMenu()
    {
        _tracker.Refresh();
        var items = _tray.ContextMenuStrip!.Items;
        var oldItems = items.Cast<ToolStripItem>().ToList();
        items.Clear();
        foreach (var oldItem in oldItems) oldItem.Dispose();

        var clients = OrderedClients();
        if (clients.Count == 0) items.Add(new ToolStripMenuItem("No EVE clients running") { Enabled = false });
        foreach (var client in clients)
        {
            string text = client.DisplayName + (Native.IsIconic(client.Handle) ? "   (minimized)" : "");
            var item = new ToolStripMenuItem(text) { Checked = client.Handle == _lastActive };
            item.Click += (_, _) => Focus(client);
            items.Add(item);
        }

        items.Add(new ToolStripSeparator());
        items.Add("Minimize all clients", null, (_, _) => MinimizeAll());
        if (!_config.MinimizeOthersOnSwitch)
            items.Add("Restore all clients", null, (_, _) => RestoreAll());
        items.Add(new ToolStripSeparator());
        items.Add("Save window layouts", null, (_, _) => SaveLayouts());
        items.Add("Apply window layouts", null, (_, _) => ApplyLayouts());
        items.Add(new ToolStripSeparator());
        items.Add("Settings...", null, (_, _) => ShowSettings());
        items.Add("Exit", null, (_, _) => Exit());
    }

    private void Exit()
    {
        if (_settingsForm != null)
        {
            _exitRequested = true;
            _settingsForm.Close();
            return;
        }
        ExitThread();
    }

    private void Notify(string text, ToolTipIcon icon = ToolTipIcon.Info) =>
        _tray.ShowBalloonTip(4000, "EVE Focus", text, icon);

    protected override void ExitThreadCore()
    {
        _poll.Stop();
        _configWatcher.Dispose();
        _foreground.Dispose();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
