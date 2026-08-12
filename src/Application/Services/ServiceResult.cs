using System;
using System.Collections.Generic;
using System.Linq;

namespace Messenger.Application.Services
{
    /// <summary>
    /// ผลลัพธ์ของ operation ใน service layer
    ///
    /// ใช้ result object แทนการโยน exception เพราะ "ข้อมูลไม่ผ่านกฎ" และ
    /// "มีคนแก้ตัดหน้า" เป็นเหตุการณ์ปกติของระบบ ไม่ใช่ความผิดพลาดของโปรแกรม
    /// </summary>
    public class ServiceResult<T>
    {
        private static readonly IReadOnlyList<string> NoErrors = new string[0];

        public bool Success { get; private set; }

        public T Value { get; private set; }

        public IReadOnlyList<string> Errors { get; private set; } = NoErrors;

        /// <summary>
        /// เรื่องที่ผู้ใช้ควรรู้ แต่ไม่ได้ทำให้ operation ล้มเหลว
        /// เช่น ปิดงานสำเร็จแล้วแต่ส่งอีเมลแจ้งผู้แจ้งไม่ออก (D26)
        /// </summary>
        public IReadOnlyList<string> Warnings { get; private set; } = NoErrors;

        public bool HasWarnings => Warnings.Count > 0;

        /// <summary>true เมื่อมีคนอื่นแก้ใบงานนี้ไปแล้วระหว่างที่เรากรอกฟอร์ม (BR-2)</summary>
        public bool IsConcurrencyConflict { get; private set; }

        public string FirstError => Errors.FirstOrDefault();

        public static ServiceResult<T> Ok(T value)
        {
            return new ServiceResult<T> { Success = true, Value = value };
        }

        /// <summary>สำเร็จ แต่มีเรื่องที่ต้องบอกผู้ใช้ด้วย</summary>
        public static ServiceResult<T> OkWithWarnings(T value, IEnumerable<string> warnings)
        {
            var list = warnings == null ? new string[0] : warnings.Where(w => !string.IsNullOrWhiteSpace(w)).ToArray();

            return new ServiceResult<T>
            {
                Success = true,
                Value = value,
                Warnings = list
            };
        }

        public static ServiceResult<T> Fail(params string[] errors)
        {
            if (errors == null || errors.Length == 0)
                throw new ArgumentException("ต้องระบุเหตุผลที่ไม่สำเร็จอย่างน้อย 1 ข้อ", nameof(errors));

            return new ServiceResult<T> { Success = false, Errors = errors };
        }

        public static ServiceResult<T> Fail(IEnumerable<string> errors)
        {
            return Fail(errors == null ? new string[0] : errors.ToArray());
        }

        public static ServiceResult<T> Conflict(string message)
        {
            return new ServiceResult<T>
            {
                Success = false,
                IsConcurrencyConflict = true,
                Errors = new[] { message }
            };
        }
    }
}
