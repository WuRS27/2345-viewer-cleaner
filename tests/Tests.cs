using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ViewerExtrasCleaner;

class TestIdentity : IIdentity {
    readonly string root;
    public TestIdentity(string root){this.root=root;}
    public bool ViewerRoot(string path){return Safety.Equal(root,path);}
    public bool ApplicationRoot(string path){return ViewerRoot(path);}
    public bool VendorFile(string path){return Safety.Within(path,root);}
}
static class Tests {
    static int passed,failed;
    static string testRoot;
    static readonly List<string> links=new List<string>();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateSymbolicLink(string link,string target,uint flags);
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Throws(Action action,string message){try{action();}catch{return;}throw new Exception(message);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static string NewRoot(){string path=Path.Combine(testRoot,Guid.NewGuid().ToString("N"),"2345Pic");Directory.CreateDirectory(path);return path;}
    static Journal Journal(){return new Journal{Id=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)};}
    static FileChange Item(string root,string name){return new FileChange{Root=root,Original=Path.Combine(root,name),Directory=name=="Protect"};}
    static string Session(string root){string folder=Path.Combine(Path.GetDirectoryName(root),"backup");Directory.CreateDirectory(folder);return folder;}
    static int Main(){
        testRoot=Path.Combine(Path.GetTempPath(),"2345CleanerTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(testRoot);
        try{
            Test("path boundary rejects sibling prefixes",()=>{string root=NewRoot();Assert(Safety.Within(Path.Combine(root,"a.exe"),root),"child rejected");Assert(!Safety.Within(root+"-other\\a.exe",root),"sibling accepted");});
            Test("network and alternate data stream paths rejected",()=>{Throws(()=>Safety.Full(@"\\server\share\a.exe"),"UNC accepted");Throws(()=>Safety.Full(@"C:\folder\file.exe:payload"),"ADS accepted");});
            Test("command path parsing handles spaces and quotes",()=>{Assert(Host.ExeFromCommand(@"""D:\Program Files\2345Pic\Uninstall.exe"" /S")==@"D:\Program Files\2345Pic\Uninstall.exe","quoted");Assert(Host.ExeFromCommand(@"D:\Program Files\2345Pic\Uninstall.exe /S")==@"D:\Program Files\2345Pic\Uninstall.exe","unquoted");Assert(Host.ExeFromCommand("not-an-executable")==null,"invalid");});
            Test("file backup disable restore round trip",()=>{
                string root=NewRoot();var item=Item(root,"2345PicWorker.exe");File.WriteAllText(item.Original,"original-worker");var journal=Journal();int commits=0;
                var tx=new FileTransactions(new TestIdentity(root));tx.Disable(item,Session(root),journal,()=>commits++);
                Assert(!File.Exists(item.Original)&&File.Exists(item.Disabled),"not disabled");Assert(File.ReadAllText(item.Backup)=="original-worker","backup mismatch");Assert(commits>=3,"journal missing");
                tx.Restore(item,journal,()=>commits++);Assert(File.ReadAllText(item.Original)=="original-worker"&&!File.Exists(item.Disabled),"restore mismatch");
            });
            Test("nested Protect directory preserved",()=>{
                string root=NewRoot();var item=Item(root,"Protect");Directory.CreateDirectory(Path.Combine(item.Original,"nested"));File.WriteAllText(Path.Combine(item.Original,"nested","a.dll"),"data");
                var tx=new FileTransactions(new TestIdentity(root));var journal=Journal();tx.Disable(item,Session(root),journal,()=>{});tx.Restore(item,journal,()=>{});
                Assert(File.ReadAllText(Path.Combine(item.Original,"nested","a.dll"))=="data","directory data changed");
            });
            Test("main viewer cannot be disabled",()=>{
                string root=NewRoot();var item=Item(root,"2345PicViewer.exe");File.WriteAllText(item.Original,"main");
                Throws(()=>new FileTransactions(new TestIdentity(root)).Disable(item,Session(root),Journal(),()=>{}),"main executable accepted");Assert(File.ReadAllText(item.Original)=="main","main changed");
            });
            Test("restore refuses to overwrite a new file",()=>{
                string root=NewRoot();var item=Item(root,"2345PicUpdate.exe");File.WriteAllText(item.Original,"old");var j=Journal();var tx=new FileTransactions(new TestIdentity(root));
                tx.Disable(item,Session(root),j,()=>{});File.WriteAllText(item.Original,"new update");Throws(()=>tx.Restore(item,j,()=>{}),"overwrite allowed");Assert(File.ReadAllText(item.Original)=="new update","new file changed");Assert(File.Exists(item.Disabled),"backup lost");
            });
            Test("restore rejects changed disabled bytes",()=>{
                string root=NewRoot();var item=Item(root,"2345PicWorker.exe");File.WriteAllText(item.Original,"old");var j=Journal();var tx=new FileTransactions(new TestIdentity(root));tx.Disable(item,Session(root),j,()=>{});
                File.WriteAllText(item.Disabled,"changed");Throws(()=>tx.Restore(item,j,()=>{}),"hash mismatch accepted");Assert(!File.Exists(item.Original),"changed bytes restored");
            });
            Test("crash after rename can be recovered",()=>{
                string root=NewRoot();var item=Item(root,"2345PicWorker.exe");File.WriteAllText(item.Original,"old");var j=Journal();var tx=new FileTransactions(new TestIdentity(root));tx.Disable(item,Session(root),j,()=>{});
                item.Status="RenamePlanned";tx.Restore(item,j,()=>{});Assert(File.ReadAllText(item.Original)=="old","crash recovery failed");
            });
            Test("change between backup and rename is detected",()=>{
                string root=NewRoot();var item=Item(root,"2345PicWorker.exe");File.WriteAllText(item.Original,"old");var j=Journal();var tx=new FileTransactions(new TestIdentity(root));
                Throws(()=>tx.Disable(item,Session(root),j,()=>{if(item.Status=="RenamePlanned")File.WriteAllText(item.Original,"changed");}),"racing write accepted");
                Assert(File.ReadAllText(item.Original)=="changed","source removed");Assert(File.ReadAllText(item.Backup)=="old","snapshot lost");Assert(!File.Exists(item.Disabled),"unexpected rename");
            });
            Test("tampered restore destination rejected",()=>{
                string root=NewRoot();var item=Item(root,"2345PicWorker.exe");File.WriteAllText(item.Original,"old");var j=Journal();var tx=new FileTransactions(new TestIdentity(root));tx.Disable(item,Session(root),j,()=>{});
                item.Original=Path.Combine(Path.GetDirectoryName(root),"2345PicWorker.exe");Throws(()=>tx.Restore(item,j,()=>{}),"outside destination accepted");Assert(!File.Exists(item.Original),"outside path written");
            });
            Test("backup filename collision preserves source",()=>{
                string root=NewRoot();var item=Item(root,"2345PicWorker.exe");File.WriteAllText(item.Original,"original");var j=Journal();string collision=item.Original+".disabled-2345cleaner-"+j.Id;File.WriteAllText(collision,"existing");
                Throws(()=>new FileTransactions(new TestIdentity(root)).Disable(item,Session(root),j,()=>{}),"collision accepted");Assert(File.ReadAllText(item.Original)=="original"&&File.ReadAllText(collision)=="existing","collision overwrote data");
            });
            Test("directory symlink cannot escape backup root",()=>{
                string root=NewRoot();var item=Item(root,"Protect");Directory.CreateDirectory(item.Original);string outside=Path.Combine(testRoot,"outside");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"keep.txt"),"keep");
                string link=Path.Combine(item.Original,"escape");
                if(!CreateSymbolicLink(link,outside,1|2) && !CreateSymbolicLink(link,outside,1))throw new Exception("Unable to create fixture symlink: "+Marshal.GetLastWin32Error());
                links.Add(link);Throws(()=>new FileTransactions(new TestIdentity(root)).Disable(item,Session(root),Journal(),()=>{}),"symlink traversed");Assert(File.ReadAllText(Path.Combine(outside,"keep.txt"))=="keep","outside data changed");
            });
            Test("journal update survives serialization round trip",()=>{
                string root=NewRoot();string path=Path.Combine(root,"manifest.json");var j=Journal();j.Messages.Add("中文恢复记录");Json.Save(path,j);j.State="Disabled";Json.Save(path,j);var read=Json.Read<Journal>(path);Assert(read.State=="Disabled"&&read.Messages[0]=="中文恢复记录","journal data changed");
            });
            Test("unchanged restored files permit repeat recovery",()=>{
                string root=NewRoot();var item=Item(root,"2345PicWorker.exe");File.WriteAllText(item.Original,"old");var j=Journal();var tx=new FileTransactions(new TestIdentity(root));tx.Disable(item,Session(root),j,()=>{});tx.Restore(item,j,()=>{});tx.Restore(item,j,()=>{});Assert(File.ReadAllText(item.Original)=="old","repeat restore changed file");
            });
        }finally{
            // Remove fixture links without following them before deleting the verified test directory.
            foreach(string link in links){if(Directory.Exists(link)&&Safety.Within(link,testRoot))Directory.Delete(link,false);}
            string resolved=Path.GetFullPath(testRoot);
            string parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if(Path.GetDirectoryName(resolved).Equals(parent,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("2345CleanerTests-",StringComparison.Ordinal) &&
                (File.GetAttributes(resolved)&FileAttributes.ReparsePoint)==0)Directory.Delete(resolved,true);
            else throw new Exception("Fixture cleanup path validation failed");
        }
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
