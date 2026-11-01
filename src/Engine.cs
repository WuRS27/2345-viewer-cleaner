using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace ViewerExtrasCleaner {
    public class OperationResult {
        public string Folder;
        public string Summary;
        public bool Warning;
        public Scan After;
    }
    public sealed class Engine {
        readonly Action<string> log;
        readonly Action<bool> waiting;
        readonly Func<bool> stopWaiting;
        public Engine(Action<string> log,Action<bool> waiting,Func<bool> stopWaiting) { this.log=log;this.waiting=waiting;this.stopWaiting=stopWaiting; }
        static string[] CoreNames={"2345PicViewer.exe","2345Pic.exe","2345PicHome.exe","2345PicEdit.exe","2345PicCapture.exe","2345PicPrinter.exe","2345PdfReader.exe"};
        public OperationResult Clean(string manualRoot,bool uninstallSafeCenter,bool disableUpdater) {
            log("重新扫描并核对本次操作范围…");
            var scan=Discovery.Get(manualRoot);
            if(scan.ViewerRoots.Count==0)throw new InvalidOperationException("没有找到可验证的看图王主程序，未修改系统。");
            if(scan.ActiveWindows.Count>0)throw new InvalidOperationException("请先保存并关闭以下程序，再点击清理：\r\n"+String.Join("\r\n",scan.ActiveWindows));
            var targets=scan.Files.Where(f=>disableUpdater || !Path.GetFileName(f.Original).Equals("2345PicUpdate.exe",StringComparison.OrdinalIgnoreCase)).ToList();
            bool changeService=scan.ServiceApproved && (scan.ServiceStart!=4 || scan.ServiceRunning);
            if(targets.Count==0 && !changeService && !(uninstallSafeCenter && scan.SafeCenterInstalled))
                throw new InvalidOperationException(scan.RestartNeeded?"已完成可识别的停用操作，请保存工作后重启电脑。":"未发现需要处理的已识别组件。");
            string id=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
            string folder=Path.Combine(Host.BackupRoot,id);Safety.NoReparse(folder);Directory.CreateDirectory(folder);
            var journal=new Journal{Id=id,Computer=Environment.MachineName,UserSid=Host.UserSid,Created=DateTimeOffset.Now.ToString("o"),State="Preparing",
                ServiceImage=scan.ServiceImage,ServiceStart=scan.ServiceStart,ServiceWasRunning=scan.ServiceRunning,Associations=Host.Associations()};
            Action save=()=>Json.Save(Path.Combine(folder,"manifest.json"),journal);
            Action<string> record=text=>{journal.Messages.Add(DateTime.Now.ToString("HH:mm:ss")+" "+text);save();log(text);};
            bool warning=scan.Warnings.Count>0;
            foreach(string root in scan.ViewerRoots)foreach(string name in CoreNames) {
                string path=Path.Combine(root,name);if(File.Exists(path))journal.CoreHashes[path]=Safety.Sha256(path);
            }
            save(); Json.Save(Path.Combine(folder,"before-scan.json"),scan);
            try {
                foreach(string name in new[]{"2345Pic","2345SafeCenterSvc"}.Concat(Host.Drivers)) {
                    string image;int start,deleted;
                    if(!Host.RegService(name,out image,out start,out deleted))continue;
                    string output;
                    int result=Host.Run(Path.Combine(Environment.SystemDirectory,"reg.exe"),"export \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\"+name+"\" \""+Path.Combine(folder,"service-"+name+".reg")+"\" /y",15000,out output);
                    if(result!=0)throw new IOException("注册表备份失败，停止清理："+name+"\r\n"+output);
                }
                if(uninstallSafeCenter && scan.SafeCenterUninstaller!=null) {
                    Safety.Copy(scan.SafeCenterUninstaller,Path.Combine(folder,"official-uninstaller.backup"),false);
                    if(Safety.Sha256(scan.SafeCenterUninstaller)!=Safety.Sha256(Path.Combine(folder,"official-uninstaller.backup")))throw new IOException("官方卸载器备份校验失败。");
                }
                record("备份位置："+folder);
                foreach(string message in scan.Warnings)record("扫描提示："+message);
                if(changeService) {
                    journal.ServiceChangePlanned=true;save();
                    Host.ServiceStart("2345Pic",4);
                    using(var service=new ServiceController("2345Pic")) {
                        if(service.Status!=ServiceControllerStatus.Stopped) {
                            if(service.Status!=ServiceControllerStatus.StopPending)service.Stop();
                            service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(20));
                        }
                    }
                    record("看图后台服务已停止并设为禁用。");
                }
                if(uninstallSafeCenter && scan.SafeCenterInstalled) {
                    if(scan.SafeCenterUninstaller==null) { warning=true;record("没有可验证的官方卸载器，安全组件未卸载。其他已确认项目仍会处理。"); }
                    else {
                        journal.SafeCenterRemovalAttempted=true;save();
                        if(!Trust.IsTrusted2345(scan.SafeCenterUninstaller) || Safety.Sha256(scan.SafeCenterUninstaller)!=Safety.Sha256(Path.Combine(folder,"official-uninstaller.backup")))
                            throw new InvalidOperationException("卸载程序在扫描后发生变化，已停止运行。");
                        record("请在弹出的 2345 官方卸载窗口中点击“开始卸载”，完成后点击“确定”。");
                        waiting(true);
                        try {
                            using(var process=Process.Start(new ProcessStartInfo(scan.SafeCenterUninstaller){UseShellExecute=true,WorkingDirectory=scan.SafeCenterRoot,WindowStyle=ProcessWindowStyle.Normal})) { }
                            var timer=Stopwatch.StartNew();int goneCount=0;
                            while(timer.Elapsed<TimeSpan.FromMinutes(5) && !stopWaiting()) {
                                string image;int start,deleted;
                                bool registered=Host.RegService("2345SafeCenterSvc",out image,out start,out deleted) && deleted!=1;
                                bool listed=Host.Apps().Any(a=>a.Name.Contains("安全组件")||a.Name.Contains("安全中心"));
                                if(!registered && !listed) { if(++goneCount>=3)break; } else goneCount=0;
                                Thread.Sleep(1000);
                            }
                            if(goneCount<3) {warning=true;record("安全组件卸载尚未确认完成。可以先完成官方卸载，再重新扫描；工具没有强行终止卸载器。");}
                            else {
                                record("已确认安全组件服务和安装列表条目被移除。");
                                Host.StopExact(Path.Combine(scan.SafeCenterRoot,"2345RTProtect.exe"),record);
                            }
                        } finally { waiting(false); }
                    }
                }
                var identity=new Identity();var files=new FileTransactions(identity);
                foreach(var target in targets) {
                    try {
                        if(target.Directory) {
                            if(!identity.VendorFile(Path.Combine(target.Original,"PicService.exe")))throw new InvalidOperationException("守护目录身份在扫描后发生变化。");
                            foreach(string name in Host.BackgroundNames) {
                                string path=Path.Combine(target.Original,name);
                                if(File.Exists(path) && identity.VendorFile(path))Host.StopExact(path,record);
                            }
                        } else {
                            if(!identity.VendorFile(target.Original))throw new InvalidOperationException("文件身份在扫描后变化。");
                            Host.StopExact(target.Original,record);
                        }
                        files.Disable(target,folder,journal,save);
                        record("已备份并停用："+target.Original);
                    } catch(Exception ex) { warning=true;record("此项未能完成："+target.Original+"；"+ex.Message); }
                }
                var associations=Host.Associations();
                foreach(var pair in journal.CoreHashes) {
                    if(!File.Exists(pair.Key) || Safety.Sha256(pair.Key)!=pair.Value) {warning=true;record("需人工核对：主程序文件发生变化："+pair.Key);}
                }
                foreach(var pair in journal.Associations)if(associations[pair.Key]!=pair.Value) {warning=true;record("需人工核对："+pair.Key+" 默认打开方式发生变化。");}
                var after=Discovery.Get(manualRoot);Json.Save(Path.Combine(folder,"after-scan.json"),after);
                if(uninstallSafeCenter && after.SafeCenterInstalled)warning=true;
                if(after.ActiveExtras.Count>0) {warning=true;record("仍发现附加进程，请查看重新扫描结果。");}
                if(after.RestartNeeded)record("驱动已禁用或标记删除，但仍需保存工作并重启 Windows 完成退出。工具不会自动重启。");
                journal.State=warning?"CompletedWithWarnings":"Completed";save();
                string summary=warning?"部分项目需要继续处理，请查看日志。":"已完成所选清理项目。";
                if(after.RestartNeeded)summary+=" 保存工作后重启一次，以完成驱动清理。";
                if(disableUpdater)summary+=" 自动升级已停用。";
                record(summary);
                return new OperationResult{Folder=folder,After=after,Warning=warning,Summary=summary};
            } catch(Exception ex) {
                journal.State="Interrupted";record("操作已停止，已完成的修改有备份记录："+ex.Message);
                throw new InvalidOperationException(ex.Message+"\r\n备份与恢复记录："+folder,ex);
            }
        }
        public OperationResult Restore(string folder,string component,bool restoreService) {
            Safety.NoReparse(folder);
            if(!Safety.Equal(Path.GetDirectoryName(folder),Host.BackupRoot))throw new InvalidOperationException("只能恢复本机工具备份目录中的记录。");
            var journal=Json.Read<Journal>(Path.Combine(folder,"manifest.json"));
            if(journal.Format!=1 || journal.Computer!=Environment.MachineName || journal.UserSid!=Host.UserSid || Path.GetFileName(folder)!=journal.Id)
                throw new InvalidOperationException("备份与当前电脑、用户或格式不匹配，未执行恢复。");
            if(component!="Worker" && component!="Updater" && component!="All")throw new InvalidOperationException("恢复范围无效。");
            if(restoreService && component!="All")throw new InvalidOperationException("恢复服务需要先选择全部停用文件。");
            var scan=Discovery.Get(null);
            if(scan.ActiveWindows.Count>0)throw new InvalidOperationException("请先保存并关闭看图王及图片编辑窗口。");
            Action save=()=>Json.Save(Path.Combine(folder,"manifest.json"),journal);
            var transaction=new FileTransactions(new Identity());bool warning=false;
            var selected=journal.Files.Where(f=>component=="All" || Path.GetFileName(f.Original).Equals(component=="Worker"?"2345PicWorker.exe":"2345PicUpdate.exe",StringComparison.OrdinalIgnoreCase)).ToList();
            if(selected.Count==0 && !restoreService)throw new InvalidOperationException("该备份中没有所选类别的停用文件。");
            foreach(var item in selected) {
                try { transaction.Restore(item,journal,save);log("已恢复："+item.Original); }
                catch(Exception ex){warning=true;log("恢复已跳过："+item.Original+"；"+ex.Message);}
            }
            if(restoreService && journal.ServiceChangePlanned && !warning) {
                string image;int start,deleted;
                if(!Host.RegService("2345Pic",out image,out start,out deleted) || !Safety.Equal(Host.ExeFromCommand(image),journal.ServiceImage) ||
                    !(new Identity()).VendorFile(journal.ServiceImage) || start!=4 || deleted!=0)
                    throw new InvalidOperationException("服务配置已变化，未覆盖它。文件恢复结果已记录。");
                Host.ServiceStart("2345Pic",journal.ServiceStart);
                if(journal.ServiceWasRunning && journal.ServiceStart!=4) {
                    using(var service=new ServiceController("2345Pic")){service.Start();service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(20));}
                }
                journal.ServiceRestored=true;log("已恢复看图后台服务。它可能再次安装附加组件。");
            }
            journal.State=journal.Files.All(f=>f.Status=="Restored") && (!journal.ServiceChangePlanned||journal.ServiceRestored)?"Restored":"PartiallyRestored";save();
            return new OperationResult{Folder=folder,Warning=warning,Summary=warning?"部分恢复项目未完成，请查看日志。":"所选停用项已恢复；未重新安装安全组件。",After=Discovery.Get(null)};
        }
    }
}
