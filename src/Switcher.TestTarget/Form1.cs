using System.Text;

namespace Switcher.TestTarget;

/// <summary>
/// Test harness for Switcher adapter testing.
/// Contains native Edit controls that can be used to verify NativeEditTargetAdapter behavior.
///
/// Test procedure:
/// 1. Launch Switcher.App
/// 2. Launch this app
/// 3. Focus an input field below
/// 4. Type a test word (e.g. ghbdsn) and press the safe-mode hotkey (Ctrl+Shift+K)
/// 5. Verify the word is corrected to привіт
/// 6. Repeat for all test cases
/// </summary>
public partial class Form1 : Form
{
    public Form1()
    {
        InitializeComponent();
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "EN-UA Switcher Test Target — Native EDIT Controls";
        Width = 700;
        Height = 580;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(224, 224, 224);
        Font = new Font("Segoe UI", 10f);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 0,
            ColumnCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        AddHeader(layout, "PLAIN TEXT BOX (EDIT control)");
        var tb1 = AddTextBox(layout, "Single-line — type ghbdsn, press Ctrl+Shift+K → привіт", false);

        AddHeader(layout, "RICH TEXT BOX (RichEdit control)");
        var rt1 = AddRichTextBox(layout, "Multi-line — type руддщ, press Ctrl+Shift+K → hello");

        AddHeader(layout, "TEST CASES (for reference)");
        AddLabel(layout, "ghbdsn + Ctrl+Shift+K → привіт");
        AddLabel(layout, "руддщ + Ctrl+Shift+K → hello");
        AddLabel(layout, "hello — should NOT change");
        AddLabel(layout, "привіт — should NOT change");
        AddLabel(layout, "test, user, code — should NOT change");
        AddLabel(layout, "дякую, можна, текст — should NOT change");

        AddHeader(layout, "SELECTION TEST (multi-word)");
        var tb2 = AddTextBox(layout, "Select a word, press Ctrl+Shift+L → fix selection", false);
        tb2.Text = "ghbdsn руддщ hello привіт";
    }

    private static void AddHeader(TableLayoutPanel layout, string text)
    {
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var lbl = new Label
        {
            Text = text,
            ForeColor = Color.FromArgb(0, 120, 212),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 2)
        };
        layout.Controls.Add(lbl);
        layout.SetColumnSpan(lbl, 2);
    }

    private static void AddLabel(TableLayoutPanel layout, string text)
    {
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var lbl = new Label
        {
            Text = "  • " + text,
            ForeColor = Color.FromArgb(160, 160, 160),
            AutoSize = true,
            Margin = new Padding(4, 1, 0, 1)
        };
        layout.Controls.Add(lbl);
        layout.SetColumnSpan(lbl, 2);
    }

    private static TextBox AddTextBox(TableLayoutPanel layout, string placeholder, bool multiline)
    {
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lbl = new Label
        {
            Text = "TextBox:",
            ForeColor = Color.FromArgb(160, 160, 160),
            AutoSize = true,
            Margin = new Padding(0, 6, 4, 0)
        };
        layout.Controls.Add(lbl);

        var tb = new TextBox
        {
            Multiline = multiline,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Color.FromArgb(224, 224, 224),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 0, 4),
            Height = multiline ? 60 : 26,
            Font = new Font("Segoe UI", 10f)
        };
        layout.Controls.Add(tb);
        return tb;
    }

    private static RichTextBox AddRichTextBox(TableLayoutPanel layout, string placeholder)
    {
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));

        var lbl = new Label
        {
            Text = "RichTextBox:",
            ForeColor = Color.FromArgb(160, 160, 160),
            AutoSize = true,
            Margin = new Padding(0, 6, 4, 0)
        };
        layout.Controls.Add(lbl);

        var rt = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Color.FromArgb(224, 224, 224),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 0, 4),
            Font = new Font("Segoe UI", 10f)
        };
        layout.Controls.Add(rt);
        return rt;
    }
}

