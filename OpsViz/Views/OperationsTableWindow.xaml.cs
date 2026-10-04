using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using OpsViz.Infra;
using OpsViz.Model;
using OpsViz.ViewModels;

namespace OpsViz.Views
{
    public partial class OperationsTableWindow : Window
    {
        readonly MainViewModel _vm;
        readonly System.Windows.Threading.DispatcherTimer _filterTimer;

        public OperationsTableWindow(MainViewModel vm)
        {
            _vm = vm;
            InitializeComponent();
            DataContext = _vm;
            UpdateCount();
            // Фильтр применяется с задержкой после последней буквы,
            // чтобы таблица не перестраивалась на каждое нажатие.
            _filterTimer = new System.Windows.Threading.DispatcherTimer();
            _filterTimer.Interval = System.TimeSpan.FromMilliseconds(250);
            _filterTimer.Tick += (s, e) => { _filterTimer.Stop(); ApplyFilter(); };
            FilterBox.TextChanged += (s, e) => { _filterTimer.Stop(); _filterTimer.Start(); };
            OpsGrid.PreviewMouseRightButtonDown += (s, e) => GridCopy.SyncCurrentCell(OpsGrid, e);
            _vm.MarkedChanged += RefreshView;
            Closed += (s, e) => { _vm.MarkedChanged -= RefreshView; _filterTimer.Stop(); };
        }

        void RefreshView()
        {
            // Пометки обновляются сами через INPC операции — полный Refresh не нужен.
            UpdateCount();
        }

        void ApplyFilter()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_vm.AllOps);
            if (view == null) return;
            string t = (FilterBox.Text ?? "").Trim();
            bool onlyMarked = MarkedOnlyBtn.IsChecked == true;
            if (t.Length == 0 && !onlyMarked) view.Filter = null;
            else view.Filter = o =>
            {
                var op = o as Operation;
                if (op == null) return false;
                if (onlyMarked && !op.Marked) return false;
                return t.Length == 0 || Match(op, t);
            };
            UpdateCount();
        }

        void MarkedOnly_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        static bool Match(Operation o, string t)
        {
            if (o == null) return false;
            return Contains(o.SourceName, t) || Contains(o.ReceiverName, t)
                || Contains(o.SourceProduct, t) || Contains(o.ReceiverProduct, t)
                || Contains(o.SourceTag, t) || Contains(o.ReceiverTag, t)
                || Contains(o.Uid, t);
        }

        static bool Contains(string s, string t)
        {
            return !string.IsNullOrEmpty(s) && s.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void UpdateCount()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_vm.AllOps);
            int shown = 0, total = _vm.AllOps.Count;
            if (view != null) foreach (var _ in view) shown++;
            int marked = _vm.AllOps.Count(o => o.Marked);
            CountText.Text = "Показано: " + shown + " / " + total + (marked > 0 ? "   Помечено: " + marked : "");
        }

        void BtnMark_Click(object sender, RoutedEventArgs e)
        {
            _vm.ToggleMarked(OpsGrid.SelectedItems);
        }

        void Goto_Click(object sender, RoutedEventArgs e)
        {
            GoSelected();
        }

        void OpsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridCopy.IsRowHit(e)) GoSelected();
        }

        void GoSelected()
        {
            var o = OpsGrid.SelectedItem as Operation;
            if (o != null) _vm.GotoOperation(o);
        }

        void CopyCell_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var cm = mi != null ? mi.Parent as ContextMenu : null;
            var grid = cm != null ? cm.PlacementTarget as DataGrid : null;
            if (grid != null) GridCopy.CopyCurrentCell(grid);
        }
    }
}
