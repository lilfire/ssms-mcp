using System.Windows.Forms;

namespace SsmsMcp.Extension;

public sealed class PasswordDialog : Form
{
    private readonly TextBox _password;

    public PasswordDialog(string server, string userName)
    {
        Text = "SSMS MCP Server – SQL-pålogging";
        Width = 440;
        Height = 170;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        Label label = new() { Left = 16, Top = 14, Width = 390, Text = $"Passord for {userName} på {server}:" };
        _password = new TextBox { Left = 16, Top = 43, Width = 390, UseSystemPasswordChar = true };
        Button accept = new() { Text = "Koble til", Left = 218, Top = 80, Width = 90, DialogResult = DialogResult.OK };
        Button cancel = new() { Text = "Avbryt", Left = 316, Top = 80, Width = 90, DialogResult = DialogResult.Cancel };
        Controls.Add(label);
        Controls.Add(_password);
        Controls.Add(accept);
        Controls.Add(cancel);
        AcceptButton = accept;
        CancelButton = cancel;
    }

    public string Password => _password.Text;
}
