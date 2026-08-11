using System;
using System.Collections.Generic;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Application.Abstractions
{
    /// <summary>ข้อมูลการเปลี่ยนสถานะ 1 ครั้ง (§6)</summary>
    public class StatusChangeData
    {
        public int ReqId { get; set; }

        public string BranchCode { get; set; }

        /// <summary>
        /// สถานะที่ service เห็นตอนตัดสินใจ — repository ต้องใช้เป็นเงื่อนไขในการอัปเดต
        /// ถ้าสถานะจริงใน DB ไม่ใช่ค่านี้แล้ว แปลว่ามีคนกดตัดหน้าไป ต้องไม่อัปเดต
        /// </summary>
        public RequestStatus FromStatus { get; set; }

        public RequestStatus ToStatus { get; set; }

        public string ByEmpCode { get; set; }

        public DateTime ChangedAt { get; set; }

        /// <summary>ข้อความที่จะบันทึกลง tblStatusHistory</summary>
        public string Note { get; set; }

        /// <summary>
        /// เหตุผล — บันทึกลง tblPauseReason / tblCancelReason ตามสถานะปลายทาง
        /// ปล่อย null ได้เมื่อ transition นั้นไม่บังคับเหตุผล
        /// </summary>
        public string Reason { get; set; }
    }

    /// <summary>ข้อมูลการยืนยันรับงานของ Messenger (Received → Delivering)</summary>
    public class ConfirmAssignmentData
    {
        public int ReqId { get; set; }

        public string BranchCode { get; set; }

        /// <summary>Messenger ที่จะถูกบันทึกเป็นผู้รับงาน (D11 — สาขาละคนเดียว)</summary>
        public string MessengerEmpCode { get; set; }

        /// <summary>คนที่กดยืนยันจริง (Admin กดแทนได้ ดู D22)</summary>
        public string ByEmpCode { get; set; }

        public DateTime ConfirmedAt { get; set; }

        public string Note { get; set; }
    }

    /// <summary>
    /// การเปลี่ยนสถานะ / คิวงานของ Messenger
    ///
    /// ทุก method รับ <c>branchCode</c> และต้องใช้เป็นเงื่อนไขใน SQL เสมอ (BR-6)
    /// การตัดสินว่า "เปลี่ยนสถานะนี้ได้ไหม / ใครทำได้" เป็นหน้าที่ของ service layer
    /// repository มีหน้าที่แค่เขียนให้ atomic และไม่ทับกันเท่านั้น
    /// </summary>
    public interface IRequestWorkflowRepository
    {
        /// <summary>
        /// เปลี่ยนสถานะ + บันทึก audit trail (+ เหตุผล ถ้ามี) ใน transaction เดียว
        /// คืน false เมื่อสถานะปัจจุบันไม่ใช่ <see cref="StatusChangeData.FromStatus"/> แล้ว
        /// (มีคนกดตัดหน้า) หรือใบงานอยู่คนละสาขา
        /// </summary>
        bool ChangeStatus(StatusChangeData data);

        /// <summary>
        /// ยืนยันรับงาน : เปลี่ยน Received → Delivering, จองลำดับวิ่งงานถัดไปของวันนั้น
        /// และบันทึก assignment + audit trail ใน transaction เดียว
        ///
        /// คืน null เมื่อใบงานไม่ได้อยู่สถานะ Received แล้ว (มีคนยืนยันตัดหน้า)
        /// </summary>
        MessengerAssignment Confirm(ConfirmAssignmentData data);

        /// <summary>
        /// สลับลำดับวิ่งงานของใบงาน 2 ใบ (ต้องอยู่สาขาเดียวกันและวันส่งเดียวกัน)
        /// คืน false ถ้าใบใดใบหนึ่งไม่มี assignment แล้ว
        /// </summary>
        bool SwapSequence(int reqIdA, int reqIdB, string branchCode);

        /// <summary>audit trail ของใบงาน เรียงจากเก่าไปใหม่ — ว่างเปล่าถ้าใบงานอยู่คนละสาขา</summary>
        IReadOnlyList<StatusHistoryEntry> ListHistory(int reqId, string branchCode);
    }
}
