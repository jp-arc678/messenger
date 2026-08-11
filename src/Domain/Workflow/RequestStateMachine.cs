using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Domain.Enums;

namespace Messenger.Domain.Workflow
{
    /// <summary>
    /// การกระทำที่เปลี่ยนสถานะใบแจ้งงาน — 1 action = 1 บรรทัดในตาราง CLAUDE.md §6
    /// (การสร้างใบงานใหม่ไม่นับเป็น action เพราะไม่มีสถานะต้นทาง)
    /// </summary>
    public enum RequestAction
    {
        /// <summary>Messenger ยืนยันรับงาน : Received → Delivering</summary>
        Confirm = 1,

        /// <summary>พักการส่ง (ต้องระบุเหตุผล) : Delivering → Paused</summary>
        Pause = 2,

        /// <summary>กลับมาส่งต่อ : Paused → Delivering</summary>
        Resume = 3,

        /// <summary>ปิดงาน : Delivering → Completed</summary>
        Complete = 4,

        /// <summary>ยกเลิก : Received/Delivering/Paused → Cancelled</summary>
        Cancel = 5
    }

    /// <summary>
    /// การเปลี่ยนสถานะ 1 เส้นทางที่ระบบอนุญาต พร้อมเงื่อนไขประกอบ
    /// (ใครกดได้ · ต้องระบุเหตุผลไหม)
    /// </summary>
    public class StatusTransition
    {
        internal StatusTransition(RequestAction action,
                                  RequestStatus from,
                                  RequestStatus to,
                                  string displayName,
                                  bool reasonRequired,
                                  bool allowedForMessenger,
                                  bool allowedForAdmin,
                                  bool allowedForOwnerUser)
        {
            Action = action;
            From = from;
            To = to;
            DisplayName = displayName;
            ReasonRequired = reasonRequired;
            AllowedForMessenger = allowedForMessenger;
            AllowedForAdmin = allowedForAdmin;
            AllowedForOwnerUser = allowedForOwnerUser;
        }

        public RequestAction Action { get; }

        public RequestStatus From { get; }

        public RequestStatus To { get; }

        /// <summary>ข้อความบนปุ่ม</summary>
        public string DisplayName { get; }

        /// <summary>true = ห้ามทำถ้าไม่กรอกเหตุผล (§6 : พัก และ ยกเลิกหลังเริ่มส่งแล้ว)</summary>
        public bool ReasonRequired { get; }

        public bool AllowedForMessenger { get; }

        public bool AllowedForAdmin { get; }

        /// <summary>U-User ทำได้ แต่เฉพาะใบที่ตัวเองเป็นผู้แจ้ง (D7 + D17)</summary>
        public bool AllowedForOwnerUser { get; }

