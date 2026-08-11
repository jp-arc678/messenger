using System;

namespace Messenger.Domain.Enums
{
    /// <summary>
    /// สถานะใบแจ้งงาน 5 สถานะ (CLAUDE.md §4)
    ///
    /// การเปลี่ยนสถานะทำได้เฉพาะ transition ที่มีในตาราง §6 เท่านั้น
    /// enum นี้เป็นเพียงคำศัพท์ของ domain ส่วนตัวตัดสินว่าเปลี่ยนได้ไหม
    /// อยู่ที่ <see cref="Workflow.RequestStateMachine"/>
    /// </summary>
    public enum RequestStatus
    {
        /// <summary>รับแจ้ง — สถานะเริ่มต้นของทุกใบงาน แก้ไขได้ในสถานะนี้เท่านั้น (BR-2)</summary>
        Received = 1,

        /// <summary>กำลังส่ง</summary>
        Delivering = 2,

        /// <summary>พักการส่ง</summary>
        Paused = 3,

        /// <summary>เสร็จงานแล้ว (terminal)</summary>
        Completed = 4,

        /// <summary>ยกเลิก (terminal)</summary>
        Cancelled = 5
    }

    /// <summary>แปลงระหว่าง <see cref="RequestStatus"/> กับค่าที่เก็บใน DB (เก็บเป็นชื่อ enum ตรง ๆ)</summary>
    public static class RequestStatuses
    {
        public static string ToCode(RequestStatus status)
        {
            if (!Enum.IsDefined(typeof(RequestStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status), status, "ไม่รู้จักสถานะนี้");

            return status.ToString();
        }

        /// <summary>ค่าที่ไม่รู้จักถือเป็นข้อมูลผิดพลาด จึงโยน exception แทนการเดา</summary>
        public static RequestStatus Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("ไม่ได้ระบุสถานะ", nameof(code));

            RequestStatus parsed;
            if (!Enum.TryParse(code.Trim(), ignoreCase: true, result: out parsed) ||
                !Enum.IsDefined(typeof(RequestStatus), parsed))
            {
                throw new ArgumentException($"ไม่รู้จักสถานะ '{code}'", nameof(code));
            }

            return parsed;
        }

        public static string ToDisplayName(RequestStatus status)
        {
            switch (status)
            {
                case RequestStatus.Received:
                    return "รับแจ้ง";
                case RequestStatus.Delivering:
                    return "กำลังส่ง";
                case RequestStatus.Paused:
                    return "พักการส่ง";
                case RequestStatus.Completed:
                    return "เสร็จงานแล้ว";
                case RequestStatus.Cancelled:
                    return "ยกเลิก";
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, "ไม่รู้จักสถานะนี้");
            }
        }
    }
}
