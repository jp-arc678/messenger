using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Messenger.Application.Dtos;
using Messenger.Application.Services;

namespace Messenger.Infrastructure.Export
{
    /// <summary>
    /// สร้างไฟล์ Excel (.xlsx) ของรายงาน (Phase 5 · D30, D31)
    ///
    /// ไฟล์มี 2 ชีต :
    /// - "รายการใบงาน" : 1 แถว = 1 ใบงาน สำหรับเอาไป pivot ต่อเอง (D31)
    /// - "สรุป"        : ตัวเลขเดียวกับที่เห็นบนหน้าจอ ไว้ดูเร็ว ๆ
    ///
    /// ตัวเลขวันที่เขียนเป็น "ค่า date จริง" ไม่ใช่ข้อความ เพื่อให้ Excel เรียง/กรองได้
    /// และตั้งรูปแบบเป็น dd/MM/yyyy ตาม D19 (ค.ศ.)
    /// </summary>
    public class ExcelReportExporter : IReportExporter
    {
        private const string DateFormat = "dd/MM/yyyy";
        private const string DateTimeFormat = "dd/MM/yyyy HH:mm";

        public string ContentType =>
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        public string BuildFileName(DailyReport report)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            var range = report.DateFrom == report.DateTo
                ? report.DateFrom.ToString("yyyyMMdd")
                : report.DateFrom.ToString("yyyyMMdd") + "-" + report.DateTo.ToString("yyyyMMdd");

            return $"Messenger-Report-{report.BranchCode}-{range}.xlsx";
        }

