using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Globalization;
using OpsViz.Graph;
using OpsViz.Infra;
using ShapesPath = System.Windows.Shapes.Path;
using OpsViz.Model;

namespace OpsViz.Controls
{
    public partial class FlowSchemaControl : UserControl
    {
        public event Action<NodeInfo> NodeSelected;
        public event Action<EdgeInfo> EdgeSelected;
        public event Action SelectionCleared;
        public event Action<NodeInfo> HideRequested;
        public event Action ShowAllRequested;
        public event Action<NodeInfo, bool> OnlyRequested;
        public event Action<NodeInfo> FocusRequested;

        FlowGraph _graph;
        SchemaOptions _opt = new SchemaOptions();
        NodeInfo _selNode; EdgeInfo _selEdge;
        Matrix _m = new Matrix(1, 0, 0, 1, 0, 0);

        readonly Dictionary<NodeInfo, NodeVisual> _nodeVis = new Dictionary<NodeInfo, NodeVisual>();
        readonly Dictionary<EdgeInfo, EdgeVisual> _edgeVis = new Dictionary<EdgeInfo, EdgeVisual>();
        List<NodeInfo> _searchMatches = new List<NodeInfo>();

        // pan / drag
        Point _panStart; bool _panning;
        NodeInfo _dragNode; Point _dragStart, _dragOrigin;

        class NodeVisual { public Grid Root; public Rectangle Body; public Rectangle Outline; public TextBlock Stats; }
        class EdgeVisual { public ShapesPath Line; public ShapesPath Hit; public ShapesPath Arrow; public Border LabelStart; public Border LabelEnd; }

        Point _downPos; bool _needFit;
        System.Windows.Media.Animation.Storyboard _pulse;

        public FlowSchemaControl()
        {
            InitializeComponent();
            Host.MouseWheel += OnWheel;
            Host.MouseLeftButtonDown += OnHostDown;
            Host.MouseMove += OnHostMove;
            Host.MouseLeftButtonUp += OnHostUp;
            Host.SizeChanged += (s, e) => { if (_needFit) { _needFit = false; Fit(); } };
        }
    

        // ================= публичное API =================
        public void Update(FlowGraph g, SchemaOptions opt)
        {
            _graph = g; _opt = opt;
            _selNode = null; _selEdge = null;
            _searchMatches.Clear();
            StopPulse();
            Cv.Children.Clear(); _nodeVis.Clear(); _edgeVis.Clear();
            Placeholder.Visibility = g == null || g.Nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (g == null) return;
            foreach (var e in g.Edges) CreateEdgeLine(e);   // сначала все линии
            foreach (var e in g.Edges) CreateEdgeLabels(e); // подписи поверх линий…
            foreach (var n in g.Nodes) CreateNode(n);       // …но под узлами
            LayoutLabels(); // раскладка подписей сразу, а не только после drag
            ApplyHighlight();
            _needFit = true;
            Fit();
        }

        public void SetOptions(SchemaOptions opt) { _opt = opt; ApplyHighlight(); }

        public void SetSelection(NodeInfo n, EdgeInfo e)
        {
            if (_selNode == n && _selEdge == e) return;
            _selNode = n; _selEdge = e; ApplyHighlight();
        }

        int _searchIndex = -1;

        public int HighlightSearch(string text)
        {
            StopPulse();
            _searchMatches.Clear();
            _searchIndex = -1;
            if (!string.IsNullOrWhiteSpace(text) && _graph != null)
                _searchMatches = _graph.Nodes
                    .Where(n => n.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(n => n.Name).ToList();
            foreach (var kv in _nodeVis)
                kv.Value.Outline.Visibility = _searchMatches.Contains(kv.Key) ? Visibility.Visible : Visibility.Collapsed;
            StartPulse();
            return _searchMatches.Count;
        }

        void StartPulse()
        {
            StopPulse();
            if (_searchMatches.Count == 0) return;
            _pulse = new System.Windows.Media.Animation.Storyboard
            {
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3)
            };
            foreach (var kv in _nodeVis)
            {
                if (!_searchMatches.Contains(kv.Key)) continue;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 0.25,
                    new Duration(TimeSpan.FromMilliseconds(450))) { AutoReverse = true };
                System.Windows.Media.Animation.Storyboard.SetTarget(anim, kv.Value.Outline);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(anim,
                    new PropertyPath("Opacity"));
                _pulse.Children.Add(anim);
            }
            _pulse.FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop;
            _pulse.Begin();
        }

