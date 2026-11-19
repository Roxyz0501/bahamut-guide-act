using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BahamutGuide
{
    internal sealed class EditorControl : UserControl
    {
        private readonly PluginSettings settings;
        private readonly GuideSession session;
        private readonly GroupPreview preview = new GroupPreview();
        private readonly ListBox groups = new ListBox { Dock=DockStyle.Fill, IntegralHeight=false };
        private readonly ComboBox patterns = new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=450, DropDownWidth=800 };
        private readonly CheckBox enabled = new CheckBox {AutoSize=true}, show = new CheckBox {AutoSize=true}, locked = new CheckBox {AutoSize=true}, allow = new CheckBox {AutoSize=true};
        private readonly NumericUpDown x=N(-50000,50000,1), y=N(-50000,50000,1), zoom=N(5,500,5);
        private readonly TextBox source = PathBox(), target = PathBox(), images = PathBox();
        private readonly TextBox diff = new TextBox {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Both,WordWrap=false};
        private readonly Label status = new Label {AutoSize=true,MaximumSize=new Size(950,0),ForeColor=Color.FromArgb(73,96,128)};
        private readonly List<Tuple<Control,string>> localized=new List<Tuple<Control,string>>();
        private string lastStatusKey;
        private bool loading;
        private System.Windows.Media.Imaging.BitmapSource bitmap;
        internal event EventHandler SettingsChanged;
        internal EditorControl(PluginSettings value)
        {
            settings=value; Dock=DockStyle.Fill; AutoScroll=true;
            session=new GuideSession(settings);
            var tabs=new Roxyz.Act.UI.PluginTabs(null); var display=new TabPage {BackColor=Color.White};var update=new TabPage {BackColor=Color.White};
            Bind(display,"DisplayTab");Bind(update,"SourceTab");tabs.TabPages.AddRange(new[]{display,update});Controls.Add(tabs);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(8)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));display.Controls.Add(layout);
            var hint=new Label {Dock=DockStyle.Fill};Bind(hint,"DisplayHint");layout.Controls.Add(hint,0,0);
            var split=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2};split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));split.Controls.Add(groups,0,0);
            var controls=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};split.Controls.Add(controls,1,0);layout.Controls.Add(split,0,1);
            Bind(enabled,"GroupEnabled");Bind(show,"GroupPreview");Bind(locked,"LockPreview");controls.Controls.Add(enabled);
            controls.Controls.Add(Row("PositionX",x));controls.Controls.Add(Row("PositionY",y));controls.Controls.Add(Row("GroupZoom",zoom));
            var patLabel=new Label {AutoSize=true};Bind(patLabel,"Pattern");controls.Controls.Add(patLabel);controls.Controls.Add(patterns);controls.Controls.Add(show);controls.Controls.Add(locked);
            controls.Controls.Add(Button("ResetGroup",()=>{var g=Selected;if(g==null)return;var s=session.Current.Setting(settings,g);s.X=g.OriginX;s.Y=g.OriginY;s.Zoom=1;s.Enabled=true;SelectGroup();Changed();}));
            var all=new FlowLayoutPanel {AutoSize=true};all.Controls.Add(Button("AllOn",()=>SetAll(true)));all.Controls.Add(Button("AllOff",()=>SetAll(false)));controls.Controls.Add(all);
            var footer=new FlowLayoutPanel {Dock=DockStyle.Fill};footer.Controls.Add(Button("ApplyTimeline",()=>{if(session.Current!=null)foreach(var n in session.Current.Document.Notices)GroupPreview.Resolve(n.Get("image"),source.Text,images.Text);session.Apply(target.Text);Changed();Message("Applied");}));footer.Controls.Add(Button("RestoreTimeline",()=>{session.RestoreTarget();Changed();Message("Applied");}));layout.Controls.Add(footer,0,2);
            var srcLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(8)};
            foreach(int h in new[]{54,54,54,42,42}) srcLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));srcLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));srcLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));update.Controls.Add(srcLayout);
            srcLayout.Controls.Add(PathRow("SourceFile",source,()=>Browse(source,false)),0,0);srcLayout.Controls.Add(PathRow("TargetFile",target,()=>Browse(target,false)),0,1);srcLayout.Controls.Add(PathRow("ImageFolder",images,()=>Browse(images,true)),0,2);
            Bind(allow,"AllowSourceUpdates");allow.Checked=settings.AllowSourceUpdates;srcLayout.Controls.Add(allow,0,3);
            var actions=new FlowLayoutPanel {Dock=DockStyle.Fill};actions.Controls.Add(Button("CheckSource",CheckSource));actions.Controls.Add(Button("AcceptSource",()=>{session.AcceptSource();source.Text=settings.SourcePath;RefreshGroups();Changed();diff.Text=T("Imported");}));srcLayout.Controls.Add(actions,0,4);srcLayout.Controls.Add(diff,0,5);
            var sourceHint=new Label {Dock=DockStyle.Fill};Bind(sourceHint,"SourceHint");srcLayout.Controls.Add(sourceHint,0,6);
            var bottom=new Panel {Dock=DockStyle.Bottom,Height=44,Padding=new Padding(8)};bottom.Controls.Add(status);Controls.Add(bottom);
            source.Text=settings.SourcePath;target.Text=settings.TargetPath;images.Text=settings.ExtraImageFolder;
            groups.SelectedIndexChanged+=(s,e)=>SelectGroup();
            enabled.CheckedChanged+=(s,e)=>Edit(false); x.ValueChanged+=(s,e)=>Edit(false);y.ValueChanged+=(s,e)=>Edit(false);zoom.ValueChanged+=(s,e)=>Edit(true);
            patterns.SelectedIndexChanged+=(s,e)=>{if(!loading) DrawPreview(true);};show.CheckedChanged+=(s,e)=>DrawPreview(true);locked.CheckedChanged+=(s,e)=>DrawPreview(false);
            allow.CheckedChanged+=(s,e)=>{lock(settings)settings.AllowSourceUpdates=allow.Checked;Changed();};
            preview.Moved+=(px,py)=>{if(Selected==null)return;loading=true;x.Value=Bound(x,px);y.Value=Bound(y,py);loading=false;Edit(false);};
            RefreshGroups();ApplyLanguage();
        }
        private MechanicGroup Selected => session.Current==null||groups.SelectedIndex<0?null:session.Current.Groups[groups.SelectedIndex];
        private void RefreshGroups()
        {
            preview.Clear();
            loading=true;groups.Items.Clear();if(session.Current!=null)foreach(var g in session.Current.Groups)groups.Items.Add(GroupName(g));loading=false;if(groups.Items.Count>0)groups.SelectedIndex=0;else {show.Checked=false;preview.Hide();}
        }
        private string GroupName(MechanicGroup g) => (g.Id=="Dragons"||g.Id=="NaelQuotes"?T(g.Id):g.Section)+" ("+g.Notices.Count+")";
        private void SelectGroup()
        {
            var g=Selected;if(g==null)return;loading=true;var s=session.Current.Setting(settings,g);enabled.Checked=s.Enabled;x.Value=Bound(x,s.X);y.Value=Bound(y,s.Y);zoom.Value=Bound(zoom,s.Zoom*100);patterns.Items.Clear();
            if(g.Id=="Dragons"){patterns.Items.Add(T("DragonSample"));patterns.Items.Add(T("DragonAdjustment"));}else foreach(var n in g.Notices)patterns.Items.Add(n.Context+" / "+n.Get("image"));
            patterns.SelectedIndex=0;loading=false;DrawPreview(true);
        }
        private void Edit(bool redraw)
        {
            if(loading||Selected==null)return;lock(settings){var s=session.Current.Setting(settings,Selected);s.Enabled=enabled.Checked;s.X=(double)x.Value;s.Y=(double)y.Value;s.Zoom=(double)zoom.Value/100;}
            DrawPreview(redraw);Changed();Message("PendingApply");
        }
        private void SetAll(bool value){if(session.Current==null)return;lock(settings)foreach(var g in session.Current.Groups)session.Current.Setting(settings,g).Enabled=value;SelectGroup();Changed();Message("PendingApply");}
        private void DrawPreview(bool redraw)
        {
            if(loading)return;
            if(!show.Checked||Selected==null){preview.Hide();bitmap=null;return;}
            try {var s=session.Current.Setting(settings,Selected);if(redraw||bitmap==null)bitmap=preview.Render(session.Current,Selected,s,patterns.SelectedIndex,source.Text,images.Text);preview.Present(bitmap,s,locked.Checked);}
            catch(Exception ex){preview.Hide();bitmap=null;Message(ex.Message);}
        }
        private void CheckSource()
        {
            session.CheckSource(source.Text);
            var old=session.Current?.Document.File.Text??"";var next=session.Candidate.Document.File.Text;
            var oldLines=old.Split('\n');var nextLines=next.Split('\n');var changes=new List<string>();
            for(int i=0;i<Math.Max(oldLines.Length,nextLines.Length)&&changes.Count<200;i++)
                if((i<oldLines.Length?oldLines[i]:"")!=(i<nextLines.Length?nextLines[i]:"")) {changes.Add("L"+(i+1));if(i<oldLines.Length)changes.Add("- "+oldLines[i]);if(i<nextLines.Length)changes.Add("+ "+nextLines[i]);}
            diff.Text=T(session.SourceChanged?"SourceReview":"SourceUnchanged")+Environment.NewLine+T("SourceCounts",session.Current?.Document.Notices.Count??0,session.Candidate.Document.Notices.Count)+Environment.NewLine+string.Join(Environment.NewLine,changes);
        }
        private void Browse(TextBox box,bool folder)
        {
            if(folder){using(var d=new FolderBrowserDialog()){if(d.ShowDialog(this)!=DialogResult.OK)return;box.Text=d.SelectedPath;}}
            else {using(var d=new OpenFileDialog {Filter="XML|*.xml",CheckFileExists=true,RestoreDirectory=true}){if(d.ShowDialog(this)!=DialogResult.OK)return;box.Text=d.FileName;}}
            if(box==target && box.Text.Length>0){settings.TargetPath=box.Text;settings.LastAppliedHash="";settings.LastBackup="";}else if(box==images){settings.ExtraImageFolder=box.Text;preview.Clear();}
            Changed();DrawPreview(true);
        }
        private void Changed()=>SettingsChanged?.Invoke(this,EventArgs.Empty);
        internal bool ConfirmDiscard()=>true;
        internal void ApplyLanguage(){if(lastStatusKey!=null)Message(lastStatusKey);foreach(var item in localized)item.Item1.Text=T(item.Item2);var index=groups.SelectedIndex;loading=true;for(int i=0;i<groups.Items.Count;i++)groups.Items[i]=GroupName(session.Current.Groups[i]);loading=false;if(index>=0)SelectGroup();}
        private string T(string key,params object[] args)=>Localization.Get(settings.EffectiveLanguage,key,args);
        private void Bind(Control c,string key){localized.Add(Tuple.Create(c,key));c.Text=T(key);}
        private void Message(string key){lastStatusKey=key;var text=T(key);status.Text=text==key?T("OperationError"):text;}
        private Button Button(string key,Action action){var b=new Button {AutoSize=true,MinimumSize=new Size(110,30),BackColor=Color.FromArgb(244,247,251),FlatStyle=FlatStyle.Flat};Bind(b,key);b.Click+=(s,e)=>{try{lock(settings)action();}catch(Exception ex){Message(ex.Message);}};return b;}
        private Control Row(string key,Control c){var p=new FlowLayoutPanel {AutoSize=true,WrapContents=false};var l=new Label {AutoSize=false,Width=150,Height=30,TextAlign=ContentAlignment.MiddleLeft};Bind(l,key);p.Controls.Add(l);p.Controls.Add(c);return p;}
        private Control PathRow(string key,TextBox box,Action browse){var p=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};var label=new Label {Width=110,Height=32,TextAlign=ContentAlignment.MiddleLeft};Bind(label,key);p.Controls.Add(label);p.Controls.Add(box);p.Controls.Add(Button("Browse",browse));return p;}
        private static TextBox PathBox()=>new TextBox {Width=420,ReadOnly=true};
        private static NumericUpDown N(decimal min,decimal max,decimal step)=>new NumericUpDown {Minimum=min,Maximum=max,Increment=step,Width=110,DecimalPlaces=1};
        private static decimal Bound(NumericUpDown n,double v)=>Math.Max(n.Minimum,Math.Min(n.Maximum,(decimal)v));
        protected override void Dispose(bool disposing){if(disposing)preview.Dispose();base.Dispose(disposing);}
    }
}