        public byte[] Export(DailyReport report)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            using (var workbook = new XLWorkbook())
            {
                BuildDetailSheet(workbook, report);
                BuildSummarySheet(workbook, report);

                using (var buffer = new MemoryStream())
                {
                    workbook.SaveAs(buffer);
                    return buffer.ToArray();
                }
            }
        }

        private static void BuildDetailSheet(XLWorkbook workbook, DailyReport report)
        {
            var sheet = workbook.Worksheets.Add("รายการใบงาน");

            WriteTitle(sheet, report, columns: 13);

            var headers = new[]
            {
                "เลขใบงาน", "สาขา", "วันที่ส่ง", "ลำดับ", "วันที่บันทึก", "สถานะ",
                "ผู้แจ้ง", "หน่วยงาน", "ผู้รับงาน", "ประเภทงาน",
                "ผู้ติดต่อ", "เบอร์โทร", "ที่อยู่"
            };

            const int headerRow = 4;

            for (var i = 0; i < headers.Length; i++)
                sheet.Cell(headerRow, i + 1).Value = headers[i];

            var headerRange = sheet.Range(headerRow, 1, headerRow, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

            var row = headerRow + 1;

            foreach (var request in report.Requests)
            {
                sheet.Cell(row, 1).Value = request.ReqNo;
                sheet.Cell(row, 2).Value = request.BranchCode;

                sheet.Cell(row, 3).Value = request.SendDate;
                sheet.Cell(row, 3).Style.DateFormat.Format = DateFormat;

                if (request.SequenceOrder.HasValue)
                    sheet.Cell(row, 4).Value = request.SequenceOrder.Value;

                sheet.Cell(row, 5).Value = request.RequestDateTime;
                sheet.Cell(row, 5).Style.DateFormat.Format = DateTimeFormat;

                sheet.Cell(row, 6).Value = request.StatusDisplayName;
                sheet.Cell(row, 7).Value = request.RequesterName;
                sheet.Cell(row, 8).Value = request.RequesterUnitName;
                sheet.Cell(row, 9).Value = request.Assignment?.MessengerName;
                sheet.Cell(row, 10).Value = DescribeJobTypes(request);
                sheet.Cell(row, 11).Value = request.ContactName;

                // เบอร์โทรต้องเป็นข้อความ ไม่งั้น Excel จะกินศูนย์หน้าทิ้ง
                sheet.Cell(row, 12).SetValue(request.Phone ?? string.Empty);
                sheet.Cell(row, 12).Style.NumberFormat.Format = "@";

                sheet.Cell(row, 13).Value = request.Address;

                row++;
            }

            if (report.Requests.Count > 0)
            {
                sheet.Range(headerRow, 1, row - 1, headers.Length).CreateTable();
            }

            sheet.SheetView.FreezeRows(headerRow);
            sheet.Columns().AdjustToContents(5, 60);
        }

        private static void BuildSummarySheet(XLWorkbook workbook, DailyReport report)
        {
            var sheet = workbook.Worksheets.Add("สรุป");

            WriteTitle(sheet, report, columns: 7);

            var row = 4;

            row = WriteSection(sheet, row, "ภาพรวม");
            row = WriteKeyValue(sheet, row, "จำนวนใบงานทั้งหมด", report.TotalCount);
            row = WriteKeyValue(sheet, row, "งานของบริษัท", report.CompanyCount);
            row = WriteKeyValue(sheet, row, "งานฝากส่วนตัว", report.PersonalCount);
            row++;

            row = WriteSection(sheet, row, "แยกตามสถานะ");
            foreach (var status in report.ByStatus)
                row = WriteKeyValue(sheet, row, status.StatusDisplayName, status.Count);
            row++;

            if (report.ByMessenger.Count > 0)
            {
                row = WriteSection(sheet, row, "แยกตามเจ้าหน้าที่ Messenger");
                WriteRow(sheet, row++, true, "รหัส", "ชื่อ", "ทั้งหมด", "เสร็จแล้ว", "กำลังวิ่ง", "ยกเลิก");

                foreach (var messenger in report.ByMessenger)
                {
                    WriteRow(sheet, row++, false,
                        messenger.MessengerEmpCode, messenger.MessengerName,
                        messenger.Total, messenger.Completed, messenger.InProgress, messenger.Cancelled);
                }

                row++;
            }

            row = WriteSection(sheet, row, "แยกรายวัน (ตามวันที่ส่ง)");
            WriteRow(sheet, row++, true, "วันที่", "ทั้งหมด", "รับแจ้ง", "กำลังส่ง", "พักการส่ง", "เสร็จแล้ว", "ยกเลิก");

            foreach (var day in report.ByDay)
            {
                sheet.Cell(row, 1).Value = day.SendDate;
                sheet.Cell(row, 1).Style.DateFormat.Format = DateFormat;
                sheet.Cell(row, 2).Value = day.Total;
                sheet.Cell(row, 3).Value = day.Received;
                sheet.Cell(row, 4).Value = day.Delivering;
                sheet.Cell(row, 5).Value = day.Paused;
                sheet.Cell(row, 6).Value = day.Completed;
                sheet.Cell(row, 7).Value = day.Cancelled;
                row++;
            }

            sheet.Columns().AdjustToContents(5, 40);
        }

        private static void WriteTitle(IXLWorksheet sheet, DailyReport report, int columns)
        {
            sheet.Cell(1, 1).Value = "รายงานสรุปงานรับ-ส่งเอกสาร";
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 14;

            var range = report.DateFrom == report.DateTo
                ? report.DateFrom.ToString(DateFormat)
                : report.DateFrom.ToString(DateFormat) + " ถึง " + report.DateTo.ToString(DateFormat);

            sheet.Cell(2, 1).Value =
                $"สาขา {report.BranchCode} — {report.BranchName} · วันที่ส่ง {range} · " +
                (report.WholeBranch ? "ทั้งสาขา" : "เฉพาะใบที่ตนเองเป็นผู้แจ้ง");

            sheet.Range(1, 1, 1, columns).Merge();
            sheet.Range(2, 1, 2, columns).Merge();
        }

        private static int WriteSection(IXLWorksheet sheet, int row, string title)
        {
            sheet.Cell(row, 1).Value = title;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            return row + 1;
        }

        private static int WriteKeyValue(IXLWorksheet sheet, int row, string label, int value)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 2).Value = value;
            return row + 1;
        }

        private static void WriteRow(IXLWorksheet sheet, int row, bool bold, params object[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                var cell = sheet.Cell(row, i + 1);

                if (values[i] is int number)
                    cell.Value = number;
                else
                    cell.Value = values[i]?.ToString();

                cell.Style.Font.Bold = bold;
            }
        }

        private static string DescribeJobTypes(Messenger.Domain.Entities.DeliveryRequest request)
        {
            if (request.JobTypes == null || request.JobTypes.Count == 0)
                return string.Empty;

            return string.Join(", ", request.JobTypes.Select(j => j.JobTypeDisplayName));
        }
    }
}
