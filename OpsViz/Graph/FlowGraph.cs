using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using OpsViz.Model;

namespace OpsViz.Graph
{
    public class GraphOptions
    {
        public bool PerOperation;
        public double MinMass;
        public string ProductFilter; // null = все
        public HashSet<string> Hidden;
        public DateTime? DateFrom; // по дате начала операции, null = без ограничения
        public DateTime? DateTo;
    }

    public class SchemaOptions
    {
        public bool ShowLabels = true;
        public bool ChainMode = true;
        public double ImbalanceThresholdPct = 5.0;
    }

    public class NodeInfo
    {
        public string Name;
        public double InMass, OutMass;
        public int Layer, Order;
        public double X, Y, W, H;
        public readonly List<EdgeInfo> In = new List<EdgeInfo>();
        public readonly List<EdgeInfo> Out = new List<EdgeInfo>();
        public bool IsPureSource => In.Count == 0 && Out.Count > 0;
        public bool IsPureSink => Out.Count == 0 && In.Count > 0;
        public double Throughput => Math.Max(InMass, OutMass);
        // Сколько операций узла (вход+выход) имеют расхождение выше допуска.
        // Выставляется пересчетом в MainViewModel, сам граф допуск не знает.
        public int DivergedOps;
        public override string ToString() => Name;
    }

    public class EdgeInfo
    {
        public NodeInfo Src, Dst;
        public double Mass;
        public int Count;
        public string MainProduct = "";
        public Dictionary<string, double> Products = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public List<Operation> Ops = new List<Operation>();
        public bool IsBack;
        public double Thickness;
        public Point[] Samples;
        public double LabelX, LabelY;
        double SrcPortY, DstPortY;
        public override string ToString() => Src.Name + " -> " + Dst.Name;
        internal void SetPorts(double s, double d) { SrcPortY = s; DstPortY = d; }
        internal double GetSrcPort() => SrcPortY;
        internal double GetDstPort() => DstPortY;
    }

    public class FlowGraph
    {
        public List<NodeInfo> Nodes = new List<NodeInfo>();
        public List<EdgeInfo> Edges = new List<EdgeInfo>();
        public Rect Bounds;
        public double MaxEdgeMass = 1;
        double _maxNodeT = 1;

        const double NodeW = 175, HGap = 245, VGap = 26, TopPad = 30, LeftPad = 30;

