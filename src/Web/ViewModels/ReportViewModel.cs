using Messenger.Application.Dtos;

namespace Messenger.Web.ViewModels
{
    /// <summary>หน้ารายงานสรุปงาน (Phase 5)</summary>
    public class ReportViewModel
    {
        public DailyReport Report { get; set; }

        /// <summary>ช่วงวันที่ที่กรอกในฟอร์ม (รูปแบบ ISO yyyy-MM-dd)</summary>
        public string DateFrom { get; set; }

        public string DateTo { get; set; }

        public string ErrorMessage { get; set; }

        /// <summary>ปุ่มลัดช่วงเวลาที่ใช้บ่อย</summary>
        public string TodayText { get; set; }

        public string WeekStartText { get; set; }

        public string MonthStartText { get; set; }
    }
}
