[CmdletBinding()]
param([Parameter(Mandatory)][string] $ExePath)

$ErrorActionPreference = 'Stop'
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public static class PackagedStartupCheck {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct StartupInfo {
        public int cb; public string reserved, desktop, title;
        public int x,y,xSize,ySize,xChars,yChars,fill,flags;
        public short show,reserved2; public IntPtr reservedPtr,input,output,error;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo { public IntPtr process,thread; public int pid,tid; }
    delegate bool EnumWindow(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,int flags,uint access,IntPtr security);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CreateProcess(string application,StringBuilder command,IntPtr ps,IntPtr ts,bool inherit,uint flags,IntPtr env,string directory,ref StartupInfo startup,out ProcessInfo process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr handle,out uint code);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr handle,uint code);
    [DllImport("user32.dll")] static extern bool EnumDesktopWindows(IntPtr desktop,EnumWindow callback,IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int capacity);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
    public static string Run(string exe) {
        string name="MinePackZipCheck-"+Guid.NewGuid().ToString("N");
        IntPtr desktop=CreateDesktop(name,IntPtr.Zero,IntPtr.Zero,0,0x1FF,IntPtr.Zero);
        if(desktop==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        ProcessInfo pi=new ProcessInfo();
        try {
            StartupInfo si=new StartupInfo(); si.cb=Marshal.SizeOf(typeof(StartupInfo)); si.desktop=name;
            if(!CreateProcess(exe,new StringBuilder("\""+exe+"\""),IntPtr.Zero,IntPtr.Zero,false,0,IntPtr.Zero,System.IO.Path.GetDirectoryName(exe),ref si,out pi))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr main=IntPtr.Zero; string title=null;
            EnumWindow callback=delegate(IntPtr hwnd,IntPtr data) {
                uint pid; GetWindowThreadProcessId(hwnd,out pid);
                if(pid==(uint)pi.pid) { var text=new StringBuilder(512); GetWindowText(hwnd,text,512);
                    if(text.ToString().Contains("MinePack")) { main=hwnd; title=text.ToString(); } }
                return true;
            };
            var deadline=DateTime.UtcNow.AddSeconds(30);
            while(main==IntPtr.Zero && WaitForSingleObject(pi.process,0)==258 && DateTime.UtcNow<deadline) { EnumDesktopWindows(desktop,callback,IntPtr.Zero); Thread.Sleep(200); }
            if(main==IntPtr.Zero) throw new Exception("Packaged EXE did not create its main window.");
            Thread.Sleep(2500); IntPtr result;
            if(WaitForSingleObject(pi.process,0)!=258 || SendMessageTimeout(main,0,IntPtr.Zero,IntPtr.Zero,2,5000,out result)==IntPtr.Zero)
                throw new Exception("Packaged main window crashed or stopped responding.");
            PostMessage(main,0x10,IntPtr.Zero,IntPtr.Zero);
            uint exitCode;
            if(WaitForSingleObject(pi.process,10000)!=0 || !GetExitCodeProcess(pi.process,out exitCode) || exitCode!=0) throw new Exception("Packaged EXE did not close normally.");
            return "PASS: packaged EXE opened responsive window '"+title+"' and exited with code 0 on an isolated desktop; user desktop was not switched.";
        } finally {
            if(pi.process!=IntPtr.Zero && WaitForSingleObject(pi.process,0)==258) { TerminateProcess(pi.process,1); WaitForSingleObject(pi.process,5000); }
            if(pi.thread!=IntPtr.Zero) CloseHandle(pi.thread); if(pi.process!=IntPtr.Zero) CloseHandle(pi.process); CloseDesktop(desktop);
        }
    }
}
'@
[PackagedStartupCheck]::Run($ExePath)
