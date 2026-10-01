using System;
using System.Windows.Forms;

namespace SsmsMcp.Extension;

public sealed class SsmsDialogOwner : IWin32Window
{
    public SsmsDialogOwner(IntPtr handle)
    {
        Handle = handle;
    }

    public IntPtr Handle { get; }
}
