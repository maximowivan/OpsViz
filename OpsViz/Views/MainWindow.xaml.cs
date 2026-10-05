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
using OpsViz.Infra;
using OpsViz.Model;
using OpsViz.ViewModels;

namespace OpsViz.Views
{
    public partial class MainWindow : Window
    {
        readonly MainViewModel _vm = new MainViewModel();
        OperationsTableWindow _opsWin;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
            try
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                Title = "Схема потоков операций v" + v.ToString();
                Infra.Logger.Log("start v" + v.ToString());
            }
            catch { }

            _vm.GraphUpdated += () => RefreshSchema();

            _vm.SelectionChanged += () => { Schema.SetSelection(_vm.SelectedNode, _vm.SelectedEdge); ApplyManualVisibility(); };
            _vm.OptionsChanged += () => Schema.SetOptions(_vm.Options);

            Schema.NodeSelected += n => _vm.SelectNode(n);
            Schema.EdgeSelected += e => _vm.SelectEdge(e);
            Schema.SelectionCleared += () => _vm.ClearSelection();
            Schema.HideRequested += n => _vm.HideNode(n);
            Schema.ShowAllRequested += () => _vm.ShowAllHidden();
            Schema.OnlyRequested += (n, chain) => _vm.ShowOnlyNode(n, chain);
            Schema.FocusRequested += n => _vm.FocusNode(n);
            _vm.GotoEdge += e => { Schema.CenterOn(e); this.Activate(); };

            IncomingList.SelectionChanged += (s, e) => SelectOpRow(IncomingList.SelectedItem);
            OutgoingList.SelectionChanged += (s, e) => SelectOpRow(OutgoingList.SelectedItem);
            IncomingList.PreviewMouseRightButtonDown += GridRightDown;
            OutgoingList.PreviewMouseRightButtonDown += GridRightDown;
            EdgeOpsGrid.PreviewMouseRightButtonDown += GridRightDown;

            SearchBox.TextChanged += (s, e) => UpdateSearch();
            SearchBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) Schema.SelectFirstMatch();
                else if (e.Key == System.Windows.Input.Key.F3) Schema.SelectNextMatch();
            };

            ProductCombo.SelectionChanged += (s, e) => { var p = ProductCombo.SelectedItem as string; if (p != null && p != _vm.SelectedProduct) _vm.SelectedProduct = p; };
            
            this.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) _vm.ClearSelection(); };

            // VM могла автозагрузить файл раньше подписок — дотолкнуть схему сейчас.
            RefreshSchema();
        }

        void RefreshSchema()
        {
            Schema.Update(_vm.Graph, _vm.Options);
            if (ProductCombo.SelectedItem as string != _vm.SelectedProduct) ProductCombo.SelectedItem = _vm.SelectedProduct;
            BtnShowHidden.Visibility = _vm.HiddenCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnShowHidden.Content = "Показать скрытые (" + _vm.HiddenCount + ")";
            BtnHideAll.Visibility = _vm.Graph != null && _vm.Graph.Nodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void SelectOpRow(object item)
        {
            var row = item as OpRow;
            if (row == null || _vm.Graph == null) return;
            var edge = _vm.Graph.Edges.FirstOrDefault(x => x.Ops.Contains(row.Op));
            if (edge != null) _vm.PreviewEdge(edge);
        }

        void GridRow_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridCopy.IsRowHit(e) && _vm.SelectedEdge != null) _vm.SelectEdge(_vm.SelectedEdge);
        }

        void ApplyManualVisibility()
        {
            var v = _vm.HasManualData ? Visibility.Visible : Visibility.Collapsed;
            ManSrcIn.Visibility = v; ManDstIn.Visibility = v;
            ManSrcOut.Visibility = v; ManDstOut.Visibility = v;
            ManSrcEdge.Visibility = v; ManDstEdge.Visibility = v;
        }

        void UpdateSearch()
        {
            int n = Schema.HighlightSearch(SearchBox.Text);
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchCount.Visibility = Visibility.Collapsed;
            }
            else if (n > 0)
            {
                SearchCount.Text = "Найдено: " + n;
                SearchCount.Visibility = Visibility.Visible;
            }
            else
            {
                SearchCount.Text = _vm.IsViewNarrowed()
                    ? "Найдено: 0 (вид сужен — сбросьте фильтры/скрытия)"
                    : "Найдено: 0";
                SearchCount.Visibility = Visibility.Visible;
            }
        }

        void BtnFit_Click(object sender, RoutedEventArgs e) => Schema.Fit();

        void CopyCell_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var cm = mi != null ? mi.Parent as ContextMenu : null;
            var grid = cm != null ? cm.PlacementTarget as DataGrid : null;
            if (grid != null) GridCopy.CopyCurrentCell(grid);
        }

        void GridRightDown(object sender, MouseButtonEventArgs e)
        {
            GridCopy.SyncCurrentCell(sender as DataGrid, e);
        }

        void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            _vm.ResetFilters();
        }

        void BtnAllOps_Click(object sender, RoutedEventArgs e)
        {
            if (_opsWin == null)
            {
                _opsWin = new OperationsTableWindow(_vm) { Owner = this };
                _opsWin.Closed += (s, e2) => { _opsWin = null; };
            }
            _opsWin.Show();
            _opsWin.Activate();
        }

        private void BtnShowHidden_Click(object sender, RoutedEventArgs e)
        {
            _vm.ShowAllHidden();
        }

        private void BtnHideAll_Click(object sender, RoutedEventArgs e)
        {
            _vm.HideAll();
        }

        private void BtnLog_Click(object sender, RoutedEventArgs e)
        {
            Infra.Logger.OpenLog();
        }
    }
}
