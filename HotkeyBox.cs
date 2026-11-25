using System.Drawing;
using System.Windows.Forms;

namespace EveFocus;

internal sealed class HotkeyBox : TextBox
{
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private string _combo = "";

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
    }

    public string Combo
    {
        get => _combo;
        set
        {
            _combo = value?.Trim() ?? "";
            Text = _combo;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        Keys modifiers = keyData & Keys.Modifiers;

        if (key == Keys.Tab && (modifiers == Keys.None || modifiers == Keys.Shift))
            return base.ProcessCmdKey(ref msg, keyData);

        if (modifiers == Keys.None && key is Keys.Back or Keys.Delete or Keys.Escape)
        {
            Combo = "";
            return true;
        }

        if (key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            Text = Hotkey.Format(modifiers) + "+";
            return true;
        }

        bool functionKey = key >= Keys.F1 && key <= Keys.F24;
        bool withCtrlOrAlt = (modifiers & (Keys.Control | Keys.Alt)) != 0;
        if (functionKey || withCtrlOrAlt) Combo = Hotkey.Format(keyData);
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_KEYUP || m.Msg == WM_SYSKEYUP)
        {
            Text = _combo;
            if (m.Msg == WM_SYSKEYUP) return;
        }
        base.WndProc(ref m);
    }
}
