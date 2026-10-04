using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OpsViz.Graph;
using OpsViz.Import;
using OpsViz.Infra;
using OpsViz.Model;

namespace OpsViz.ViewModels
{
    public class OpRow
    {
        public string TimeText { get; set; }
        public string TimeRange { get; set; }
        public string Counterpart { get; set; }
        public string ProductText { get; set; }
        public string MassText { get; set; }
        public string TagText { get; set; }
        public string SrcMassText { get; set; }
        public string DstMassText { get; set; }
        public string DeltaText { get; set; }
        public bool Diverged { get; set; }
        public bool NoData { get; set; }
        public bool Incoming { get; set; }
        public Operation Op { get; set; }
    }

    public class LegendItem
    {
        public Brush Brush { get; set; }
        public string Text { get; set; }
    }

    public class MainViewModel : NotifyBase
    {
        public const string AllProducts = "Все продукты";
        readonly HashSet<string> _hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int HiddenCount { get { return _hidden.Count; } }

        public void HideNode(NodeInfo n)
        {
            if (n == null) return;
            _hidden.Add(n.Name);
            SaveHidden();
            Rebuild();
        }

        public void ShowAllHidden()
        {
            if (_hidden.Count == 0) return;
            _hidden.Clear();
            SaveHidden();
            Rebuild();
        }

        public void HideAll()
        {
            if (_graph == null || _graph.Nodes.Count == 0) return;
            foreach (var node in _graph.Nodes) _hidden.Add(node.Name);
            SaveHidden();
            Rebuild();
        }