        void StopPulse()
        {
            if (_pulse != null) { try { _pulse.Stop(); } catch { } _pulse = null; }
            if (_nodeVis != null)
                foreach (var kv in _nodeVis)
                    if (kv.Value.Outline != null) kv.Value.Outline.Opacity = 1;
        }

        public void SelectFirstMatch() { _searchIndex = -1; SelectNextMatch(); }

        public void SelectNextMatch()
        {
            if (_searchMatches.Count == 0) return;
            _searchIndex = (_searchIndex + 1) % _searchMatches.Count;
            var m = _searchMatches[_searchIndex];
            NodeSelected?.Invoke(m);
            CenterOnNode(m);
        }

        // Переход к узлу: центрируем и ставим читаемый масштаб, иначе
        // на общем виде найденный объект — пылинка и непонятно где он.
        public void CenterOnNode(NodeInfo n)
        {
            if (n == null) return;
            if (Host.ActualWidth < 10 || Host.ActualHeight < 10) return;
            double s = 1.0;
            var c = new Point(n.X + n.W / 2, n.Y + n.H / 2);
            _m = new Matrix(s, 0, 0, s,
                Host.ActualWidth / 2 - c.X * s,
                Host.ActualHeight / 2 - c.Y * s);
            _needFit = false;
            Cv.RenderTransform = new MatrixTransform(_m);
        }

        public void Fit()
        {
            if (_graph == null || Host.ActualWidth < 10 || Host.ActualHeight < 10) return;
            var b = _graph.Bounds;
            if (b.Width <= 0 || b.Height <= 0) return;
            double s = Math.Min(Host.ActualWidth / b.Width, Host.ActualHeight / b.Height);
            s = Math.Min(2.5, Math.Max(0.05, s));
            _m = new Matrix(s, 0, 0, s,
                -b.X * s + (Host.ActualWidth - b.Width * s) / 2,
                -b.Y * s + (Host.ActualHeight - b.Height * s) / 2);
            Cv.RenderTransform = new MatrixTransform(_m);
        }

        public void CenterOn(EdgeInfo e)
        {
            if (e == null || e.Samples == null || e.Samples.Length == 0) return;
            if (Host.ActualWidth < 10 || Host.ActualHeight < 10) return;
            var mid = e.Samples[e.Samples.Length / 2];
            var p = _m.Transform(mid);
            var c = new Point(Host.ActualWidth / 2, Host.ActualHeight / 2);
            _m.OffsetX += c.X - p.X;
            _m.OffsetY += c.Y - p.Y;
            _needFit = false;
            Cv.RenderTransform = new MatrixTransform(_m);
        }

        // ================= построение визуала =================
        void CreateEdgeLine(EdgeInfo e)
        {
            var geo = BuildGeometry(e.Samples);
            var brush = e.IsBack ? Brush("#DC2626") : ProductColors.For(e.MainProduct);
            var line = new ShapesPath { Data = geo, Stroke = brush, StrokeThickness = e.Thickness, Opacity = 0.85, IsHitTestVisible = false };
            if (e.IsBack) line.StrokeDashArray = new DoubleCollection { 4, 3 };
            var arrow = new ShapesPath { Data = BuildArrow(e), Fill = brush, Opacity = 0.85, IsHitTestVisible = false };
            var hit = new ShapesPath { Data = geo, Stroke = Brushes.Transparent, StrokeThickness = Math.Max(14, e.Thickness + 8), Cursor = Cursors.Hand };
            hit.ToolTip = EdgeTooltip(e);
            hit.MouseLeftButtonDown += (s, a) => { EdgeSelected?.Invoke(e); a.Handled = true; };
            Cv.Children.Add(line); Cv.Children.Add(arrow); Cv.Children.Add(hit);
            _edgeVis[e] = new EdgeVisual { Line = line, Arrow = arrow, Hit = hit };
        }

