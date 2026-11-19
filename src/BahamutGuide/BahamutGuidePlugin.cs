using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BahamutGuide
{
    public sealed class BahamutGuidePlugin : IActPluginV1
    {
        private UpdateService updates;
        private CancellationTokenSource cancellation;
        private PluginSettings settings;
        private SettingsControl control;
        private Label status;
        private string settingsPath;
        private Func<string> registeredDll;
        private SettingsSaveCoordinator settingsSaver;
        private volatile bool stopped = true;
        private bool checking, preparing, updateScheduled;
        private int generation;
        private readonly object taskGate = new object();
        private readonly System.Collections.Generic.List<Task> operations = new System.Collections.Generic.List<Task>();
        private EventHandler checkHandler, installHandler, laterHandler;
        private UpdateCheckResult lastUpdate;

        public void InitPlugin(TabPage page, Label statusLabel)
        {
            InitPlugin(page, statusLabel, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker", "Config", "BahamutGuide.xml"), new UpdateService(), Path.Combine(Path.GetDirectoryName(ResolvePluginDllPath(ActGlobals.oFormActMain.ActPlugins, this)), "GuideData"));
        }

        internal void InitPlugin(TabPage page, Label statusLabel, string path, UpdateService service, string dataDirectory = null, Func<string> registeredDll = null)
        {
            if (!stopped) throw new InvalidOperationException("Plugin is already initialized.");
            generation++; stopped = false; checking = preparing = updateScheduled = false; lastUpdate = null;
            updates = service; cancellation = new CancellationTokenSource();
            operations.Clear();
            status = statusLabel;
            settingsPath = path;
            this.registeredDll = registeredDll ?? (() => ResolvePluginDllPath(ActGlobals.oFormActMain.ActPlugins, this));
            settings = PluginSettings.Load(settingsPath); settings.Normalize();
            settings.DataDirectory = dataDirectory ?? Path.Combine(Path.GetDirectoryName(path), "GuideData");
            var initialSnapshot = settings.Serialize();
            byte[] initialDisk = null;
            try { if (File.Exists(settingsPath)) initialDisk = File.ReadAllBytes(settingsPath); } catch { }
            settingsSaver = new SettingsSaveCoordinator(settingsPath, () => settings.Serialize(), initialSnapshot, initialDisk);
            settings.InitializeLanguageIfMissing(CultureInfo.CurrentUICulture);
            settings.BackupDirectoryResolver = () => {
                try { return Path.Combine(Path.GetDirectoryName(ResolvePluginDllPath(ActGlobals.oFormActMain.ActPlugins, this)), "Backups", "Timeline"); }
                catch { throw new IOException("BackupUnavailable"); }
            };
            try { control = new SettingsControl(settings); page.Text = "Bahamut Guide"; page.Controls.Add(control); }
            catch { stopped = true; settingsSaver.StopAndFlush(); updates.Dispose(); cancellation.Dispose(); status.Text = T("OperationError"); return; }
            checkHandler = delegate { StartOperation(() => CheckUpdatesAsync(true)); };
            installHandler = (s, e) => StartOperation(InstallAsync);
            laterHandler = delegate { if (lastUpdate?.Release == null) return; settings.SkippedVersion = lastUpdate.Release.Version.ToString(); control.ShowUpdateSkipped(settings.SkippedVersion); };
            control.CheckUpdatesRequested += checkHandler;
            control.InstallUpdateRequested += installHandler;
            control.LaterRequested += laterHandler;
            status.Text = T("Started");
            if (settings.CheckUpdatesOnStartup) StartOperation(() => CheckUpdatesAsync(false));
        }
        private string T(string key, params object[] args) => Localization.Get(settings?.Language ?? "en", key, args);
        private void StartOperation(Func<Task> operation)
        {
            if (stopped) return;
            var task = operation();
            lock (taskGate) { operations.RemoveAll(x => x.IsCompleted); operations.Add(task); }
        }
        private async Task CheckUpdatesAsync(bool manual)
        {
            if (checking || preparing || updateScheduled || stopped) return; checking = true; control.SetUpdateChecking();
            var run = generation;
            try
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version;
                var result = await updates.CheckAsync(current, cancellation.Token).ConfigureAwait(false); if (stopped) return;
                PostUi(run, () => { lastUpdate = result; control.ShowUpdateResult(result, current);
                    if (result.Status == UpdateCheckStatus.UpdateAvailable)
                    {
                        if (!manual && settings.SkippedVersion == result.Release.Version.ToString()) control.ShowUpdateSkipped(settings.SkippedVersion);
                        else { control.FocusUpdateTab(); status.Text = T("UpdateAvailable", current.ToString(3), result.Release.Version); }
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch { PostUi(run, () => control.SetUpdateText("CheckFailed")); }
            finally { checking = false; }
        }
        private async Task InstallAsync()
        {
            if (preparing || checking || updateScheduled || stopped || lastUpdate?.Status != UpdateCheckStatus.UpdateAvailable) return;
            preparing = true;
            var run = generation;
            control.SetUpdateText("Preparing");
            try
            {
                var target = registeredDll();
                var original = UpdatePackageVerifier.ComputeSha256(target);
                var package = await updates.DownloadAndVerifyAsync(lastUpdate.Release, cancellation.Token).ConfigureAwait(false);
                PostUi(run, () =>
                {
                    try
                    {
                        var registered = registeredDll();
                        if (!string.Equals(registered, target, StringComparison.OrdinalIgnoreCase)) throw new IOException("PluginPathUnavailable");
                        using (var act = Process.GetCurrentProcess())
                        using (var helper = Process.Start(UpdaterStartInfo(package, target, original, act)))
                        {
                            if (helper == null) throw new IOException("UpdaterUnavailable");
                        }
                        updateScheduled = true;
                        control.SetUpdateText("Prepared");
                        status.Text = T("Prepared");
                    }
                    catch { ShowInstallFailure(); }
                    finally { preparing = false; }
                });
            }
            catch { PostUi(run, () => { preparing = false; ShowInstallFailure(); }); }
        }

        private void ShowInstallFailure()
        {
            control.SetUpdateText("UpdateFailed", T("UpdateProblem"));
            status.Text = T("UpdateProblem");
        }

        internal static ProcessStartInfo UpdaterStartInfo(PreparedUpdate package, string target, string original, Process act)
        {
            var version = package.Release.Version;
            UpdatePackageVerifier.ValidateUpdaterAssembly(package.StagedUpdaterPath, version);
            if (!UpdatePackageVerifier.VerifySha256(package.StagedUpdaterPath, package.UpdaterSha256))
                throw new InvalidDataException("HashFailed");
            return new ProcessStartInfo
            {
                FileName = package.StagedUpdaterPath,
                Arguments = string.Format(CultureInfo.InvariantCulture,
                    "--wait-pid {0} --start {1} --source \"{2}\" --target \"{3}\" --sha256 {4} --version {5} --original {6}",
                    act.Id, act.StartTime.ToUniversalTime().Ticks, package.StagedDllPath, target, package.DllSha256, version, original),
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
        }
        private void PostUi(int run, Action action)
        {
            var target = control;
            if (stopped || run != generation || target == null || target.IsDisposed || !target.IsHandleCreated) return;
            try { target.BeginInvoke(new Action(() => { if (!stopped && run == generation && !target.IsDisposed) action(); })); }
            catch (InvalidOperationException) { }
        }
        internal static string ResolvePluginDllPath(IEnumerable<ActPluginData> plugins, IActPluginV1 instance)
        {
            var registration = plugins?.FirstOrDefault(x => x != null && ReferenceEquals(x.pluginObj, instance));
            return NormalizePluginPath(registration?.pluginFile?.FullName);
        }
        internal static string NormalizePluginPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("PluginPathUnavailable");
            Uri uri; if (Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.IsFile) value = uri.LocalPath;
            if (!Path.IsPathRooted(value)) throw new InvalidOperationException("PluginPathUnavailable");
            var path = Path.GetFullPath(value);
            if (!string.Equals(Path.GetFileName(path), UpdateConfiguration.PluginFileName, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new InvalidOperationException("PluginPathUnavailable");
            return path;
        }
        public void DeInitPlugin()
        {
            if (stopped) return;
            stopped = true;
            if (control != null) { control.CheckUpdatesRequested -= checkHandler; control.InstallUpdateRequested -= installHandler; control.LaterRequested -= laterHandler; }
            cancellation.Cancel();
            Task[] pending; lock (taskGate) pending = operations.Where(x => !x.IsCompleted).ToArray();
            try { Task.WaitAll(pending); } catch (AggregateException) { }
            control?.Dispose();
            settingsSaver?.StopAndFlush();
            if (settingsSaver?.LastError != null) status.Text = T("SaveFailed"); else status.Text = T("Stopped");
            updates.Dispose(); cancellation.Dispose();
        }
    }
}
