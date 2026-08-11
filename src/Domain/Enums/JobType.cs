using System;
using System.Collections.Generic;

namespace Messenger.Domain.Enums
{
    /// <summary>
    /// ประเภทงาน 6 แบบ (CLAUDE.md §4)
    /// 1 ใบแจ้งงานเลือกได้มากกว่า 1 ประเภท และระบุรายละเอียดเพิ่มต่อแต่ละประเภทได้
    /// </summary>
    public enum JobType
    {
        /// <summary>ส่งเอกสาร</summary>
        SendDoc = 1,

        /// <summary>รับเอกสาร — ประเภทนี้ทำให้ใบงานต้องกดยืนยันรับของก่อนปิด (BR-4)</summary>
        ReceiveDoc = 2,

        /// <summary>รับเช็ค</summary>
        ReceiveCheck = 3,

        /// <summary>วางบิล</summary>
        PlaceBill = 4,

        /// <summary>ต่อภาษี</summary>
        RenewTax = 5,

        /// <summary>อื่นๆ</summary>
        Other = 6
    }

    /// <summary>
    /// แปลงระหว่าง <see cref="JobType"/> กับค่าที่เก็บใน DB
    /// (คอลัมน์ tblRequestJobType.JobType เก็บเป็นชื่อ enum ตรง ๆ)
    /// </summary>
    public static class JobTypes
    {
        /// <summary>ลำดับที่ใช้แสดงบนหน้าจอ — ตรงกับลำดับใน CLAUDE.md §4</summary>
        public static readonly IReadOnlyList<JobType> All = new[]
        {
            JobType.SendDoc,
            JobType.ReceiveDoc,
            JobType.ReceiveCheck,
            JobType.PlaceBill,
            JobType.RenewTax,
            JobType.Other
        };

        public static string ToCode(JobType jobType)
        {
            if (!Enum.IsDefined(typeof(JobType), jobType))
                throw new ArgumentOutOfRangeException(nameof(jobType), jobType, "ไม่รู้จักประเภทงานนี้");

            return jobType.ToString();
        }

        /// <summary>
        /// แปลงค่าจาก DB กลับเป็น enum
        /// ค่าที่ไม่รู้จักถือเป็นข้อมูลผิดพลาด จึงโยน exception แทนการเดา
        /// (ต่างจาก role ที่มี default ชัดเจนตาม D10)
        /// </summary>
        public static JobType Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("ไม่ได้ระบุประเภทงาน", nameof(code));

            JobType parsed;
            if (!Enum.TryParse(code.Trim(), ignoreCase: true, result: out parsed) ||
                !Enum.IsDefined(typeof(JobType), parsed))
            {
                throw new ArgumentException($"ไม่รู้จักประเภทงาน '{code}'", nameof(code));
            }

            return parsed;
        }

        public static string ToDisplayName(JobType jobType)
        {
            switch (jobType)
            {
                case JobType.SendDoc:
                    return "ส่งเอกสาร";
                case JobType.ReceiveDoc:
                    return "รับเอกสาร";
                case JobType.ReceiveCheck:
                    return "รับเช็ค";
                case JobType.PlaceBill:
                    return "วางบิล";
                case JobType.RenewTax:
                    return "ต่อภาษี";
                case JobType.Other:
                    return "อื่นๆ";
                default:
                    throw new ArgumentOutOfRangeException(nameof(jobType), jobType, "ไม่รู้จักประเภทงานนี้");
            }
        }
    }
}
