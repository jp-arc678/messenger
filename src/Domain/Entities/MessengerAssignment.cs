using System;

namespace Messenger.Domain.Entities
{
    /// <summary>
    /// การรับงานของ Messenger — เกิดขึ้นตอนยืนยันรับงาน (Received → Delivering)
    ///
    /// D11 — แต่ละสาขามี Messenger ประจำคนเดียว จึงเปลี่ยนตัวกลางคันไม่ได้
    ///       และ <see cref="SequenceOrder"/> เป็นลำดับวิ่งงาน "ต่อวัน ต่อสาขา"
    ///       ไม่ได้แยกต่อ Messenger
    /// </summary>
    public class MessengerAssignment
    {
        public int ReqId { get; set; }

        public string MessengerEmpCode { get; set; }

        public string MessengerName { get; set; }

        public DateTime ConfirmedAt { get; set; }

        /// <summary>ลำดับการวิ่งงานของวันนั้น เริ่มที่ 1</summary>
        public int SequenceOrder { get; set; }

        public string Route { get; set; }

        public decimal? DistanceKm { get; set; }

        public bool? ReturnToOffice { get; set; }
    }
}
