using System.IO;
using System.Runtime.InteropServices;

namespace MinePack.Installer;

internal static class RecycleBinService
{
    private const uint FofNoErrorUi = 0x00000400;
    private const uint FofxRecycleOnDelete = 0x00080000;
    private const uint FofxEarlyFailure = 0x00100000;
    private const uint ClsctxInprocServer = 0x1;
    private static readonly Guid FileOperationClass = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    private static readonly Guid FileOperationInterface = new("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
    private static readonly Guid ShellItemInterface = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    public static Task MoveToRecycleBinAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                MoveOnSta(path);
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        })
        {
            IsBackground = true,
            Name = "MinePack Recycle Bin operation"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void MoveOnSta(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Recycle Bin operations are required.");
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(fullPath) || fullPath.StartsWith("\\\\", StringComparison.Ordinal))
            throw new IOException("The selected local folder is no longer available.");

        var operationClass = FileOperationClass;
        var operationIid = FileOperationInterface;
        var hr = CoCreateInstance(ref operationClass, IntPtr.Zero, ClsctxInprocServer, ref operationIid,
            out var operation);
        Marshal.ThrowExceptionForHR(hr);
        IShellItem? item = null;
        try
        {
            Check(operation.SetOperationFlags(FofNoErrorUi | FofxRecycleOnDelete | FofxEarlyFailure));
            var itemIid = ShellItemInterface;
            Check(SHCreateItemFromParsingName(fullPath, IntPtr.Zero, ref itemIid, out item));
            Check(operation.DeleteItem(item!, IntPtr.Zero));
            var performResult = operation.PerformOperations();
            var abortedResult = operation.GetAnyOperationsAborted(out var aborted);
            Check(abortedResult);
            Check(performResult);
            if (aborted) throw new IOException("Windows reported that the Recycle Bin operation was cancelled.");
            if (Directory.Exists(fullPath) || File.Exists(fullPath))
                throw new IOException("Windows did not remove the source directory after the Recycle Bin operation.");
        }
        finally
        {
            if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
            if (Marshal.IsComObject(operation)) Marshal.FinalReleaseComObject(operation);
        }
    }

    private static void Check(int hresult) => Marshal.ThrowExceptionForHR(hresult);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoCreateInstance(ref Guid classId, IntPtr outer, uint context, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IFileOperation instance);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport]
    [Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr progressDialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr ownerWindow);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item, IntPtr sink);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items, IntPtr sink);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IShellItem destination);
        [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IShellItem destination);
        [PreserveSig] int DeleteItem(IShellItem item, IntPtr sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string templateName, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }
}
