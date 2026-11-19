using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace BahamutGuide
{
    internal sealed class LiveMap : IDisposable
    {
        private readonly PluginSettings settings;
        private readonly ArenaFeed feed=new ArenaFeed();
        private readonly ArenaState state=new ArenaState();
        private readonly object gate=new object();
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private readonly Timer timer=new Timer();
        private readonly PreviewWindow overlay=new PreviewWindow();
        private bool listening,active,closed,visible;
        internal bool Preview;
        private string signature;
        private OctetRoute route;
        private long routeUntil;
        private BitmapSource image;
        internal LiveMap(PluginSettings settings)
        {
            this.settings=settings;
            overlay.Moved+=(x,y)=>{lock(settings){settings.MapX=x;settings.MapY=y;}};
            timer.Tick+=(s,e)=>Tick();
            Configure();
        }
        internal void Configure()
        {
            if(closed)return;
            bool want=settings.MapEnabled && ActGlobals.oFormActMain!=null;
            if(listening!=want)
            {
                if(want) ActGlobals.oFormActMain.OnLogLineRead+=Log;
                else ActGlobals.oFormActMain.OnLogLineRead-=Log;
                listening=want;
            }
            if(!want)Deactivate();
            timer.Enabled=want;
            Tick();
        }
        private void Log(bool import,LogLineEventArgs e)
        {
            if(import || e==null)return;
            lock(gate)
            {
                if(!active || closed)return;
                var raw=e.originalLogLine;
                if(raw!=null && raw.Length>2 && raw[2]=='|') state.Observe(raw,clock.ElapsedMilliseconds);
                else state.Observe(e.logLine,clock.ElapsedMilliseconds);
            }
        }
        private void Deactivate(){lock(gate){active=false;state.Reset();}}
        private void Tick()
        {
            try
            {
                bool arena=listening && feed.InArena();
                timer.Interval=arena?250:2000;
                if(!arena){Deactivate();route=null;}
                else lock(gate)active=true;
                if(!arena && !Preview){Hide();return;}
                int phase;uint[] targets;
                lock(gate){phase=state.Phase;targets=state.Targets(clock.ElapsedMilliseconds);}
                var actors=arena?feed.Read(targets):new List<MapActor>();
                if(Preview && !arena)
                {
                    phase=7;targets=new uint[]{2,3};
                    actors.Add(new MapActor{Id=1,Kind=0x1FE8,Name="Bahamut",X=0,Y=0});
                    actors.Add(new MapActor{Id=2,Name="Ally A",X=12,Y=6});
                    actors.Add(new MapActor{Id=3,Name="Ally B",X=-8,Y=-12});
                    actors.Add(new MapActor{Id=4,Self=true,Name=Localization.Get(settings.EffectiveLanguage,"MapYou"),X=8,Y=10});
                }
                // At duty entry the two inactive bosses share Twintania's spawn coordinates.
                if(phase==0 && actors.Any(a=>a.Kind==0x1FDF && Math.Abs(a.X)<0.1 && Math.Abs(a.Y+15)<0.1))
                    actors.RemoveAll(a=>(a.Kind==0x1FE1 || a.Kind==0x1FE8) && Math.Abs(a.X)<0.1 && Math.Abs(a.Y+15)<0.1);
                actors.RemoveAll(a=>!a.VisibleIn(phase) && !targets.Contains(a.Id));
                if(actors.Count==0){Hide();return;}
                if(phase!=8)route=null;
                else if(route==null)
                {
                    route=OctetRoute.From(actors);
                    if(route!=null)routeUntil=clock.ElapsedMilliseconds+38000;
                }
                var shownRoute=clock.ElapsedMilliseconds<routeUntil?route:null;
                var key=(arena?"live":"preview")+(shownRoute==null?"":shownRoute.Start+"/"+shownRoute.Step+"/"+shownRoute.Adjusted)+phase+"|"+settings.EffectiveLanguage+"|"+settings.MapSize+"|"+settings.MapCones+"|"+string.Join(",",targets)+"|"+
                    string.Join(";",actors.Select(a=>a.Id+":"+a.Kind+":"+a.Name+":"+(targets.Length>0?a.X:Math.Round(a.X*5))+":"+(targets.Length>0?a.Y:Math.Round(a.Y*5))+":"+a.Self+":"+a.Party+":"+a.Dead));
                if(key!=signature){image=ArenaDrawing.Render(actors,targets,phase,settings.MapCones,settings.EffectiveLanguage,Math.Max(180,Math.Min(640,settings.MapSize)),shownRoute,!arena);signature=key;}
                overlay.Present(image,settings.MapX,settings.MapY,true,settings.MapLocked);
                visible=true;
            }
            catch {Deactivate();Hide();timer.Interval=2000;}
        }
        private void Hide(){if(visible)overlay.Present(null,0,0,false,true);visible=false;signature=null;image=null;}
        public void Dispose()
        {
            lock(gate){if(closed)return;closed=true;active=false;state.Reset();}
            if(listening)ActGlobals.oFormActMain.OnLogLineRead-=Log;
            listening=false;
            timer.Stop();timer.Dispose();overlay.Close();
        }
    }
}
