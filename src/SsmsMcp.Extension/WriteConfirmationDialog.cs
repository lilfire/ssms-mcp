using System.Windows.Forms;

namespace SsmsMcp.Extension;

public sealed class WriteConfirmationDialog : Form
{
    public WriteConfirmationDialog(string server, string database, string operation, string sql)
    {
        Text = "SSMS MCP Server – bekreft skrivekommando";
        Width = 760;
        Height = 520;
        MinimumSize = new System.Drawing.Size(500, 340);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.Sizable;

        Label heading = new()
        {
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(12),
            Text = $"Bekreft {operation.ToUpperInvariant()} før kjøring.\nServer: {server}\nDatabase: {database}"
        };
        TextBox sqlText = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Text = sql
        };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        Button cancel = new() { Text = "Avbryt", Width = 100, DialogResult = DialogResult.Cancel };
        Button confirm = new() { Text = $"Kjør {operation.ToUpperInvariant()}", Width = 120, DialogResult = DialogResult.Yes };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(confirm);
        Controls.Add(sqlText);
        Controls.Add(heading);
        Controls.Add(buttons);
        CancelButton = cancel;
    }
}
