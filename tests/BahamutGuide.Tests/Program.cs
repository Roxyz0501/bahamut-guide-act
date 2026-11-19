using BahamutGuide;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;

internal static partial class Program
{
    private static string artifacts;
    private static int checks;
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            if(args.SequenceEqual(new[]{"--update-test-host"}))
            {
                return Console.ReadLine()=="exit"?0:1;
            }
            artifacts=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(artifacts);
            MapTests();
            int mapLog=Array.IndexOf(args,"--map-log");if(mapLog>=0)ReplayMap(args[mapLog+1]);
            HostedLifecycle();TransformAndSource();UpdateSafety();SettingsPersistence();Lifecycle();UpdateTraffic();
            foreach(var lang in new[]{"en","ja","zh-CN","ko"})
            {
                Check(Localization.Get(lang,"AcceptSource")!="AcceptSource","source translations "+lang);
                Check(Localization.Get(lang,"FallbackProbe")=="English fallback","translation fallback "+lang);
                Check(Localization.MapCulture(new System.Globalization.CultureInfo(lang))==lang,"OS culture "+lang);
            }
            var initial=new PluginSettings();Check(initial.InitializeLanguageIfMissing(new System.Globalization.CultureInfo("ja-JP")),"initial language");Check(!initial.InitializeLanguageIfMissing(new System.Globalization.CultureInfo("ko-KR"))&&initial.Language=="ja","language preserved");
            string error;Check(!SettingsControl.TryOpenSupportLink(_=>{throw new IOException();},out error),"support browser failure caught");int links=0;SettingsControl.TryOpenSupportLink(_=>{links++;return null;},out error);Check(links==1,"support explicit click only");
            if(args.Contains("--ui"))RenderUi();
            var index=Array.IndexOf(args,"--timeline");if(index>=0)Actual(args[index+1],args.Length>index+2?args[index+2]:null);
            if(args.Contains("--release")) {using(var service=new UpdateService()){var release=service.CheckAsync(new Version(0,0,0),System.Threading.CancellationToken.None).GetAwaiter().GetResult().Release;Check(release!=null,"public stable release");var expected=Array.IndexOf(args,"--expect-tag");if(expected>=0)Check(release.Tag==args[expected+1],"expected public tag");Console.WriteLine("Verified release tag: "+release.Tag);var package=service.DownloadAndVerifyAsync(release,System.Threading.CancellationToken.None).GetAwaiter().GetResult();Check(File.Exists(package.StagedDllPath),"public ZIP and SHA verified");}}
            Console.WriteLine("PASS "+checks+" checks; artifacts: "+artifacts);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);checks++;}
    private static void Throws(Action action,string message){try{action();}catch{checks++;return;}throw new Exception("FAIL expected rejection: "+message);}
    private const string Fixture="<?xml version=\"1.0\" encoding=\"utf-8\"?><timeline><name>Test</name><zone>The Unending Coil Of Bahamut (Ultimate)</zone><default target-element=\"ImageNotice\" target-attr=\"scale\" value=\"0.5\"/><!-- keep comment -->@{ var x = 1; }<s name=\"P2 ネール\"><script><![CDATA[untouched < > &&]]></script><t sync=\"alpha\" notice=\"voice\"><v-notice text=\"unchanged\"/><i-notice image=\"絶バハムートP2_龍レイヤー_円.png\" left=\"100\" top=\"200\" duration=\"220\" delay=\"0.1\" sync-to-hide=\"end\"/></t><t sync=\"beta\"><i-notice image=\"絶バハムートP2_龍レイヤー_四角_赤.png\" left=\"150\" top=\"250\"/></t><t sync=\"quote\"><i-notice image=\"quote.png\" left=\"400\" top=\"80\"/></t></s></timeline>";
    private static PluginSettings Setup(string name)
    {
        var dir=Path.Combine(artifacts,name);Directory.CreateDirectory(dir);
        return new PluginSettings {Language="ja",CheckUpdatesOnStartup=false,DataDirectory=Path.Combine(dir,"data"),SourcePath=Path.Combine(dir,"source.xml"),TargetPath=Path.Combine(dir,"target.xml")};
    }
    private static void TransformAndSource()
    {
        var s=Setup("source");File.WriteAllText(s.SourcePath,Fixture,new UTF8Encoding(false));File.WriteAllText(s.TargetPath,Fixture,new UTF8Encoding(false));var raw=File.ReadAllBytes(s.SourcePath);
        var session=new GuideSession(s);Check(session.Current==null&&!s.AllowSourceUpdates,"no implicit import; source updates default off");session.CheckSource(s.SourcePath);Check(!Directory.Exists(s.DataDirectory),"check is read-only");session.AcceptSource();
        Check(session.Current.Groups.Count==2,"dragon and quotes separated");var group=session.Current.Groups.Single(g=>g.Id=="Dragons");var setting=session.Current.Setting(s,group);setting.X=300;setting.Y=400;setting.Zoom=2;setting.Enabled=false;
        var result=session.Current.Render(s);var doc=TimelineDocument.Parse(result);var images=doc.Descendants("i-notice").ToList();
        Check((string)images[0].Attribute("left")=="300"&&(string)images[1].Attribute("left")=="400","group origin and relative x scale");
        Check((string)images[1].Attribute("top")=="500"&&(string)images[0].Attribute("scale")=="1","group relative y and image scale");Check(images.Take(2).All(n=>(string)n.Attribute("enabled")=="false")&&images[2].Attribute("enabled")==null,"only chosen group disabled");
        Check(result.Contains("<![CDATA[untouched < > &&]]>")&&result.Contains("@{ var x = 1; }")&&result.Contains("notice=\"voice\""),"scripts Razor voice preserved byte-for-byte");Check(session.Current.Render(s)==result,"transform does not compound");
        session.Apply(s.TargetPath);Check(File.Exists(s.LastBackup)&&File.ReadAllText(s.LastBackup)==Fixture,"original destination durably backed up");Check(File.ReadAllBytes(s.SourcePath).SequenceEqual(raw),"source never rewritten");
        var backup=s.LastBackup;session.Apply(s.TargetPath);Check(s.LastBackup==backup,"unchanged apply creates no backup/write");session.RestoreTarget();Check(File.ReadAllText(s.TargetPath)==Fixture,"rollback restores original");
        Throws(()=>session.Apply(s.SourcePath),"source cannot be destination");Throws(()=>session.CheckSource(s.SourcePath),"updates refused while option off");
        File.WriteAllText(s.SourcePath,Fixture.Replace("quote.png","new.png"));s.AllowSourceUpdates=true;session.CheckSource(s.SourcePath);Check(session.SourceChanged,"changed source detected");
        s.AllowSourceUpdates=false;Throws(()=>session.AcceptSource(),"option checked again at accept");s.AllowSourceUpdates=true;session.AcceptSource();Check(session.Current.Setting(s,session.Current.Groups[0]).X==300&&!session.Current.Setting(s,session.Current.Groups[0]).Enabled,"source update preserves settings");
        session.CheckSource(s.SourcePath);File.AppendAllText(s.SourcePath," ");Throws(()=>session.AcceptSource(),"source changed after review refused");
        File.AppendAllText(s.TargetPath," ");Throws(()=>session.Apply(s.TargetPath),"external destination change refused");
        var restart=new GuideSession(s);Check(restart.Current.Document.File.Text.Contains("new.png"),"restart uses accepted snapshot");Check(restart.Current.Document.File.Text!=File.ReadAllText(s.SourcePath),"restart never imports changed source");
        var invalid=Path.Combine(artifacts,"bad.xml");File.WriteAllText(invalid,"<!DOCTYPE timeline [<!ENTITY x SYSTEM 'file:///secret'>]><timeline>&x;</timeline>");Throws(()=>new GuideDocument(invalid),"DTD rejected");setting.Zoom=double.NaN;Throws(()=>GuideDocument.Validate(setting),"NaN rejected");
    }
    private static void Actual(string path,string imageRoot)
    {
        var s=Setup("actual");File.Copy(path,s.SourcePath);File.Copy(path,s.TargetPath);var sourceHash=GuideSession.Hash(File.ReadAllBytes(path));
        var session=new GuideSession(s);session.CheckSource(s.SourcePath);session.AcceptSource();Check(session.Current.Document.Notices.Count==86,"actual timeline has 86 image notices");
        foreach(var g in session.Current.Groups){var p=session.Current.Setting(s,g);p.X=100;p.Y=100;p.Zoom=1.25;p.Enabled=false;}
        var result=session.Current.Render(s);var before=TimelineDocument.Parse(File.ReadAllText(path));var after=TimelineDocument.Parse(result);
        foreach(var n in before.Descendants("i-notice").Concat(after.Descendants("i-notice"))) foreach(var key in new[]{"left","top","scale","enabled"}) n.Attribute(key)?.Remove();
        Check(before.ToString(SaveOptions.DisableFormatting)==after.ToString(SaveOptions.DisableFormatting),"actual timeline only display attributes changed");
        session.Apply(s.TargetPath);session.RestoreTarget();Check(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(s.TargetPath)),"actual timeline rollback exact bytes");
        Check(GuideSession.Hash(File.ReadAllBytes(path))==sourceHash,"actual source untouched");
        if(imageRoot!=null)using(var renderer=new GroupPreview())foreach(var g in session.Current.Groups)
        {
            var p=session.Current.Setting(s,g);p.Zoom=1;for(int variant=0;variant<(g.Id=="Dragons"?2:1);variant++)
            {var bitmap=renderer.Render(session.Current,g,p,variant,path,imageRoot);using(var stream=File.Create(Path.Combine(artifacts,"preview-"+session.Current.Groups.IndexOf(g)+"-"+variant+".png"))){var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));encoder.Save(stream);}Check(bitmap.PixelWidth>0,"actual group renders");}
        }
        Console.WriteLine("Actual groups: "+string.Join(", ",session.Current.Groups.Select(g=>g.Id+"="+g.Notices.Count)));
    }
    private static void RenderUi()
    {
        foreach(var lang in new[]{"en","ja","zh-CN","ko"})
        {
            var s=Setup("ui-"+lang);s.Language=lang;File.WriteAllText(s.SourcePath,Fixture);var session=new GuideSession(s);session.CheckSource(s.SourcePath);session.AcceptSource();
            using(var form=new RenderForm())using(var ui=new SettingsControl(s))
            {
                form.ClientSize=new Size(1000,760);form.Controls.Add(ui);form.Show();Application.DoEvents();CreateHandles(ui);
                CaptureUi(ui,"ui-"+lang);
                var nested=Find<TabControl>(ui).Last();nested.SelectedIndex=1;Application.DoEvents();CaptureUi(ui,"source-"+lang);
                var top=Find<TabControl>(ui).First();top.SelectedIndex=1;Application.DoEvents();
                Check(top.SelectedTab.Text==Localization.Get(lang,"MapTab") && top.SelectedTab.Text!="MapTab","localized live map "+lang);CaptureUi(ui,"map-"+lang);
                Check(Find<TabControl>(ui).All(t=>t is Roxyz.Act.UI.PluginTabs && t.SizeMode==TabSizeMode.Normal && t.Font.Bold),"all tabs use common control "+lang);
                var languages=Find<Roxyz.Act.UI.PluginLanguagePage>(ui).Single();
                var selector=Find<ComboBox>(languages).Single();
                foreach(int choice in new[]{1,2,3,0,0,1})
                {
                    selector.SelectedIndex=choice;
                    Check(s.Language==languages.LanguageCode,"standalone language saved");
                }
                selector.SelectedIndex=Array.IndexOf(Roxyz.Act.UI.PluginLanguagePage.Codes,lang);
                var shared=Find<Roxyz.Act.UI.PluginUpdatePage>(ui).Single();
                top.SelectedTab=shared;
                ((Roxyz.Act.UI.PluginTabs)top).AddContentPage(new TabPage("Extra"));
                Check(top.TabPages[top.TabCount-1].Text==Localization.Get(lang,"Support"),"support remains last");
                Check(Find<Label>(shared).Any(l=>l.Text.Contains("Roxyz0501")) && Find<LinkLabel>(shared).Any(l=>l.Text.Contains("bahamut-guide-act")),"common update metadata");
                form.ClientSize=new Size(440,540); Application.DoEvents(); CaptureUi(ui,"update-"+lang);
                Check(ui.Controls.Count>0,"headless UI "+lang);form.Close();
            }
        }
        var window=new PreviewWindow();window.Present(null,0,0,false,false);var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
        for(int i=0;i<10;i++){window.SetLocked(i%2==0);window.Present(null,0,0,false,i%2==0);Check(new System.Windows.Interop.WindowInteropHelper(window).Handle==handle,"preview handle remains stable");}window.Close();
    }
    private static void CreateHandles(Control c){var h=c.Handle;foreach(Control child in c.Controls)CreateHandles(child);}
    private sealed class RenderForm:Form
    {
        protected override bool ShowWithoutActivation=>true;
        internal RenderForm(){ShowInTaskbar=false;Opacity=0;StartPosition=FormStartPosition.Manual;Location=new Point(-32000,-32000);}
    }
    private static System.Collections.Generic.IEnumerable<T> Find<T>(Control root) where T:Control
    {foreach(Control c in root.Controls){if(c is T)yield return (T)c;foreach(var x in Find<T>(c))yield return x;}}
    private static void CaptureUi(Control ui,string name){using(var image=new Bitmap(ui.Width,ui.Height)){ui.DrawToBitmap(image,new Rectangle(Point.Empty,ui.Size));image.Save(Path.Combine(artifacts,name+".png"));}}
}