        static string HiddenFile()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "OpsViz");
                System.IO.Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "hidden_object.txt");
            }
            catch
            {
                return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hidden_object.txt");
            }
        }

        static string LegacyHiddenFile()
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hidden_object.txt");
        }

        void LoadHidden()
        {
            try
            {
                var p = HiddenFile();
                if (System.IO.File.Exists(p))
                {
                    foreach (var line in System.IO.File.ReadAllLines(p))
                    {
                        if (!string.IsNullOrWhiteSpace(line)) _hidden.Add(line.Trim());
                    }
                    return;
                }
                var legacy = LegacyHiddenFile();
                if (!string.Equals(legacy, p, StringComparison.OrdinalIgnoreCase)
                    && System.IO.File.Exists(legacy))
                {
                    foreach (var line in System.IO.File.ReadAllLines(legacy))
                    {
                        if (!string.IsNullOrWhiteSpace(line)) _hidden.Add(line.Trim());
                    }
                }
            }
            catch { }
        }

        void SaveHidden()
        {
            try
            {
                System.IO.File.WriteAllLines(HiddenFile(), new List<string>(_hidden));
            }
            catch { }
        }


        List<Operation> _ops = new List<Operation>();
        FlowGraph _graph;
        public FlowGraph Graph => _graph;

        public event Action GraphUpdated;
        public event Action SelectionChanged;
        public event Action OptionsChanged;

        string _periodText = ""; public string PeriodText
        {
            get { return _periodText; }
            set { Set(ref _periodText, value); }
        }
        string _statusText = "Готово"; public string StatusText
        {
            get { return _statusText; }
            set { Set(ref _statusText, value); }
        }

        List<string> _products = new List<string> { AllProducts };
    
    public List<string> Products
        {
            get { return _products; }
            set { Set(ref _products, value); }
        }
        string _selectedProduct = AllProducts;
        public string SelectedProduct { get { return _selectedProduct; } set { if (Set(ref _selectedProduct, value)) Rebuild(); } }

        DateTime? _dateFrom;
        public DateTime? DateFrom { get { return _dateFrom; } set { if (Set(ref _dateFrom, value)) Rebuild(); } }

        DateTime? _dateTo;
        public DateTime? DateTo { get { return _dateTo; } set { if (Set(ref _dateTo, value)) Rebuild(); } }

        public void ResetFilters()
        {
            _selectedProduct = AllProducts; Raise(nameof(SelectedProduct));
            Set(ref _dateFrom, null, "DateFrom");
            Set(ref _dateTo, null, "DateTo");
            Rebuild();
        }

        bool _showLabels = true;
        public bool ShowLabels { get { return _showLabels; } set { if (Set(ref _showLabels, value)) OptionsChanged?.Invoke(); } }

        bool _chainMode = true;
        public bool ChainMode { get { return _chainMode; } set { if (Set(ref _chainMode, value)) OptionsChanged?.Invoke(); } }

        bool _groupPairs;
        public bool GroupPairs { get { return _groupPairs; } set { if (Set(ref _groupPairs, value)) Rebuild(); } }

        string _thresholdText = "5";
        double _thresholdValid = 5.0;
        public string ThresholdText
        {
            get { return _thresholdText; }
            set
            {
                if (Set(ref _thresholdText, value))
                {
                    double v;
                    string s = (_thresholdText ?? "").Trim().Replace(',', '.');
                    if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v))
                    {
                        if (v < 0) v = 0;
                        if (v > 100) v = 100;
                        _thresholdValid = v;
                    }
                    OptionsChanged?.Invoke();
                    RefreshDiverged();
                    if (_selNode != null) FillNodeDetails(_selNode);
                    else if (_selEdge != null) FillEdgeDetails(_selEdge);
                }
            }
        }

        public double ThresholdPct
        {
            get { return _thresholdValid; }
        }

        // Расхождение по операции выше допуска + счетчики на узлах.
        // Допуск относится к паре источник/приемник внутри операции
        // (прибор vs изменение массы), а НЕ к разнице прихода/расхода узла:
        // остаток в резервуаре — это накопление, а не дисбаланс.
        void RefreshDiverged()
        {
            foreach (var o in _ops)
                o.Diverged = o.DeltaPct.HasValue && o.DeltaPct.Value > ThresholdPct;
            if (_graph == null) return;
            foreach (var n in _graph.Nodes)
            {
                int k = 0;
                foreach (var e in n.In) foreach (var o in e.Ops) if (o.Diverged) k++;
                foreach (var e in n.Out) foreach (var o in e.Ops) if (o.Diverged) k++;
                n.DivergedOps = k;
            }
        }

        public SchemaOptions Options => new SchemaOptions { ShowLabels = _showLabels, ChainMode = _chainMode, ImbalanceThresholdPct = ThresholdPct };

        // детали
        bool _isNodeDetails, _isEdgeDetails;
        public bool IsNodeDetails { get { return _isNodeDetails; } set { Set(ref _isNodeDetails, value); Raise(nameof(IsLegendVisible)); } }
        public bool IsEdgeDetails { get { return _isEdgeDetails; } set { Set(ref _isEdgeDetails, value); Raise(nameof(IsLegendVisible)); } }
        public bool IsLegendVisible => !_isNodeDetails && !_isEdgeDetails;

        string _detailsTitle = ""; public string DetailsTitle { get { return _detailsTitle; } set { Set(ref _detailsTitle, value); } }
        string _detailsStats = ""; public string DetailsStats { get { return _detailsStats; } set { Set(ref _detailsStats, value); } }

        public ObservableCollection<OpRow> IncomingRows { get; } = new ObservableCollection<OpRow>();
        public ObservableCollection<OpRow> OutgoingRows { get; } = new ObservableCollection<OpRow>();
        public ObservableCollection<Operation> EdgeOps { get; } = new ObservableCollection<Operation>();
        public ObservableCollection<LegendItem> Legend { get; } = new ObservableCollection<LegendItem>();

        bool _hasManualData;
        public bool HasManualData { get { return _hasManualData; } set { Set(ref _hasManualData, value); } }

        static bool HasManual(IEnumerable<Operation> ops)
        {
            foreach (var o in ops)
            {
                if (o.ManualSourceMass.HasValue && o.ManualSourceMass.Value >= 0) return true;
                if (o.ManualReceiverMass.HasValue && o.ManualReceiverMass.Value >= 0) return true;
            }
            return false;
        }

        NodeInfo _selNode; EdgeInfo _selEdge;
        public NodeInfo SelectedNode => _selNode;
        public EdgeInfo SelectedEdge => _selEdge;

        public System.Windows.Input.ICommand OpenFileCommand { get; }

        public MainViewModel()
        {
            OpenFileCommand = new RelayCommand(_ => OpenFile());
            UpdateFromDbCommand = new RelayCommand(_ => UpdateFromDb());
            LoadHidden();
            try
            {
                string f = "";
                if (System.IO.File.Exists(LastFilePath()))
                    f = System.IO.File.ReadAllText(LastFilePath()).Trim().Trim('"');
                if (f.Length > 0 && System.IO.File.Exists(f)) TryLoadOps(f, false);
                else if (f.Length > 0) StatusText = "Файл не найден: " + f;
            }
            catch (Exception ex) { StatusText = "Автозагрузка: " + ex.Message; }
        }

        static string LastFilePath()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "OpsViz");
                System.IO.Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "lastfile.txt");
            }
            catch
            {
                return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lastfile.txt");
            }
        }

        static void SaveLastFile(string path)
        {
            try { System.IO.File.WriteAllText(LastFilePath(), path); }
            catch { }
        }

        static string LastFileDir()
        {
            try
            {
                string last = System.IO.File.Exists(LastFilePath())
                    ? System.IO.File.ReadAllText(LastFilePath()).Trim() : "";
                if (last.Length > 0)
                {
                    string dir = System.IO.Path.GetDirectoryName(last);
                    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) return dir;
                }
            }
            catch { }
            return null;
        }

        void OpenFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Excel (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|Все файлы|*.*" };
            string dir = LastFileDir();
            if (dir != null) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog() != true) return;
            if (TryLoadOps(dlg.FileName, true)) SaveLastFile(dlg.FileName);
        }

        bool TryLoadOps(string path, bool showErrors)
        {
            List<Operation> ops;
            StatusText = "Чтение файла…";
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try { ops = ExcelOperationsImporter.Import(path); }
            catch (Exception ex)
            {
                if (showErrors)
                {
                    MessageBox.Show("Ошибка чтения файла:\n" + ex.Message, "OpsViz", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText = "Готово";
                }
                else StatusText = "Не удалось открыть файл: " + ex.Message;
                return false;
            }
            finally { System.Windows.Input.Mouse.OverrideCursor = null; }
            if (ops.Count == 0)
            {
                if (showErrors) MessageBox.Show("На листе 'Операции' не найдено операций.\nПроверьте заголовки: Название источника/приемника, Время начала/конца, Продукты, Источник/Приемник.", "OpsViz", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            FinishLoadOps(ops, "файл " + System.IO.Path.GetFileName(path));
            return true;
        }

        // Общий финиш загрузки из любого источника (файл / база): списки,
        // продукты, период, перестройка схемы. Источники не смешиваются:
        // каждая загрузка целиком заменяет операции, пометки сбрасываются
        // (живут на объектах), скрытия/фильтры действуют по именам.
        string _sourceLabel = "—";
        void FinishLoadOps(List<Operation> ops, string source)
        {
            _sourceLabel = source;
            _ops = ops;
            AllOps.Clear();
            foreach (var o in _ops.OrderBy(o => o.Start)) AllOps.Add(o);

            Products = new List<string> { AllProducts };
            Products.AddRange(_ops.SelectMany(o => new[] { o.SourceProduct, o.ReceiverProduct })
                .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p));
            _selectedProduct = AllProducts; Raise(nameof(SelectedProduct));

            if (_ops.Count > 0)
            {
                var starts = _ops.Select(o => o.Start).Where(d => d > DateTime.MinValue).ToList();
                var min = starts.Count > 0 ? starts.Min() : _ops.Min(o => o.Start);
                var max = _ops.Where(o => o.End.HasValue).Select(o => o.End.Value).DefaultIfEmpty(_ops.Max(o => o.Start)).Max();
                PeriodText = min.ToString("dd.MM.yy HH:mm") + " — " + max.ToString("dd.MM.yy HH:mm");
            }
            Rebuild();
        }

        static string DbConnectionPath()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "OpsViz");
                System.IO.Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "sql.txt");
            }
            catch
            {
                return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sql.txt");
            }
        }

        static string LoadDbConnectionString()
        {
            try
            {
                string p = DbConnectionPath();
                if (System.IO.File.Exists(p)) return SqlTransferLoader.NormalizeConnectionString(System.IO.File.ReadAllText(p));
            }
            catch { }
            return null;
        }

        bool _dbBusy;

        public System.Windows.Input.ICommand UpdateFromDbCommand { get; private set; }

        // Окно ввода строки подключения показывает View (у VM нет окон):
        // MainWindow назначает этот колбэк при старте. Возвращает готовую
        // нормализованную строку или null (отмена).
        public System.Func<string> PromptConnection { get; set; }

        // Кнопка «Обновить из базы»: тот же запрос, что скрипт выгрузки Excel,
        // период берется из верхних фильтров (по умолчанию — вчера/сегодня).
        // Выполняется в фоне, интерфейс не виснет (у скрипта таймаут 200с).
        async void UpdateFromDb()
        {
            if (_dbBusy) return;
            string cs = LoadDbConnectionString();
            if (string.IsNullOrWhiteSpace(cs))
            {
                if (PromptConnection == null) return;
                cs = PromptConnection();
                if (string.IsNullOrWhiteSpace(cs)) return;
                try { System.IO.File.WriteAllText(DbConnectionPath(), cs); }
                catch (Exception ex) { MessageBox.Show("Не удалось сохранить строку:\n" + ex.Message, "OpsViz", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
            DateTime from = (_dateFrom ?? DateTime.Today.AddDays(-1)).Date;
            DateTime to = (_dateTo ?? DateTime.Today).Date.AddDays(1).AddTicks(-1);
            _dbBusy = true;
            StatusText = "Загрузка из базы…";
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            List<Operation> ops = null;
            string err = null;
            try { ops = await Task.Run(() => SqlTransferLoader.Load(from, to, cs)); }
            catch (Exception ex) { err = ex.Message; }
            _dbBusy = false;
            System.Windows.Input.Mouse.OverrideCursor = null;
            if (err != null)
            {
                MessageBox.Show("Не удалось загрузить из базы:\n" + err +
                    "\n\nПроверьте сеть до SQL и строку в файле:\n" + DbConnectionPath(),
                    "OpsViz", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = "Готово";
                return;
            }
            if (ops.Count == 0)
            {
                MessageBox.Show("База вернула 0 операций за период " +
                    from.ToString("dd.MM.yy") + " — " + to.ToString("dd.MM.yy") + ".",
                    "OpsViz", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            FinishLoadOps(ops, "база " + from.ToString("dd.MM.yy") + "—" + to.ToString("dd.MM.yy"));
        }

        public ObservableCollection<Operation> AllOps { get; } = new ObservableCollection<Operation>();

        public event Action MarkedChanged;
        public event Action<EdgeInfo> GotoEdge;

        public void ToggleMarked(System.Collections.IList ops)
        {
            var list = new List<Operation>();
            foreach (var item in ops)
            {
                var o = item as Operation;
                if (o != null) list.Add(o);
            }
            if (list.Count == 0) return;
            bool target = list.Any(o => !o.Marked);
            foreach (var o in list) o.Marked = target;
            MarkedChanged?.Invoke();
        }

        public void GotoOperation(Operation o)
        {
            if (o == null || _ops.Count == 0) return;
            var edge = _graph != null ? _graph.Edges.FirstOrDefault(x => x.Ops.Contains(o)) : null;
            if (edge == null)
            {
                _hidden.Remove(o.SourceName);
                _hidden.Remove(o.ReceiverName);
                SaveHidden();
                _selectedProduct = AllProducts; Raise(nameof(SelectedProduct));
                Set(ref _dateFrom, null, "DateFrom");
                Set(ref _dateTo, null, "DateTo");
                Rebuild();
                edge = _graph != null ? _graph.Edges.FirstOrDefault(x => x.Ops.Contains(o)) : null;
            }
            if (edge == null) return;
            SelectEdge(edge);
            GotoEdge?.Invoke(edge);
        }

        public void Rebuild()
        {
            var opt = new GraphOptions
            {
                PerOperation = !_groupPairs,
                MinMass = 0,
                ProductFilter = _selectedProduct == AllProducts ? null : _selectedProduct,
                Hidden = _hidden,
                DateFrom = _dateFrom,
                DateTo = _dateTo
            };
            _graph = FlowGraph.Build(_ops, opt);
            RefreshDiverged();
            HasManualData = false;
            _selNode = null; _selEdge = null;
            IsNodeDetails = false; IsEdgeDetails = false;

            Legend.Clear();
            foreach (var p in _graph.Edges.SelectMany(e => e.Products).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Sum(x => x.Value)).Take(12))
                Legend.Add(new LegendItem { Brush = ProductColors.For(p.Key), Text = p.Key + " — " + Fmt.Mass(p.Sum(x => x.Value)) });

            StatusText = "Источник: " + _sourceLabel + "   Операций: " + _ops.Count + "   Объектов: " + _graph.Nodes.Count + "   Связей: " + _graph.Edges.Count;
            Infra.Logger.Log("Rebuild ops=" + _ops.Count + " hidden=" + _hidden.Count + " nodes=" + _graph.Nodes.Count + " edges=" + _graph.Edges.Count);
            GraphUpdated?.Invoke();
            SelectionChanged?.Invoke();
        }

        public void SelectNode(NodeInfo n)
        {
            if (n == null || (_selNode == n && _selEdge == null)) return;
            _selNode = n; _selEdge = null;
            FillNodeDetails(n);
            SelectionChanged?.Invoke();
        }

        public void SelectEdge(EdgeInfo e)
        {
            if (e == null || (_selEdge == e && _selNode == null)) return;
            _selEdge = e; _selNode = null;
            FillEdgeDetails(e);
            SelectionChanged?.Invoke();
        }

        // Только подсветить связь на схеме, не уходя с текущей таблицы.
        public void PreviewEdge(EdgeInfo e)
        {
            if (e == null) return;
            _selEdge = e; _selNode = null;
            SelectionChanged?.Invoke();
        }

        public void ClearSelection()
        {
            if (_selNode == null && _selEdge == null) return;
            _selNode = null; _selEdge = null;
            IsNodeDetails = false; IsEdgeDetails = false;
            SelectionChanged?.Invoke();
        }

        static string MassOrDash(double? m)
        {
            return m.HasValue ? Fmt.Mass(m.Value) : "—";
        }

        static string OpTimeRange(Operation o)
        {
            string s = o.Start > DateTime.MinValue ? o.Start.ToString("dd.MM HH:mm") : "?";
            if (!o.End.HasValue || o.End.Value <= o.Start) return s + " — ...";
            var f = o.End.Value;
            if (o.Start > DateTime.MinValue && o.Start.Date == f.Date)
                return o.Start.ToString("dd.MM HH:mm") + "–" + f.ToString("HH:mm");
            return s + "–" + f.ToString("dd.MM HH:mm");
        }

        static OpRow MakeRow(Operation o, bool incoming)
        {
            string delta = o.Delta.HasValue
                ? (o.Delta.Value >= 0 ? "+" : "") + o.Delta.Value.ToString("#,0.##", CultureInfo.InvariantCulture) + " т"
                : "—";
            if (o.DeltaPct.HasValue) delta += " (" + o.DeltaPct.Value.ToString("#,0.#", CultureInfo.InvariantCulture) + "%)";
            return new OpRow
            {
                TimeText = o.Start > DateTime.MinValue ? o.Start.ToString("dd.MM HH:mm") : "?",
                TimeRange = OpTimeRange(o),
                Counterpart = incoming ? o.SourceName : o.ReceiverName,
                ProductText = (incoming ? o.SourceProduct ?? o.ReceiverProduct : o.ReceiverProduct ?? o.SourceProduct) ?? "",
                MassText = incoming ? MassOrDash(o.ReceiverMass ?? o.SourceMass) : MassOrDash(o.SourceMass ?? o.ReceiverMass),
                TagText = incoming ? (o.SourceTag ?? "") : (o.ReceiverTag ?? ""),
                SrcMassText = MassOrDash(o.SourceMass),
                DstMassText = MassOrDash(o.ReceiverMass),
                DeltaText = delta,
                Diverged = o.Diverged,
                NoData = o.NoData,
                Incoming = incoming,
                Op = o
            };
        }

        void FillNodeDetails(NodeInfo n)
        {
            IsNodeDetails = true; IsEdgeDetails = false;
            DetailsTitle = n.Name;
            DetailsStats = "Приход: " + Fmt.Mass(n.InMass) + " (" + n.In.Count + " связ.)   Расход: " + Fmt.Mass(n.OutMass) + " (" + n.Out.Count + " связ.)";
            if (_hidden.Count > 0)
                DetailsStats += "\nВид сужен: показаны только видимые связи (скрыто объектов: " + _hidden.Count + ")";

            IncomingRows.Clear();
            foreach (var o in n.In.SelectMany(e => e.Ops).OrderBy(o => o.Start))
                IncomingRows.Add(MakeRow(o, true));
            OutgoingRows.Clear();
            foreach (var o in n.Out.SelectMany(e => e.Ops).OrderBy(o => o.Start))
                OutgoingRows.Add(MakeRow(o, false));
            HasManualData = HasManual(n.In.SelectMany(e => e.Ops)) || HasManual(n.Out.SelectMany(e => e.Ops));
        }

        void FillEdgeDetails(EdgeInfo e)
        {
            IsEdgeDetails = true; IsNodeDetails = false;
            DetailsTitle = e.Src.Name + "  →  " + e.Dst.Name;
            DetailsStats = "Масса: " + Fmt.Mass(e.Mass) + ", операций: " + e.Count + "\n" +
                string.Join("\n", e.Products.OrderByDescending(p => p.Value).Select(p => p.Key + ": " + Fmt.Mass(p.Value)));
            EdgeOps.Clear();
            foreach (var o in e.Ops.OrderBy(o => o.Start)) EdgeOps.Add(o);
            HasManualData = HasManual(e.Ops);
        }

        public bool IsViewNarrowed()
        {
            return _hidden.Count > 0 || _selectedProduct != AllProducts
                || _dateFrom.HasValue || _dateTo.HasValue;
        }

        // Полный граф по текущим фильтрам данных (без учета скрытых) —
        // база для фокуса/цепочек, чтобы из суженного вида было куда идти.
        FlowGraph BuildFullGraph()
        {
            var fullOpt = new GraphOptions
            {
                PerOperation = true,
                ProductFilter = _selectedProduct == AllProducts ? null : _selectedProduct,
                DateFrom = _dateFrom,
                DateTo = _dateTo,
                Hidden = null
            };
            return FlowGraph.Build(_ops, fullOpt);
        }

        public void ShowOnlyNode(NodeInfo n, bool withChain)
        {
            if (n == null) return;
            Infra.Logger.Log("ShowOnlyNode name=" + n.Name + " chain=" + withChain + " hiddenBefore=" + _hidden.Count);
            if (!withChain && _graph != null)
            {
                // «Только этот объект» — строгое сужение в пределах видимого:
                // ранее скрытое не воскрешаем, только добавляем скрытия.
                var cur = _graph.Nodes.FirstOrDefault(x => string.Equals(x.Name, n.Name, StringComparison.OrdinalIgnoreCase));
                if (cur != null)
                {
                    var keepVisible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    keepVisible.Add(cur.Name);
                    foreach (var e in cur.In) keepVisible.Add(e.Src.Name);
                    foreach (var e in cur.Out) keepVisible.Add(e.Dst.Name);
                    foreach (var node in _graph.Nodes)
                    {
                        if (!keepVisible.Contains(node.Name)) _hidden.Add(node.Name);
                    }
                    SaveHidden();
                    Rebuild();
                    return;
                }
            }
            // Цепочку считаем по ПОЛНОМУ графу. Здесь Clear корректен: скрытия
            // пересчитываются заново от полного набора имен.
            FlowGraph full;
            try { full = BuildFullGraph(); }
            catch { return; }
            var start = full.Nodes.FirstOrDefault(x => string.Equals(x.Name, n.Name, StringComparison.OrdinalIgnoreCase));
            if (start == null) return;
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            keep.Add(start.Name);
            if (withChain)
            {
                foreach (var e in full.ChainEdges(start, true, true)) { keep.Add(e.Src.Name); keep.Add(e.Dst.Name); }
            }
            else
            {
                foreach (var e in start.In) keep.Add(e.Src.Name);
                foreach (var e in start.Out) keep.Add(e.Dst.Name);
            }
            _hidden.Clear();
            foreach (var node in full.Nodes)
            {
                if (!keep.Contains(node.Name)) _hidden.Add(node.Name);
            }
            SaveHidden();
            Rebuild();
        }

        // Даблклик: перейти к объекту — он сам + ВСЕ его связи из полных
        // данных (включая сейчас скрытые) + вписать вид. Так ходят по цепочке
        // от соседа к соседу: из тупика всегда есть куда раскрыться.
        public void FocusNode(NodeInfo n)
        {
            if (n == null) return;
            Infra.Logger.Log("FocusNode name=" + n.Name + " hiddenBefore=" + _hidden.Count);
            FlowGraph full;
            try { full = BuildFullGraph(); }
            catch { return; }
            var start = full.Nodes.FirstOrDefault(x => string.Equals(x.Name, n.Name, StringComparison.OrdinalIgnoreCase));
            if (start == null) return;
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            keep.Add(start.Name);
            foreach (var e in start.In) keep.Add(e.Src.Name);
            foreach (var e in start.Out) keep.Add(e.Dst.Name);
            _hidden.Clear();
            foreach (var node in full.Nodes)
            {
                if (!keep.Contains(node.Name)) _hidden.Add(node.Name);
            }
            SaveHidden();
            Rebuild();
        }
    }
}
