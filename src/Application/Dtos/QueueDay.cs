using System;
using System.Collections.Generic;
using Messenger.Domain.Entities;

namespace Messenger.Application.Dtos
{
    /// <summary>
    /// คิวงานของสาขา 1 วัน (แยกตามวันที่ส่ง)
    ///
    /// แบ่งเป็น 3 กลุ่มตามงานจริงของ Messenger :
    /// รอยืนยัน → กำลังวิ่ง (เรียงตามลำดับ D11) → ปิดแล้ว
    /// </summary>
    public class QueueDay
    {
        public DateTime SendDate { get; set; }

        public string BranchCode { get; set; }

        public string BranchName { get; set; }

        /// <summary>สถานะ Received — ยังไม่มีใครยืนยันรับงาน เรียงตามเวลาที่แจ้ง</summary>
        public IReadOnlyList<DeliveryRequest> Pending { get; set; } = new List<DeliveryRequest>();

        /// <summary>สถานะ Delivering/Paused — เรียงตาม sequenceOrder ของวันนั้น</summary>
        public IReadOnlyList<DeliveryRequest> Running { get; set; } = new List<DeliveryRequest>();

        /// <summary>สถานะ Completed/Cancelled ของวันนั้น (terminal แล้ว)</summary>
        public IReadOnlyList<DeliveryRequest> Closed { get; set; } = new List<DeliveryRequest>();

        public int TotalCount => Pending.Count + Running.Count + Closed.Count;
    }
}
