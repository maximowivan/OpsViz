using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;
using ExcelDataReader;
using OpsViz.Model;

namespace OpsViz.Import
{
    public static class ExcelOperationsImporter
    {
        static readonly string[] Required = {
            "названиеисточника","названиеприемника","времяначала","времяконца",
            "продуктпоисточнику","продуктпоприемнику","источник","приемник" };

        static readonly string[] Optional = {
            "uidоперации",
            "тегисточника","тегприемника",
            "ручнойвводмассыист","ручнойвводмассыпр","ручнойвводмассыисточника","ручнойвводмассыприемника",
            "массыпосменам" };

        public static List<Operation> Import(string path)
        {
            var list = new List<Operation>();
            using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var rd = ExcelReaderFactory.CreateReader(fs))
            {
                bool sheetFound = false;
                do { if (Norm(rd.Name) == "операции") { sheetFound = true; break; } }
                while (rd.NextResult());
                if (!sheetFound) throw new InvalidDataException("Не найден лист 'Операции' в файле " + System.IO.Path.GetFileName(path));

                Dictionary<string, int> cols = null; int guard = 0;
                while (rd.Read())
                {
                    if (cols == null)
                    {
                        cols = TryMap(rd);
                        if (cols == null && ++guard > 60) throw new InvalidDataException("Не найдена строка заголовков на листе 'Операции'");
                        continue;
                    }
                    var op = ReadRow(rd, cols);
                    if (op != null) list.Add(op);
                }
            }
            return list;
        }

        static string Norm(string s)
        {
            if (s == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var c0 in s)
            {
                var ch = c0;
                if (ch == 'ё' || ch == 'Ё') ch = 'е';
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        static Dictionary<string, int> TryMap(IExcelDataReader rd)
        {
            var map = new Dictionary<string, int>();
            for (int i = 0; i < rd.FieldCount; i++)
            {
                var key = Norm(AsString(rd.GetValue(i)));
                if (key.Length > 0 && !map.ContainsKey(key)) map[key] = i;
            }
            foreach (var r in Required) if (!map.ContainsKey(r)) return null;
            var res = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Required) res[r] = map[r];
            foreach (var o in Optional) if (map.ContainsKey(o) && !res.ContainsKey(o)) res[o] = map[o];
            return res;
        }

        static string AsString(object v)
        {
            if (v == null) return null;
            string s = v as string;
            if (s != null) return s;
            if (v is DateTime) return ((DateTime)v).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            if (v is IFormattable) return ((IFormattable)v).ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }

        static bool TryCol(Dictionary<string, int> c, string name, out int idx)
        {
            return c.TryGetValue(name, out idx);
        }

        static Operation ReadRow(IExcelDataReader rd, Dictionary<string, int> c)
        {
            string src = AsString(rd.GetValue(c["названиеисточника"]));
            if (string.IsNullOrWhiteSpace(src)) return null;
            string dst = AsString(rd.GetValue(c["названиеприемника"])) ?? "";
            if (string.IsNullOrWhiteSpace(dst)) return null;
            double? autoSrc = SanMass(ToDouble(rd.GetValue(c["источник"])));
            double? autoDst = SanMass(ToDouble(rd.GetValue(c["приемник"])));
            // Ручной ввод действует только если > 0: в VBA NULL превращается в -1,
            // а для приемника по багу скрипта — в 0. Ноль вручную не вводят.
            double? manSrc = PosMass(GetOptDouble(rd, c, "ручнойвводмассыист", "ручнойвводмассыисточника"));
            double? manDst = PosMass(GetOptDouble(rd, c, "ручнойвводмассыпр", "ручнойвводмассыприемника"));
            var op = new Operation
            {
                SourceName = src.Trim(),
                ReceiverName = dst.Trim(),
                Start = ToDate(rd.GetValue(c["времяначала"])) ?? DateTime.MinValue,
                End = ToDate(rd.GetValue(c["времяконца"])),
                SourceProduct = Clean(AsString(rd.GetValue(c["продуктпоисточнику"]))),
                ReceiverProduct = Clean(AsString(rd.GetValue(c["продуктпоприемнику"]))),
                SourceMass = manSrc ?? autoSrc,
                ReceiverMass = manDst ?? autoDst,
                ManualSourceMass = GetOptDouble(rd, c, "ручнойвводмассыист", "ручнойвводмассыисточника"),
                ManualReceiverMass = GetOptDouble(rd, c, "ручнойвводмассыпр", "ручнойвводмассыприемника")
            };
            int uid;
            if (TryCol(c, "uidоперации", out uid)) op.Uid = Clean(AsString(rd.GetValue(uid)));
            int st;
            if (TryCol(c, "тегисточника", out st)) op.SourceTag = Clean(AsString(rd.GetValue(st)));
            int rt;
            if (TryCol(c, "тегприемника", out rt)) op.ReceiverTag = Clean(AsString(rd.GetValue(rt)));
            int sm;
            if (TryCol(c, "массыпосменам", out sm)) op.ShiftMasses = Clean(AsString(rd.GetValue(sm)));
            return op;
        }

        static double? SanMass(double? v)
        {
            if (!v.HasValue) return null;
            if (v.Value < 0) return null;
            return v;
        }

        static double? PosMass(double? v)
        {
            if (!v.HasValue) return null;
            if (v.Value <= 0) return null;
            return v;
        }

        static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            string t = s.Trim();
            return t.Length == 0 ? null : t;
        }

        static double? GetOptDouble(IExcelDataReader rd, Dictionary<string, int> c, params string[] names)
        {
            foreach (string n in names)
            {
                int idx;
                if (c.TryGetValue(n, out idx))
                {
                    double? v = ToDouble(rd.GetValue(idx));
                    if (v.HasValue) return v;
                }
            }
            return null;
        }

        static readonly CultureInfo Ru = new CultureInfo("ru-RU");

        static DateTime? ToDate(object v)
        {
            if (v == null) return null;
            if (v is DateTime) return (DateTime)v;
            if (v is double) { try { return DateTime.FromOADate((double)v); } catch { return null; } }
            if (v is float) { try { return DateTime.FromOADate((float)v); } catch { return null; } }
            if (v is decimal) { try { return DateTime.FromOADate((double)(decimal)v); } catch { return null; } }
            string s = (v as string) ?? v.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            DateTime p;
            if (DateTime.TryParse(s, Ru, DateTimeStyles.None, out p)) return p;
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out p)) return p;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out p)) return p;
            double oa;
            if (double.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out oa))
            {
                try { return DateTime.FromOADate(oa); } catch { }
            }
            return null;
        }

        static double? ToDouble(object v)
        {
            if (v == null) return null;
            if (v is double) return (double)v;
            if (v is float) return (float)v;
            if (v is decimal) return (double)(decimal)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            if (v is short) return (short)v;
            string s = (v as string) ?? v.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().Replace('\u00A0', ' ').Replace(" ", "");
            if (s.EndsWith("т") || s.EndsWith("Т")) s = s.Substring(0, s.Length - 1);
            s = s.Trim();
            if (s.Length == 0 || s == "-") return null;
            double p;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out p)) return p;
            if (double.TryParse(s, NumberStyles.Any, Ru, out p)) return p;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out p)) return p;
            string swapped = s.Contains(",") && !s.Contains(".") ? s.Replace(',', '.') : s.Replace('.', ',');
            if (double.TryParse(swapped, NumberStyles.Any, Ru, out p)) return p;
            if (double.TryParse(swapped, NumberStyles.Any, CultureInfo.InvariantCulture, out p)) return p;
            return null;
        }
    }
}
