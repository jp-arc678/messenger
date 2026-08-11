using System;
using Messenger.Domain.Enums;

namespace Messenger.Domain.Entities
{
    /// <summary>
    /// 1 บรรทัดของ audit trail การเปลี่ยนสถานะ (§6 — ทุก transition ต้องบันทึก)
    ///
    /// <see cref="FromStatus"/> เป็น null เฉพาะบรรทัดแรกสุด คือตอนสร้างใบงาน
    /// </summary>
    public class StatusHistoryEntry
    {
        public long HistoryId { get; set; }

        public int ReqId { get; set; }

        public RequestStatus? FromStatus { get; set; }

        public RequestStatus ToStatus { get; set; }

        public string ByEmpCode { get; set; }

        public string ByName { get; set; }

        public DateTime ChangedAt { get; set; }

        /// <summary>หมายเหตุ/เหตุผล (เหตุผลการพักและการยกเลิกถูกบันทึกซ้ำไว้ที่นี่ด้วย)</summary>
        public string Note { get; set; }

        public string FromStatusDisplayName =>
            FromStatus.HasValue ? RequestStatuses.ToDisplayName(FromStatus.Value) : "สร้างใบงาน";

        public string ToStatusDisplayName => RequestStatuses.ToDisplayName(ToStatus);
    }
}
