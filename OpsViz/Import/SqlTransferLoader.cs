using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using OpsViz.Model;

namespace OpsViz.Import
{
    // Прямая выгрузка операций из InduSoft I-OMS (iomsdb.dbo/oms.Transfer) —
    // тот же запрос и тот же маппинг, что кнопка «Операции» в Excel
    // (Лист1.CommandButton1_Click), но без Excel: параметризованный SQL,
    // понятные ошибки подключения. VBA для сравнения лежит в разборе xlsm.
    public static class SqlTransferLoader
    {
        public const int CommandTimeoutSec = 200;

        public static void TestConnection(string connectionString)
        {
            using (var c = new SqlConnection(connectionString))
            {
                c.Open();
            }
        }

        // VBA хранит OLEDB-строку "Provider=SQLOLEDB;...". SqlClient такой
        // префикс не переваривает — вырезаем, остальное (Data Source,
        // Initial Catalog, Integrated Security / User ID) понимает как есть.
        public static string NormalizeConnectionString(string raw)
        {
            string s = (raw ?? "").Trim();
            int i = s.IndexOf("Provider=", StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                int end = s.IndexOf(';', i);
                if (end >= 0) s = (s.Substring(0, i) + s.Substring(end + 1)).TrimStart(' ', ';');
            }
            return s.Trim();
        }

        public static List<Operation> Load(DateTime from, DateTime to, string connectionString)
        {
            const string sql =
                "SELECT oms.Transfer.TransferUID, oms.Transfer.StartTime, oms.Transfer.EndTime, " +
                "ObjectSource.ObjectName AS SourceName, ObjectDestination.ObjectName AS DestinationName, " +
                "ProductSource.ProductName AS SourceProduct, ProductDestination.ProductName AS DestProduct, " +
                "oms.Transfer.SourceFlow, oms.Transfer.DestFlow, " +
                "oms.Transfer.SourceMass, oms.Transfer.DestinationMass, oms.Transfer.ShiftMasses, " +
                "oms.Transfer.SourceTagMass, oms.Transfer.DestTagMass, " +
                "oms.Transfer.SourceMassManualInput, oms.Transfer.DestinationMassManualInput " +
                "FROM oms.Transfer " +
                "INNER JOIN oms.Object AS ObjectSource ON oms.Transfer.SourceUID = ObjectSource.ObjectUID " +
                "INNER JOIN oms.Object AS ObjectDestination ON oms.Transfer.DestinationUID = ObjectDestination.ObjectUID " +
                "LEFT OUTER JOIN oms.Product AS ProductDestination ON oms.Transfer.DestProductUID = ProductDestination.ProductUID " +
                "LEFT OUTER JOIN oms.Product AS ProductSource ON oms.Transfer.SourceProductUID = ProductSource.ProductUID " +
                "WHERE (ObjectSource.ObjectName <> ObjectDestination.ObjectName) " +
                "AND oms.Transfer.StartTime >= @startTime AND oms.Transfer.StartTime <= @endTime " +
                "AND (oms.Transfer.EndTime <= @endTime OR oms.Transfer.EndTime IS NULL) " +
                "ORDER BY oms.Transfer.StartTime, oms.Transfer.EndTime";
            var list = new List<Operation>();
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = CommandTimeoutSec;
                    cmd.Parameters.Add("@startTime", SqlDbType.DateTime).Value = from;
                    cmd.Parameters.Add("@endTime", SqlDbType.DateTime).Value = to;
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var op = Map(r);
                            if (op != null) list.Add(op);
                        }
                    }
                }
            }
            return list;
        }

        static string Str(object v)
        {
            if (v == null || v is DBNull) return "";
            return Convert.ToString(v);
        }

        static Operation Map(IDataRecord r)
        {
            string sName = Str(r["SourceName"]);
            string dName = Str(r["DestinationName"]);
            if (string.IsNullOrWhiteSpace(sName) || string.IsNullOrWhiteSpace(dName)) return null;

            string prodSrc = Str(r["SourceProduct"]);
            string flowSrc = Str(r["SourceFlow"]);
            string prodDst = Str(r["DestProduct"]);
            string flowDst = Str(r["DestFlow"]);

            // VBA: NULL масс -> 0, NULL ручного ввода -> -1.
            double srcMass = r["SourceMass"] == null || r["SourceMass"] is DBNull ? 0 : Convert.ToDouble(r["SourceMass"]);
            double dstMass = r["DestinationMass"] == null || r["DestinationMass"] is DBNull ? 0 : Convert.ToDouble(r["DestinationMass"]);
            double srcMan = r["SourceMassManualInput"] == null || r["SourceMassManualInput"] is DBNull
                ? -1 : Convert.ToDouble(r["SourceMassManualInput"]);
            // БАГ скрипта (Лист1, строки ~329-333): при NULL DestinationMassManualInput
            // в колонку ложится 0 (Double по умолчанию). Повторяем 1в1 с Excel,
            // иначе цифры OpsViz и Excel разойдутся. Ноль как ручной ввод все равно
            // игнорируется правилом > 0 (см. ExcelOperationsImporter.PosMass).
            object dstManRaw = r["DestinationMassManualInput"];
            double dstMan = dstManRaw == null || dstManRaw is DBNull ? 0 : Convert.ToDouble(dstManRaw);

            object startRaw = r["StartTime"];
            object endRaw = r["EndTime"];
            // Приоритет и чистка — 1в1 как в Excel-пути (SanMass/PosMass):
            // авто: отрицательные -> нет данных; ручной: действует только если > 0.
            double? aSrc = srcMass < 0 ? (double?)null : srcMass;
            double? aDst = dstMass < 0 ? (double?)null : dstMass;
            double? mSrc = srcMan > 0 ? srcMan : (double?)null;
            double? mDst = dstMan > 0 ? dstMan : (double?)null;
            var op = new Operation
            {
                SourceName = sName.Trim(),
                ReceiverName = dName.Trim(),
                Start = startRaw == null || startRaw is DBNull ? DateTime.MinValue : Convert.ToDateTime(startRaw),
                End = endRaw == null || endRaw is DBNull ? (DateTime?)null : Convert.ToDateTime(endRaw),
                SourceProduct = flowSrc.Length > 0 ? prodSrc + ". " + flowSrc : prodSrc,
                ReceiverProduct = flowDst.Length > 0 ? prodDst + ". " + flowDst : prodDst,
                SourceMass = mSrc ?? aSrc,
                ReceiverMass = mDst ?? aDst,
                ManualSourceMass = srcMan,
                ManualReceiverMass = dstMan,
                Uid = Str(r["TransferUID"]),
                SourceTag = Str(r["SourceTagMass"]),
                ReceiverTag = Str(r["DestTagMass"]),
                ShiftMasses = Str(r["ShiftMasses"])
            };
            if (string.IsNullOrWhiteSpace(op.SourceProduct)) op.SourceProduct = null;
            if (string.IsNullOrWhiteSpace(op.ReceiverProduct)) op.ReceiverProduct = null;
            if (string.IsNullOrWhiteSpace(op.Uid)) op.Uid = null;
            if (string.IsNullOrWhiteSpace(op.SourceTag)) op.SourceTag = null;
            if (string.IsNullOrWhiteSpace(op.ReceiverTag)) op.ReceiverTag = null;
            if (string.IsNullOrWhiteSpace(op.ShiftMasses)) op.ShiftMasses = null;
            return op;
        }
    }
}
