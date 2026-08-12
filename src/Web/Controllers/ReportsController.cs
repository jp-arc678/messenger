using System;
using System.Web.Mvc;
using Messenger.Application.Abstractions;
using Messenger.Application.Dtos;
using Messenger.Application.Services;
using Messenger.Web.ViewModels;

namespace Messenger.Web.Controllers
{
    /// <summary>
    /// รายงานสรุปงาน + export (Phase 5)
    ///
    /// ขอบเขตข้อมูลถูกจำกัดที่ service ตาม §5 + BR-6 — controller ไม่ตัดสินเอง
    /// User ก็เปิดหน้านี้ได้ แต่จะเห็นเฉพาะใบที่ตัวเองเป็นผู้แจ้ง
    /// </summary>
    public class ReportsController : BaseController
    {
        private readonly IReportService _reports;
        private readonly IReportExporter _exporter;
        private readonly IClock _clock;

        public ReportsController(IReportService reports, IReportExporter exporter, IClock clock)
        {
            _reports = reports ?? throw new ArgumentNullException(nameof(reports));
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        [HttpGet]
        public ActionResult Index(string dateFrom, string dateTo)
        {
            var query = BuildQuery(dateFrom, dateTo);
            var result = _reports.GetReport(CurrentUser, query);

            var model = new ReportViewModel
            {
                DateFrom = RequestFormViewModel.FormatDate(query.DateFrom.Value),
                DateTo = RequestFormViewModel.FormatDate(query.DateTo.Value),
                TodayText = RequestFormViewModel.FormatDate(_clock.Today),
                WeekStartText = RequestFormViewModel.FormatDate(StartOfWeek()),
                MonthStartText = RequestFormViewModel.FormatDate(StartOfMonth()),
                Report = result.Success ? result.Value : null,
                ErrorMessage = result.Success ? null : result.FirstError
            };

            // ช่วงวันที่ไม่ถูกต้อง — แสดงฟอร์มพร้อมข้อความ ไม่ต้องพาไปหน้า error
            if (!result.Success)
            {
                var fallback = _reports.GetReport(CurrentUser, new ReportQuery());
                model.Report = fallback.Success ? fallback.Value : null;
            }

            return View(model);
        }

        [HttpGet]
        public ActionResult Export(string dateFrom, string dateTo)
        {
            var result = _reports.GetReport(CurrentUser, BuildQuery(dateFrom, dateTo));

            if (!result.Success)
            {
                TempData["Error"] = result.FirstError;
                return RedirectToAction("Index", new { dateFrom, dateTo });
            }

            var content = _exporter.Export(result.Value);

            return File(content, _exporter.ContentType, _exporter.BuildFileName(result.Value));
        }

        // ---------------- helpers ----------------

        /// <summary>ค่าเริ่มต้นคือ "วันนี้" วันเดียว (D29)</summary>
        private ReportQuery BuildQuery(string dateFrom, string dateTo)
        {
            var from = RequestFormViewModel.ParseDate(dateFrom) ?? _clock.Today;
            var to = RequestFormViewModel.ParseDate(dateTo) ?? from;

            return new ReportQuery { DateFrom = from, DateTo = to };
        }

        /// <summary>วันจันทร์ของสัปดาห์ปัจจุบัน</summary>
        private DateTime StartOfWeek()
        {
            var today = _clock.Today;
            var offset = ((int)today.DayOfWeek + 6) % 7;   // จันทร์ = 0
            return today.AddDays(-offset);
        }

        private DateTime StartOfMonth()
        {
            var today = _clock.Today;
            return new DateTime(today.Year, today.Month, 1);
        }
    }
}
