using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace EveFocus;

internal sealed class SettingsForm : Form
{
    private const int VisibleRows = 8;

    private readonly Config _source;
    private readonly float _dpiScale;
    private readonly List<CharacterRow> _rows = new();
    private readonly TableLayoutPanel _characterTable;
    private readonly Panel _characterScroll;
    private readonly Label _nameHeader;
    private readonly Label _hotkeyHeader;
    private readonly HotkeyBox _minimizeAllBox;
    private readonly HotkeyBox _nextClientBox;
    private readonly HotkeyBox _previousClientBox;
    private readonly CheckBox _keepOnlyActiveBox;
    private readonly CheckBox _autoLayoutsBox;
    private readonly CheckBox _checkUpdatesBox;

    public SettingsForm(Config config, bool firstRun)
    {
        _source = config;
        Result = config;
        using (var screen = Graphics.FromHwnd(IntPtr.Zero)) _dpiScale = screen.DpiX / 96f;

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        Font = SystemFonts.MessageBoxFont;
        Text = firstRun ? "EVE Focus - first setup" : "EVE Focus - settings";
        Icon = AppIcon.Load();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Px(12));

        _nameHeader = NewHeader("Character");
        _hotkeyHeader = NewHeader("Hotkey");
        _characterTable = new TableLayoutPanel
        {
            ColumnCount = 5,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = Point.Empty,
            Margin = Padding.Empty,
        };
        _characterScroll = new Panel { AutoScroll = true, Margin = Padding.Empty };
        _characterScroll.Controls.Add(_characterTable);

        _minimizeAllBox = NewHotkeyBox();
        _nextClientBox = NewHotkeyBox();
        _previousClientBox = NewHotkeyBox();
        _keepOnlyActiveBox = new CheckBox
        {
            Text = "Keep only the active client open (the others are minimized and use almost no GPU)",
            AutoSize = true,
            Checked = config.MinimizeOthersOnSwitch,
        };
        _autoLayoutsBox = new CheckBox
        {
            Text = "Put a client back in its saved window layout when the character logs in",
            AutoSize = true,
            Checked = config.AutoApplyLayouts,
        };
        _checkUpdatesBox = new CheckBox
        {
            Text = "Check for updates once a day (connects only to GitHub)",
            AutoSize = true,
            Checked = config.CheckForUpdates,
        };

