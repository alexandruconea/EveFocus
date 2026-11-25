using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Text;

namespace EveFocus;

[DataContract]
internal sealed class Config
{
    [DataMember(Order = 1)] public List<CharacterEntry> Characters { get; set; } = new();
    [DataMember(Order = 2)] public HotkeyConfig Hotkeys { get; set; } = new();
    [DataMember(Order = 3)] public bool MinimizeOthersOnSwitch { get; set; } = true;
    [DataMember(Order = 4)] public bool AutoApplyLayouts { get; set; } = true;
    [DataMember(Order = 5)] public Dictionary<string, WindowLayout> Layouts { get; set; } = NewLayoutMap();
    [DataMember(Order = 6)] public bool CheckForUpdates { get; set; } = true;

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        Characters = new List<CharacterEntry>();
        Hotkeys = new HotkeyConfig();
        MinimizeOthersOnSwitch = true;
        AutoApplyLayouts = true;
        Layouts = NewLayoutMap();
        CheckForUpdates = true;
    }

    public void Normalize()
    {
        Characters = (Characters ?? new List<CharacterEntry>())
            .Where(c => c != null)
            .Select(c => new CharacterEntry { Name = ClientTracker.CharacterName(c.Name), Hotkey = (c.Hotkey ?? "").Trim() })
            .Where(c => c.Name.Length > 0)
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        Hotkeys ??= new HotkeyConfig();
        Hotkeys.Normalize();
        var layouts = NewLayoutMap();
        if (Layouts != null)
            foreach (var pair in Layouts)
                if (pair.Value != null) layouts[pair.Key] = pair.Value;
        Layouts = layouts;
    }

    public Config Clone()
    {
        var layouts = NewLayoutMap();
        foreach (var pair in Layouts) layouts[pair.Key] = pair.Value.Clone();
        return new Config
        {
            Characters = Characters.Select(c => new CharacterEntry { Name = c.Name, Hotkey = c.Hotkey }).ToList(),
            Hotkeys = Hotkeys.Clone(),
            MinimizeOthersOnSwitch = MinimizeOthersOnSwitch,
            AutoApplyLayouts = AutoApplyLayouts,
            Layouts = layouts,
            CheckForUpdates = CheckForUpdates,
        };
    }

    private static Dictionary<string, WindowLayout> NewLayoutMap() => new(StringComparer.OrdinalIgnoreCase);
}

[DataContract]
internal sealed class CharacterEntry
{
    [DataMember(Order = 1)] public string Name { get; set; } = "";
    [DataMember(Order = 2)] public string Hotkey { get; set; } = "";
}

[DataContract]
internal sealed class HotkeyConfig
{
    [DataMember(Order = 1)] public string MinimizeAll { get; set; } = "Ctrl+Alt+M";
    [DataMember(Order = 2)] public string NextClient { get; set; } = "";
    [DataMember(Order = 3)] public string PreviousClient { get; set; } = "";

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        MinimizeAll = "";
        NextClient = "";
        PreviousClient = "";
    }

    public void Normalize()
    {
        MinimizeAll = (MinimizeAll ?? "").Trim();
        NextClient = (NextClient ?? "").Trim();
        PreviousClient = (PreviousClient ?? "").Trim();
    }

    public HotkeyConfig Clone() => new() { MinimizeAll = MinimizeAll, NextClient = NextClient, PreviousClient = PreviousClient };
}

[DataContract]
internal sealed class WindowLayout
{
    [DataMember(Order = 1)] public int X { get; set; }
    [DataMember(Order = 2)] public int Y { get; set; }
    [DataMember(Order = 3)] public int Width { get; set; }
    [DataMember(Order = 4)] public int Height { get; set; }
    [DataMember(Order = 5)] public bool Maximized { get; set; }

    public WindowLayout Clone() => new() { X = X, Y = Y, Width = Width, Height = Height, Maximized = Maximized };
}

internal static class ConfigStore
{
    public const string FileName = "EveFocus.json";

    private static readonly DataContractJsonSerializer Serializer = new(typeof(Config),
        new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

    public static string FilePath { get; } = ResolveFilePath();

    public static string FolderPath => Path.GetDirectoryName(FilePath)!;

    public static bool Exists => File.Exists(FilePath);

    public static string ToJson(Config config)
    {
        using var stream = new MemoryStream();
        using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true, "  "))
        {
            Serializer.WriteObject(writer, config);
            writer.Flush();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Config FromJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var config = Serializer.ReadObject(stream) as Config ?? new Config();
        config.Normalize();
        return config;
    }

    public static bool TryLoad(out Config config, out string error)
    {
        try
        {
            config = FromJson(File.ReadAllText(FilePath, Encoding.UTF8));
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            config = new Config();
            error = ex.Message;
            return false;
        }
    }

    public static bool TrySave(Config config, out string error)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, ToJson(config), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string ResolveFilePath()
    {
        string besideExe = Path.Combine(AppContext.BaseDirectory, FileName);
        string inAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EveFocus", FileName);
        if (File.Exists(besideExe)) return besideExe;
        if (File.Exists(inAppData)) return inAppData;
        return CanWriteTo(AppContext.BaseDirectory) ? besideExe : inAppData;
    }

    private static bool CanWriteTo(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}
