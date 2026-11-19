using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BahamutGuide
{
    internal sealed class ArenaState
    {
        private readonly List<uint> current = new List<uint>(4);
        private readonly List<uint> next = new List<uint>(4);
        private long expires, firstMark, lastHit = -1000;
        internal int Phase { get; private set; }
        internal void Reset(int phase = 0)
        {
            Phase = phase;
            current.Clear();
            next.Clear();
            lastHit = -1000;
            expires = 0;
        }
        internal uint[] Targets(long now)
        {
            if (now >= expires) { current.Clear(); next.Clear(); }
            return current.ToArray();
        }
        internal void Observe(string raw, long now)
        {
            if (string.IsNullOrEmpty(raw)) return;
            int start = raw[0] == '[' ? raw.IndexOf(']') + 1 : 0;
            while (start < raw.Length && raw[start] == ' ') start++;
            if (raw.Length < start + 3) return;
            char separator = raw[start+2];
            if (separator != ':' && separator != '|') return;
            string type = raw.Substring(start,2);
            bool network = separator == '|';
            bool marker = type == (network ? "27" : "1B");
            bool cast = type == (network ? "20" : "14");
            bool action = network ? type=="21" || type=="22" : type=="15" || type=="16";
            if (!marker && !cast && !action)
            {
                if ((type=="33" || type=="21") && (raw.Contains("40000010") || raw.Contains("4000000F"))) Reset();
                return;
            }
            if (marker && Phase != 7 && Phase != 0) return;
            string[] fields = raw.Substring(start).Split(new[]{separator}, 9);
            if (marker)
            {
                int actor = network ? 2 : 1, icon = network ? 6 : 5;
                uint id;
                if (fields.Length <= icon || fields[icon] != "0028" || !uint.TryParse(fields[actor],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out id)) return;
                if (now >= expires) { current.Clear(); next.Clear(); }
                if (current.Contains(id) || next.Contains(id)) return;
                // New icons can precede the previous wave's damage. A dead player may leave a wave short.
                if (current.Count == 0) firstMark = now;
                var group = current.Count < 4 && now - firstMark < 1800 ? current : next;
                if (group.Count < 4) group.Add(id);
                expires = now + 12000;
                return;
            }
            int abilityField = network ? 4 : cast && fields.Length>1 && fields[1].Length==4 ? 1 : 3;
            if (fields.Length <= abilityField) return;
            int ability;
            if (!int.TryParse(fields[abilityField], NumberStyles.HexNumber,CultureInfo.InvariantCulture,out ability)) return;
            int phase = ability >= 0x26E2 && ability <= 0x26E7 ? 3 + ability - 0x26E2 :
                ability == 0x26E8 ? 9 : ability == 0x26F2 || ability == 0x26E9 ? 10 :
                Phase <= 2 && (ability == 0x26B8 || ability == 0x26C2) ? 2 :
                Phase <= 3 && ability == 0x26D1 ? 3 :
                Phase == 0 && ability >= 0x26A7 && ability <= 0x26AE ? 1 : 0;
            if (phase != 0 && phase != Phase) Reset(phase);
            if ((Phase == 7 || Phase == 0) && action && ability == 0x26D9 && now-lastHit > 800)
            {
                current.Clear(); current.AddRange(next); next.Clear();
                firstMark = now;
                lastHit = now;
            }
        }
    }

    internal sealed class ShakerWarning
    {
        internal const double Reach = 60;
        internal const double HalfAngle = Math.PI / 4;
        internal readonly HashSet<uint> DangerousCones = new HashSet<uint>();
        internal readonly HashSet<uint> EndangeredPlayers = new HashSet<uint>();

        internal static ShakerWarning Evaluate(MapActor boss, List<MapActor> actors, uint[] targets)
        {
            var warning = new ShakerWarning();
            if (boss == null || targets.Length == 0) return warning;
            foreach (var owner in actors.Where(a => targets.Contains(a.Id) && !a.Dead))
            {
                double aimX = owner.X - boss.X, aimY = owner.Y - boss.Y;
                if (aimX * aimX + aimY * aimY < 0.01) continue;
                foreach (var member in actors)
                {
                    if (member.Id == owner.Id || member.Dead || !(member.Party || member.Self || targets.Contains(member.Id))) continue;
                    double dx = member.X - boss.X, dy = member.Y - boss.Y;
                    if (dx * dx + dy * dy > Reach * Reach) continue;
                    // tan(45 degrees) = 1. Equality includes players on the edge.
                    double forward = dx * aimX + dy * aimY;
                    double sideways = Math.Abs(dx * aimY - dy * aimX);
                    if (forward < sideways) continue;
                    warning.DangerousCones.Add(owner.Id);
                    warning.EndangeredPlayers.Add(member.Id);
                }
            }
            return warning;
        }
    }

    internal sealed class OctetRoute
    {
        internal readonly int Start, Step;
        internal readonly bool Adjusted;
        private OctetRoute(int boss, int nael)
        {
            Step = boss % 2 == 1 ? 1 : -1;
            int opposite = (boss + 4) % 8;
            Adjusted = opposite == nael;
            Start = (opposite + (Adjusted ? Step : 0) + 8) % 8;
        }
        internal static OctetRoute From(List<MapActor> actors)
        {
            var slots = new List<int>(3);
            foreach (var kind in new uint[] {0x1FE8, 0x1FE1, 0x1FDF})
            {
                var seen = actors.Where(a => a.Kind == kind && a.Rim).Select(a => a.Slot).Distinct().ToArray();
                if (seen.Length != 1 || slots.Contains(seen[0])) return null;
                slots.Add(seen[0]);
            }
            return new OctetRoute(slots[0],slots[1]);
        }
    }

    internal sealed class MapActor
    {
        internal uint Id, Kind;
        internal double X,Y;
        internal string Name;
        internal bool Self, Party, Dead;
        internal int Slot => ((int)Math.Round(Math.Atan2(X,-Y)*4/Math.PI)+8)%8;
        internal bool Rim => Math.Abs(Math.Sqrt(X*X+Y*Y)-23.6)<2;
        internal bool VisibleIn(int phase)
        {
            if (Self || Kind == 0 || phase == 0) return true;
            if (Kind == 0x1FE9) return phase == 10;
            if (Kind == 0x1FE1) return phase >= 2 && phase <= 9;
            if (Kind == 0x1FDF) return phase == 1 || phase >= 3 && phase <= 9;
            if (Kind == 0x1FE8) return phase >= 3 && phase != 9;
            if (Dragon) return phase == 2 || phase == 8;
            return true;
        }
        internal bool Dragon => Kind>=0x1FE3 && Kind<=0x1FE7;
    }
}