        void CreateEdgeLabels(EdgeInfo e)
        {
            EdgeVisual v;
            if (!_edgeVis.TryGetValue(e, out v)) return;
            var ls = MakeLabel(StartLabelText(e));
            var le = MakeLabel(EndLabelText(e));
            Cv.Children.Add(ls); Cv.Children.Add(le);
            v.LabelStart = ls; v.LabelEnd = le;
        }

        // ================= подписи без наезда на узлы =================
        // Позиция подписи зависит ТОЛЬКО от геометрии: выбор, подсветка, зум
        // и Esc ее никогда не двигают — прыгать нечему. Для каждой подписи
        // перебираем кандидатов вдоль ее линии и берем первую точку, где
        // прямоугольник подписи не задевает ни один узел и ни одну уже
        // положенную подпись. Короткие связи (соседние колонки) предпочитают
        // привычное место у порта (начальная — справа от источника, конечная —
        // слева от приемника), длинные и возвратные — середину линии.
        // Слои на холсте: линии -> подписи -> узлы (узлы — страховка сверху).
        // align: 0 = левый край у точки +6, 1 = правый край у точки -6, 2 = по центру.
        static readonly int[] StartNear = { 8, 16, 25, 38, 50 };
        static readonly int[] StartFar = { 25, 38, 12, 50, 62, 75 };
        static readonly int[] EndNear = { 92, 84, 75, 62, 50 };
        static readonly int[] EndFar = { 75, 62, 88, 50, 38, 25 };

        void LayoutLabels()
        {
            Rect[] blocks = _graph != null
                ? _graph.Nodes.Select(n => new Rect(n.X - 3, n.Y - 3, n.W + 6, n.H + 6)).ToArray()
                : new Rect[0];
            var placed = new List<Rect>();
            foreach (var kv in _edgeVis)
            {
                var e = kv.Key; var v = kv.Value;
                if (e.Samples == null || e.Samples.Length == 0) continue;
                bool adjacent = !e.IsBack && e.Dst.Layer == e.Src.Layer + 1;
                if (v.LabelStart != null) PlaceLabel(v.LabelStart, e, true, adjacent, blocks, placed);
                if (v.LabelEnd != null) PlaceLabel(v.LabelEnd, e, false, adjacent, blocks, placed);
            }
        }

        static void PlaceLabel(Border label, EdgeInfo e, bool atStart, bool adjacent, Rect[] blocks, List<Rect> placed)
        {
            double w = label.DesiredSize.Width, h = label.DesiredSize.Height;
            if (!(w > 0)) w = 60;
            if (!(h > 0)) h = 14;
            var pts = e.Samples;
            int last = pts.Length - 1;
            double bx = 0, by = 0;
            bool found = false;
            if (adjacent)
            {
                // Привычное место у порта; если занято — ползем вдоль линии.
                int[] pcts = atStart ? StartNear : EndNear;
                int edgeAlign = atStart ? 0 : 1;
                for (int k = -1; k < pcts.Length && !found; k++)
                {
                    double x, y;
                    if (k < 0)
                    {
                        var p = atStart ? pts[0] : pts[last];
                        if (edgeAlign == 0) { x = p.X + 6; y = p.Y - h / 2; }
                        else { x = p.X - 6 - w; y = p.Y - h / 2; }
                    }
                    else
                    {
                        int idx = pcts[k] * last / 100;
                        if (idx < 0) idx = 0;
                        if (idx > last) idx = last;
                        var p = pts[idx];
                        x = p.X - w / 2; y = p.Y - h / 2;
                    }
                    var r = new Rect(x, y, w, h);
                    if (!Hits(r, blocks) && !HitsPlaced(r, placed)) { bx = x; by = y; found = true; }
                }
            }
            else
            {
                int[] pcts = atStart ? StartFar : EndFar;
                for (int k = 0; k < pcts.Length && !found; k++)
                {
                    int idx = pcts[k] * last / 100;
                    if (idx < 0) idx = 0;
                    if (idx > last) idx = last;
                    var p = pts[idx];
                    var r = new Rect(p.X - w / 2, p.Y - h / 2, w, h);
                    if (!Hits(r, blocks) && !HitsPlaced(r, placed)) { bx = r.X; by = r.Y; found = true; }
                }
            }
            if (!found)
            {
                // Вдоль линии щелей нет — пробуем сдвиг перпендикулярно ей,
                // выше и ниже средней точки (линии тонкие, рядом часто свободно).
                var p = pts[last / 2];
                double cx = p.X - w / 2, cy = p.Y - h / 2;
                double[,] alts = { { cx, cy - h - 2 }, { cx, cy + h + 2 } };
                for (int a = 0; a < 2 && !found; a++)
                {
                    var r = new Rect(alts[a, 0], alts[a, 1], w, h);
                    if (!Hits(r, blocks) && !HitsPlaced(r, placed)) { bx = r.X; by = r.Y; found = true; }
                }
                if (!found) { bx = cx; by = cy; }
            }
            Canvas.SetLeft(label, bx);
            Canvas.SetTop(label, by);
            placed.Add(new Rect(bx, by, w, h));
        }