        var addCharacter = NewButton("Add character");
        addCharacter.Click += (_, _) => AddEmptyRow();
        var addLoggedIn = NewButton("Add logged-in characters");
        addLoggedIn.Click += (_, _) => AddLoggedInCharacters();
        var characterButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, Px(4), 0, 0) };
        characterButtons.Controls.Add(addCharacter);
        characterButtons.Controls.Add(addLoggedIn);

        var hotkeyTable = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        AddHotkeyRow(hotkeyTable, "Minimize / restore all clients", _minimizeAllBox);
        AddHotkeyRow(hotkeyTable, "Next client", _nextClientBox);
        AddHotkeyRow(hotkeyTable, "Previous client", _previousClientBox);

        var save = NewButton(firstRun ? "Save and start" : "Save");
        save.Click += (_, _) => Save();
        var cancel = NewButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;
        var footer = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, Px(12), 0, 0),
        };
        footer.Controls.Add(cancel);
        footer.Controls.Add(save);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        if (firstRun)
            root.Controls.Add(NewNote("Welcome! Add the characters you play and press the keys you want for each of them. " +
                                      "You can change everything later by double-clicking the EVE Focus icon in the tray.", false));
        root.Controls.Add(NewSection("Characters"));
        root.Controls.Add(_characterScroll);
        root.Controls.Add(characterButtons);
        root.Controls.Add(NewSection("Hotkeys"));
        root.Controls.Add(hotkeyTable);
        root.Controls.Add(NewSection("Options"));
        root.Controls.Add(_keepOnlyActiveBox);
        root.Controls.Add(_autoLayoutsBox);
        root.Controls.Add(_checkUpdatesBox);
        root.Controls.Add(NewNote("Click a key box and press the keys. Letters and digits need Ctrl or Alt, " +
                                  "F1-F24 also work alone. Backspace clears a box.", true));
        root.Controls.Add(NewNote("Settings file: " + ConfigStore.FilePath, true));
        root.Controls.Add(footer);
        Controls.Add(root);

        foreach (var character in config.Characters) AddRow(character.Name, character.Hotkey);
        if (_rows.Count == 0) AddRow("", "");
        RebuildTable();
        _minimizeAllBox.Combo = config.Hotkeys.MinimizeAll;
        _nextClientBox.Combo = config.Hotkeys.NextClient;
        _previousClientBox.Combo = config.Hotkeys.PreviousClient;

        ResumeLayout(true);
    }

    public Config Result { get; private set; }

    private int Px(int value) => (int)Math.Round(value * _dpiScale);

    private CharacterRow AddRow(string name, string combo)
    {
        var row = new CharacterRow(this, name, combo);
        row.UpButton.Click += (_, _) => MoveRow(row, -1);
        row.DownButton.Click += (_, _) => MoveRow(row, 1);
        row.RemoveButton.Click += (_, _) => BeginInvoke(new Action(() => RemoveRow(row)));
        _rows.Add(row);
        return row;
    }

    private void AddEmptyRow()
    {
        var row = AddRow("", "");
        RebuildTable();
        _characterScroll.ScrollControlIntoView(row.NameBox);
        row.NameBox.Focus();
    }

    private void AddLoggedInCharacters()
    {
        var tracker = new ClientTracker();
        tracker.Refresh();
        var listed = new HashSet<string>(_rows.Select(r => r.CharacterName), StringComparer.OrdinalIgnoreCase);
        var names = tracker.Clients
            .Where(c => c.IsLoggedIn && !listed.Contains(c.Name))
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            MessageBox.Show(this, "No other logged-in characters were found.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (var blank in _rows.Where(r => r.IsBlank).ToList())
        {
            _rows.Remove(blank);
            blank.Dispose();
        }
        foreach (string name in names) AddRow(name, "");
        RebuildTable();
    }

    private void MoveRow(CharacterRow row, int offset)
    {
        int index = _rows.IndexOf(row);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= _rows.Count) return;
        _rows.RemoveAt(index);
        _rows.Insert(target, row);
        RebuildTable();
        var button = offset < 0 ? row.UpButton : row.DownButton;
        if (button.Enabled) button.Focus();
        else row.NameBox.Focus();
    }

    private void RemoveRow(CharacterRow row)
    {
        if (!_rows.Remove(row)) return;
        row.Dispose();
        if (_rows.Count == 0) AddRow("", "");
        RebuildTable();
    }

    private void RebuildTable()
    {
        _characterTable.SuspendLayout();
        _characterTable.Controls.Clear();
        _characterTable.RowStyles.Clear();
        _characterTable.RowCount = _rows.Count + 1;
        _characterTable.Controls.Add(_nameHeader, 0, 0);
        _characterTable.Controls.Add(_hotkeyHeader, 1, 0);
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            row.UpButton.Enabled = i > 0;
            row.DownButton.Enabled = i < _rows.Count - 1;
            row.NameBox.TabIndex = i * 5;
            row.KeyBox.TabIndex = i * 5 + 1;
            row.UpButton.TabIndex = i * 5 + 2;
            row.DownButton.TabIndex = i * 5 + 3;
            row.RemoveButton.TabIndex = i * 5 + 4;
            _characterTable.Controls.Add(row.NameBox, 0, i + 1);
            _characterTable.Controls.Add(row.KeyBox, 1, i + 1);
            _characterTable.Controls.Add(row.UpButton, 2, i + 1);
            _characterTable.Controls.Add(row.DownButton, 3, i + 1);
            _characterTable.Controls.Add(row.RemoveButton, 4, i + 1);
        }
        _characterTable.ResumeLayout(true);

        int visibleHeight = _characterTable.GetRowHeights().Take(VisibleRows + 1).Sum();
        var preferred = _characterTable.GetPreferredSize(Size.Empty);
        _characterScroll.Size = new Size(
            preferred.Width + SystemInformation.VerticalScrollBarWidth,
            Math.Min(preferred.Height, visibleHeight) + Px(2));
    }

    private void Save()
    {
        var characters = new List<CharacterEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            string name = row.CharacterName;
            if (name.Length == 0)
            {
                if (row.KeyBox.Combo.Length == 0) continue;
                Warn("A row with the hotkey " + row.KeyBox.Combo + " has no character name.");
                row.NameBox.Focus();
                return;
            }
            if (!names.Add(name))
            {
                Warn(name + " is in the list twice.");
                row.NameBox.Focus();
                return;
            }
            characters.Add(new CharacterEntry { Name = name, Hotkey = row.KeyBox.Combo });
        }

        var assignments = characters.Where(c => c.Hotkey.Length > 0).Select(c => (What: c.Name, Combo: c.Hotkey)).ToList();
        if (_minimizeAllBox.Combo.Length > 0) assignments.Add(("Minimize / restore all clients", _minimizeAllBox.Combo));
        if (_nextClientBox.Combo.Length > 0) assignments.Add(("Next client", _nextClientBox.Combo));
        if (_previousClientBox.Combo.Length > 0) assignments.Add(("Previous client", _previousClientBox.Combo));

        var invalid = assignments.FirstOrDefault(a => !Hotkey.TryParse(a.Combo, out _, out _));
        if (invalid.What != null)
        {
            Warn(invalid.What + " has keys that can't be used: " + invalid.Combo);
            return;
        }

        var duplicate = assignments.GroupBy(a => KeyOf(a.Combo)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            Warn(string.Join(" and ", duplicate.Select(a => a.What)) + " use the same keys (" + duplicate.First().Combo + ").");
            return;
        }

        var taken = assignments.Where(a => !Hotkey.IsAvailable(a.Combo)).ToList();
        if (taken.Count > 0)
        {
            string list = string.Join("\n", taken.Select(a => a.Combo + "   (" + a.What + ")"));
            var answer = MessageBox.Show(this,
                "These keys are already used by another program, so they won't work while it runs:\n\n" + list + "\n\nSave anyway?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        Result = new Config
        {
            Characters = characters,
            Hotkeys = new HotkeyConfig
            {
                MinimizeAll = _minimizeAllBox.Combo,
                NextClient = _nextClientBox.Combo,
                PreviousClient = _previousClientBox.Combo,
            },
            MinimizeOthersOnSwitch = _keepOnlyActiveBox.Checked,
            AutoApplyLayouts = _autoLayoutsBox.Checked,
            Layouts = _source.Clone().Layouts,
            CheckForUpdates = _checkUpdatesBox.Checked,
        };
        DialogResult = DialogResult.OK;
    }

    private static string KeyOf(string combo) =>
        Hotkey.TryParse(combo, out uint modifiers, out uint key) ? modifiers + ":" + key : combo.ToUpperInvariant();

    private void Warn(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void AddHotkeyRow(TableLayoutPanel table, string text, HotkeyBox box)
    {
        int row = table.Controls.Count / 2;
        table.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, Px(16), 3),
        }, 0, row);
        table.Controls.Add(box, 1, row);
    }

    private static Label NewHeader(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
    };

    private Label NewSection(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Font, FontStyle.Bold),
        Margin = new Padding(0, Px(12), 0, Px(4)),
    };

    private Label NewNote(string text, bool muted) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(Px(470), 0),
        ForeColor = muted ? SystemColors.GrayText : SystemColors.ControlText,
        Margin = new Padding(0, Px(8), 0, 0),
    };

    private Button NewButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(Px(88), 0),
        UseVisualStyleBackColor = true,
    };

    private Button NewSmallButton(string text) => new()
    {
        Text = text,
        Size = new Size(Px(28), Px(24)),
        UseVisualStyleBackColor = true,
    };

    private HotkeyBox NewHotkeyBox() => new() { Width = Px(140) };

    private sealed class CharacterRow
    {
        public CharacterRow(SettingsForm form, string name, string combo)
        {
            NameBox = new TextBox { Width = form.Px(200), Text = name };
            KeyBox = new HotkeyBox { Width = form.Px(140), Combo = combo };
            UpButton = form.NewSmallButton("↑");
            DownButton = form.NewSmallButton("↓");
            RemoveButton = form.NewSmallButton("×");
        }

        public TextBox NameBox { get; }
        public HotkeyBox KeyBox { get; }
        public Button UpButton { get; }
        public Button DownButton { get; }
        public Button RemoveButton { get; }

        public string CharacterName => ClientTracker.CharacterName(NameBox.Text);
        public bool IsBlank => CharacterName.Length == 0 && KeyBox.Combo.Length == 0;

        public void Dispose()
        {
            NameBox.Dispose();
            KeyBox.Dispose();
            UpButton.Dispose();
            DownButton.Dispose();
            RemoveButton.Dispose();
        }
    }
}
