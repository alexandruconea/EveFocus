using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace EveFocus;

internal sealed class EveClient
{
    public EveClient(IntPtr handle, string name)
    {
        Handle = handle;
        Name = name;
    }

    public IntPtr Handle { get; }
    public string Name { get; }
    public bool IsLoggedIn => Name.Length > 0;
    public string DisplayName => IsLoggedIn ? Name : "(not logged in)";
}

internal sealed class ClientTracker
{
    private const string NamedTitlePrefix = "EVE - ";

    private readonly Dictionary<uint, bool> _isEveProcess = new();
    private readonly Dictionary<IntPtr, string> _knownNames = new();
    private List<EveClient> _clients = new();
    private bool _initialScanDone;

    public IReadOnlyList<EveClient> Clients => _clients;

    public event Action<EveClient>? CharacterLoggedIn;

    public void Refresh()
    {
        var found = new List<EveClient>();
        var livePids = new HashSet<uint>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            livePids.Add(pid);
            if (TryGetName(hwnd, pid, out string name)) found.Add(new EveClient(hwnd, name));
            return true;
        }, IntPtr.Zero);

        foreach (uint pid in _isEveProcess.Keys.Where(p => !livePids.Contains(p)).ToList())
            _isEveProcess.Remove(pid);

        var loggedIn = new List<EveClient>();
        foreach (var client in found)
        {
            _knownNames.TryGetValue(client.Handle, out string? previous);
            if (client.IsLoggedIn && !string.Equals(previous, client.Name, StringComparison.Ordinal))
                loggedIn.Add(client);
            _knownNames[client.Handle] = client.Name;
        }
        var alive = new HashSet<IntPtr>(found.Select(c => c.Handle));
        foreach (IntPtr handle in _knownNames.Keys.Where(h => !alive.Contains(h)).ToList())
            _knownNames.Remove(handle);

        _clients = found;
        if (_initialScanDone)
            foreach (var client in loggedIn) CharacterLoggedIn?.Invoke(client);
        _initialScanDone = true;
    }

    public EveClient? Find(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? null : _clients.FirstOrDefault(c => c.Handle == hwnd);

    public EveClient? Find(string name)
    {
        string wanted = CharacterName(name);
        return _clients.FirstOrDefault(c =>
            c.IsLoggedIn && string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    public static string CharacterName(string? nameOrTitle)
    {
        string name = (nameOrTitle ?? "").Trim();
        return name.StartsWith(NamedTitlePrefix, StringComparison.OrdinalIgnoreCase)
            ? name.Substring(NamedTitlePrefix.Length).Trim()
            : name;
    }

    public bool IsEveWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        return TryGetName(hwnd, pid, out _);
    }

    private bool TryGetName(IntPtr hwnd, uint pid, out string name)
    {
        name = "";
        if (!IsEveProcess(pid)) return false;
        string title = GetTitle(hwnd);
        if (!title.StartsWith("EVE", StringComparison.Ordinal)) return false;
        if (title.StartsWith(NamedTitlePrefix, StringComparison.Ordinal))
            name = title.Substring(NamedTitlePrefix.Length).Trim();
        return true;
    }

    private bool IsEveProcess(uint pid)
    {
        if (_isEveProcess.TryGetValue(pid, out bool isEve)) return isEve;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            isEve = process.ProcessName.Equals("exefile", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            isEve = false;
        }
        _isEveProcess[pid] = isEve;
        return isEve;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        int length = Native.GetWindowTextLength(hwnd);
        if (length == 0) return "";
        var text = new StringBuilder(length + 1);
        Native.GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }
}