        static bool Hits(Rect r, Rect[] blocks)
        {
            for (int i = 0; i < blocks.Length; i++) if (r.IntersectsWith(blocks[i])) return true;
            return false;
        }

        static bool HitsPlaced(Rect r, List<Rect> placed)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                var q = placed[i];
                if (r.X < q.X + q.Width + 2 && q.X < r.X + r.Width + 2
                    && r.Y < q.Y + q.Height + 2 && q.Y < r.Y + r.Height + 2) return true;
            }
            return false;
        }

        void CreateNode(NodeInfo n)
        {
            var grid = new Grid { Width = n.W, Height = n.H, Cursor = Cursors.Hand };
            var outline = new Rectangle { RadiusX = 10, RadiusY = 10, Stroke = Brush("#EA580C"), StrokeThickness = 2.5, Margin = new Thickness(-4), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            var body = new Rectangle { RadiusX = 8, RadiusY = 8, Fill = FillFor(n), Stroke = StrokeFor(n), StrokeThickness = 1.2 };
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 3, 9, 3) };
            var name = new TextBlock { Text = n.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1E293B"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = n.Name };
            var stats = new TextBlock { Text = StatsText(n), FontSize = 9.5, Foreground = Brush("#64748B"), TextTrimming = TextTrimming.CharacterEllipsis };
            sp.Children.Add(name); sp.Children.Add(stats);
            grid.Children.Add(outline); grid.Children.Add(body); grid.Children.Add(sp);

            var menu = new ContextMenu();
            var miHide = new MenuItem { Header = "Спрятать объект" };
            miHide.Click += (s2, e2) => { if (HideRequested != null) HideRequested(n); };
            var miShow = new MenuItem { Header = "Показать все спрятанные" };
            var miOnly = new MenuItem { Header = "Показать только этот объект" };
            miOnly.Click += (s2, e2) => { if (OnlyRequested != null) OnlyRequested(n, false); };
            var miChainOnly = new MenuItem { Header = "Показать только цепочку этого объекта" };
            miChainOnly.Click += (s2, e2) => { if (OnlyRequested != null) OnlyRequested(n, true); };
            menu.Items.Add(miOnly);
            menu.Items.Add(miChainOnly);

            miShow.Click += (s2, e2) => { if (ShowAllRequested != null) ShowAllRequested(); };
            menu.Items.Add(miHide);
            menu.Items.Add(miShow);
            grid.ContextMenu = menu;

            Canvas.SetLeft(grid, n.X); Canvas.SetTop(grid, n.Y);
            grid.ToolTip = NodeTooltip(n);
            grid.MouseLeftButtonDown += (s, a) =>
            {
                // Даблклик — перейти к объекту: сфокусировать вид на нем
                // (он + все его связи из полных данных) и ВПИСАТЬ вид в окно.
                // Именно вписать, а не центрировать с фикс. зумом: соседи бывают
                // в дальних колонках и при 1.0 уходят за кадр.
                if (a.ClickCount >= 2)
                {
                    Infra.Logger.Log("dblclick node=" + n.Name);
                    FocusRequested?.Invoke(n);
                    NodeInfo fresh = null;
                    if (_graph != null)
                        foreach (var x in _graph.Nodes)
                            if (string.Equals(x.Name, n.Name, StringComparison.OrdinalIgnoreCase)) { fresh = x; break; }
                    if (fresh != null) NodeSelected?.Invoke(fresh);
                    Fit();
                    a.Handled = true;
                    return;
                }
                NodeSelected?.Invoke(n);
                _dragNode = n; _dragStart = ToContent(a.GetPosition(Host)); _dragOrigin = new Point(n.X, n.Y);
                Mouse.Capture(grid); a.Handled = true;
            };
            grid.MouseMove += (s, a) =>
            {
                if (_dragNode != n || a.LeftButton != MouseButtonState.Pressed) return;
                var p = ToContent(a.GetPosition(Host));
                n.X = _dragOrigin.X + (p.X - _dragStart.X);
                n.Y = _dragOrigin.Y + (p.Y - _dragStart.Y);
                _graph.Reroute(); SyncVisuals();
            };
            grid.MouseLeftButtonUp += (s, a) => { _dragNode = null; Mouse.Capture(null); };

            Cv.Children.Add(grid);
            _nodeVis[n] = new NodeVisual { Root = grid, Body = body, Outline = outline, Stats = stats };
        }

        void SyncVisuals()
        {
            foreach (var kv in _nodeVis) { Canvas.SetLeft(kv.Value.Root, kv.Key.X); Canvas.SetTop(kv.Value.Root, kv.Key.Y); }
            foreach (var kv in _edgeVis)
            {
                var e = kv.Key; var v = kv.Value;
                var geo = BuildGeometry(e.Samples);
                v.Line.Data = geo; v.Hit.Data = geo; v.Arrow.Data = BuildArrow(e);
            }
            LayoutLabels();
        }

        // ================= геометрия =================
        static Geometry BuildGeometry(Point[] pts)
        {
            var fig = new PathFigure { StartPoint = pts[0], IsClosed = false };
            fig.Segments.Add(new PolyLineSegment(new PointCollection(pts.Skip(1)), true));
            return new PathGeometry(new PathFigure[] { fig });
        }

        static Geometry BuildArrow(EdgeInfo e)
        {
            var pts = e.Samples;
            var tip = pts[pts.Length - 1];
            var prev = pts[pts.Length - 2];
            double dx = tip.X - prev.X;
            double dy = tip.Y - prev.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return Geometry.Empty;
            dx = dx / len;
            dy = dy / len;
            double px = -dy;
            double py = dx;
            double L = Math.Max(9, e.Thickness * 0.9 + 3);
            var t2 = new Point(tip.X - dx * 2, tip.Y - dy * 2);
            var b = new Point(t2.X - dx * L, t2.Y - dy * L);
            var p1 = new Point(b.X + px * L * 0.45, b.Y + py * L * 0.45);
            var p2 = new Point(b.X - px * L * 0.45, b.Y - py * L * 0.45);
            var fig = new PathFigure { StartPoint = t2, IsClosed = true };
            fig.Segments.Add(new PolyLineSegment(new PointCollection { p1, p2 }, true));
            return new PathGeometry(new PathFigure[] { fig });
        }

        Border MakeLabel(string text)
        {
            var tb = new TextBlock { Text = text, FontSize = 9.5, Foreground = Brush("#1E293B") };
            var b = new Border { Background = Brush("#FFFFFFFF"), BorderBrush = Brush("#CBD5E1"), BorderThickness = new Thickness(0.8), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1), Child = tb, IsHitTestVisible = false };
            b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return b;
        }

        // ================= подсветка / цепочки =================
        void ApplyHighlight()
        {
            HashSet<EdgeInfo> hi = null; HashSet<NodeInfo> hiN = null;
            if (_selEdge != null)
            {
                hi = new HashSet<EdgeInfo> { _selEdge };
                hiN = new HashSet<NodeInfo> { _selEdge.Src, _selEdge.Dst };
                if (_opt.ChainMode && _graph != null)
                {
                    foreach (var e in _graph.ChainEdges(_selEdge.Dst, true, false)) { hi.Add(e); hiN.Add(e.Src); hiN.Add(e.Dst); }
                    foreach (var e in _graph.ChainEdges(_selEdge.Src, false, true)) { hi.Add(e); hiN.Add(e.Src); hiN.Add(e.Dst); }
                }
            }
            else if (_selNode != null)
            {
                hiN = new HashSet<NodeInfo> { _selNode };
                hi = _graph == null ? new HashSet<EdgeInfo>()
                    : _opt.ChainMode ? _graph.ChainEdges(_selNode, true, true)
                                     : new HashSet<EdgeInfo>(_selNode.In.Concat(_selNode.Out));
                foreach (var e in hi) { hiN.Add(e.Src); hiN.Add(e.Dst); }
            }

            foreach (var kv in _edgeVis)
            {
                bool on = hi == null || hi.Contains(kv.Key);
                kv.Value.Line.Opacity = on ? 0.92 : 0.08;
                kv.Value.Arrow.Opacity = on ? 0.92 : 0.08;
                kv.Value.Line.StrokeThickness = kv.Key.Thickness * (hi != null && on ? 1.15 : 1.0);
                // kv.Value.Label.Visibility = on && _opt.ShowLabels ? Visibility.Visible : Visibility.Collapsed;
                var vis = on && _opt.ShowLabels ? Visibility.Visible : Visibility.Collapsed;
                kv.Value.LabelStart.Visibility = vis;
                kv.Value.LabelEnd.Visibility = vis;
            }
            foreach (var kv in _nodeVis)
            {
                bool on = hiN == null || hiN.Contains(kv.Key);
                kv.Value.Root.Opacity = on ? 1 : 0.22;
                bool sel = kv.Key == _selNode || kv.Key == (_selEdge != null ? _selEdge.Src : null) || kv.Key == (_selEdge != null ? _selEdge.Dst : null);
                kv.Value.Body.StrokeThickness = sel ? 2.4 : 1.2;
                kv.Value.Body.Stroke = sel ? Brush("#EA580C") : StrokeFor(kv.Key);
            }
        }

        // ================= зум / пан =================
        void OnWheel(object s, MouseWheelEventArgs e)
        {
            _needFit = false;
            double k = e.Delta > 0 ? 1.2 : 1 / 1.2;
            var p = e.GetPosition(Host);
            _m = new Matrix(_m.M11 * k, 0, 0, _m.M22 * k,
                p.X - k * (p.X - _m.OffsetX), p.Y - k * (p.Y - _m.OffsetY));
            Cv.RenderTransform = new MatrixTransform(_m);
            e.Handled = true;
        }

        void OnHostDown(object s, MouseButtonEventArgs e)
        {
            _needFit = false;
            _panning = true; _panStart = e.GetPosition(Host); _downPos = _panStart;
            Mouse.Capture(Host);
        }
        void OnHostMove(object s, MouseEventArgs e)
        {
            if (!_panning || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(Host);
            _m.OffsetX += p.X - _panStart.X; _m.OffsetY += p.Y - _panStart.Y;
            _panStart = p;
            Cv.RenderTransform = new MatrixTransform(_m);
        }
        void OnHostUp(object s, MouseButtonEventArgs e)
        {
            _panning = false;
            Mouse.Capture(null);
            var p = e.GetPosition(Host);
            double dx = p.X - _downPos.X, dy = p.Y - _downPos.Y;
            bool click = dx * dx + dy * dy < 9;
            if (click && (e.OriginalSource == Cv || e.OriginalSource == Host)) SelectionCleared?.Invoke();
            else if (e.ClickCount == 2 && (e.OriginalSource == Cv || e.OriginalSource == Host)) SelectionCleared?.Invoke();
        }
        
        Point ToContent(Point hostPt) =>
            new Point((hostPt.X - _m.OffsetX) / _m.M11, (hostPt.Y - _m.OffsetY) / _m.M22);

        // ================= тексты / кисти =================
        static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

        static Brush FillFor(NodeInfo n) =>
            n.IsPureSource ? Brush("#DFF2E1") : n.IsPureSink ? Brush("#E3EDFB") : Brush("#FFFFFF");
        static Brush StrokeFor(NodeInfo n) =>
            n.IsPureSource ? Brush("#16A34A") : n.IsPureSink ? Brush("#2563B8") : Brush("#CBD5E1");

        string StatsText(NodeInfo n)
        {
            if (n.IsPureSource) return "расход " + Fmt.Mass(n.OutMass);
            if (n.IsPureSink) return "приход " + Fmt.Mass(n.InMass);
            return "приход " + Fmt.Mass(n.InMass) + " → расход " + Fmt.Mass(n.OutMass);
        }

        //static string LabelText(EdgeInfo e) => Fmt.Mass(e.Mass) + (e.Count > 1 ? " ×" + e.Count : "");

        static string TimeRangeText(Operation o)
        {
            var s = o.Start;
            var f = o.End;
            if (f == null || f.Value <= s) return s.ToString("HH:mm" + "-...");
            if (s.Date == f.Value.Date) return s.ToString("HH:mm") + "-" + f.Value.ToString("HH:mm");
            return s.ToString("dd.MM HH:mm") + "-" + f.Value.ToString("dd.MM HH:mm");
        }

        string StartLabelText(EdgeInfo e)
        {
            if (e.Ops.Count == 1)
            {
                var o = e.Ops[0];
                return Fmt.Mass(o.SourceMass ?? e.Mass) + " · " + TimeRangeText(o);
            }
            return Fmt.Mass(e.Mass) + (e.Count > 1 ? "  ×" + e.Count : "");
        }

        string EndLabelText(EdgeInfo e)
        {
            if (e.Ops.Count == 1)
            {
                var o = e.Ops[0];
                return Fmt.Mass(o.ReceiverMass ?? o.SourceMass ?? e.Mass) + " · " + TimeRangeText(o);
            }
            return Fmt.Mass(e.Mass) + (e.Count > 1 ? "  ×" + e.Count : "");
        }
        static string NodeTooltip(NodeInfo n) =>
            n.Name + "\n" +
            "Приход: " + Fmt.Mass(n.InMass) + " (" + n.In.Count + " связ.)\n" +
            "Расход: " + Fmt.Mass(n.OutMass) + " (" + n.Out.Count + " связ.)";

        static string EdgeTooltip(EdgeInfo e)
        {
            var sb = new StringBuilder();
            sb.AppendLine(e.Src.Name + "  →  " + e.Dst.Name);
            sb.AppendLine("Масса: " + Fmt.Mass(e.Mass) + ", операций: " + e.Count);
            foreach (var p in e.Products.OrderByDescending(x => x.Value))
                sb.AppendLine("   " + p.Key + ": " + Fmt.Mass(p.Value));
            if (e.Ops.Count > 0)
            {
                var min = e.Ops.Min(o => o.Start);
                var maxEnd = e.Ops.Where(o => o.End.HasValue).Select(o => o.End.Value).DefaultIfEmpty(min).Max();
                sb.AppendLine(min.ToString("dd.MM.yy HH:mm", CultureInfo.InvariantCulture) + " — " + maxEnd.ToString("dd.MM.yy HH:mm", CultureInfo.InvariantCulture));
            }
            if (e.IsBack) sb.AppendLine("(возвратный / рециркуляционный поток)");
            return sb.ToString();
        }
    }
}