        /// <summary>
        /// ผู้ใช้ role นี้กดได้หรือไม่
        /// <paramref name="isOwner"/> = ผู้ใช้เป็น "ผู้แจ้ง" ของใบงานนี้หรือไม่
        /// (ไม่ใช่คนที่กดสร้าง — ดู D17)
        /// </summary>
        public bool IsAllowedFor(Role role, bool isOwner)
        {
            switch (role)
            {
                case Role.Admin:
                    return AllowedForAdmin;
                case Role.Messenger:
                    return AllowedForMessenger;
                case Role.User:
                    return AllowedForOwnerUser && isOwner;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), role, "ไม่รู้จัก role นี้");
            }
        }
    }

    /// <summary>
    /// State machine ของใบแจ้งงาน — สำเนาตรงของตาราง CLAUDE.md §6
    ///
    /// ตารางนี้คือแหล่งความจริงเดียวของคำถาม "เปลี่ยนสถานะนี้ได้ไหม"
    /// service layer ต้องถามที่นี่เสมอ ห้าม if-else สถานะกระจายตามที่ต่าง ๆ
    /// และห้ามเพิ่ม transition ที่ไม่มีใน §6 (§10 ข้อ 5)
    /// </summary>
    public static class RequestStateMachine
    {
        private static readonly StatusTransition[] Transitions =
        {
            // จาก          ไป            ใครทำได้                     เหตุผล
            new StatusTransition(RequestAction.Confirm,  RequestStatus.Received,   RequestStatus.Delivering, "ยืนยันรับงาน",
                                 reasonRequired: false, allowedForMessenger: true, allowedForAdmin: true, allowedForOwnerUser: false),

            // D7 — User ยกเลิกใบตัวเองได้เฉพาะก่อน Messenger ยืนยันรับงาน
            //      และช่วงนี้ยังไม่บังคับเหตุผล (§6 บังคับเฉพาะหลังเริ่มส่งแล้ว)
            new StatusTransition(RequestAction.Cancel,   RequestStatus.Received,   RequestStatus.Cancelled,  "ยกเลิกใบงาน",
                                 reasonRequired: false, allowedForMessenger: true, allowedForAdmin: true, allowedForOwnerUser: true),

            new StatusTransition(RequestAction.Pause,    RequestStatus.Delivering, RequestStatus.Paused,     "พักการส่ง",
                                 reasonRequired: true,  allowedForMessenger: true, allowedForAdmin: true, allowedForOwnerUser: false),

            new StatusTransition(RequestAction.Complete, RequestStatus.Delivering, RequestStatus.Completed,  "ปิดงาน",
                                 reasonRequired: false, allowedForMessenger: true, allowedForAdmin: true, allowedForOwnerUser: false),

            new StatusTransition(RequestAction.Cancel,   RequestStatus.Delivering, RequestStatus.Cancelled,  "ยกเลิกใบงาน",
                                 reasonRequired: true,  allowedForMessenger: true, allowedForAdmin: true, allowedForOwnerUser: false),

            new StatusTransition(RequestAction.Resume,   RequestStatus.Paused,     RequestStatus.Delivering, "กลับมาส่งต่อ",
                                 reasonRequired: false, allowedForMessenger: true, allowedForAdmin: true, allowedForOwnerUser: false),

            new StatusTransition(RequestAction.Cancel,   RequestStatus.Paused,     RequestStatus.Cancelled,  "ยกเลิกใบงาน",
                                 reasonRequired: true,  allowedForMessenger: true, allowedForAdmin: true, allowedForOwnerUser: false)
        };

        /// <summary>ทุก transition ที่ระบบยอมรับ (ใช้ในเทสต์เพื่อไล่ตรวจกับ §6)</summary>
        public static IReadOnlyList<StatusTransition> All => Transitions;

        /// <summary>สถานะปลายทางที่ไปต่อไม่ได้แล้ว</summary>
        public static bool IsTerminal(RequestStatus status)
        {
            return !Transitions.Any(t => t.From == status);
        }

        /// <summary>
        /// หา transition ของ action นี้จากสถานะปัจจุบัน — คืน null ถ้า §6 ไม่มีเส้นทางนี้
        /// </summary>
        public static StatusTransition Find(RequestStatus from, RequestAction action)
        {
            return Transitions.FirstOrDefault(t => t.From == from && t.Action == action);
        }

        /// <summary>หา transition จากคู่สถานะ — คืน null ถ้า §6 ไม่มีเส้นทางนี้</summary>
        public static StatusTransition Find(RequestStatus from, RequestStatus to)
        {
            return Transitions.FirstOrDefault(t => t.From == from && t.To == to);
        }

        /// <summary>ทุก transition ที่ออกจากสถานะนี้ได้ (ยังไม่คิดเรื่องสิทธิ์)</summary>
        public static IReadOnlyList<StatusTransition> From(RequestStatus status)
        {
            return Transitions.Where(t => t.From == status).ToList();
        }

        /// <summary>
        /// ปุ่มที่ผู้ใช้คนนี้กดได้จริงบนใบงานที่อยู่สถานะนี้
        /// (หน้าจอใช้ค่านี้แสดงปุ่ม — แต่ service ยังต้องตรวจซ้ำตอนกดเสมอ)
        /// </summary>
        public static IReadOnlyList<StatusTransition> AllowedFor(RequestStatus status, Role role, bool isOwner)
        {
            return Transitions
                .Where(t => t.From == status && t.IsAllowedFor(role, isOwner))
                .ToList();
        }
    }
}
