using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace BahamutGuide
{
    public sealed class GroupSetting
    {
        public string Id { get; set; }
        public bool Enabled { get; set; } = true;
        public double X { get; set; }
        public double Y { get; set; }
        public double Zoom { get; set; } = 1;
    }
    internal sealed class MechanicGroup
    {
        internal string Id, Section;
        internal readonly List<Notice> Notices = new List<Notice>();
        internal double OriginX, OriginY;
        public override string ToString() => Id;
    }
    // SPESPE retains all timing, scripting, sound and log synchronization. Only image
    // display attributes are changed; source text (including Razor) is preserved.
    internal sealed class GuideDocument
    {
        internal readonly TimelineDocument Document;
        internal readonly List<MechanicGroup> Groups;
        internal GuideDocument(string path)
        {
            Document = new TimelineDocument(path);
            var zone = (string)Document.Xml.Root.Element("zone") ?? "";
            var name = (string)Document.Xml.Root.Element("name") ?? "";
            if (zone.IndexOf("Unending Coil", StringComparison.OrdinalIgnoreCase) < 0 && !name.Contains("絶バハムート")) throw new InvalidDataException("NotBahamut");
            if (Document.Notices.Count == 0 || Document.Notices.Count > 5000) throw new InvalidDataException("NoImages");
            Groups = Document.Notices.GroupBy(Classify).Select(g => {
                var result = new MechanicGroup { Id = g.Key, Section = g.First().Section };
                result.Notices.AddRange(g);
                result.OriginX = g.Min(n => Number(n, "left", 0));
                result.OriginY = g.Min(n => Number(n, "top", 0));
                return result;
            }).ToList();
        }
        internal static string Classify(Notice n)
        {
            if (n.Get("image").Contains("龍レイヤー")) return "Dragons";
            if (n.Section.StartsWith("P2 ", StringComparison.Ordinal)) return "NaelQuotes";
            return "Phase:" + n.Section;
        }
        internal double Number(Notice n, string attr, double fallback) => Notice.Number(n.Get(attr), Document.Default(n, attr, fallback));
        internal GroupSetting Setting(PluginSettings settings, MechanicGroup group)
        {
            lock (settings)
            {
            var s = settings.Groups.FirstOrDefault(g => g.Id == group.Id);
            if (s == null) { s = new GroupSetting { Id = group.Id, X = group.OriginX, Y = group.OriginY }; settings.Groups.Add(s); }
            Validate(s); return s;
            }
        }
        internal static void Validate(GroupSetting s)
        {
            if (s == null || double.IsNaN(s.Zoom) || double.IsInfinity(s.Zoom) || s.Zoom < .05 || s.Zoom > 5 ||
                double.IsNaN(s.X) || double.IsInfinity(s.X) || Math.Abs(s.X) > 50000 ||
                double.IsNaN(s.Y) || double.IsInfinity(s.Y) || Math.Abs(s.Y) > 50000) throw new InvalidDataException("InvalidSettings");
        }
        internal string Render(PluginSettings settings)
        {
            foreach (var n in Document.Notices) n.Changes.Clear();
            foreach (var group in Groups)
            {
                var s = Setting(settings, group);
                foreach (var n in group.Notices)
                {
                    // Use the immutable original XML attributes, never the last transform.
                    var x = Original(n, "left", 0); var y = Original(n, "top", 0); var scale = Original(n, "scale", 1);
                    if ((x == -1 || y == -1) && (s.X != group.OriginX || s.Y != group.OriginY || s.Zoom != 1)) throw new InvalidDataException("CenteredNotice");
                    n.Changes["left"] = F(s.X + (x - group.OriginX) * s.Zoom);
                    n.Changes["top"] = F(s.Y + (y - group.OriginY) * s.Zoom);
                    n.Changes["scale"] = F(scale * s.Zoom);
                    // Respect notices disabled in the original timeline.
                    if (!s.Enabled) n.Changes["enabled"] = "false";
                }
            }
            return Document.Render();
        }
        internal double Original(Notice n, string attr, double fallback) => Notice.Number((string)n.Element.Attribute(attr), Document.Default(n, attr, fallback));
        private static string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
    }
    internal sealed class GuideSession
    {
        internal readonly PluginSettings Settings;
        internal GuideDocument Current { get; private set; }
        internal GuideDocument Candidate { get; private set; }
        private string candidateHash;
        private string Baseline => Path.Combine(Settings.DataDirectory, "baseline.xml");
        internal GuideSession(PluginSettings settings)
        {
            Settings = settings;
            if (File.Exists(Baseline)) Current = new GuideDocument(Baseline);
        }
        internal void CheckSource(string path)
        {
            if (Current != null && !Settings.AllowSourceUpdates) throw new InvalidOperationException("SourceDisabled");
            Candidate = new GuideDocument(path); candidateHash = Hash(File.ReadAllBytes(path));
            Candidate.Document.File.VerifyUnchanged();
        }
        internal bool SourceChanged => Current == null || Candidate == null || !File.ReadAllBytes(Baseline).SequenceEqual(File.ReadAllBytes(Candidate.Document.File.PathName));
        internal void AcceptSource()
        {
            if (Candidate == null) throw new InvalidOperationException("CheckFirst");
            if (Current != null && !Settings.AllowSourceUpdates) throw new InvalidOperationException("SourceDisabled");
            var path = Candidate.Document.File.PathName;
            var bytes = File.ReadAllBytes(path);
            if (Hash(bytes) != candidateHash) throw new IOException("ExternalChange");
            Directory.CreateDirectory(Settings.DataDirectory);
            if (!File.Exists(Baseline)) PluginSettings.SaveSerialized(Baseline, bytes, null);
            else if (!File.ReadAllBytes(Baseline).SequenceEqual(bytes))
            {
                // Durable source rollback, independent from the destination XML backup.
                new SafeTextFile(Baseline, Path.Combine(Settings.DataDirectory, "Backups", "Source")).Save(Candidate.Document.File.Text);
            }
            Settings.SourcePath = path;
            Current = new GuideDocument(Baseline);
            foreach (var g in Current.Groups) Current.Setting(Settings, g);
            Candidate = null;
        }
        internal void Apply(string target)
        {
            if (Current == null) throw new InvalidOperationException("CheckFirst");
            target = Path.GetFullPath(target);
            if (string.Equals(target, Path.GetFullPath(Settings.SourcePath), StringComparison.OrdinalIgnoreCase) || string.Equals(target, Path.GetFullPath(Baseline), StringComparison.OrdinalIgnoreCase)) throw new IOException("SamePath");
            // Require an existing Bahamut timeline. Do not create a second active timeline.
            var destination = new GuideDocument(target);
            var before = File.ReadAllBytes(target);
            if (!string.IsNullOrEmpty(Settings.LastAppliedHash) && string.Equals(target, Settings.TargetPath, StringComparison.OrdinalIgnoreCase) && Hash(before) != Settings.LastAppliedHash) throw new IOException("TargetChanged");
            var rendered = Current.Render(Settings);
            var safe = new SafeTextFile(target, Path.Combine(Settings.DataDirectory, "Backups", "Timeline"));
            if (!safe.Matches(before)) throw new IOException("TargetChanged");
            if (safe.Text != rendered) Settings.LastBackup = safe.Save(rendered);
            Settings.TargetPath = target; Settings.LastAppliedHash = Hash(File.ReadAllBytes(target));
        }
        internal void RestoreTarget()
        {
            if (string.IsNullOrWhiteSpace(Settings.LastBackup) || !File.Exists(Settings.LastBackup)) throw new IOException("NoBackup");
            var target = Settings.TargetPath;
            if (Hash(File.ReadAllBytes(target)) != Settings.LastAppliedHash) throw new IOException("TargetChanged");
            var backup = new SafeTextFile(Settings.LastBackup, null);
            new GuideDocument(Settings.LastBackup);
            var safe = new SafeTextFile(target, Path.Combine(Settings.DataDirectory, "Backups", "Timeline"));
            if (safe.OriginalHash != Settings.LastAppliedHash) throw new IOException("TargetChanged");
            var redo = safe.Save(backup.Text);
            Settings.LastBackup = redo; Settings.LastAppliedHash = Hash(File.ReadAllBytes(target));
        }
        internal static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
