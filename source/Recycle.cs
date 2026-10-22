using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Chitoge {
public static class FilePolicy {
    public static string[] Validate(IEnumerable<string> paths) {
        var list = new List<string>();
        foreach (string raw in paths) {
            if (String.IsNullOrWhiteSpace(raw) || !Path.IsPathRooted(raw) || raw.StartsWith(@"\\") || raw.IndexOf(':', 2) >= 0)
                throw new IOException("只接受本机磁盘上的普通文件或文件夹。");
            string p = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar);
            string root = Path.GetPathRoot(p);
            if (p.Length <= root.Length || new DriveInfo(root).DriveType != DriveType.Fixed)
                throw new IOException("不接收磁盘根目录、网络位置或移动设备。");
            if (!File.Exists(p) && !Directory.Exists(p)) throw new IOException("文件已不存在：" + p);
            string[] protectedTrees = { Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') };
            foreach (string special in protectedTrees) if (!String.IsNullOrEmpty(special) && (Within(p, special) || Within(special, p)))
                throw new IOException("不能回收系统目录、程序自身或它们的上级目录。");
            foreach (Environment.SpecialFolder sf in new [] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments })
                if (String.Equals(p, Environment.GetFolderPath(sf).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("请拖入文件夹里的具体项目，不要拖入整个个人目录。");
            for (string q = p; !String.IsNullOrEmpty(q) && q.Length > root.Length; q = Path.GetDirectoryName(q)) {
                if ((File.GetAttributes(q) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("暂不接收链接、重定向目录或云端占位文件：" + p);
            }
            if (!list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
        }
        if (list.Count == 0) throw new IOException("没有可回收的文件。");
        // A dropped folder already contains its descendants; never queue both.
        return list.Where(p => !list.Any(q => !String.Equals(p,q,StringComparison.OrdinalIgnoreCase) && Directory.Exists(q) && Within(p,q))).ToArray();
    }
    public static bool Within(string p, string parent) {
        return String.Equals(p, parent, StringComparison.OrdinalIgnoreCase) || p.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem {
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint sigdn, out IntPtr name);
    void GetAttributes(uint mask, out uint attributes);
    void Compare(IShellItem other, uint hint, out int order);
}
[ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IFileOperation {
    void Advise(IFileOperationProgressSink sink, out uint cookie);
    void Unadvise(uint cookie);
    void SetOperationFlags(uint flags);
    void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
    void SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object dialog);
    void SetProperties([MarshalAs(UnmanagedType.Interface)] object properties);
    void SetOwnerWindow(uint hwnd);
    void ApplyPropertiesToItem(IShellItem item);
    void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
    void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
    void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
    void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
    void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
    void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
    void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
    void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
    void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
    void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IFileOperationProgressSink sink);
    void PerformOperations();
    void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}
[ComVisible(true), Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFileOperationProgressSink {
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int hr);
    [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int hr, IShellItem created);
    [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int hr, IShellItem created);
    [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int hr, IShellItem created);
    [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
    [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int hr, IShellItem created);
    [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int hr, IShellItem created);
    [PreserveSig] int UpdateProgress(uint total, uint done);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RecycleSink : IFileOperationProgressSink {
    public bool Recycled, Blocked;
    public int Error;
    public string RecycledPath;
    public uint PreFlags;
    public int PreDeleteItem(uint f, IShellItem i) {
        PreFlags=f;
        // Refuse any shell delete callback that is not marked as recycling.
        if ((f & 0x80) == 0) { Blocked=true; return unchecked((int)0x800704C7); }
        return 0;
    }
    public int PostDeleteItem(uint f, IShellItem i, int hr, IShellItem created) {
        Error=hr;
        if (hr >= 0 && created != null) { Recycled=true; RecycledPath=RecycleService.Name(created); }
        return 0;
    }
    public int StartOperations(){return 0;} public int FinishOperations(int h){return 0;}
    public int PreRenameItem(uint f,IShellItem i,string n){return 0;}
    public int PostRenameItem(uint f,IShellItem i,string n,int h,IShellItem c){return 0;}
    public int PreMoveItem(uint f,IShellItem i,IShellItem d,string n){return 0;}
    public int PostMoveItem(uint f,IShellItem i,IShellItem d,string n,int h,IShellItem c){return 0;}
    public int PreCopyItem(uint f,IShellItem i,IShellItem d,string n){return 0;}
    public int PostCopyItem(uint f,IShellItem i,IShellItem d,string n,int h,IShellItem c){return 0;}
    public int PreNewItem(uint f,IShellItem d,string n){return 0;}
    public int PostNewItem(uint f,IShellItem d,string n,string t,uint a,int h,IShellItem c){return 0;}
    public int UpdateProgress(uint t,uint d){return 0;} public int ResetTimer(){return 0;}
    public int PauseTimer(){return 0;} public int ResumeTimer(){return 0;}
}
public class RecycleResult {
    public int Completed;
    public bool Aborted;
    public string Error="";
    public List<string> StoredPaths=new List<string>();
}
public static class RecycleService {
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]
    static extern void SHCreateItemFromParsingName(string name,IntPtr context,ref Guid iid,out IShellItem item);
    public static string Name(IShellItem item) {
        IntPtr p=IntPtr.Zero;
        try { item.GetDisplayName(0x80058000,out p); return Marshal.PtrToStringUni(p); }
        catch { return ""; } finally {if(p!=IntPtr.Zero) Marshal.FreeCoTaskMem(p);}
    }
    public static RecycleResult Send(string[] paths,IntPtr owner) {
        paths=FilePolicy.Validate(paths); // Recheck immediately before touching files.
        var result=new RecycleResult(); var items=new List<IShellItem>(); var sinks=new List<RecycleSink>();
        IFileOperation op=null;
        try {
            op=(IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09")));
            // Recycle + Explorer undo + stop on failure; no permanent-delete fallback.
            op.SetOperationFlags(0x00080000 | 0x20000000 | 0x00100000 | 0x0400 | 0x0010 | 0x0040 | 0x2000 | 0x0004 | 0x4000);
            op.SetOwnerWindow(unchecked((uint)owner.ToInt64()));
            foreach(string path in paths) {
                Guid iid=typeof(IShellItem).GUID; IShellItem item;
                SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out item); items.Add(item);
                var sink=new RecycleSink(); sinks.Add(sink); op.DeleteItem(item,sink);
            }
            op.PerformOperations(); bool aborted; op.GetAnyOperationsAborted(out aborted); result.Aborted=aborted;
        } catch(Exception e) {result.Error=e.Message; result.Aborted=true;}
        finally {
            foreach(var s in sinks) {
                if(s.Recycled) {result.Completed++; result.StoredPaths.Add(s.RecycledPath);}
                if(s.Blocked) result.Error="Windows 未提供可回收的删除方式，已停止，未改用永久删除。（状态 0x"+s.PreFlags.ToString("X")+"）";
                else if(s.Error<0 && result.Error=="") result.Error=Marshal.GetExceptionForHR(s.Error).Message;
            }
            foreach(var item in items) Marshal.ReleaseComObject(item);
            if(op!=null) Marshal.ReleaseComObject(op);
        }
        return result;
    }
}
}
