using BahamutGuide;
using Advanced_Combat_Tracker;
using System;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
internal static partial class Program
{
    private static void UpdateSafety()
    {
        SemVersion a, b; Check(SemVersion.TryParse("v1.10.0", out a) && SemVersion.TryParse("1.9.0", out b) && a.CompareTo(b) > 0, "SemVer numeric");
        Check(!SemVersion.TryParse("1.01.0", out a) && !SemVersion.TryParse("1.0.0-a..b", out a), "invalid SemVer");
        Throws(() => ReleaseParser.ParseStableReleases("broken"), "invalid JSON");
        var json = "[{\"tag_name\":\"v9.0.0\",\"draft\":false,\"prerelease\":true},{\"tag_name\":\"v1.0.0\",\"draft\":false,\"prerelease\":false}]";
        Check(ReleaseParser.ParseStableReleases(json)[0].Version.ToString() == "1.0.0", "stable only");
        Throws(() => UpdateService.EnsureAllowedUri(new Uri("http://github.com/x"), false), "HTTP rejected");
        Throws(() => UpdateService.EnsureAllowedUri(new Uri("https://evil.githubusercontent.com/x"), false), "arbitrary host rejected");
        Throws(() => UpdatePackageVerifier.SafeDestination(artifacts, "../outside"), "Zip Slip rejected");
        var zip = Path.Combine(artifacts, "bad-" + Guid.NewGuid().ToString("N") + ".zip"); using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntry("evil.exe");
        Throws(() => UpdatePackageVerifier.ExtractValidated(zip, Path.Combine(artifacts, "extract")), "unexpected asset rejected");
        var dll = typeof(PluginSettings).Assembly.Location;
        Check(UpdatePackageVerifier.VerifySha256(dll, UpdatePackageVerifier.ComputeSha256(dll)), "hash valid"); Check(!UpdatePackageVerifier.VerifySha256(dll, new string('0', 64)), "corrupt hash");
        var hash = UpdatePackageVerifier.ComputeSha256(dll); Check(UpdatePackageVerifier.FindManifestHash(hash + "  a.zip", "b.zip") == null, "manifest filename");
        SemVersion.TryParse(typeof(PluginSettings).Assembly.GetName().Version.ToString(3), out a); UpdatePackageVerifier.ValidatePluginAssembly(dll, a);
        var instance = new BahamutGuidePlugin(); var target = Path.Combine(artifacts, "日本語 space", "BahamutGuide.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(target)); File.WriteAllText(target, "previous");
        var registration = new ActPluginData { pluginObj = instance, pluginFile = new FileInfo(target) };
        Check(BahamutGuidePlugin.ResolvePluginDllPath(new[] { registration }, instance) == target, "ACT registration priority");
        Throws(() => BahamutGuidePlugin.ResolvePluginDllPath(null, instance), "missing ACT path stops update");
        Directory.CreateDirectory(Path.GetDirectoryName(target)); File.WriteAllText(target, "previous");
        Throws(() => BahamutGuide.Updater.Program.ReplaceWithBackup(dll, target, hash, System.Reflection.AssemblyName.GetAssemblyName(dll).Version.ToString(3), (s, d, backup) => { File.Replace(s, d, backup); File.WriteAllText(d, "partial"); throw new IOException("failure"); }), "simulated copy failure");
        Check(File.ReadAllText(target) == "previous", "rollback restores original");
        BahamutGuide.Updater.Program.ReplaceWithBackup(dll, target, hash, System.Reflection.AssemblyName.GetAssemblyName(dll).Version.ToString(3)); Check(UpdatePackageVerifier.VerifySha256(target, hash), "successful replacement");
        UpdateOnExit(dll, target, a);
        Check(!ReleaseParser.Summarize("**bold** `code`").Contains("**"), "release Markdown removed");
    }

    private static void UpdateOnExit(string dll, string target, SemVersion version)
    {
        string helperDir = Path.Combine(artifacts, "検証済み package");
        Directory.CreateDirectory(helperDir);
        string helper = Path.Combine(helperDir, "BahamutGuide.Updater.exe");
        File.Copy(typeof(BahamutGuide.Updater.Program).Assembly.Location, helper);
        var package = new PreparedUpdate { Release = new ReleaseInfo { Version = version }, StagedDllPath = dll,
            DllSha256 = UpdatePackageVerifier.ComputeSha256(dll), StagedUpdaterPath = helper,
            UpdaterSha256 = UpdatePackageVerifier.ComputeSha256(helper) };
        foreach (bool externalChange in new[]{false,true})
        {
            File.WriteAllText(target, "installed");
            using (var host = Process.Start(new ProcessStartInfo(typeof(Program).Assembly.Location, "--update-test-host") {
                UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true }))
            {
                try
                {
                    var start = BahamutGuidePlugin.UpdaterStartInfo(package, target, UpdatePackageVerifier.ComputeSha256(target), host);
                    using (var updater = Process.Start(start))
                    {
                        Check(!updater.WaitForExit(350) && File.ReadAllText(target)=="installed", "registered DLL unchanged until host exits");
                        if(externalChange) File.WriteAllText(target,"external");
                        host.StandardInput.WriteLine("exit"); host.StandardInput.Close();
                        Check(updater.WaitForExit(10000), "updater finishes after host exits");
                        if(externalChange) Check(updater.ExitCode!=0 && File.ReadAllText(target)=="external", "changed DLL preserved");
                        else Check(updater.ExitCode==0 && UpdatePackageVerifier.VerifySha256(target,package.DllSha256), "verified helper applies DLL to registered path");
                    }
                }
                finally { if(!host.HasExited) host.StandardInput.Close(); host.WaitForExit(5000); }
            }
        }
    }

}
