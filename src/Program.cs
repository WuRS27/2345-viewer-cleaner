using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("看图王附加组件清理工具")]
[assembly: AssemblyDescription("Local, reversible cleanup of identified 2345 viewer extras")]
[assembly: AssemblyCompany("Personal utility")]
[assembly: AssemblyProduct("Viewer Extras Cleaner")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace ViewerExtrasCleaner {
    static class Program {
        [STAThread] static int Main(string[] args) {
            try {
                if(args.Length==2 && args[0]=="--scan") { Json.Save(Path.GetFullPath(args[1]),Discovery.Get(null));return 0; }
                if(args.Length==2 && args[0]=="--verify-signature") {
                    return Trust.IsTrusted2345(Path.GetFullPath(args[1]))?0:2;
                }
                if(args.Length>0) return 2;
                bool created;
                using(var mutex=new Mutex(true,@"Local\2345ViewerCleaner-"+Host.UserSid,out created)) {
                    if(!created){MessageBox.Show("工具已经打开，请使用现有窗口。","看图王附加组件清理工具");return 0;}
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm());
                }
                return 0;
            }catch(Exception ex) {
                if(args.Length==0)MessageBox.Show(ex.Message,"无法启动",MessageBoxButtons.OK,MessageBoxIcon.Error);
                else {try{File.WriteAllText(Path.Combine(Path.GetTempPath(),"2345ViewerCleaner-error.txt"),ex.ToString());}catch{}}
                return 1;
            }
        }
    }
    sealed class MainForm : Form {
        readonly DataGridView grid=new DataGridView();
        readonly TextBox logBox=new TextBox();
        readonly Label status=new Label();
        readonly Label location=new Label();
        readonly Button scanButton=Button("重新扫描",110);
        readonly Button folderButton=Button("选择安装目录",135);
        readonly Button backupsButton=Button("查看备份",105);
        readonly Button restoreButton=Button("恢复停用项",120);
        readonly Button cleanButton=Button("开始清理",140);
        readonly Button stopButton=Button("结束卸载等待",135);
        readonly CheckBox uninstall=new CheckBox();
        readonly CheckBox updater=new CheckBox();
        bool busy;volatile bool stopWait;string manualRoot;Scan current;
        readonly Color blue=Color.FromArgb(31,97,205);
        public MainForm() {
            Text="看图王附加组件清理工具 · v1.0";StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Microsoft YaHei UI",9F);BackColor=Color.White;Icon=SystemIcons.Shield;
            Size=new Size(1160,820);MinimumSize=new Size(980,700);AutoScaleMode=AutoScaleMode.Dpi;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,18,24,14),ColumnCount=1,RowCount=8};
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,47));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,65));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,24));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,35));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
            Controls.Add(root);
            var heading=new Panel{Dock=DockStyle.Fill};
            heading.Controls.Add(new Label{Text="保留看图王，处理附加后台",Font=new Font(Font.FontFamily,22F,FontStyle.Bold),ForeColor=blue,AutoSize=true,Location=new Point(0,0)});
            heading.Controls.Add(new Label{Text="在本机扫描与备份，不联网、不自动重启。先关闭看图和图片编辑窗口。",AutoSize=true,ForeColor=Color.FromArgb(90,100,116),Location=new Point(2,48)});
            root.Controls.Add(heading,0,0);
            status.Dock=DockStyle.Fill;status.BackColor=Color.FromArgb(237,244,255);status.Padding=new Padding(12,9,10,5);status.Text="正在扫描安装位置…";status.AutoEllipsis=true;root.Controls.Add(status,0,1);
            var options=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(0,6,0,0)};
            uninstall.Text="卸载独立安全组件（官方窗口出现后，请点击“开始卸载”）";uninstall.AutoSize=true;uninstall.Checked=true;
            updater.Text="停用自动升级，减少附加组件被重新装回的机会";updater.AutoSize=true;updater.Checked=true;
            options.Controls.Add(uninstall);options.Controls.Add(updater);root.Controls.Add(options,0,2);
            grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;
            grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.FixedSingle;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
            grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.DefaultCellStyle.Padding=new Padding(4,5,4,5);grid.RowTemplate.Height=34;
            grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(244,247,251);grid.ColumnHeadersDefaultCellStyle.ForeColor=Color.FromArgb(40,50,65);
            grid.ColumnHeadersDefaultCellStyle.Font=new Font(Font,FontStyle.Bold);grid.EnableHeadersVisualStyles=false;
            grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="项目",FillWeight=24});grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="处理方式 / 当前状态",FillWeight=23});
            grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="路径 / 说明",FillWeight=53});root.Controls.Add(grid,0,3);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,9,0,0)};
            foreach(var button in new[]{scanButton,folderButton,backupsButton,restoreButton,cleanButton,stopButton})buttons.Controls.Add(button);
            cleanButton.BackColor=blue;cleanButton.ForeColor=Color.White;cleanButton.FlatStyle=FlatStyle.Flat;cleanButton.FlatAppearance.BorderSize=0;
            stopButton.Visible=false;root.Controls.Add(buttons,0,4);
            root.Controls.Add(new Label{Text="操作记录",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(70,80,95)},0,5);
            logBox.Dock=DockStyle.Fill;logBox.Multiline=true;logBox.ReadOnly=true;logBox.ScrollBars=ScrollBars.Vertical;
            logBox.BackColor=Color.FromArgb(248,250,252);logBox.BorderStyle=BorderStyle.FixedSingle;logBox.Font=new Font("Microsoft YaHei UI",9F);root.Controls.Add(logBox,0,6);
            location.Dock=DockStyle.Fill;location.AutoEllipsis=true;location.Padding=new Padding(0,6,0,0);location.ForeColor=Color.FromArgb(110,118,130);
            location.Text="本机备份："+Host.BackupRoot;root.Controls.Add(location,0,7);
            scanButton.Click+=async(s,e)=>await ScanNow();
            folderButton.Click+=async(s,e)=>{using(var dialog=new FolderBrowserDialog{Description="选择包含 2345PicViewer.exe 或 2345Pic.exe 的文件夹",ShowNewFolderButton=false}){
                if(dialog.ShowDialog(this)==DialogResult.OK){manualRoot=dialog.SelectedPath;await ScanNow();}}};
            backupsButton.Click+=(s,e)=>{Directory.CreateDirectory(Host.BackupRoot);Process.Start(new ProcessStartInfo(Host.BackupRoot){UseShellExecute=true});};
            cleanButton.Click+=async(s,e)=>{bool remove=uninstall.Checked,update=updater.Checked;stopWait=false;
                await Operate(()=>new Engine(Log,SetWaiting,()=>stopWait).Clean(manualRoot,remove,update));};
            restoreButton.Click+=async(s,e)=>{
                using(var dialog=new RestoreDialog())if(dialog.ShowDialog(this)==DialogResult.OK){
                    string folder=dialog.Folder,component=dialog.Component;bool service=dialog.Service;
                    await Operate(()=>new Engine(Log,SetWaiting,()=>false).Restore(folder,component,service));}
            };
            stopButton.Click+=(s,e)=>{stopWait=true;stopButton.Enabled=false;Log("已请求结束等待；官方卸载窗口会保留，可稍后继续完成卸载。");};
            Shown+=async(s,e)=>{var bounds=Screen.FromControl(this).WorkingArea;Height=Math.Min(Height,bounds.Height-60);Width=Math.Min(Width,bounds.Width-60);await ScanNow();};
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;MessageBox.Show(this,"正在扫描或写入处理记录，请等待操作结束。若正在等待官方卸载，可先点击“结束卸载等待”。","操作进行中",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
        }
        static Button Button(string text,int width) { return new Button{Text=text,Width=width,Height=34,Margin=new Padding(0,0,9,0),UseVisualStyleBackColor=true}; }
        void Busy(bool value) {
            busy=value;foreach(var control in new Control[]{scanButton,folderButton,restoreButton,cleanButton,uninstall,updater})control.Enabled=!value;
            if(!value && current!=null)cleanButton.Enabled=current.ViewerRoots.Count>0 && (current.Files.Count>0 || current.SafeCenterInstalled || (current.ServiceApproved && (current.ServiceStart!=4 || current.ServiceRunning)));
        }
        void Render(Scan scan) {
            current=scan;grid.Rows.Clear();
            foreach(var row in scan.Rows) {
                int index=grid.Rows.Add(row.Item,row.State,row.Path);
                if(row.State=="保留" || row.State=="已禁用")grid.Rows[index].Cells[1].Style.ForeColor=Color.FromArgb(27,124,82);
                if(row.State.Contains("重启") || row.State.Contains("确认"))grid.Rows[index].Cells[1].Style.ForeColor=Color.FromArgb(158,95,14);
            }
            if(grid.Rows.Count==0)grid.Rows.Add("未识别到看图王","请选择安装目录","选择含有看图王主程序的文件夹后重新扫描。");
            status.Text=scan.RestartNeeded?"已发现等待重启清理的驱动。请保存工作后重启一次；这里的扫描不会自动重启。":
                "已识别 "+scan.ViewerRoots.Count+" 处看图王安装，"+scan.Files.Count+" 个可停用项目。请核对上方选项和下方清单。";
            if(scan.ActiveWindows.Count>0)status.Text="请先保存并关闭："+String.Join("、",scan.ActiveWindows);
        }
        async Task ScanNow() {
            Busy(true);status.Text="正在扫描，未修改软件和系统设置…";
            try {var scan=await Task.Run(()=>Discovery.Get(manualRoot));Render(scan);Log("扫描完成。识别到 "+scan.ViewerRoots.Count+" 处安装目录。");}
            catch(Exception ex){Log("扫描失败："+ex.Message);status.Text="扫描未完成，请查看操作记录。";}
            finally{Busy(false);}
        }
        async Task Operate(Func<OperationResult> action) {
            Busy(true);status.Text="正在处理，请留意操作记录及可能出现的官方卸载窗口…";
            try {var result=await Task.Run(action);Render(result.After);status.Text=result.Summary;Log("记录保存在："+result.Folder);}
            catch(Exception ex){Log(ex.Message);status.Text="操作未全部完成，请查看记录。已完成的修改可从本机备份中恢复。";
                MessageBox.Show(this,ex.Message,"处理提示",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            finally{SetWaiting(false);Busy(false);}
        }
        void SetWaiting(bool value) {
            if(InvokeRequired){BeginInvoke(new Action<bool>(SetWaiting),value);return;}
            stopButton.Visible=value;stopButton.Enabled=value;
            if(value)status.Text="请在 2345 官方卸载窗口中点击“开始卸载”，完成后点击“确定”。工具最多等待 5 分钟。";
        }
        void Log(string text) {
            if(InvokeRequired){BeginInvoke(new Action<string>(Log),text);return;}
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);
        }
    }
    sealed class BackupChoice {
        public string Folder,Label;
        public override string ToString(){return Label;}
    }
    sealed class RestoreDialog : Form {
        readonly ComboBox backups=new ComboBox();readonly ComboBox component=new ComboBox();readonly CheckBox service=new CheckBox();
        public string Folder {get{return ((BackupChoice)backups.SelectedItem).Folder;}}
        public string Component {get{return component.SelectedIndex==0?"Worker":component.SelectedIndex==1?"Updater":"All";}}
        public bool Service {get{return service.Checked;}}
        public RestoreDialog() {
            Text="恢复停用项";Size=new Size(700,360);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
            Font=new Font("Microsoft YaHei UI",9F);BackColor=Color.White;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=6};Controls.Add(layout);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            layout.Controls.Add(new Label{Text="选择本机备份。恢复只撤销停用，不会重新安装安全组件。",AutoSize=true},0,0);
            backups.Dock=DockStyle.Top;backups.DropDownStyle=ComboBoxStyle.DropDownList;layout.Controls.Add(backups,0,1);
            if(Directory.Exists(Host.BackupRoot))foreach(string folder in Directory.GetDirectories(Host.BackupRoot).OrderByDescending(p=>p)) {
                try{var record=Json.Read<Journal>(Path.Combine(folder,"manifest.json"));backups.Items.Add(new BackupChoice{Folder=folder,Label=record.Created+"  ·  "+record.State});}catch{}
            }
            if(backups.Items.Count>0)backups.SelectedIndex=0;
            component.Dock=DockStyle.Top;component.DropDownStyle=ComboBoxStyle.DropDownList;
            component.Items.AddRange(new object[]{"仅恢复后台 Worker（排查功能异常时使用）","仅恢复升级程序","恢复全部停用文件"});component.SelectedIndex=0;layout.Controls.Add(component,0,2);
            service.Text="同时恢复看图后台服务（可能重新带回推广与安全组件）";service.AutoSize=true;service.Enabled=false;layout.Controls.Add(service,0,3);
            component.SelectedIndexChanged+=(s,e)=>{service.Enabled=component.SelectedIndex==2;if(!service.Enabled)service.Checked=false;};
            layout.Controls.Add(new Label{Text="恢复前请关闭看图王。原路径已有文件或文件已变化时，工具会停止该项恢复，不会覆盖现有文件。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(95,103,116)},0,4);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
            var ok=new Button{Text="恢复所选项目",Width=135,Height=32,Enabled=backups.Items.Count>0};var cancel=new Button{Text="取消",Width=85,Height=32,DialogResult=DialogResult.Cancel};
            ok.Click+=(s,e)=>{DialogResult=DialogResult.OK;Close();};buttons.Controls.Add(ok);buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,5);CancelButton=cancel;
        }
    }
}
