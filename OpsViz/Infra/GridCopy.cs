using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OpsViz.Infra
{
    // Копирование значения текущей ячейки грида (для легких TextBlock-ячеек,
    // текст из которых мышью не выделить). Имена/теги выделяются мышью сами.
    public static class GridCopy
    {
        public static bool CopyCurrentCell(DataGrid grid)
        {
            try
            {
                var ci = grid.CurrentCell;
                if (grid == null || ci.Column == null) return false;
                int colIndex = grid.Columns.IndexOf(ci.Column);
                if (colIndex < 0) return false;
                var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(ci.Item);
                if (row == null) return false;
                var presenter = FindChild<DataGridCellsPresenter>(row);
                if (presenter == null) return false;
                var cell = (DataGridCell)presenter.ItemContainerGenerator.ContainerFromIndex(colIndex);
                if (cell == null) return false;
                string text = FindText(cell);
                if (string.IsNullOrEmpty(text)) return false;
                Clipboard.SetText(text);
                return true;
            }
            catch { return false; }
        }

        static string FindText(DependencyObject p)
        {
            var tb = p as TextBox;
            if (tb != null) return string.IsNullOrEmpty(tb.SelectedText) ? tb.Text : tb.SelectedText;
            var blk = p as TextBlock;
            if (blk != null) return blk.Text;
            int n = VisualTreeHelper.GetChildrenCount(p);
            for (int i = 0; i < n; i++)
            {
                string s = FindText(VisualTreeHelper.GetChild(p, i));
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return null;
        }

        // Правый клик в WPF не двигает текущую ячейку сам: без этого меню
        // копировало бы старую ячейку, а не ту, по которой кликнули.
        // Выделение при этом не трогаем (мультиселект для пометок цел).
        public static void SyncCurrentCell(DataGrid grid, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (grid == null) return;
                DependencyObject p = e.OriginalSource as DependencyObject;
                DataGridCell cell = null;
                while (p != null && cell == null)
                {
                    cell = p as DataGridCell;
                    if (cell == null && p is DataGrid) break;
                    p = VisualTreeHelper.GetParent(p);
                }
                if (cell == null || cell.Column == null) return;
                var item = cell.DataContext;
                if (item == null) return;
                grid.CurrentCell = new DataGridCellInfo(item, cell.Column);
            }
            catch { }
        }

        static T FindChild<T>(DependencyObject p) where T : DependencyObject
        {
            int n = VisualTreeHelper.GetChildrenCount(p);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(p, i);
                if (c is T) return (T)c;
                var r = FindChild<T>(c);
                if (r != null) return r;
            }
            return null;
        }

        // Даблклик имеет смысл только по строке данных (а не по шапке,
        // скроллбару или пустому месту — иначе сработает старое выделение).
        public static bool IsRowHit(System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                DependencyObject p = e.OriginalSource as DependencyObject;
                while (p != null)
                {
                    if (p is DataGridRow) return true;
                    if (p is DataGrid) return false;
                    p = VisualTreeHelper.GetParent(p);
                }
            }
            catch { }
            return false;
        }
    }
}
