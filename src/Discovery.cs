using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace ViewerExtrasCleaner {
    public static class Discovery {
        public static Scan Get(string manualRoot) {
            var scan=new Scan(); var identity=new Identity();
            var candidates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var apps=Host.Apps();
            Action<string> add=p=>{ if(!String.IsNullOrWhiteSpace(p)) try{candidates.Add(Safety.Full(p));}catch{} };
            add(manualRoot);
            foreach(var app in apps.Where(a=>a.Name.Contains("看图"))) {
                add(app.Location);
                string exe=Host.ExeFromCommand(app.Uninstall); if(exe!=null)add(Path.GetDirectoryName(exe));
                exe=Host.ExeFromCommand(app.Icon); if(exe!=null)add(Path.GetDirectoryName(exe));
            }
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
            using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))
            using(var key=machine.OpenSubKey(@"SOFTWARE\2345Pic")) if(key!=null)add(Convert.ToString(key.GetValue("Path")));
            string image; int start,deleted;
            if(Host.RegService("2345Pic",out image,out start,out deleted)) {
                scan.ServiceImage=Host.ExeFromCommand(image); scan.ServiceStart=start;
                if(scan.ServiceImage!=null) {
                    string protect=Path.GetDirectoryName(scan.ServiceImage);
                    if(String.Equals(Path.GetFileName(protect),"Protect",StringComparison.OrdinalIgnoreCase))add(Path.GetDirectoryName(protect));
                }
                scan.ServiceRunning=Host.ServiceState("2345Pic")==4;
            }
            foreach(string basePath in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)})
                if(!String.IsNullOrEmpty(basePath))add(Path.Combine(basePath,"2345Soft","2345Pic"));
            foreach(var drive in DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed))add(Path.Combine(drive.RootDirectory.FullName,"2345Soft","2345Pic"));
            var processes=Host.Processes();
            foreach(var process in processes.Where(p=>new[]{"2345PicViewer.exe","2345Pic.exe","2345PicHome.exe"}.Contains(p.Name,StringComparer.OrdinalIgnoreCase)))add(Path.GetDirectoryName(process.Path));
            foreach(string root in candidates) {
                try {
                    Safety.NoReparse(root);
                    if(identity.ViewerRoot(root)) { scan.ViewerRoots.Add(root);scan.Rows.Add(new Row("看图王主程序","保留",root)); }
                } catch(Exception ex) { scan.Warnings.Add(ex.Message); }
            }
            scan.ApplicationRoots.AddRange(scan.ViewerRoots);
            string roaming=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"2345Pic","Application");
            if(Directory.Exists(roaming)) {
                Safety.NoReparse(roaming);
                foreach(string directory in Directory.GetDirectories(roaming)) {
                    try { Safety.NoReparse(directory); if(identity.ApplicationRoot(directory) && !scan.ApplicationRoots.Contains(directory,StringComparer.OrdinalIgnoreCase))scan.ApplicationRoots.Add(directory); }
                    catch(Exception ex){scan.Warnings.Add(ex.Message);}
                }
            }
            foreach(string root in scan.ViewerRoots) {
                string protect=Path.Combine(root,"Protect");
                if(Directory.Exists(protect)) {
                    try {
                        Safety.NoReparse(protect);
                        if(identity.VendorFile(Path.Combine(protect,"PicService.exe"))) {
                            scan.Files.Add(new FileChange{Root=root,Original=protect,Directory=true});
                            scan.Rows.Add(new Row("守护与附带安装器","将停用",protect));
                        } else scan.Warnings.Add("未识别的 Protect 目录已跳过："+protect);
                    }catch(Exception ex){scan.Warnings.Add(ex.Message);}
                }
            }
            foreach(string root in scan.ApplicationRoots)
            foreach(string name in new[]{"2345PicWorker.exe","2345PicUpdate.exe"}) {
                string file=Path.Combine(root,name);
                if(!File.Exists(file))continue;
                try {
                    Safety.NoReparse(file);
                    if(identity.VendorFile(file)) {
                        scan.Files.Add(new FileChange{Root=root,Original=file,Directory=false});
                        scan.Rows.Add(new Row(name.Contains("Worker")?"后台 Worker":"自动升级程序","将停用",file));
                    } else scan.Warnings.Add("发布者信息不匹配，已跳过："+file);
                }catch(Exception ex){scan.Warnings.Add(ex.Message);}
            }
            if(scan.ServiceImage!=null) {
                scan.ServiceApproved=scan.ViewerRoots.Any(r=>Safety.Equal(scan.ServiceImage,Path.Combine(r,"Protect","PicService.exe"))) &&
                    (identity.VendorFile(scan.ServiceImage) || (scan.ServiceStart==4 && !File.Exists(scan.ServiceImage)));
                scan.Rows.Add(new Row("看图后台服务",scan.ServiceApproved ? (scan.ServiceStart==4 && !scan.ServiceRunning?"已禁用":"将停止并禁用") : "身份待确认，不操作",scan.ServiceImage));
                if(!scan.ServiceApproved)scan.Warnings.Add("看图后台服务的可执行文件身份未能确认，该服务不会自动修改。");
            }
            foreach(var app in apps.Where(a=>a.Name.Contains("安全组件") || a.Name.Contains("安全中心"))) {
                scan.SafeCenterInstalled=true;
                string uninstaller=Host.ExeFromCommand(app.Uninstall);
                if(uninstaller==null || !File.Exists(uninstaller)) { scan.Warnings.Add("安全组件卸载程序缺失，请先通过 Windows 设置修复或卸载。");continue; }
                try {
                    string root=Path.GetDirectoryName(Safety.Full(uninstaller)); Safety.NoReparse(root);
                    bool separate=scan.ViewerRoots.All(r=>!Safety.Equal(r,root) && !Safety.Within(r,root) && !Safety.Within(root,r));
                    if(separate && String.Equals(Path.GetFileName(uninstaller),"Uninstall.exe",StringComparison.OrdinalIgnoreCase) &&
                       identity.VendorFile(uninstaller) && identity.VendorFile(Path.Combine(root,"2345RTProtect.exe")) &&
                       identity.VendorFile(Path.Combine(root,"2345SafeCenterSvc.exe")) && Trust.IsTrusted2345(uninstaller)) {
                        scan.SafeCenterUninstaller=uninstaller;scan.SafeCenterRoot=root;
                        scan.Rows.Add(new Row("2345 安全组件","将调用官方卸载器",root));
                    } else scan.Warnings.Add("安全组件的路径或离线数字签名验证未通过，工具不会运行它："+uninstaller);
                } catch(Exception ex) { scan.Warnings.Add(ex.Message); }
            }
            if(Host.RegService("2345SafeCenterSvc",out image,out start,out deleted) && deleted!=1) {
                scan.SafeCenterInstalled=true;
                if(scan.SafeCenterUninstaller==null)scan.Warnings.Add("安全中心服务仍存在，但没有找到可验证的独立卸载器。此项需要在 Windows 设置中处理。");
            }
            foreach(string name in Host.Drivers) {
                bool registered=Host.RegService(name,out image,out start,out deleted);
                int state=Host.ServiceState(name);
                if(registered || state!=0) {
                    bool pending=deleted==1 || start==4;
                    if(deleted==1 || (pending && state!=0))scan.RestartNeeded=true;
                    scan.Rows.Add(new Row("安全组件驱动 "+name,pending?"待重启清理":"由官方卸载器处理",image??""));
                }
            }
            foreach(var p in processes) {
                bool viewer=scan.ApplicationRoots.Any(r=>Safety.Within(p.Path,r));
                bool safe=scan.SafeCenterRoot!=null && Safety.Within(p.Path,scan.SafeCenterRoot);
                if(!viewer && !safe)continue;
                if(Host.BackgroundNames.Contains(p.Name,StringComparer.OrdinalIgnoreCase) || safe)scan.ActiveExtras.Add(p.Path);
                else if(!new[]{"2345Login.exe","2345PicDumper.exe"}.Contains(p.Name,StringComparer.OrdinalIgnoreCase))scan.ActiveWindows.Add(p.Name+" (PID "+p.Id+")");
            }
            if(scan.ViewerRoots.Count==0)scan.Warnings.Add("未找到可验证的看图王安装目录。可以点击“选择安装目录”，选择含 2345PicViewer.exe 的文件夹。");
            foreach(string warning in scan.Warnings.Distinct())scan.Rows.Add(new Row("提示","需留意",warning));
            return scan;
        }
    }
}