        public static FlowGraph Build(List<Operation> ops, GraphOptions opt)
        {
            var g = new FlowGraph();
            var nodeMap = new Dictionary<string, NodeInfo>(StringComparer.OrdinalIgnoreCase);
            Func<string, NodeInfo> Node = name =>
            {
                NodeInfo n;
                if (!nodeMap.TryGetValue(name, out n)) { n = new NodeInfo { Name = name }; nodeMap[name] = n; g.Nodes.Add(n); }
                return n;
            };

            var kept = ops.Where(o => !string.IsNullOrWhiteSpace(o.SourceName) && !string.IsNullOrWhiteSpace(o.ReceiverName)
                && !o.SourceName.Equals(o.ReceiverName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(opt.ProductFilter))
                kept = kept.Where(o => Eq(o.SourceProduct, opt.ProductFilter) || Eq(o.ReceiverProduct, opt.ProductFilter));
            if (opt.MinMass > 0)
                kept = kept.Where(o => (o.SourceMass ?? 0) >= opt.MinMass || (o.ReceiverMass ?? 0) >= opt.MinMass);
            if (opt.Hidden != null)
                kept = kept.Where(o => !opt.Hidden.Contains(o.SourceName) && !opt.Hidden.Contains(o.ReceiverName));
            if (opt.DateFrom.HasValue)
            {
                var d = opt.DateFrom.Value;
                kept = kept.Where(o => o.Start > DateTime.MinValue && o.Start >= d);
            }
            if (opt.DateTo.HasValue)
            {
                var d = opt.DateTo.Value;
                // Как в SQL скрипта выгрузки: конец операции должен быть внутри
                // периода (или операция еще открыта), иначе это уже другой период.
                kept = kept.Where(o => o.Start > DateTime.MinValue && o.Start <= d
                    && (!o.End.HasValue || o.End.Value <= d));
            }
            var list = kept.ToList();

            if (opt.PerOperation)
            {
                foreach (var o in list)
                {
                    var e = new EdgeInfo { Src = Node(o.SourceName), Dst = Node(o.ReceiverName) };
                    AddOp(e, o); g.Edges.Add(e);
                }
            }
            else
            {
                foreach (var grp in list.GroupBy(o => o.SourceName + "\u0001" + o.ReceiverName, StringComparer.OrdinalIgnoreCase))
                {
                    var e = new EdgeInfo { Src = Node(grp.First().SourceName), Dst = Node(grp.First().ReceiverName) };
                    foreach (var o in grp) AddOp(e, o);
                    g.Edges.Add(e);
                }
            }

            foreach (var e in g.Edges)
            {
                e.Src.Out.Add(e); e.Dst.In.Add(e);
                e.Src.OutMass += e.Mass; e.Dst.InMass += e.Mass;
                e.MainProduct = e.Products.OrderByDescending(p => p.Value).Select(p => p.Key).FirstOrDefault() ?? "";
            }
            g.Nodes.RemoveAll(n => n.In.Count == 0 && n.Out.Count == 0);
            g.MaxEdgeMass = g.Edges.Count > 0 ? Math.Max(1, g.Edges.Max(e => e.Mass)) : 1;
            g.Layout();
            return g;
        }

        static void AddOp(EdgeInfo e, Operation o)
        {
            var m = Math.Max(o.SourceMass ?? 0, o.ReceiverMass ?? 0);
            e.Mass += m; e.Count++; e.Ops.Add(o);
            var p = string.IsNullOrWhiteSpace(o.SourceProduct) ? (o.ReceiverProduct ?? "—") : o.SourceProduct;
            if (e.Products.ContainsKey(p)) e.Products[p] += m; else e.Products[p] = m;
        }

        static bool Eq(string a, string b) => string.Equals(a == null ? null : a.Trim(), b == null ? null : b.Trim(), StringComparison.OrdinalIgnoreCase);

        // ---------- раскладка ----------
        void Layout()
        {
            if (Nodes.Count == 0) { Bounds = new Rect(0, 0, 200, 200); Reroute(); return; }
            MarkBackEdges();

            var layer = Nodes.ToDictionary(n => n, n => 0);
            bool changed = true; int guard = 0;
            while (changed && guard++ < Nodes.Count + 2)
            {
                changed = false;
                foreach (var e in Edges)
                    if (!e.IsBack)
                    { int v = layer[e.Src] + 1; if (v > layer[e.Dst]) { layer[e.Dst] = v; changed = true; } }
            }
            foreach (var n in Nodes) n.Layer = layer[n];

            var layers = Nodes.GroupBy(n => n.Layer).OrderBy(gr => gr.Key)
                .Select(gr => gr.OrderByDescending(n => n.Throughput).ThenBy(n => n.Name).ToList()).ToList();

            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 1; i < layers.Count; i++)
                    OrderByBarycenter(layers[i], n => n.In.Where(e => !e.IsBack).Select(e => e.Src), layers[i - 1]);
                for (int i = layers.Count - 2; i >= 0; i--)
                    OrderByBarycenter(layers[i], n => n.Out.Where(e => !e.IsBack).Select(e => e.Dst), layers[i + 1]);
            }

            _maxNodeT = Math.Max(1, Nodes.Max(n => n.Throughput));
            var colH = layers.Select(col => col.Sum(n => NodeHeight(n)) + VGap * Math.Max(0, col.Count - 1)).ToList();
            double maxColH = colH.Count > 0 ? colH.Max() : 0;

            for (int i = 0; i < layers.Count; i++)
            {
                double y = TopPad + (maxColH - colH[i]) / 2; int order = 0;
                foreach (var n in layers[i])
                {
                    n.W = NodeW; n.H = NodeHeight(n);
                    n.X = LeftPad + i * (NodeW + HGap); n.Y = y; n.Order = order++;
                    y += n.H + VGap;
                }
            }
            Reroute();
        }

        double NodeHeight(NodeInfo n)
        {
            //return Clamp(24 + 56 * Math.Sqrt(n.Throughput / _maxNodeT), 24, 84);
            int ports = n.In.Count > n.Out.Count ? n.In.Count : n.Out.Count;
            double byPorts = 26.0 * (ports + 1);
            double byMass = 24 + 56 * Math.Sqrt(n.Throughput / _maxNodeT);
            double h = byMass > byPorts ? byMass : byPorts;
            if (h < 24) h = 24;
            if (h > 1500) h = 1500;
            return h;
        }

        static void OrderByBarycenter(List<NodeInfo> col, Func<NodeInfo, IEnumerable<NodeInfo>> neighbors, List<NodeInfo> refLayer)
        {
            var idx = new Dictionary<NodeInfo, int>();
            for (int i = 0; i < refLayer.Count; i++) idx[refLayer[i]] = i;
            var keyed = col.Select((n, i) =>
            {
                var ns = neighbors(n).Where(idx.ContainsKey).Select(x => idx[x]).ToList();
                return new { N = n, K = ns.Count == 0 ? (double)i : ns.Average(), I = i };
            }).OrderBy(a => a.K).ThenBy(a => a.I).ToList();
            col.Clear();
            foreach (var a in keyed) col.Add(a.N);
        }

