using System;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SsmsMcp.Extension;

public sealed class ActiveContextReader
{
    private readonly AsyncPackage _package;
    private readonly string _newQueryCommand = "File.NewQuery";

    public ActiveContextReader(AsyncPackage package)
    {
        _package = package;
    }

    public async Task<ActiveContext> ReadAsync(bool openQueryIfNeeded = false, bool allowDisconnected = false)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
        IVsMonitorSelection selection = (IVsMonitorSelection)(await _package.GetServiceAsync(typeof(SVsShellMonitorSelection))
            ?? throw new InvalidOperationException("SSMS-vindusvalg er utilgjengelig."));
        DTE dte = (DTE)(await _package.GetServiceAsync(typeof(SDTE))
            ?? throw new InvalidOperationException("SSMS-editoren er utilgjengelig."));
        object? editor = SqlEditor(selection);
        if (editor is null && openQueryIfNeeded)
        {
            dte.ExecuteCommand(_newQueryCommand);
            editor = SqlEditor(selection);
        }

        if (editor is null)
            throw new InvalidOperationException("Et SQL-vindu må være aktivt.");

        object? info = Property(editor, "Connection");
        IDbConnection? session = Session(editor);
        string server = Property(info, "ServerName")?.ToString() ?? string.Empty;
        string database = Property(editor, "CurrentDB")?.ToString() ?? session?.Database ?? string.Empty;
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database) ||
            (!allowDisconnected && !(bool)(Property(editor, "IsConnected") ?? false)))
            throw new InvalidOperationException("Det aktive SQL-vinduet er ikke tilkoblet.");

        Document? document = dte.ActiveDocument;
        TextDocument? textDocument = document?.Object("TextDocument") as TextDocument;
        string text = textDocument?.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint) ?? string.Empty;
        string selectedText = (document?.Selection as TextSelection)?.Text ?? string.Empty;
        bool textTruncated = text.Length > 65536 || selectedText.Length > 65536;
        if (text.Length > 65536)
            text = text.Substring(0, 65536);

        if (selectedText.Length > 65536)
            selectedText = selectedText.Substring(0, 65536);

        string connectionString = session?.ConnectionString ?? string.Empty;
        string authentication = Property(info, "AuthenticationType")?.ToString() ?? string.Empty;
        string userName = Property(info, "UserName")?.ToString() ?? string.Empty;
        bool isExecuting = (bool)(Property(editor, "IsExecuting") ?? false);
        return new ActiveContext(editor, server, database, authentication, userName, connectionString, text,
            selectedText, textTruncated, isExecuting, session);
    }

    private static object? SqlEditor(IVsMonitorSelection selection)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (ErrorHandler.Failed(selection.GetCurrentElementValue(
                (uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out object frameValue)) ||
            frameValue is not IVsWindowFrame frame ||
            ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out object editor)))
            return null;

        return editor is not null && (editor.GetType().FullName ?? string.Empty).StartsWith(
            "Microsoft.SqlServer.Management.UI.VSIntegration.Editors.SqlScriptEditorControl", StringComparison.Ordinal)
            ? editor : null;
    }

    private static object? Property(object? value, string name)
    {
        return value?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);
    }

    private static IDbConnection? Session(object editor)
    {
        Type? type = editor.GetType();
        while (type is not null)
        {
            FieldInfo? field = type.GetField("m_connection", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field is not null)
                return field.GetValue(editor) as IDbConnection;

            type = type.BaseType;
        }

        return null;
    }
}
