using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace ViewerExtrasCleaner {
    [DataContract] public class FileChange {
        [DataMember] public string Root;
        [DataMember] public string Original;
        [DataMember] public string Disabled;
        [DataMember] public string Backup;
        [DataMember] public string Hash;
        [DataMember] public string Status;
        [DataMember] public bool Directory;
    }
    [DataContract] public class Journal {
        [DataMember] public int Format = 1;
        [DataMember] public string Id;
        [DataMember] public string Computer;
        [DataMember] public string UserSid;
        [DataMember] public string Created;
        [DataMember] public string State;
        [DataMember] public string ServiceImage;
        [DataMember] public int ServiceStart = -1;
        [DataMember] public bool ServiceWasRunning;
        [DataMember] public bool ServiceChangePlanned;
        [DataMember] public bool ServiceRestored;
        [DataMember] public bool SafeCenterRemovalAttempted;
        [DataMember] public List<FileChange> Files = new List<FileChange>();
        [DataMember] public List<string> Messages = new List<string>();
        [DataMember] public Dictionary<string,string> CoreHashes = new Dictionary<string,string>();
        [DataMember] public Dictionary<string,string> Associations = new Dictionary<string,string>();
    }
    [DataContract] public class Row {
        [DataMember] public string Item;
        [DataMember] public string State;
        [DataMember] public string Path;
        public Row(string item, string state, string path) { Item=item; State=state; Path=path; }
    }
    [DataContract] public class Scan {
        [DataMember] public List<string> ViewerRoots = new List<string>();
        [DataMember] public List<string> ApplicationRoots = new List<string>();
        [DataMember] public List<string> Warnings = new List<string>();
        [DataMember] public List<Row> Rows = new List<Row>();
        [DataMember] public List<FileChange> Files = new List<FileChange>();
        [DataMember] public string SafeCenterUninstaller;
        [DataMember] public string SafeCenterRoot;
        [DataMember] public string ServiceImage;
        [DataMember] public int ServiceStart = -1;
        [DataMember] public bool ServiceRunning;
        [DataMember] public bool ServiceApproved;
        [DataMember] public bool SafeCenterInstalled;
        [DataMember] public bool RestartNeeded;
        [DataMember] public List<string> ActiveWindows = new List<string>();
        [DataMember] public List<string> ActiveExtras = new List<string>();
    }
    public class AppEntry {
        public string Name, Location, Uninstall, Icon, Key;
    }
    public class ProcessEntry {
        public int Id;
        public string Path;
        public string Name { get { return System.IO.Path.GetFileName(Path); } }
    }
    public interface IIdentity {
        bool ViewerRoot(string path);
        bool ApplicationRoot(string path);
        bool VendorFile(string path);
    }
    public sealed class Identity : IIdentity {
        public bool VendorFile(string path) {
            try {
                if (!File.Exists(path)) return false;
                var v=FileVersionInfo.GetVersionInfo(path);
                string description=v.ProductName + " " + v.FileDescription;
                return ContainsVendor(v.CompanyName) && (ContainsVendor(description) || description.Contains("看图") || description.Contains("安全中心"));
            } catch { return false; }
        }
        public static bool ContainsVendor(string s) {
            return !String.IsNullOrEmpty(s) && (s.IndexOf("2345",StringComparison.OrdinalIgnoreCase)>=0 || s.Contains("二三四五"));
        }
        public bool ViewerRoot(string path) {
            return VendorFile(Path.Combine(path,"2345PicViewer.exe")) || VendorFile(Path.Combine(path,"2345Pic.exe"));
        }
        public bool ApplicationRoot(string path) {
            if (ViewerRoot(path)) return true;
            // Version folders below the current user's 2345Pic application folder only.
            string allowed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"2345Pic","Application");
            return Safety.Within(path,allowed) &&
                (VendorFile(Path.Combine(path,"2345Setting.exe")) || VendorFile(Path.Combine(path,"2345PicLoader.exe")));
        }
    }
    public static class Safety {
        public static string Full(string path) {
            if (String.IsNullOrWhiteSpace(path) || path.IndexOfAny(new char[]{'\r','\n','\0','"'})>=0)
                throw new InvalidOperationException("路径无效。");
            string p=Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            if (p.StartsWith(@"\\",StringComparison.Ordinal) || p.Substring(2).Contains(":"))
                throw new InvalidOperationException("只支持本机磁盘上的普通路径。");
            return p.TrimEnd(Path.DirectorySeparatorChar);
        }
        public static bool Equal(string a,string b) { return String.Equals(Full(a),Full(b),StringComparison.OrdinalIgnoreCase); }
        public static bool Within(string path,string root) {
            try { return Full(path).StartsWith(Full(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        public static void NoReparse(string path) {
            string current=Full(path);
            while (!String.IsNullOrEmpty(current)) {
                if (File.Exists(current) || Directory.Exists(current)) {
                    if ((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                        throw new InvalidOperationException("为避免误操作，跳过链接或重解析点："+current);
                }
                string parent=Path.GetDirectoryName(current);
                if (parent==current) break;
                current=parent;
            }
        }
        public static void ValidateTarget(FileChange item, IIdentity identity) {
            NoReparse(item.Root); NoReparse(item.Original);
            if (!Equal(Path.GetDirectoryName(item.Original),item.Root))
                throw new InvalidOperationException("目标不在已验证的程序目录内。");
            string name=Path.GetFileName(item.Original);
            if (item.Directory) {
                if (!String.Equals(name,"Protect",StringComparison.OrdinalIgnoreCase) || !identity.ViewerRoot(item.Root))
                    throw new InvalidOperationException("守护目录身份无法确认。");
            } else {
                if (!(String.Equals(name,"2345PicWorker.exe",StringComparison.OrdinalIgnoreCase) ||
                      String.Equals(name,"2345PicUpdate.exe",StringComparison.OrdinalIgnoreCase)) || !identity.ApplicationRoot(item.Root))
                    throw new InvalidOperationException("附加程序身份无法确认。");
            }
        }
        public static string Sha256(string path) {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
        }
        public static List<string> TreeFiles(string root) {
            NoReparse(root);
            var result=new List<string>(); var queue=new Queue<string>(); queue.Enqueue(root);
            long bytes=0;
            while(queue.Count>0) {
                string directory=queue.Dequeue();
                foreach(var child in Directory.GetFileSystemEntries(directory)) {
                    NoReparse(child);
                    if (Directory.Exists(child)) queue.Enqueue(child);
                    else { result.Add(child); bytes+=new FileInfo(child).Length; }
                    if(result.Count+queue.Count>20000 || bytes>1024L*1024*1024)
                        throw new InvalidOperationException("附加组件目录大小异常，已停止处理。");
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase); return result;
        }
        public static string Digest(string path,bool directory) {
            if(!directory) return Sha256(path);
            var text=new StringBuilder();
            foreach(var file in TreeFiles(path)) text.Append(file.Substring(Full(path).Length+1).ToLowerInvariant()).Append('\0').Append(Sha256(file)).Append('\n');
            using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");
        }
        public static void Copy(string from,string to,bool directory) {
            NoReparse(from); NoReparse(to);
            if(directory) {
                Directory.CreateDirectory(to);
                foreach(var file in TreeFiles(from)) {
                    string target=Path.Combine(to,file.Substring(Full(from).Length+1));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file,target,false);
                }
            } else { Directory.CreateDirectory(Path.GetDirectoryName(to)); File.Copy(from,to,false); }
        }
        public static bool Exists(string path) { return File.Exists(path)||Directory.Exists(path); }
        public static void Move(string from,string to,bool directory) {
            NoReparse(from); NoReparse(to);
            if(!Equal(Path.GetDirectoryName(from),Path.GetDirectoryName(to))) throw new InvalidOperationException("只允许在原目录内改名。");
            if(Exists(to)) throw new IOException("目标已存在，未覆盖："+to);
            if(directory) Directory.Move(from,to); else File.Move(from,to);
        }
    }
    public static class Json {
        public static void Save<T>(string path,T value) {
            Safety.NoReparse(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+".new";
            Safety.NoReparse(temporary);
            using(var file=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)) {
                new DataContractJsonSerializer(typeof(T)).WriteObject(file,value); file.Flush(true);
            }
            if(File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
        }
        public static T Read<T>(string path) {
            Safety.NoReparse(path);
            if(new FileInfo(path).Length>8*1024*1024) throw new InvalidOperationException("记录文件过大。");
            using(var file=File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(file);
        }
    }
    public static class Host {
        public static readonly string[] Drivers={"ysdefmon","ysfwdrv","ysnetmgr","yssysflt"};
        public static readonly string[] BackgroundNames={"PicService.exe","YsPicProtect.exe","2345PicWorker.exe","2345PicUpdate.exe","AssistPic.exe","PicServiceManager.exe"};
        public static string DataRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"2345ViewerCleaner"); } }
        public static string BackupRoot { get { return Path.Combine(DataRoot,"Backups"); } }
        public static string UserSid { get { return WindowsIdentity.GetCurrent().User.Value; } }
        public static string ExeFromCommand(string text) {
            if(String.IsNullOrWhiteSpace(text)) return null;
            text=Environment.ExpandEnvironmentVariables(text).Trim();
            if(text.StartsWith("\"")) { int end=text.IndexOf('"',1); return end>1 ? text.Substring(1,end-1) : null; }
            var match=Regex.Match(text,@"^(.+?\.exe)(?:\s|,|$)",RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
        public static List<AppEntry> Apps() {
            var found=new List<AppEntry>();
            foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}) {
                using(var baseKey=RegistryKey.OpenBaseKey(hive,view))
                using(var key=baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")) {
                    if(key==null) continue;
                    foreach(string name in key.GetSubKeyNames()) {
                        using(var app=key.OpenSubKey(name)) {
                            if(app==null) continue;
                            string display=Convert.ToString(app.GetValue("DisplayName"));
                            if(!Identity.ContainsVendor(display)) continue;
                            found.Add(new AppEntry{Name=display,Location=Convert.ToString(app.GetValue("InstallLocation")),
                                Uninstall=Convert.ToString(app.GetValue("UninstallString")),Icon=Convert.ToString(app.GetValue("DisplayIcon")),
                                Key=hive+"/"+view+"/"+name});
                        }
                    }
                }
            }
            return found;
        }
        public static int ServiceState(string name) {
            IntPtr manager=Native.OpenSCManager(null,null,1);
            if(manager==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try {
                IntPtr service=Native.OpenService(manager,name,4);
                if(service==IntPtr.Zero) { int error=Marshal.GetLastWin32Error(); if(error==1060 || error==1072) return 0; throw new System.ComponentModel.Win32Exception(error); }
                try { Native.SERVICE_STATUS state; if(!Native.QueryServiceStatus(service,out state)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); return state.State; }
                finally { Native.CloseServiceHandle(service); }
            } finally { Native.CloseServiceHandle(manager); }
        }
        public static bool RegService(string name,out string image,out int start,out int deleted) {
            image=null; start=-1; deleted=0;
            using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name)) {
                if(key==null) return false;
                image=Convert.ToString(key.GetValue("ImagePath")); start=Convert.ToInt32(key.GetValue("Start",-1)); deleted=Convert.ToInt32(key.GetValue("DeleteFlag",0)); return true;
            }
        }
        public static void ServiceStart(string name,int start) {
            if(name!="2345Pic" || (start!=2 && start!=3 && start!=4)) throw new InvalidOperationException("不支持的服务操作。");
            IntPtr manager=Native.OpenSCManager(null,null,1);
            if(manager==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try {
                IntPtr service=Native.OpenService(manager,name,2);
                if(service==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try { if(!Native.ChangeServiceConfig(service,0xFFFFFFFF,(uint)start,0xFFFFFFFF,null,null,IntPtr.Zero,null,null,null,null)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
                finally { Native.CloseServiceHandle(service); }
            } finally { Native.CloseServiceHandle(manager); }
        }
        public static List<ProcessEntry> Processes() {
            var result=new List<ProcessEntry>();
            foreach(var p in Process.GetProcesses()) {
                using(p) {
                    IntPtr handle=Native.OpenProcess(0x1000,false,p.Id);
                    if(handle==IntPtr.Zero) continue;
                    try { var path=new StringBuilder(32768); int length=path.Capacity;
                        if(Native.QueryFullProcessImageName(handle,0,path,ref length)) result.Add(new ProcessEntry{Id=p.Id,Path=path.ToString()});
                    } finally { Native.CloseHandle(handle); }
                }
            }
            return result;
        }
        public static void StopExact(string path,Action<string> log) {
            foreach(var entry in Processes().Where(p=>Safety.Equal(p.Path,path))) {
                try {
                    using(var process=Process.GetProcessById(entry.Id)) {
                        IntPtr pinnedHandle=process.Handle;
                        // Recheck after opening, so a reused PID cannot target another application.
                        string actual=Native.ProcessPath(process.Id);
                        if(actual==null || !Safety.Equal(actual,path)) continue;
                        process.Kill(); if(!process.WaitForExit(10000)) throw new TimeoutException("进程未及时退出。");
                        log("已停止："+entry.Name);
                    }
                } catch(ArgumentException) {} catch(InvalidOperationException) {}
            }
        }
        public static int Run(string file,string arguments,int timeout,out string output) {
            var info=new ProcessStartInfo(file,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            var lines=new StringBuilder(); object gate=new object();
            using(var process=new Process{StartInfo=info}) {
                process.OutputDataReceived+=(s,e)=>{if(e.Data!=null) lock(gate) lines.AppendLine(e.Data);};
                process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null) lock(gate) lines.AppendLine(e.Data);};
                process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                if(!process.WaitForExit(timeout)) { try{process.Kill();}catch{} throw new TimeoutException("本地命令超时："+Path.GetFileName(file)); }
                process.WaitForExit(); output=lines.ToString(); return process.ExitCode;
            }
        }
        public static Dictionary<string,string> Associations() {
            var result=new Dictionary<string,string>();
            foreach(string ext in new[]{".png",".jpg",".jpeg",".bmp",".gif",".webp",".tif",".tiff"})
                using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\"+ext+@"\UserChoice"))
                    result[ext]=key==null ? "" : Convert.ToString(key.GetValue("ProgId"))+"|"+Convert.ToString(key.GetValue("Hash"));
            return result;
        }
    }
    internal static class Native {
        [StructLayout(LayoutKind.Sequential)] internal struct SERVICE_STATUS { public int Type,State,Accepted,Win32Exit,SpecificExit,Checkpoint,WaitHint; }
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern IntPtr OpenSCManager(string machine,string database,int access);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern IntPtr OpenService(IntPtr manager,string name,int access);
        [DllImport("advapi32.dll",SetLastError=true)] internal static extern bool QueryServiceStatus(IntPtr service,out SERVICE_STATUS status);
        [DllImport("advapi32.dll",SetLastError=true)] internal static extern bool CloseServiceHandle(IntPtr handle);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool ChangeServiceConfig(IntPtr service,uint type,uint start,uint error,string path,string group,IntPtr tag,string dependencies,string user,string password,string display);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern IntPtr OpenProcess(int access,bool inherit,int id);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool QueryFullProcessImageName(IntPtr handle,int flags,StringBuilder path,ref int length);
        internal static string ProcessPath(int id) { IntPtr h=OpenProcess(0x1000,false,id); if(h==IntPtr.Zero)return null;
            try { var b=new StringBuilder(32768);int n=b.Capacity;return QueryFullProcessImageName(h,0,b,ref n)?b.ToString():null;} finally{CloseHandle(h);} }
    }
    public static class Trust {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct FileInfoData { public uint Size; public string Path; public IntPtr File,KnownSubject; }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct TrustData {
            public uint Size; public IntPtr Policy,Sip; public uint Ui,Revocation,Choice; public IntPtr File;
            public uint StateAction; public IntPtr State; public string Url; public uint Flags,UiContext;
        }
        [DllImport("wintrust.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int WinVerifyTrust(IntPtr window,[In] ref Guid action,[In] ref TrustData data);
        public static bool IsTrusted2345(string path) {
            Safety.NoReparse(path);
            try {
                using(var cert=new X509Certificate2(X509Certificate.CreateFromSignedFile(path))) {
                    if(!Identity.ContainsVendor(cert.Subject)) return false;
                }
            } catch { return false; }
            var file=new FileInfoData{Size=(uint)Marshal.SizeOf(typeof(FileInfoData)),Path=path};
            IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FileInfoData)));
            Marshal.StructureToPtr(file,pointer,false);
            try {
                var data=new TrustData{Size=(uint)Marshal.SizeOf(typeof(TrustData)),Ui=2,Revocation=0,Choice=1,File=pointer,Flags=0x1000|0x10};
                Guid action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                return WinVerifyTrust(new IntPtr(-1),ref action,ref data)==0;
            } finally { Marshal.DestroyStructure(pointer,typeof(FileInfoData));Marshal.FreeHGlobal(pointer); }
        }
    }
}
