using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BahamutGuide
{
    internal static class ArenaDrawing
    {
        private static readonly Brush[] DragonColors={Brushes.Tomato,Brushes.DeepSkyBlue,Brushes.MediumOrchid,Brushes.Black,Brushes.White};
        private static Point Map(MapActor a)=>new Point(160+a.X*5,158+a.Y*5);
        private static void Label(DrawingContext dc,string text,Point at,double size=13)
        {
            var t=new FormattedText(text??"",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,Brushes.White,1);
            var pos=new Point(Math.Max(1,Math.Min(319-t.Width,at.X-t.Width/2)),at.Y);
            dc.DrawRoundedRectangle(Brushes.Black,null,new Rect(pos.X-2,pos.Y,t.Width+4,t.Height),2,2);
            dc.DrawText(t,pos);
        }
        private static void Cone(DrawingContext dc,Point from,Point to,bool danger)
        {
            Vector axis=to-from;
            if(axis.LengthSquared<.25)return;
            double angle=Math.Atan2(axis.Y,axis.X);
            double radius=ShakerWarning.Reach*5;
            Func<double,Point> edge=offset=>new Point(from.X+radius*Math.Cos(angle+offset),from.Y+radius*Math.Sin(angle+offset));
            var shape=new StreamGeometry();
            using(var path=shape.Open())
            {
                path.BeginFigure(from,true,true);
                path.LineTo(edge(-ShakerWarning.HalfAngle),true,false);
                path.ArcTo(edge(ShakerWarning.HalfAngle),new Size(radius,radius),0,false,SweepDirection.Clockwise,true,false);
            }
            var fill=danger?Color.FromArgb(148,244,32,72):Color.FromArgb(65,255,110,32);
            dc.DrawGeometry(new SolidColorBrush(fill),new Pen(danger?Brushes.HotPink:Brushes.Orange,danger?2:1),shape);
        }
        internal static BitmapSource Render(List<MapActor> actors,uint[] targets,int phase,bool cones,string lang,int size,OctetRoute route=null,bool sample=false)
        {
            var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size/320d,size/320d));
                var center=new Point(160,158);
                var arena=new EllipseGeometry(center,110,110);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(205,17,26,40)),null,new Rect(0,0,320,320),12,12);
                dc.DrawGeometry(Brushes.DarkSlateGray,new Pen(Brushes.LightSlateGray,2),arena);
                dc.DrawLine(new Pen(Brushes.SlateGray,1),new Point(50,158),new Point(270,158));
                dc.DrawLine(new Pen(Brushes.SlateGray,1),new Point(160,48),new Point(160,268));
                var boss=actors.FirstOrDefault(a=>a.Kind==0x1FE8);
                var warning=ShakerWarning.Evaluate(boss,actors,targets);
                dc.PushClip(arena);
                if(cones && boss!=null)foreach(var a in actors.Where(a=>targets.Contains(a.Id) && !a.Dead).OrderBy(a=>warning.DangerousCones.Contains(a.Id)))
                    Cone(dc,Map(boss),Map(a),warning.DangerousCones.Contains(a.Id));
                dc.Pop();
                Label(dc,"N ↑",new Point(160,5));
                if(sample)Label(dc,Localization.Get(lang,"MapSample"),new Point(277,7));
                if(phase>0)Label(dc,"P"+phase,new Point(24,7));
                var dragons=actors.Where(a=>a.Dragon && a.Rim).OrderBy(a=>a.Slot).ToList();
                bool numbered=dragons.Count==5 && dragons.Select(a=>a.Slot).Distinct().Count()==5;
                foreach(var a in actors.OrderBy(a=>a.Self || targets.Contains(a.Id)?1:0))
                {
                    Point p=Map(a);
                    if(a.Dragon)
                    {
                        dc.DrawRectangle(DragonColors[a.Kind-0x1FE3],new Pen(Brushes.Gray,1),new Rect(p.X-9,p.Y-9,18,18));
                        if(phase==2 && numbered && dragons.Contains(a))Label(dc,(dragons.IndexOf(a)+1).ToString(),new Point(p.X,p.Y-8),12);
                        continue;
                    }
                    bool marked=targets.Contains(a.Id);
                    Brush color=marked?Brushes.OrangeRed:a.Self?Brushes.LimeGreen:a.Kind==0x1FE8?Brushes.Orange:a.Kind==0x1FE1?Brushes.Violet:Brushes.Cyan;
                    if(a.Kind==0x1E9663){dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(100,255,72,32)),new Pen(Brushes.Orange,1),p,20,20);continue;}
                    dc.DrawEllipse(color,new Pen(Brushes.White,1),p,marked?6:4,marked?6:4);
                    if(a.Self && marked)dc.DrawEllipse(null,new Pen(Brushes.LimeGreen,2),p,8,8);
                    bool endangered=warning.EndangeredPlayers.Contains(a.Id);
                    if(endangered)
                    {
                        dc.DrawEllipse(null,new Pen(Brushes.White,4),p,10,10);
                        dc.DrawEllipse(null,new Pen(Brushes.Crimson,2.5),p,10,10);
                    }
                    if(marked || endangered || (!a.Party && !a.Self && a.Kind!=0x1E88FF))
                        Label(dc,(endangered?"! ":"")+a.Name,new Point(p.X,p.Y+(endangered?11:5)),12);
                }
                string hint="";
                if(phase==2 && numbered && dragons[1].Slot==4)hint=Localization.Get(lang,"MapAdjust");
                if(phase==8 && route!=null)
                {
                    double angle=route.Start*Math.PI/4;
                    var meeting=new Point(center.X+98*Math.Sin(angle),center.Y-98*Math.Cos(angle));
                    dc.DrawEllipse(null,new Pen(Brushes.Gold,3),meeting,7,7);
                    hint=new[]{"↑","↗","→","↘","↓","↙","←","↖"}[route.Start]+"   "+(route.Step>0?"↻":"↺")+
                        (route.Adjusted?"   "+Localization.Get(lang,"MapAdjust"):"");
                }
                if(targets.Length>0)hint=Localization.Get(lang,"MapShaker")+"  "+actors.Count(a=>targets.Contains(a.Id))+"/"+targets.Length;
                if(warning.EndangeredPlayers.Count>0)hint+="   ! "+Localization.Get(lang,"MapRisk");
                if(hint.Length>0)Label(dc,hint,new Point(160,288),15);
                dc.Pop();
            }
            var image=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);
            image.Render(visual);image.Freeze();return image;
        }
    }

}
