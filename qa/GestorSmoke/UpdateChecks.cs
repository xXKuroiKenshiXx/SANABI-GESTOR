using System.Diagnostics;
using System.Text.Json;
using SanabiGestor.Core;

static class UpdateChecks
{
    static void Need(bool ok, string message) { if(!ok)throw new InvalidOperationException(message); }
    public static void Run(string root)
    {
        Need(SanabiCharacterCore.HealthBridge.Resolve(2,3,3,3)==2,"Native actor damage reaches chapter HP");
        Need(SanabiCharacterCore.HealthBridge.Resolve(3,2,3,3)==2,"Environment damage reaches alternate actor");
        Need(SanabiCharacterCore.HealthBridge.Resolve(2,2,3,3)==2,"Same damage notification never applies twice");
        Need(SanabiCharacterCore.HealthBridge.Resolve(2,0,3,3)==0,"Simultaneous lethal environment damage is not overwritten");
        Need(SanabiCharacterCore.HealthBridge.Resolve(4,3,3,3)==4,"Native healing reaches chapter HP");
        Need(AppInfo.FindPortableRepoAt(Path.Combine(root,"dist"))==root,"Portable AppImage finds catalog beside actual file, outside temporary mount");
        var fixture=Path.Combine(root,"mods","_qa","updater-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        try
        {
            var payload=Path.Combine(fixture,"payload");var bytes=new byte[128];bytes[0]=(byte)'M';bytes[1]=(byte)'Z';File.WriteAllBytes(payload,bytes);
            var hash=Downloader.Sha256(payload);
            var json=$$"""{"tag_name":"v99.0.0","body":"# New release","assets":[{"name":"SanabiGestor-windows-x64.exe","browser_download_url":"{{AppInfo.RepoUrl}}/releases/download/v99.0.0/SanabiGestor-windows-x64.exe","digest":"sha256:{{hash}}","size":128}]}""";
            using var doc=JsonDocument.Parse(json); var release=AppUpdater.ParseRelease(doc.RootElement,true)!;
            Need(release.Sha256==hash && release.Version=="99.0.0" && release.Notes.Contains("New release"),"Correct Windows release asset/hash/notes");
            Need(AppUpdater.ParseRelease(doc.RootElement,false)!.AssetUrl==null,"Windows executable never selected for Linux");
            using var noHash=JsonDocument.Parse(json.Replace("sha256:","md5:"));
            Need(AppUpdater.ParseRelease(noHash.RootElement,true)!.AssetUrl==null,"Reject release with no SHA-256");
            using var malicious=JsonDocument.Parse(json.Replace("https://github.com/","https://other.test/"));
            Need(AppUpdater.ParseRelease(malicious.RootElement,true)!.AssetUrl==null,"Only download from configured repo release");
            AppUpdater.VerifyPayload(payload,hash,true);
            bytes[20]=1; File.WriteAllBytes(payload,bytes);
            try { AppUpdater.VerifyPayload(payload,hash,true);throw new Exception("Hash mismatch accepted"); }catch(InvalidDataException){ }
            var target=Path.Combine(fixture,"manager.exe");File.WriteAllText(target,"original");var backup=Path.Combine(fixture,"previous");
            try { AppUpdater.ReplaceExecutable(target,Path.Combine(fixture,"missing"),backup);throw new Exception("Missing update accepted"); }catch(FileNotFoundException){ }
            Need(File.ReadAllText(target)=="original" && !File.Exists(backup),"Failed replacement rolls original executable back");
            var data=Path.Combine(fixture,"SanabiGestor-data");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"settings.json"),"settings preserved");
            var newHash=Downloader.Sha256(payload);AppUpdater.ReplaceExecutable(target,payload,backup);
            Need(File.ReadAllText(backup)=="original" && Downloader.Sha256(target)==newHash,"Executable replaced with backup");
            Need(File.ReadAllText(Path.Combine(data,"settings.json"))=="settings preserved","Portable preferences untouched by replacement");
            Console.WriteLine("PASS: release platform/SHA selection, tamper rejection, replacement rollback, settings preservation, character HP bridge.");
        }
        finally { Directory.Delete(fixture,true); }
    }

    public static void RunHelper(string root,string exe)
    {
        if(!OperatingSystem.IsWindows())throw new InvalidOperationException("Windows helper test requires Windows");
        var fixture=Path.Combine(root,"mods","_qa","helper-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
        try
        {
            var source=Path.Combine(root,"qa","UpdaterProbe.cs");
            var csc=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Microsoft.NET","Framework64","v4.0.30319","csc.exe");
            foreach(var failure in new[]{false,true})
            {
                var home=Path.Combine(fixture,failure?"failure":"success");Directory.CreateDirectory(home);
                var target=Path.Combine(home,"manager.exe");File.WriteAllText(target,"original executable");
                var stage=Path.Combine(home,".SanabiGestor-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
                var payload=Path.Combine(stage,"payload");
                if(failure) { var bad=new byte[128];bad[0]=(byte)'M';bad[1]=(byte)'Z';File.WriteAllBytes(payload,bad); }
                else
                {
                    var compile=new ProcessStartInfo(csc) { UseShellExecute=false, CreateNoWindow=true };compile.ArgumentList.Add("/nologo");compile.ArgumentList.Add("/target:winexe");compile.ArgumentList.Add("/out:"+payload);compile.ArgumentList.Add(source);
                    using var compiler=Process.Start(compile)!;compiler.WaitForExit();Need(compiler.ExitCode==0,"Compile isolated updater probe");
                }
                File.Copy(exe,Path.Combine(stage,"helper.exe"));
                File.WriteAllText(Path.Combine(home,"keep-data"),"unchanged");
                var job=new UpdateJob { Target=target,Sha256=Downloader.Sha256(payload),ParentId=int.MaxValue,ParentStarted=0,Version="99.0.0" };
                var jobPath=Path.Combine(stage,"job.json");File.WriteAllText(jobPath,JsonSerializer.Serialize(job,JsonCtx.Default.UpdateJob));
                var start=new ProcessStartInfo(Path.Combine(stage,"helper.exe")) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage };
                start.ArgumentList.Add("--apply-update");start.ArgumentList.Add(jobPath);
                using var helper=Process.Start(start)!;Need(helper.WaitForExit(20000),"Updater helper exits within deadline");
                var result=File.ReadAllText(Path.Combine(stage,"result.txt"));
                if(failure)Need(helper.ExitCode==1 && result.StartsWith("ERROR") && File.ReadAllText(target)=="original executable","Failed launch restores original executable");
                else
                {
                    Need(helper.ExitCode==0 && result=="OK 99.0.0" && File.ReadAllText(Path.Combine(stage,"previous"))=="original executable","Helper replaces binary and retains backup");
                    var marker=Path.Combine(home,"probe-launched");for(var i=0;i<100 && !File.Exists(marker);i++)Thread.Sleep(50);
                    Need(File.Exists(marker),"New executable actually restarts");
                }
                Need(File.ReadAllText(Path.Combine(home,"keep-data"))=="unchanged","Helper preserves unrelated portable data");
            }
            Console.WriteLine("PASS: actual Windows helper replacement/relaunch and invalid-executable rollback in isolated fixtures.");
        }
        finally { Directory.Delete(fixture,true); }
    }
}
