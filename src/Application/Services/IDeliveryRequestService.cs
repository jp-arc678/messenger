using System;
using System.Collections.Generic;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Application.Services
{
    /// <summary>ประเภทงาน 1 รายการที่ผู้ใช้ติ๊กมาจากฟอร์ม (D18)</summary>
    public class JobTypeInput
    {
        public JobType JobType { get; set; }

        public string DetailText { get; set; }
    }

    /// <summary>ข้อมูลจากฟอร์มสำหรับสร้างใบแจ้งงาน</summary>
    public class CreateRequestCommand
    {
        /// <summary>ผู้แจ้ง — ปล่อยว่างได้ ระบบจะใช้ผู้ใช้ปัจจุบัน (D17)</summary>
        public string RequesterEmpCode { get; set; }

        /// <summary>ปล่อย null เพื่อให้ระบบคำนวณให้ตาม BR-1</summary>
        public DateTime? SendDate { get; set; }

        public string ContactName { get; set; }

        public string Address { get; set; }

        public string Phone { get; set; }

        public string Detail { get; set; }

        public bool IsPersonal { get; set; }

        public IReadOnlyList<JobTypeInput> JobTypes { get; set; } = new List<JobTypeInput>();
    }

    /// <summary>ข้อมูลจากฟอร์มสำหรับแก้ไขใบแจ้งงาน</summary>
    public class UpdateRequestCommand
    {
        public int ReqId { get; set; }

        public string RequesterEmpCode { get; set; }

        public DateTime SendDate { get; set; }

        public string ContactName { get; set; }

        public string Address { get; set; }

        public string Phone { get; set; }

        public string Detail { get; set; }

        public bool IsPersonal { get; set; }

        /// <summary>ค่าที่ติดมากับฟอร์ม ใช้ทำ optimistic locking (BR-2)</summary>
        public byte[] RowVersion { get; set; }

        public IReadOnlyList<JobTypeInput> JobTypes { get; set; } = new List<JobTypeInput>();
    }

    /// <summary>
    /// เงื่อนไขค้นหาที่มาจากหน้าจอ
    ///
    /// ไม่มีช่อง "สาขา" และ "ผู้แจ้ง" โดยตั้งใจ — สองค่านี้ service เป็นคนเติมเอง
    /// จาก UserContext ตาม BR-6 + §5 ถ้าเปิดให้หน้าจอส่งมาได้ ผู้ใช้จะแก้ querystring
    /// เพื่อดูข้อมูลสาขาอื่นได้ทันที
    /// </summary>
    public class RequestListQuery
    {
        public DateTime? SendDateFrom { get; set; }

        public DateTime? SendDateTo { get; set; }

        /// <summary>ช่วง "วันที่บันทึก" — คนละอย่างกับวันที่ส่ง</summary>
        public DateTime? RequestDateFrom { get; set; }

        public DateTime? RequestDateTo { get; set; }

        /// <summary>null = ทุกสถานะ</summary>
        public RequestStatus? Status { get; set; }
    }

    /// <summary>
    /// Business logic ทั้งหมดของใบแจ้งงาน — BR-1, BR-2, BR-6, BR-8 และ D15–D18
    /// Controller มีหน้าที่แค่แปลงฟอร์มเป็น command แล้วเรียก service นี้
    /// </summary>
    public interface IDeliveryRequestService
    {
        /// <summary>ค่า sendDate เริ่มต้นที่จะแสดงบนฟอร์มสร้างใบงาน (BR-1)</summary>
        DateTime GetDefaultSendDate();

        /// <summary>รายชื่อคนที่เลือกเป็น "ผู้แจ้ง" ได้ — เฉพาะสาขาเดียวกับผู้ใช้ (D17 + BR-6)</summary>
        IReadOnlyList<Employee> GetSelectableRequesters(UserContext user);

        ServiceResult<DeliveryRequest> Create(CreateRequestCommand command, UserContext user);

        ServiceResult<DeliveryRequest> Update(UpdateRequestCommand command, UserContext user);

        /// <summary>ดูใบงาน 1 ใบ — ไม่พบ/คนละสาขา/ไม่มีสิทธิ์ดู จะไม่สำเร็จ</summary>
        ServiceResult<DeliveryRequest> Get(int reqId, UserContext user);

        /// <summary>รายการใบงานที่ผู้ใช้คนนี้มีสิทธิ์เห็น (§5 + BR-6)</summary>
        IReadOnlyList<DeliveryRequest> List(UserContext user, RequestListQuery query);

        /// <summary>ผู้ใช้คนนี้แก้ใบงานนี้ได้หรือไม่ ตาม BR-2 + §5</summary>
        bool CanEdit(DeliveryRequest request, UserContext user);
    }
}
