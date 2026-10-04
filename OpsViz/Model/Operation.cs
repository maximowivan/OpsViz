using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpsViz.Model
{
    public class Operation : INotifyPropertyChanged
    {
        public string SourceName { get; set; }
        public string ReceiverName { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public string SourceProduct { get; set; }
        public string ReceiverProduct { get; set; }
        public double? SourceMass { get; set; }
        public double? ReceiverMass { get; set; }
        public string Uid { get; set; }
        public string SourceTag { get; set; }
        public string ReceiverTag { get; set; }
        // Ручной ввод из Excel как есть (для таблицы 1в1); в расчете масс
        // приоритет у ручного (>=0), см. ExcelOperationsImporter.
        public double? ManualSourceMass { get; set; }
        public double? ManualReceiverMass { get; set; }
        public string ShiftMasses { get; set; }
        public double? Delta => (SourceMass.HasValue && ReceiverMass.HasValue)
            ? ReceiverMass.Value - SourceMass.Value : (double?)null;
        // Расхождение измерения по операции, % от max: например прибор показал 25 т,
        // а по изменению массы резервуара вышло 30 т. null — данных нет с одной стороны.
        public double? DeltaPct
        {
            get
            {
                if (!SourceMass.HasValue || !ReceiverMass.HasValue) return null;
                double b = Math.Max(Math.Abs(SourceMass.Value), Math.Abs(ReceiverMass.Value));
                if (b <= 0) return null;
                return Math.Abs(ReceiverMass.Value - SourceMass.Value) / b * 100.0;
            }
        }
        // Нет данных хотя бы с одной стороны (ноль/пусто) — такие заявки разбирают отдельно.
        public bool NoData => !SourceMass.HasValue || !ReceiverMass.HasValue;
        // Выставляется пересчетом под текущий допуск (см. MainViewModel.RefreshDiverged).
        bool _diverged;
        public bool Diverged
        {
            get { return _diverged; }
            set { if (_diverged != value) { _diverged = value; Raise("Diverged"); } }
        }
        // Пометка пользователя в таблице всех операций (желтая строка).
        bool _marked;
        public bool Marked
        {
            get { return _marked; }
            set { if (_marked != value) { _marked = value; Raise("Marked"); } }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        void Raise(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
