using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Domain.Enums;

namespace Messenger.Domain.Entities
{
    /// <summary>ประเภทงาน 1 รายการที่ผูกกับใบแจ้งงาน (1 ใบมีได้หลายรายการ)</summary>
    public class RequestJobType
    {
        public int ReqJobTypeId { get; set; }

        public int ReqId { get; set; }

        public JobType JobType { get; set; }

        /// <summary>รายละเอียดเพิ่มเติมเฉพาะประเภทนี้ (ไม่บังคับ)</summary>
        public string DetailText { get; set; }

        public string JobTypeDisplayName => JobTypes.ToDisplayName(JobType);
    }

    /// <summary>
    /// ใบแจ้งงาน — entity หลักของระบบ
    /// </summary>
    public class DeliveryRequest
    {
        public int ReqId { get; set; }

        /// <summary>เลขใบงานรูปแบบ MSG-{BRANCH}-{YYMM}-{NNNN} ตาม BR-8</summary>
        public string ReqNo { get; set; }

        /// <summary>สาขาเจ้าของใบงาน — ตัวกำหนดขอบเขตการมองเห็นทั้งหมด (BR-6)</summary>
        public string BranchCode { get; set; }

        public string BranchName { get; set; }

        /// <summary>ผู้แจ้ง — อาจไม่ใช่คนที่กดสร้าง เพราะแจ้งแทนกันได้ (D17)</summary>
        public string RequesterEmpCode { get; set; }

        public string RequesterName { get; set; }

        public string RequesterDeptCode { get; set; }

        public string RequesterUnitName { get; set; }

        public string RequesterPhoneExt { get; set; }

        public string RequesterEmail { get; set; }

        /// <summary>เวลาที่บันทึกใบงาน — ใช้เป็นตัวตั้งต้นคำนวณ sendDate ตาม BR-1</summary>
        public DateTime RequestDateTime { get; set; }

        public DateTime SendDate { get; set; }

        public string ContactName { get; set; }

        public string Address { get; set; }

        public string Phone { get; set; }

        public string Detail { get; set; }

        public RequestStatus Status { get; set; }

        /// <summary>งานฝากส่วนตัว (true) หรืองานของบริษัท (false) — ใช้แยกรายงานเท่านั้น</summary>
        public bool IsPersonal { get; set; }

        /// <summary>ยืนยันแล้วว่ารับของกลับมาให้ผู้แจ้งแล้ว (BR-4)</summary>
        public bool ReceiptConfirmed { get; set; }

        /// <summary>ค่าที่ใช้ทำ optimistic locking ตาม BR-2</summary>
        public byte[] RowVersion { get; set; }

        /// <summary>คนที่กดสร้างใบงานจริง (อาจต่างจาก <see cref="RequesterEmpCode"/>)</summary>
        public string CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; }

        public string UpdatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public IList<RequestJobType> JobTypes { get; set; } = new List<RequestJobType>();

        /// <summary>
        /// การรับงานของ Messenger — null ตราบใดที่ยังไม่มีใครยืนยันรับงาน
        /// (ใบงานสถานะ Received จึงเป็น null เสมอ)
        /// </summary>
        public MessengerAssignment Assignment { get; set; }

        public string StatusDisplayName => RequestStatuses.ToDisplayName(Status);

        /// <summary>ลำดับวิ่งงานของวัน — null ถ้ายังไม่ถูกยืนยันรับงาน (D11)</summary>
        public int? SequenceOrder => Assignment?.SequenceOrder;

        /// <summary>
        /// ใบงานนี้มีประเภท "รับเอกสาร" หรือไม่
        /// ใช้ตัดสินเงื่อนไขปิดงานตาม BR-4 (บังคับใช้จริงใน Phase 3)
        /// </summary>
        public bool RequiresReceiptConfirmation =>
            JobTypes != null && JobTypes.Any(j => j.JobType == Enums.JobType.ReceiveDoc);
    }
}
