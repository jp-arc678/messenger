using System;
using System.Globalization;

namespace Messenger.Application.Services
{
    /// <summary>
    /// รูปแบบเลขใบงานตาม BR-8 : <c>MSG-{BRANCH}-{YYMM}-{NNNN}</c>
    ///
    /// ส่วน "เลขลำดับถัดไป" มาจาก stored procedure (ต้องกันชนกันระดับ DB)
    /// ส่วน "การประกอบเป็นข้อความ" อยู่ที่นี่ที่เดียว เพื่อให้ทดสอบได้และ
    /// ไม่มีสูตรซ้ำอยู่ทั้งใน C# และ T-SQL
    /// </summary>
    public static class ReqNoGenerator
    {
        public const string Prefix = "MSG";

        /// <summary>เลขลำดับมี 4 หลัก จึงรองรับได้สูงสุด 9999 ใบต่อสาขาต่อเดือน</summary>
        public const int MaxRunningNumber = 9999;

        /// <summary>
        /// แปลงวันที่เป็นส่วน {YYMM} (ปี ค.ศ. 2 หลัก + เดือน 2 หลัก)
        ///
        /// ต้องใช้ InvariantCulture เสมอ — ระบบตั้ง culture เป็น th-TH ถ้าเผลอ
        /// ฟอร์แมตตาม culture ปัจจุบัน จะได้ปีพุทธศักราชแทน (2569 แทน 2026)
        /// </summary>
        public static string ToYyMm(DateTime date)
        {
            return date.ToString("yyMM", CultureInfo.InvariantCulture);
        }

        /// <summary>ประกอบเลขใบงานให้ครบรูปแบบตาม BR-8</summary>
        public static string Build(string branchCode, DateTime requestDate, int runningNumber)
        {
            if (string.IsNullOrWhiteSpace(branchCode))
                throw new ArgumentException("ต้องระบุรหัสสาขา", nameof(branchCode));

            if (runningNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(runningNumber), runningNumber,
                    "เลขลำดับใบงานต้องเริ่มที่ 1");

            if (runningNumber > MaxRunningNumber)
                throw new ArgumentOutOfRangeException(nameof(runningNumber), runningNumber,
                    $"เลขลำดับใบงานเกิน {MaxRunningNumber} ต่อสาขาต่อเดือน");

            return string.Concat(
                Prefix, "-",
                branchCode.Trim().ToUpperInvariant(), "-",
                ToYyMm(requestDate), "-",
                runningNumber.ToString("D4", CultureInfo.InvariantCulture));
        }
    }
}
