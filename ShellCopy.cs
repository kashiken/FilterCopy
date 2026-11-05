using System.IO;
using System.Runtime.InteropServices;

namespace FilterCopy;

public record CopyOutcome(bool Aborted, List<string> Errors);

public static class ShellCopy
{
    public static Task<CopyOutcome> RunAsync(List<(string Source, string Destination)> plan, nint owner)
    {
        var completion = new TaskCompletionSource<CopyOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Run(plan, owner)); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "FilterCopy Shell STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static CopyOutcome Run(List<(string Source, string Destination)> plan, nint owner)
    {
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"))!)!;
        var items = new List<IShellItem>();
        var folders = new Dictionary<string, IShellItem>(StringComparer.OrdinalIgnoreCase);
        var sink = new CopyProgressSink();
        uint cookie = 0;
        try
        {
            operation.SetOwnerWindow(owner);
            // Keep Shell confirmation/error/progress UI; enable Undo and suppress mkdir prompts only.
            operation.SetOperationFlags(0x0040 | 0x0200 | 0x20000000);
            operation.Advise(sink, out cookie);
            foreach (var (source, target) in plan)
            {
                var directory = Path.GetDirectoryName(target)!;
                EnsureSafeDirectory(directory);
                if (!folders.TryGetValue(directory, out var folder))
                { folder = CreateItem(directory); folders.Add(directory, folder); items.Add(folder); }
                var item = CreateItem(source); items.Add(item);
                operation.CopyItem(item, folder, Path.GetFileName(target), null);
            }
            // All CopyItem calls are queued on this single instance. Never perform per file.
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            return new(aborted, sink.Errors);
        }
        finally
        {
            if (cookie != 0) operation.Unadvise(cookie);
            foreach (var item in items) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }

    private static void EnsureSafeDirectory(string directory)
    {
        // Reject junctions/symlinks in the destination chain rather than writing outside the preview.
        var chain = new Stack<string>();
        for (var current = directory; current is not null; current = Path.GetDirectoryName(current)) chain.Push(current);
        while (chain.TryPop(out var current))
        {
            if (Directory.Exists(current))
            { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException($"コピー先にリンクが含まれています: {current}"); }
            else Directory.CreateDirectory(current);
        }
    }

    private static IShellItem CreateItem(string path)
    {
        var iid = typeof(IShellItem).GUID;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out var item));
        return item;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out IShellItem item);
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint sigdn, out nint name);
    void GetAttributes(uint mask, out uint attributes);
    void Compare(IShellItem other, uint hint, out int order);
}

[ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFileOperation
{
    void Advise(IFileOperationProgressSink sink, out uint cookie);
    void Unadvise(uint cookie);
    void SetOperationFlags(uint flags);
    void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
    void SetProgressDialog(nint dialog);
    void SetProperties(nint properties);
    void SetOwnerWindow(nint owner);
    void ApplyPropertiesToItem(IShellItem item);
    void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
    void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink);
    void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
    void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);
    void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
    void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);
    void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
    void DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
    void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
    void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IFileOperationProgressSink? sink);
    void PerformOperations();
    void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}

[ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFileOperationProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int result);
    [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
    [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
    [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
    [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
    [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created);
    [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IShellItem? created);
    [PreserveSig] int UpdateProgress(uint total, uint completed);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public class CopyProgressSink : IFileOperationProgressSink
{
    public List<string> Errors { get; } = [];
    public int StartOperations() => 0;
    public int FinishOperations(int result) { if (result < 0) Errors.Add(Marshal.GetExceptionForHR(result)?.Message ?? $"HRESULT: {result:X8}"); return 0; }
    public int PreRenameItem(uint f, IShellItem i, string n) => 0;
    public int PostRenameItem(uint f, IShellItem i, string n, int r, IShellItem? c) => 0;
    public int PreMoveItem(uint f, IShellItem i, IShellItem d, string n) => 0;
    public int PostMoveItem(uint f, IShellItem i, IShellItem d, string n, int r, IShellItem? c) => 0;
    public int PreCopyItem(uint f, IShellItem i, IShellItem d, string n) => 0;
    public int PostCopyItem(uint f, IShellItem i, IShellItem d, string n, int r, IShellItem? c)
    { if (r < 0) Errors.Add($"{n}: {Marshal.GetExceptionForHR(r)?.Message}"); return 0; }
    public int PreDeleteItem(uint f, IShellItem i) => 0;
    public int PostDeleteItem(uint f, IShellItem i, int r, IShellItem? c) => 0;
    public int PreNewItem(uint f, IShellItem d, string n) => 0;
    public int PostNewItem(uint f, IShellItem d, string n, string t, uint a, int r, IShellItem? c) => 0;
    public int UpdateProgress(uint t, uint c) => 0;
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;
}
