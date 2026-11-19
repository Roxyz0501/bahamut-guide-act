using BahamutGuide;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

internal static partial class Program
{
    private static void MapTests()
    {
        var state = new ArenaState();
        state.Observe("[12:00:00.000] 14:26E6:Bahamut starts using Tenstrike",0);
        Check(state.Phase == 7,"ACT formatted Tenstrike cast");
        Action<int,long> mark = (id,t) => state.Observe("27|time|"+id.ToString("X8")+"|Member|0000|0000|0028|",t);
        for(int id=1;id<=3;id++) mark(id,100);
        mark(1,110);
        for(int id=4;id<=7;id++) mark(id,5100);
        Check(state.Targets(5110).SequenceEqual(new uint[]{1,2,3}),"short first wave and duplicate marker");
        state.Observe("21|time|ABC|Bahamut|26D9|Earthshaker|",5220);
        Check(state.Targets(5220).SequenceEqual(new uint[]{4,5,6,7}),"second wave takes over on hit");
        state.Observe("22|time|ABC|Bahamut|26D9|Earthshaker|",5221);
        Check(state.Targets(5221).Length==4,"duplicate damage does not clear next wave");
        state.Observe("15:40000001:Bahamut:26D9:Earthshaker:",10300);
        Check(state.Targets(10300).Length==0,"ACT damage clears final wave");
        state.Observe("1B:10000006:Member:0000:0000:0028:",11000);
        Check(state.Targets(11000).Single()==0x10000006,"ACT formatted marker");
        Check(state.Targets(24000).Length==0,"expired marker cleared");
        mark(5,25000);
        state.Observe("33|time|80000000|40000010|",25050);
        Check(state.Phase==0 && state.Targets(25051).Length==0,"wipe clears cones");

        var actors=new List<MapActor> {
            new MapActor{Id=10,Kind=0x1FE8,Name="Bahamut",X=0,Y=0},
            new MapActor{Id=20,Name="Ally A",X=14,Y=0},
            new MapActor{Id=30,Self=true,X=-4,Y=-6}
        };
        var targets=new uint[]{20};
        var plain=ArenaDrawing.Render(actors,targets,7,false,"ja",320);
        var cones=ArenaDrawing.Render(actors,targets,7,true,"ja",320);
        Check(Pixel(plain,225,185)!=Pixel(cones,225,185) && Pixel(plain,185,220)==Pixel(cones,185,220),"within and outside 45-degree half angle");
        Check(Pixel(plain,277,165)==Pixel(cones,277,165),"cone clipped at arena edge");
        actors[1].X=0;actors[1].Y=-14;
        var north=ArenaDrawing.Render(actors,targets,7,true,"ja",320);
        Check(Pixel(north,225,185)==Pixel(plain,225,185) && Pixel(north,175,90)!=Pixel(plain,175,90),"moving ally rotates cone north");
        actors.Add(new MapActor{Id=40,Name="Ally B",X=12,Y=8});
        actors[2].Name="Ally C";
        actors[2].X=15;actors[2].Y=3;
        var example=ArenaDrawing.Render(actors,new uint[]{20,40},7,true,"ja",400);
        using(var f=File.Create(Path.Combine(artifacts,"earthshaker.png")))
        {
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(example));png.Save(f);
        }
        var warning=ShakerWarning.Evaluate(actors[0],actors,new uint[]{20,40});
        Check(warning.DangerousCones.SetEquals(new uint[]{40}) && warning.EndangeredPlayers.SetEquals(new uint[]{30}),"only the cone clipping another player is red");
        actors[2].Dead=true;
        Check(ShakerWarning.Evaluate(actors[0],actors,new uint[]{20,40}).EndangeredPlayers.Count==0,"fallen player excluded");
        actors[2].Dead=false;actors[2].Self=false;actors[2].Party=false;
        Check(ShakerWarning.Evaluate(actors[0],actors,new uint[]{20,40}).EndangeredPlayers.Count==0,"non-party bystander excluded");
        actors[2].Party=true;actors[2].X=-4;actors[2].Y=7;
        actors[1].X=10;actors[1].Y=0;actors[3].X=4;actors[3].Y=10;
        Check(ShakerWarning.Evaluate(actors[0],actors,new uint[]{20,40}).DangerousCones.Count==0,"two cones overlap in empty space without warning");
        actors[2].X=9;actors[2].Y=9;
        warning=ShakerWarning.Evaluate(actors[0],actors,new uint[]{20,40});
        Check(warning.DangerousCones.Count==2 && warning.EndangeredPlayers.SetEquals(new uint[]{30}),"unmarked party member on shared edge");
        actors[2].X=70;actors[2].Y=5;actors[3].X=9;actors[3].Y=4;
        warning=ShakerWarning.Evaluate(actors[0],actors,new uint[]{20,40});
        Check(warning.EndangeredPlayers.SetEquals(new uint[]{20,40}),"marked players can clip each other, distant player excluded");
        actors.RemoveAt(0);
        var noBoss=ArenaDrawing.Render(actors,targets,7,true,"ja",320);
        Check(Pixel(noBoss,175,90)==Pixel(plain,175,90),"no invented source when boss missing");
        var rim=new List<MapActor>{new MapActor{Kind=0x1FE8,Y=-24},new MapActor{Kind=0x1FE1,Y=24},new MapActor{Kind=0x1FDF,X=24}};
        var route=OctetRoute.From(rim);
        Check(route.Start==3 && route.Step==-1 && route.Adjusted,"Grand Octet 45-degree adjustment");
        rim.RemoveAt(2);Check(OctetRoute.From(rim)==null,"incomplete formation gives no route");
    }
    private static int Pixel(BitmapSource frame,int x,int y)
    {
        var bytes=new byte[4];
        frame.CopyPixels(new Int32Rect(x,y,1,1),bytes,4,0);
        return BitConverter.ToInt32(bytes,0);
    }

    private static void ReplayMap(string path)
    {
        var state=new ArenaState();
        int marked=0, switches=0, clears=0, damageRows=0, inside=0;
        double largestOffset=0;
        foreach(var line in File.ReadLines(path))
        {
            if(line.Length<4 || line[2]!='|' || !(line.StartsWith("20|") || line.StartsWith("21|") || line.StartsWith("22|") || line.StartsWith("27|") || line.StartsWith("33|"))) continue;
            int end=line.IndexOf('|',3);
            DateTimeOffset time;
            if(end<0 || !DateTimeOffset.TryParse(line.Substring(3,end-3),CultureInfo.InvariantCulture,DateTimeStyles.None,out time))continue;
            long now=time.ToUnixTimeMilliseconds();
            var before=state.Targets(now);
            state.Observe(line,now);
            if(state.Phase!=7)continue;
            var after=state.Targets(now);
            if((line.StartsWith("21|") || line.StartsWith("22|")) && line.Contains("|26DA|"))
            {
                var f=line.Split('|');
                if(f.Length>43 && f[4]=="26DA" && f[6].StartsWith("1"))
                {
                    double x=double.Parse(f[30],CultureInfo.InvariantCulture)-double.Parse(f[40],CultureInfo.InvariantCulture);
                    double y=double.Parse(f[31],CultureInfo.InvariantCulture)-double.Parse(f[41],CultureInfo.InvariantCulture);
                    double heading=double.Parse(f[43],CultureInfo.InvariantCulture);
                    if(x!=0 || y!=0)
                    {
                        var offset=Math.Abs(Math.IEEERemainder(Math.Atan2(x,y)-heading,2*Math.PI));
                        damageRows++;
                        if(offset<=ShakerWarning.HalfAngle)inside++;
                        largestOffset=Math.Max(largestOffset,offset);
                    }
                }
            }
            if(line.StartsWith("27|") && line.Contains("|0028|"))marked++;
            if(line.StartsWith("21|") && line.Contains("|26D9|") && before.Length>0)
            {
                Check(!before.Intersect(after).Any(),"real log wave clears previous targets");
                if(after.Length>0)switches++;else clears++;
            }
        }
        Check(marked>0 && switches>0 && clears>0,"real Tenstrike transitions present");
        Console.WriteLine("Tenstrike replay: "+marked+" markers, "+switches+" wave switches, "+clears+" final clears; "+inside+"/"+damageRows+" actual hits inside cone; max angle "+(largestOffset*180/Math.PI).ToString("F2")+" degrees.");
    }
}
