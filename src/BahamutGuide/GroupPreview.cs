using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BahamutGuide
{
    internal sealed class GroupPreview : IDisposable
    {
        private readonly PreviewWindow window = new PreviewWindow();
        private readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private long bytes;
        internal event Action<double, double> Moved;
        internal GroupPreview() { window.Moved += (x,y) => Moved?.Invoke(x,y); }
        internal void Hide() => window.Present(null, 0, 0, false, false);
        internal void Clear() { cache.Clear(); bytes = 0; }
        internal static List<Notice> Sample(MechanicGroup group, int variant)
        {
            if (group.Id != "Dragons") return new List<Notice> { group.Notices[Math.Max(0, Math.Min(variant, group.Notices.Count - 1))] };
            var result = new List<Notice>();
            var circle = group.Notices.FirstOrDefault(n => n.Get("image").EndsWith("_円.png"));
            if (circle != null) result.Add(circle);
            // Representative 5-dragon arrangement; changing this sample never changes triggers.
            int slot = 0;
            foreach (var color in group.Notices.Where(n => n.Get("image").Contains("_四角_")).GroupBy(n => n.Get("image")))
            {
                var positions = color.ToList();
                var direction = variant == 1 && slot > 0 ? slot + 3 : slot;
                var box = positions[Math.Min(direction, positions.Count - 1)]; result.Add(box);
                var number = group.Notices.FirstOrDefault(n => n.Get("image").EndsWith("_数字_" + (slot+1) + ".png") && n.Element.Attribute("left")?.Value == box.Element.Attribute("left")?.Value && n.Element.Attribute("top")?.Value == box.Element.Attribute("top")?.Value);
                if (number != null) result.Add(number); slot++;
            }
            if (variant == 1) { var adjustment = group.Notices.FirstOrDefault(n => n.Get("image").Contains("_調整あり")); if (adjustment != null) result.Add(adjustment); }
            return result;
        }
        private BitmapSource Load(string path)
        {
            BitmapSource image;
            if (cache.TryGetValue(path, out image)) return image;
            if (!File.Exists(path) || new FileInfo(path).Length > 20 * 1024 * 1024) throw new IOException("ImageMissing");
            using (var stream = File.OpenRead(path))
            using (var probe = System.Drawing.Image.FromStream(stream, false, false))
                if ((long)probe.Width * probe.Height > 16 * 1024 * 1024) throw new IOException("ImageTooLarge");
            using (var stream = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                image = decoder.Frames[0]; image.Freeze();
            }
            var cost = (long)image.PixelWidth * image.PixelHeight * 4;
            if (bytes + cost > 64L * 1024 * 1024 || cache.Count >= 16) { cache.Clear(); bytes = 0; }
            cache.Add(path, image); bytes += cost; return image;
        }
        internal static string Resolve(string reference, string source, string extra)
        {
            if (string.IsNullOrWhiteSpace(reference) || reference.IndexOf('@') >= 0 || reference.StartsWith("http", StringComparison.OrdinalIgnoreCase)) throw new IOException("ImageMissing");
            if (Path.IsPathRooted(reference)) return reference;
            foreach (var root in new[] { Path.GetDirectoryName(source), extra })
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                var p = Path.GetFullPath(Path.Combine(root, reference));
                if (File.Exists(p)) return p;
            }
            throw new IOException("ImageMissing");
        }
        internal BitmapSource Render(GuideDocument doc, MechanicGroup group, GroupSetting setting, int variant, string source, string images)
        {
            GuideDocument.Validate(setting);
            var rows = Sample(group, variant).Select(n => new {
                Image=Load(Resolve(n.Get("image"), source, images)),
                X=(doc.Original(n,"left",0)-group.OriginX)*setting.Zoom,
                Y=(doc.Original(n,"top",0)-group.OriginY)*setting.Zoom,
                Scale=doc.Original(n,"scale",1)*setting.Zoom
            }).ToList();
            double width=rows.Max(r=>r.X+r.Image.PixelWidth*r.Scale+6), height=rows.Max(r=>r.Y+r.Image.PixelHeight*r.Scale+6);
            if (width<1 || height<1 || width>8192 || height>8192 || width*height>16*1024*1024) throw new IOException("ImageTooLarge");
            var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen())
            {
                foreach(var r in rows) dc.DrawImage(r.Image,new Rect(r.X+3,r.Y+3,r.Image.PixelWidth*r.Scale,r.Image.PixelHeight*r.Scale));
                dc.DrawRectangle(null,new Pen(new SolidColorBrush(Color.FromRgb(202,165,87)),1),new Rect(.5,.5,width-1,height-1));
            }
            var output=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(height),96,96,PixelFormats.Pbgra32);output.Render(visual);output.Freeze(); return output;
        }
        internal void Present(BitmapSource bitmap, GroupSetting s, bool locked) => window.Present(bitmap,s.X,s.Y,true,locked);
        public void Dispose() { window.Close(); cache.Clear(); bytes=0; }
    }
}
