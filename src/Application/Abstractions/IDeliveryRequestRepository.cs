using System;
using System.Collections.Generic;
using Messenger.Domain.Entities;

namespace Messenger.Application.Abstractions
{
    /// <summary>ข้อมูลที่ต้องใช้ตอนสร้างใบแจ้งงาน 1 ใบ</summary>
    public class CreateRequestData
    {
        public string BranchCode { get; set; }

        public string RequesterEmpCode { get; set; }

        public DateTime RequestDateTime { get; set; }

        public DateTime SendDate { get; set; }

        public string ContactName { get; set; }

        public string Address { get; set; }

        public string Phone { get; set; }

        public string Detail { get; set; }

        public bool IsPersonal { get; set; }

        public string CreatedBy { get; set; }

        public IReadOnlyList<RequestJobType> JobTypes { get; set; }

        /// <summary>ส่วน {YYMM} ของเลขใบงาน ใช้ขอเลขลำดับถัดไปจาก tblReqNoSequence</summary>
        public string YyMm { get; set; }

        /// <summary>
        /// ฟังก์ชันประกอบเลขใบงานจากเลขลำดับที่ DB คืนมา
        ///
        /// ที่ต้องส่งเป็น delegate เพราะรูปแบบเลขตาม BR-8 เป็น business rule
        /// จึงต้องอยู่ที่ Application layer ส่วน repository รับผิดชอบแค่
        /// การขอเลขและบันทึกให้อยู่ใน transaction เดียวกัน
        /// </summary>
        public Func<int, string> ReqNoFactory { get; set; }
    }

    /// <summary>ข้อมูลที่ต้องใช้ตอนแก้ไขใบแจ้งงาน</summary>
    public class UpdateRequestData
    {
        public int ReqId { get; set; }

        public string BranchCode { get; set; }

        public string RequesterEmpCode { get; set; }

        public DateTime SendDate { get; set; }

        public string ContactName { get; set; }

        public string Address { get; set; }

        public string Phone { get; set; }

        public string Detail { get; set; }

        public bool IsPersonal { get; set; }

        public string UpdatedBy { get; set; }

        /// <summary>ค่าที่อ่านมาตอนเปิดฟอร์ม — ใช้ตรวจว่ามีคนแก้ตัดหน้าไหม (BR-2)</summary>
        public byte[] RowVersion { get; set; }

        public IReadOnlyList<RequestJobType> JobTypes { get; set; }
    }

    /// <summary>ผลลัพธ์การสร้างใบแจ้งงาน</summary>
    public class CreatedRequest
    {
        public int ReqId { get; set; }

        public string ReqNo { get; set; }
    }

    /// <summary>
    /// เข้าถึงข้อมูลใบแจ้งงานผ่าน stored procedure เท่านั้น
    ///
    /// ทุก method รับ <c>branchCode</c> เสมอ และ implementation ต้องส่งต่อไปยัง
    /// stored procedure ทุกครั้ง เพื่อบังคับ BR-6 ที่ระดับ data access
    /// ไม่ใช่แค่หน้าจอ
    /// </summary>
    public interface IDeliveryRequestRepository
    {
        /// <summary>
        /// สร้างใบงานใหม่ — ขอเลขลำดับ, ประกอบเลขใบงาน, บันทึกใบงาน,
        /// บันทึกประเภทงาน และลง StatusHistory ทั้งหมดใน transaction เดียว
        /// </summary>
        CreatedRequest Create(CreateRequestData data);

        /// <summary>คืน null ถ้าไม่พบใบงาน หรือใบงานอยู่คนละสาขา</summary>
        DeliveryRequest GetById(int reqId, string branchCode);

        /// <summary>
        /// รายการใบงานของสาขา
        /// ส่ง <paramref name="requesterEmpCode"/> = null เพื่อดูทั้งสาขา
        /// (ผู้เรียกเป็นคนตัดสินตามสิทธิ์ ไม่ใช่ repository)
        /// </summary>
        IReadOnlyList<DeliveryRequest> List(string branchCode, string requesterEmpCode,
                                            DateTime? sendDateFrom, DateTime? sendDateTo);

        /// <summary>
        /// แก้ไขใบงาน — คืน false เมื่อไม่มีแถวถูกอัปเดต
        /// (rowVersion ไม่ตรง = มีคนแก้ตัดหน้า หรือใบงานอยู่คนละสาขา)
        /// </summary>
        bool Update(UpdateRequestData data);
    }
}
