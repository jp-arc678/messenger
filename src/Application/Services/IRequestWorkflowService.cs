using System;
using System.Collections.Generic;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;
using Messenger.Domain.Workflow;

namespace Messenger.Application.Services
{
    /// <summary>ทิศทางการจัดลำดับคิว</summary>
    public enum QueueMove
    {
        Up = -1,
        Down = 1
    }

    /// <summary>
    /// Workflow ของ Messenger — ยืนยันรับงาน จัดลำดับคิว และเปลี่ยนสถานะตาม §6
    ///
    /// ทุกการเปลี่ยนสถานะต้องผ่าน <see cref="Apply"/> เท่านั้น เพื่อให้การตรวจ
    /// state machine / สิทธิ์ / เหตุผล / audit trail เกิดขึ้นครบทุกครั้งที่เดียว
    /// </summary>
    public interface IRequestWorkflowService
    {
        /// <summary>
        /// เปลี่ยนสถานะใบงาน 1 ครั้งตามตาราง §6
        /// <paramref name="reason"/> จำเป็นเฉพาะ transition ที่ระบุว่าต้องมีเหตุผล
        /// </summary>
        ServiceResult<DeliveryRequest> Apply(int reqId, RequestAction action, string reason, UserContext user);

        /// <summary>คิวงานของสาขาในวันที่กำหนด — เปิดให้เฉพาะ Messenger/Admin (§5)</summary>
        ServiceResult<QueueDay> GetQueue(UserContext user, DateTime sendDate);

        /// <summary>สลับลำดับวิ่งงานกับใบที่อยู่ติดกันในคิวของวันเดียวกัน (D11)</summary>
        ServiceResult<DeliveryRequest> Move(int reqId, QueueMove direction, UserContext user);

        /// <summary>
        /// ปุ่มเปลี่ยนสถานะที่ผู้ใช้คนนี้กดได้จริงกับใบงานนี้
        /// (หน้าจอใช้ตัดสินว่าจะแสดงปุ่มไหน — แต่ <see cref="Apply"/> ยังตรวจซ้ำเสมอ)
        /// </summary>
        IReadOnlyList<StatusTransition> AvailableActions(DeliveryRequest request, UserContext user);

        /// <summary>audit trail การเปลี่ยนสถานะของใบงาน (เก่า → ใหม่)</summary>
        ServiceResult<IReadOnlyList<StatusHistoryEntry>> GetHistory(int reqId, UserContext user);
    }
}
