using System;
using System.IO;
using System.Linq;

namespace ViewerExtrasCleaner {
    public sealed class FileTransactions {
        readonly IIdentity identity;
        public FileTransactions(IIdentity identity) { this.identity=identity; }
        public void Disable(FileChange item,string sessionFolder,Journal journal,Action persist) {
            Safety.ValidateTarget(item,identity);
            if(!Safety.Exists(item.Original))throw new IOException("扫描后文件已变化，请重新扫描："+item.Original);
            item.Disabled=item.Original+".disabled-2345cleaner-"+journal.Id;
            if(Safety.Exists(item.Disabled))throw new IOException("停用副本已存在，未覆盖。");
            item.Backup=Path.Combine(sessionFolder,"files",journal.Files.Count.ToString("D4"));
            Safety.NoReparse(sessionFolder);
            item.Hash=Safety.Digest(item.Original,item.Directory);
            item.Status="BackupPlanned";journal.Files.Add(item);persist();
            Safety.Copy(item.Original,item.Backup,item.Directory);
            if(Safety.Digest(item.Backup,item.Directory)!=item.Hash || Safety.Digest(item.Original,item.Directory)!=item.Hash)
                throw new IOException("备份校验失败，原文件未改名。");
            item.Status="RenamePlanned";persist();
            if(Safety.Digest(item.Original,item.Directory)!=item.Hash)throw new IOException("改名前文件发生变化，已保留原路径。");
            Safety.Move(item.Original,item.Disabled,item.Directory);
            item.Status="Disabled";persist();
        }
        public void Restore(FileChange item,Journal journal,Action persist) {
            Safety.ValidateTarget(item,identity);
            if(!System.Text.RegularExpressions.Regex.IsMatch(journal.Id??"",@"^\d{8}-\d{6}-[a-f0-9]{8}$"))throw new InvalidOperationException("备份编号无效。");
            if(!Safety.Equal(item.Disabled,item.Original+".disabled-2345cleaner-"+journal.Id))throw new InvalidOperationException("备份路径与记录不匹配。");
            Safety.NoReparse(item.Disabled);
            bool original=Safety.Exists(item.Original),disabled=Safety.Exists(item.Disabled);
            if(original && disabled)throw new IOException("原路径已有文件，为避免覆盖已停止恢复："+item.Original);
            if(!disabled) {
                if(original && Safety.Digest(item.Original,item.Directory)==item.Hash) {item.Status="Restored";persist();return;}
                if(item.Status=="BackupPlanned" && original)return;
                throw new IOException("停用副本丢失或原文件已变化，请手动核对备份："+item.Original);
            }
            if(Safety.Digest(item.Disabled,item.Directory)!=item.Hash)throw new IOException("停用副本哈希不匹配，未恢复。");
            item.Status="RestorePlanned";persist();
            Safety.Move(item.Disabled,item.Original,item.Directory);
            item.Status="Restored";persist();
        }
    }
}
