using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace OpsViz.Infra
{
    public abstract class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
        protected void Raise(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RelayCommand : ICommand
    {
        readonly Action<object> _exec; readonly Func<object, bool> _can;
        public RelayCommand(Action<object> e, Func<object, bool> c = null) { _exec = e; _can = c; }
        public bool CanExecute(object o)
        {
            if (_can == null) return true;
            return _can(o);
        }
        public void Execute(object o)
        {
            _exec(o);
        }
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public static class Fmt
    {
        static readonly CultureInfo Ru = new CultureInfo("ru-RU");
        public static string Mass(double m)
        {
            if (Math.Abs(m) >= 1000) return m.ToString("#,0", Ru) + " т";
            if (Math.Abs(m) >= 10) return m.ToString("#,0.#", Ru) + " т";
            return m.ToString("#,0.###", Ru) + " т";
        }
        public static string Pct(double v)
        {
            return v.ToString("#,0.#", Ru) + "%";
        }
    }

    public static class ProductColors
    {
        static readonly string[] Palette = {
            "#E5484D","#2F7FD0","#3FA45B","#8E4EC6","#F76B15","#AD5700","#D6409F","#12A594",
            "#F5A524","#7C9BD6","#E78AC3","#A6D854","#C9A227","#22B8CF","#B37FEB","#8A94A6" };
        static readonly Dictionary<string, SolidColorBrush> Map =
            new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);
        static int _next;
        public static SolidColorBrush For(string product)
        {
            var p = string.IsNullOrWhiteSpace(product) ? "—" : product.Trim();
            lock (Map)
            {
                SolidColorBrush b;
                if (!Map.TryGetValue(p, out b))
                {
                    var c = (Color)ColorConverter.ConvertFromString(Palette[_next++ % Palette.Length]);
                    b = new SolidColorBrush(c); b.Freeze(); Map[p] = b;
                }
                return b;
            }
        }
    }

}