        void MarkBackEdges()
        {
            var state = Nodes.ToDictionary(n => n, n => 0);
            foreach (var n in Nodes.ToList()) if (state[n] == 0) Dfs(n, state);
        }
        void Dfs(NodeInfo n, Dictionary<NodeInfo, int> state)
        {
            state[n] = 1;
            foreach (var e in n.Out)
            {
                if (state[e.Dst] == 1) e.IsBack = true;
                else if (state[e.Dst] == 0) Dfs(e.Dst, state);
            }
            state[n] = 2;
        }

        // ---------- маршруты рёбер (вызывается и при drag узлов) ----------
        public void Reroute()
        {
            if (Nodes.Count == 0) { Bounds = new Rect(0, 0, 200, 200); return; }
            foreach (var e in Edges) e.Thickness = Clamp(1.5 + 14 * Math.Sqrt(e.Mass / MaxEdgeMass), 1.5, 16);

            foreach (var n in Nodes)
            {
                var outs = n.Out.OrderBy(e => e.Dst.Y + e.Dst.H / 2).ToList();
                var ins = n.In.OrderBy(e => e.Src.Y + e.Src.H / 2).ToList();
                for (int i = 0; i < outs.Count; i++) outs[i].SetPorts(n.Y + n.H * (i + 1) / (outs.Count + 1), outs[i].GetDstPort());
                for (int i = 0; i < ins.Count; i++) ins[i].SetPorts(ins[i].GetSrcPort(), n.Y + n.H * (i + 1) / (ins.Count + 1));
            }

            int lane = 0; double bottom = Nodes.Max(n => n.Y + n.H);
            foreach (var e in Edges)
            {
                if (!e.IsBack)
                {
                    var p0 = new Point(e.Src.X + e.Src.W, e.GetSrcPort());
                    var p3 = new Point(e.Dst.X, e.GetDstPort());
                    double k = Math.Max(80, (p3.X - p0.X) * 0.6);
                    e.Samples = SampleBezier(p0, new Point(p0.X + k, p0.Y), new Point(p3.X - k, p3.Y), p3, 32);
                }
                else
                {
                    double laneY = bottom + 40 + lane * 26; lane++;
                    var p0 = new Point(e.Src.X + e.Src.W / 2, e.Src.Y + e.Src.H);
                    var p3 = new Point(e.Dst.X + e.Dst.W / 2, e.Dst.Y + e.Dst.H);
                    e.Samples = SampleBezier(p0, new Point(p0.X, laneY), new Point(p3.X, laneY), p3, 40);
                }
                var mid = e.Samples[e.Samples.Length / 2];
                e.LabelX = mid.X; e.LabelY = mid.Y;
            }
            double b2 = Nodes.Max(n => n.Y + n.H);
            Bounds = new Rect(0, 0, Nodes.Max(n => n.X + n.W) + LeftPad, b2 + TopPad + 60 + lane * 26);
        }

        public static Point[] SampleBezier(Point p0, Point p1, Point p2, Point p3, int n)
        {
            var pts = new Point[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double t = i / (double)n, u = 1 - t;
                pts[i] = new Point(
                    u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                    u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
            }
            return pts;
        }

        static double Clamp(double v, double a, double b)
        {
            if (v < a) return a;
            if (v > b) return b;
            return v; 
        }

        // ---------- трассировка цепочек ----------
        public HashSet<EdgeInfo> ChainEdges(NodeInfo start, bool downstream, bool upstream)
        {
            var set = new HashSet<EdgeInfo>();
            if (downstream) Walk(start, n => n.Out, set);
            if (upstream) Walk(start, n => n.In, set);
            return set;
        }
        static void Walk(NodeInfo start, Func<NodeInfo, List<EdgeInfo>> next, HashSet<EdgeInfo> set)
        {
            var q = new Queue<NodeInfo>(); q.Enqueue(start);
            var seen = new HashSet<NodeInfo> { start };
            while (q.Count > 0)
            {
                var n = q.Dequeue();
                foreach (var e in next(n))
                {
                    if (!set.Add(e)) continue;
                    var other = e.Src == n ? e.Dst : e.Src;
                    if (seen.Add(other)) q.Enqueue(other);
                }
            }
        }
    }
}
