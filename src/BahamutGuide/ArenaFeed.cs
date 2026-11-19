using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Advanced_Combat_Tracker;

namespace BahamutGuide
{
    internal sealed class ArenaFeed
    {
        private object repository;
        private MethodInfo territory, actors, player;
        private Type actorType;
        private PropertyInfo id, kind, x, y, name, hp, partyType, objectType;
        internal bool InArena()
        {
            var plugin=ActGlobals.oFormActMain?.ActPlugins.FirstOrDefault(p=>p.pluginObj?.GetType().FullName=="FFXIV_ACT_Plugin.FFXIV_ACT_Plugin");
            var source=plugin?.pluginObj;
            var repo=source?.GetType().GetProperty("DataRepository")?.GetValue(source);
            if(repo==null) { repository=null; return false; }
            if(!ReferenceEquals(repo,repository))
            {
                var type=repo.GetType().GetInterfaces().FirstOrDefault(t=>t.Name=="IDataRepository")??repo.GetType();
                territory=type.GetMethod("GetCurrentTerritoryID");
                actors=type.GetMethod("GetCombatantList");
                player=type.GetMethod("GetCurrentPlayerID");
                repository=repo;
            }
            return territory!=null && actors!=null && Convert.ToInt32(territory.Invoke(repo,null))==733;
        }
        internal List<MapActor> Read(uint[] marked)
        {
            var result=new List<MapActor>();
            uint self=Convert.ToUInt32(player?.Invoke(repository,null));
            var list=actors.Invoke(repository,null) as IEnumerable;
            if(list==null)return result;
            foreach(var obj in list)
            {
                if(obj==null)continue;
                if(obj.GetType()!=actorType)
                {
                    actorType=obj.GetType();
                    id=actorType.GetProperty("ID");kind=actorType.GetProperty("BNpcID");name=actorType.GetProperty("Name");
                    partyType=actorType.GetProperty("PartyType");objectType=actorType.GetProperty("type");
                    x=actorType.GetProperty("PosX");y=actorType.GetProperty("PosY");hp=actorType.GetProperty("CurrentHP");
                }
                uint key=Convert.ToUInt32(id.GetValue(obj)), species=Convert.ToUInt32(kind.GetValue(obj));
                bool member=marked.Length>0 && Convert.ToInt32(objectType.GetValue(obj))==1 && Convert.ToInt32(partyType.GetValue(obj))==1;
                if(key==0 || (key!=self && !member && Array.IndexOf(marked,key)<0 && !(species>=0x1FDF && species<=0x1FE9 && species!=0x1FE2) && species!=0x1E88FF && species!=0x1E9663))continue;
                if(species>=0x1FDF && species<=0x1FE9 && Convert.ToInt64(hp.GetValue(obj))==0)continue;
                var a=new MapActor{Id=key,Kind=species,Name=Convert.ToString(name.GetValue(obj)),X=Convert.ToDouble(x.GetValue(obj)),Y=Convert.ToDouble(y.GetValue(obj)),Self=key==self,Party=member,Dead=Convert.ToInt64(hp.GetValue(obj))<=0};
                if(a.X*a.X+a.Y*a.Y<=29*29)result.Add(a);
            }
            return result;
        }
    }
}
