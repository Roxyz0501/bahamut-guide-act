using System;
using Roxyz.Act.UI;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace BahamutGuide
{
    public sealed class SettingsControl : UserControl
    {
        public const string SupportUrl = "https://ko-fi.com/roxyz0501";
        private readonly PluginSettings settings;
        private readonly Label headerTitle = L(), headerSubtitle = L();
        private readonly PluginTabs tabs;
        private readonly TabPage settingsTab = new TabPage(), supportTab = new TabPage();
        private readonly EditorControl editor;
        private readonly ArenaTab arenaTab;
        private readonly PluginLanguagePage languagePage;
        private readonly PluginUpdatePage updateTab;
        internal bool IsHosted { get; }
        private UpdatePageState updateState;
        private readonly Label supportHeading = L(), supportDescription = L(), supportOptional = L(), supportUrl = L(), supportSafety = L(), supportStatus = L();
        private readonly Button supportButton = new Button();
        private readonly System.Collections.Generic.Dictionary<Tuple<string,float,FontStyle>,Font> fonts = new System.Collections.Generic.Dictionary<Tuple<string,float,FontStyle>,Font>();
        private Font UiFont(string family,float size,FontStyle style=FontStyle.Regular)
        {
            var key=Tuple.Create(family,size,style);
            if (!fonts.TryGetValue(key,out var font)) fonts.Add(key,font=new Font(family,size,style));
            return font;
        }
        private bool loading = true;
        private string notesMessageKey;
        private string updateMessageKey = "NotChecked";
        private object[] updateMessageArgs = new object[0];

        public event EventHandler SettingsChanged;
        public event EventHandler CheckUpdatesRequested;
        public event EventHandler InstallUpdateRequested;
        public event EventHandler LaterRequested;
        internal ReleaseInfo AvailableRelease { get; private set; }

        public SettingsControl(PluginSettings value) : this(value, false) { }
        internal SettingsControl(PluginSettings value, bool hosted)
        {
            settings = value; Dock = DockStyle.Fill; Font = UiFont("Yu Gothic UI", 9f); BackColor = Color.FromArgb(244, 247, 251); Padding = new Padding(14); AutoScaleMode = AutoScaleMode.Font;
            IsHosted = hosted;
            languagePage = new PluginLanguagePage(value.EffectiveLanguage);
            updateTab = new PluginUpdatePage(UpdateConfiguration.RepositoryName, Assembly.GetExecutingAssembly().GetName().Version.ToString(3), value.CheckUpdatesOnStartup, value.EffectiveLanguage);
            editor = new EditorControl(settings);
            arenaTab = new ArenaTab(settings);
            editor.SettingsChanged += (s, e) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            var header = BuildHeader(); tabs = new PluginTabs(supportTab); ConfigureTabs();
            settingsTab.Controls.Add(editor);
            tabs.AddContentPage(settingsTab); tabs.AddContentPage(arenaTab);
            if (!hosted)
            {
                supportTab.Controls.Add(BuildSupportLayout());
                tabs.AddContentPage(languagePage); tabs.AddContentPage(updateTab); tabs.AddContentPage(supportTab);
            }
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = BackColor };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(header, 0, 0); root.Controls.Add(tabs, 0, 1); Controls.Add(root);
            WireEvents(); ApplyLanguage(); loading = false;
        }

        private Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 11, 18, 8) };
            headerTitle.Location = new Point(18, 9); headerTitle.ForeColor = Color.FromArgb(34, 44, 61);
            headerSubtitle.Location = new Point(20, 43); headerSubtitle.ForeColor = Color.FromArgb(103, 116, 137);
            header.Paint += delegate(object sender, PaintEventArgs e) { using (var pen = new Pen(Color.FromArgb(202, 165, 87), 2f)) e.Graphics.DrawLine(pen, 0, header.Height - 2, header.Width, header.Height - 2); };
            header.Controls.Add(headerTitle); header.Controls.Add(headerSubtitle); return header;
        }

        private void ConfigureTabs()
        {
            settingsTab.BackColor = Color.White; supportTab.BackColor = Color.FromArgb(255, 249, 239);
            settingsTab.Padding = supportTab.Padding = new Padding(4);
        }

        internal bool ConfirmClose() => editor.ConfirmDiscard();
        private Control BuildSupportLayout()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(255, 249, 239), Padding = new Padding(28), AutoScroll = true };
            var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(600, 400), ColumnCount = 1, RowCount = 7, BackColor = Color.White, Padding = new Padding(28, 23, 28, 24) };
            supportHeading.ForeColor = Color.FromArgb(194, 112, 22); supportHeading.Margin = new Padding(0, 0, 0, 10); card.Controls.Add(supportHeading, 0, 0);
            supportDescription.MaximumSize = new Size(700, 0); supportDescription.ForeColor = Color.FromArgb(61, 68, 79); supportDescription.Margin = new Padding(0, 0, 0, 8); card.Controls.Add(supportDescription, 0, 1);
            supportOptional.MaximumSize = new Size(700, 0); supportOptional.ForeColor = Color.FromArgb(117, 76, 24); supportOptional.Margin = new Padding(0, 0, 0, 18); card.Controls.Add(supportOptional, 0, 2);
            supportButton.AutoSize = false; supportButton.Size = new Size(360, 50); Primary(supportButton); supportButton.Font = UiFont("Yu Gothic UI", 11f, FontStyle.Bold); supportButton.Margin = new Padding(0, 8, 0, 8); card.Controls.Add(supportButton, 0, 3);
            supportUrl.Text = SupportUrl; supportUrl.ForeColor = Color.FromArgb(126, 104, 78); supportUrl.Margin = new Padding(1, 0, 0, 8); card.Controls.Add(supportUrl, 0, 4);
            supportSafety.ForeColor = Color.FromArgb(73, 96, 128); supportSafety.Margin = new Padding(0, 0, 0, 8); card.Controls.Add(supportSafety, 0, 5); supportStatus.ForeColor = Color.FromArgb(176, 65, 56); card.Controls.Add(supportStatus, 0, 6);
            panel.Controls.Add(card); return panel;
        }

        private void WireEvents()
        {
            languagePage.LanguageChanged += (s,e) => {
                if (IsHosted || loading) return;
                lock(settings) settings.Language = languagePage.LanguageCode;
                ApplyLanguage(); SettingsChanged?.Invoke(this, EventArgs.Empty);
            };
            updateTab.StartupChanged += (s,e) => {
                lock(settings) settings.CheckUpdatesOnStartup = updateTab.CheckOnStartup;
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            };
            updateTab.CheckRequested += (s,e) => CheckUpdatesRequested?.Invoke(this, EventArgs.Empty);
            updateTab.InstallRequested += (s,e) => InstallUpdateRequested?.Invoke(this, EventArgs.Empty);
            updateTab.LaterRequested += (s,e) => LaterRequested?.Invoke(this, EventArgs.Empty);
            supportButton.Click += OpenSupportLink;
        }

        public void ApplyTo(PluginSettings value)
        {
            lock(value) { if (!IsHosted) value.Language = languagePage.LanguageCode; value.CheckUpdatesOnStartup = updateTab.CheckOnStartup; }
        }
        internal void SetHostedLanguage(string code)
        {
            if (!IsHosted) return;
            string next = Localization.NormalizeLanguage(code);
            if (settings.HostedLanguage == next) return;
            settings.HostedLanguage = next;
            ApplyLanguage();
        }
        internal void SetUpdateChecking() => SetUpdateText("Checking");
        internal void ShowUpdateResult(UpdateCheckResult result, Version current)
        {
            AvailableRelease = result?.Release;
            notesMessageKey = result?.Error;
            if (result?.Status == UpdateCheckStatus.UpdateAvailable)
                SetState(UpdatePageState.Available,"UpdateAvailable",current.ToString(3),result.Release.Version);
            else if (result?.Status == UpdateCheckStatus.UpToDate) SetState(UpdatePageState.Ready,"UpToDate",current.ToString(3));
            else SetState(UpdatePageState.Ready,result?.Status==UpdateCheckStatus.NoStableRelease?"NoRelease":result?.Error??"CheckFailed");
        }
        internal void SetUpdateText(string key, string detail = null)
        {
            var state = key=="Checking"?UpdatePageState.Checking:key=="Preparing"?UpdatePageState.Preparing:
                key=="Prepared"?UpdatePageState.Prepared:key=="UpdateFailed"?UpdatePageState.PrepareFailed:UpdatePageState.Ready;
            SetState(state,key);
        }
        internal void ShowUpdateSkipped(string version) => SetState(UpdatePageState.Ready,"UpdateSkipped",version);
        internal void FocusUpdateTab() { if (!IsHosted) tabs.SelectedTab = updateTab; }
        private void SetState(UpdatePageState state,string key,params object[] args)
        {
            updateState=state; updateMessageKey=key; updateMessageArgs=args; RefreshUpdateMessage();
        }
        private void ApplyLanguage()
        {
            var family = Localization.FontFamily(CurrentLanguage); ApplyFontFamily(this, family);
            headerTitle.Text = T("PluginTab"); headerTitle.Font = UiFont(family, 17f, FontStyle.Bold); headerSubtitle.Text = T("Subtitle"); headerSubtitle.Font = UiFont(family, 8.5f);
            settingsTab.Text = T("Settings"); supportTab.Text = T("Support");
            languagePage.SetLanguage(CurrentLanguage); updateTab.SetLanguage(CurrentLanguage);
            updateTab.SetCheckOnStartup(settings.CheckUpdatesOnStartup);
            editor.ApplyLanguage(); arenaTab.ApplyLanguage();
            supportHeading.Text = T("SupportHeading"); supportHeading.Font = UiFont(family, 18f, FontStyle.Bold); supportDescription.Text = T("SupportDescription"); supportOptional.Text = T("SupportBody"); supportOptional.Font = UiFont(family, 9.5f, FontStyle.Bold); supportButton.Text = T("SupportButton"); supportSafety.Text = T("SupportSafety");
            RefreshUpdateMessage(); tabs.Invalidate(); Invalidate(true);
        }
        private void RefreshUpdateMessage()
        {
            string message = updateState==UpdatePageState.PrepareFailed ? T(updateMessageKey,T("UpdateProblem")) : T(updateMessageKey,updateMessageArgs);
            if (updateState==UpdatePageState.Available)
                message += Environment.NewLine + (notesMessageKey!=null ? T(notesMessageKey) : string.IsNullOrWhiteSpace(AvailableRelease?.Notes) ? T("NoReleaseNotes") : AvailableRelease.Notes);
            updateTab.ShowState(updateState,message);
        }
        private string CurrentLanguage => settings.EffectiveLanguage;
        private string T(string key, params object[] args) => Localization.Get(CurrentLanguage,key,args);
        protected override void Dispose(bool disposing)
        {
            if (disposing && IsHosted) { languagePage.Dispose(); updateTab.Dispose(); supportTab.Dispose(); supportButton.Dispose(); }
            base.Dispose(disposing);
            if (disposing) { foreach(var font in fonts.Values) font.Dispose(); fonts.Clear(); }
        }
        private void OpenSupportLink(object sender, EventArgs e) { string error; if (TryOpenSupportLink(Process.Start, out error)) { supportStatus.ForeColor = Color.FromArgb(17, 108, 75); supportStatus.Text = T("BrowserOpened"); } else { supportStatus.ForeColor = Color.FromArgb(176, 65, 56); supportStatus.Text = T("LinkFailed", T("BrowserFailure")); } }
        private static void Wide(TableLayoutPanel p, Control c, int row) { p.Controls.Add(c, 0, row); p.SetColumnSpan(c, 2); }
        private static Control SliderPanel(TrackBar s, Label v) { var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty }; v.Margin = new Padding(8, 8, 0, 0); p.Controls.Add(s); p.Controls.Add(v); return p; }
        private void ApplyFontFamily(Control root, string family) { if (root is PluginUpdatePage) return; if (root is PluginTabs sharedTabs) sharedTabs.SetLanguageFont(family); else root.Font = UiFont(family, root.Font.Size, root.Font.Style); foreach (Control child in root.Controls) ApplyFontFamily(child, family); }
        private static CheckBox C() => new CheckBox { AutoSize = true, ForeColor = Color.FromArgb(48, 61, 80), Anchor = AnchorStyles.Left };
        private static Label L() => new Label { AutoSize = true, ForeColor = Color.FromArgb(45, 58, 77), Anchor = AnchorStyles.Left };
        private static NumericUpDown N(decimal min, decimal max, decimal inc) => new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, Width = 120, ThousandsSeparator = true, Anchor = AnchorStyles.Left };
        private static decimal Bound(NumericUpDown c, decimal v) => Math.Max(c.Minimum, Math.Min(c.Maximum, v));
        private static int LanguageIndex(string value) { switch (Localization.NormalizeLanguage(value)) { case "ja": return 1; case "zh-CN": return 2; case "ko": return 3; default: return 0; } }
        private static void Primary(Button b) { b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.FromArgb(202, 101, 20); b.ForeColor = Color.White; b.FlatAppearance.BorderColor = Color.FromArgb(164, 79, 12); b.Cursor = Cursors.Hand; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(224,126,31); b.FlatAppearance.MouseDownBackColor = Color.FromArgb(174,83,13); }
        private static void Secondary(Button b) { b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.FromArgb(244, 247, 251); b.ForeColor = Color.FromArgb(48, 61, 80); b.FlatAppearance.BorderColor = Color.FromArgb(171, 181, 196); b.Cursor = Cursors.Hand; }
        public static bool TryOpenSupportLink(Func<ProcessStartInfo, Process> opener, out string error) { try { opener(new ProcessStartInfo(SupportUrl) { UseShellExecute = true }); error = null; return true; } catch (Exception ex) { error = ex.Message; return false; } }
    }
}
