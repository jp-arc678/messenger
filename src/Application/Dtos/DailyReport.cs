using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Application.Dtos
{
    /// <summary>ยอดของสถานะหนึ่ง</summary>
    public class StatusCount
    {
        public RequestStatus Status { get; set; }

        public int Count { get; set; }

        public string StatusDisplayName => RequestStatuses.ToDisplayName(Status);
    }

    /// <summary>สรุปงานของ Messenger 1 คนในช่วงที่เลือก</summary>
    public class MessengerSummary
    {
        public string MessengerEmpCode { get; set; }

        public string MessengerName { get; set; }

        public int Total { get; set; }

        public int Completed { get; set; }

        public int Cancelled { get; set; }

        /// <summary>ยังวิ่งอยู่ (Delivering + Paused)</summary>
        public int InProgress { get; set; }
    }

    /// <summary>สรุปของ 1 วันในช่วงที่เลือก</summary>
    public class DailySummary
    {
        public DateTime SendDate { get; set; }

        public int Total { get; set; }

        public int Received { get; set; }

        public int Delivering { get; set; }

        public int Paused { get; set; }

        public int Completed { get; set; }

        public int Cancelled { get; set; }
    }

    /// <summary>
    /// รายงานสรุปงานตามช่วงวันที่ส่ง (Phase 5)
    ///
    /// ยึด "วันที่ส่ง" เป็นแกนของรายงาน เพราะเป็นวันที่ Messenger ออกวิ่งงานจริง
    /// (ไม่ใช่วันที่ผู้ใช้กดแจ้ง ซึ่งอาจเป็นคนละวันตาม BR-1)
    /// </summary>
    public class DailyReport
    {
        public DateTime DateFrom { get; set; }

        public DateTime DateTo { get; set; }

        public string BranchCode { get; set; }

        public string BranchName { get; set; }

        /// <summary>true = เห็นงานทั้งสาขา · false = เห็นเฉพาะใบที่ตัวเองเป็นผู้แจ้ง (§5)</summary>
        public bool WholeBranch { get; set; }

        public IReadOnlyList<StatusCount> ByStatus { get; set; } = new List<StatusCount>();

        public IReadOnlyList<MessengerSummary> ByMessenger { get; set; } = new List<MessengerSummary>();

        public IReadOnlyList<DailySummary> ByDay { get; set; } = new List<DailySummary>();

        /// <summary>ใบงานทั้งหมดในช่วง — ใช้เป็นข้อมูลของไฟล์ export (D31)</summary>
        public IReadOnlyList<DeliveryRequest> Requests { get; set; } = new List<DeliveryRequest>();

        public int TotalCount => Requests.Count;

        public int PersonalCount => Requests.Count(r => r.IsPersonal);

        public int CompanyCount => TotalCount - PersonalCount;

        public int CountOf(RequestStatus status)
        {
            var found = ByStatus.FirstOrDefault(s => s.Status == status);
            return found?.Count ?? 0;
        }
    }
}
