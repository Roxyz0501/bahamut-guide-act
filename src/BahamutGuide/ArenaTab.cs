using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BahamutGuide
{
    internal sealed class ArenaTab : TabPage
    {
        private readonly PluginSettings settings;
        private readonly LiveMap map;
        private readonly Dictionary<Control,string> labels = new Dictionary<Control,string>();
        internal ArenaTab(PluginSettings settings)
        {
            this.settings = settings;
            map = new LiveMap(settings);
            BackColor = Color.White;
            var rows = new FlowLayoutPanel {
                Dock = DockStyle.Fill, Padding = new Padding(14), AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White
            };
            Controls.Add(rows);
            AddChoice(rows, "MapEnabled", settings.MapEnabled, value => settings.MapEnabled = value);
            AddChoice(rows, "MapCones", settings.MapCones, value => settings.MapCones = value);
            AddChoice(rows, "MapPreview", false, value => map.Preview = value);
            AddChoice(rows, "MapLocked", settings.MapLocked, value => settings.MapLocked = value);
            var caption = new Label { AutoSize = true, Padding = new Padding(0,8,0,0) };
            rows.Controls.Add(caption);
            labels.Add(caption, "MapSize");
            var size = new NumericUpDown { Minimum = 180, Maximum = 640, Increment = 10, Value = Math.Max(180, Math.Min(640, settings.MapSize)) };
            size.ValueChanged += (s,e) => { lock(settings) settings.MapSize = (int)size.Value; map.Configure(); };
            rows.Controls.Add(size);
            var reset = new Button { AutoSize = true };
            reset.Click += (s,e) => { lock(settings) { settings.MapX = 80; settings.MapY = 80; } map.Configure(); };
            labels.Add(reset, "MapReset");
            rows.Controls.Add(reset);
            var hint = new Label { AutoSize = true, ForeColor = Color.FromArgb(83,99,120), MaximumSize = new Size(400,0), Padding = new Padding(0,8,0,0) };
            labels.Add(hint, "MapHint");
            rows.Controls.Add(hint);
            rows.ClientSizeChanged += (s,e) => hint.MaximumSize = new Size(Math.Max(120, Math.Min(520, rows.ClientSize.Width - 58)),0);
        }
        private void AddChoice(Control rows, string key, bool initial, Action<bool> change)
        {
            var box = new CheckBox { AutoSize = true, Checked = initial, Margin = new Padding(3,6,3,6) };
            box.CheckedChanged += (s,e) => { lock(settings) change(box.Checked); map.Configure(); };
            labels.Add(box,key);
            rows.Controls.Add(box);
        }
        internal void ApplyLanguage()
        {
            Text = Localization.Get(settings.Language, "MapTab");
            foreach(var pair in labels) pair.Key.Text = Localization.Get(settings.Language,pair.Value);
            map.Configure();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) map.Dispose();
            base.Dispose(disposing);
        }
    }
}
