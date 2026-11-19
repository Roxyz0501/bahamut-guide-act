using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BahamutGuide;

internal static partial class Program
{
    private sealed class CancelledTransport : HttpMessageHandler
    {
        internal int Requests, Finished;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            try { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); throw new Exception("unreachable"); }
            finally { Interlocked.Increment(ref Finished); }
        }
    }

    private static void HostedLifecycle()
    {
        var data = Path.Combine(artifacts, "ホスト GuideData");
        var source = Setup("host-source");
        File.WriteAllText(source.SourcePath, Fixture);
        File.WriteAllText(source.TargetPath, Fixture);
        var plugin = new BahamutGuidePlugin();
        using (var page = new TabPage())
        using (var status = new Label())
        {
            for (int cycle = 0; cycle < 2; cycle++)
            {
                plugin.InitHostedPlugin(page, status, typeof(BahamutGuidePlugin).Assembly.Location, data);
                try
                {
                    var control = page.Controls.OfType<SettingsControl>().Single();
                    var settings = (PluginSettings)typeof(BahamutGuidePlugin).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(plugin);
                    var session = new GuideSession(settings);
                    Check(!Find<TabPage>(control).Any(t=>t is Roxyz.Act.UI.PluginLanguagePage || t is Roxyz.Act.UI.PluginUpdatePage || t.Text==Localization.Get(settings.Language,"Support")),"host hides shared pages");
                    string savedLanguage=settings.Language;
                    foreach (var code in new[]{"ja","ko","zh-CN","en","en","ja"})
                    {
                        plugin.SetHostedLanguage(code);
                        Check(settings.EffectiveLanguage==code && settings.Language==savedLanguage && status.Text==Localization.Get(code,"Started"),"host language applies without changing preference");
                    }
                    if (cycle == 0)
                    {
                        session.CheckSource(source.SourcePath); session.AcceptSource();
                        session.Current.Setting(settings,session.Current.Groups[0]).Enabled=false;
                        session.Apply(source.TargetPath);
                        Check(File.Exists(settings.LastBackup) && settings.LastBackup.StartsWith(data+Path.DirectorySeparatorChar), "host timeline backup in stable GuideData");
                    }
                    else
                    {
                        Check(!session.Current.Setting(settings,session.Current.Groups[0]).Enabled,"host reload preserves timeline settings");
                        session.RestoreTarget();
                        Check(File.ReadAllText(source.TargetPath)==Fixture,"host reload restores backup");
                    }
                }
                finally { plugin.DeInitPlugin(); }
            }
        }
    }

    private static void Lifecycle()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var path = Path.Combine(artifacts, "lifecycle-settings.xml");
                var initial = new PluginSettings { Language = "en", TimelineFolder = artifacts, CheckUpdatesOnStartup = false };
                PluginSettings.SaveSerialized(path, initial.Serialize(), null);
                var bytes = File.ReadAllBytes(path);
                var plugin = new BahamutGuidePlugin();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                using (var page = new TabPage())
                using (var label = new Label())
                {
                    for (var cycle = 0; cycle < 2; cycle++)
                    {
                        var transport = new CancelledTransport();
                        plugin.InitPlugin(page, label, path, new UpdateService(transport), registeredDll: () => typeof(PluginSettings).Assembly.Location);
                        // A UI context that cannot be pumped while DeInitPlugin blocks.
                        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                        Task pending;
                        if (cycle == 0)
                        {
                            SemVersion version; SemVersion.TryParse("9.0.0", out version);
                            var release = new ReleaseInfo { Tag = "v9.0.0", Version = version };
                            foreach (var extension in new[] { "zip", "sha256" })
                            {
                                var name = "BahamutGuide-v9.0.0." + extension;
                                release.Assets.Add(new ReleaseAsset { Name = name, Size = 100, DownloadUrl = "https://github.com/Roxyz0501/bahamut-guide-act/releases/download/v9.0.0/" + name });
                            }
                            typeof(BahamutGuidePlugin).GetField("lastUpdate", flags).SetValue(plugin, new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable, Release = release });
                            pending = (Task)typeof(BahamutGuidePlugin).GetMethod("InstallAsync", flags).Invoke(plugin, null);
                        }
                        else pending = (Task)typeof(BahamutGuidePlugin).GetMethod("CheckUpdatesAsync", flags).Invoke(plugin, new object[] { true });
                        var tasks = (System.Collections.Generic.List<Task>)typeof(BahamutGuidePlugin).GetField("operations", flags).GetValue(plugin);
                        tasks.Add(pending);
                        Check(transport.Requests == 1 && !pending.IsCompleted, "mock network operation is in flight");
                        plugin.DeInitPlugin();
                        Check(pending.IsCompleted && transport.Finished == 1, "shutdown drains cancelled network operation without UI pumping");
                        plugin.DeInitPlugin();
                        Application.DoEvents();
                    }
                    Check(File.ReadAllBytes(path).SequenceEqual(bytes), "reload and unchanged shutdown preserve settings bytes");
                    Check(!File.Exists(path + ".bak"), "unchanged plugin lifecycle performs zero settings replacements");
                }
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Check(thread.Join(5000), "plugin shutdown and reload finish within five seconds");
        if (failure != null) throw failure;
    }
}
