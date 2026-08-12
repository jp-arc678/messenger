using System;
using System.Collections.Generic;

namespace Messenger.Domain.Enums
{
    /// <summary>
    /// ประเภทรูปยืนยัน (BR-3)
    ///
    /// ค่าที่เก็บใน DB เป็นตัวพิมพ์เล็ก ('send'/'receive') ตาม CHECK constraint
    /// ของ tblDeliveryPhoto จึงต้องแปลงผ่าน <see cref="PhotoTypes"/> เสมอ
    /// </summary>
    public enum PhotoType
    {
        /// <summary>รูปตอนส่งเอกสารให้ปลายทาง</summary>
        Send = 1,

        /// <summary>รูปตอนรับเอกสาร/ของกลับมา</summary>
        Receive = 2
    }

    public static class PhotoTypes
    {
        public const string SendCode = "send";
        public const string ReceiveCode = "receive";

        public static IReadOnlyList<PhotoType> All => new[] { PhotoType.Send, PhotoType.Receive };

        public static string ToCode(PhotoType type)
        {
            switch (type)
            {
                case PhotoType.Send:
                    return SendCode;
                case PhotoType.Receive:
                    return ReceiveCode;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "ไม่รู้จักประเภทรูปนี้");
            }
        }

        /// <summary>ค่าที่ไม่รู้จักถือเป็นข้อมูลผิดพลาด จึงโยน exception แทนการเดา</summary>
        public static PhotoType Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("ไม่ได้ระบุประเภทรูป", nameof(code));

            switch (code.Trim().ToLowerInvariant())
            {
                case SendCode:
                    return PhotoType.Send;
                case ReceiveCode:
                    return PhotoType.Receive;
                default:
                    throw new ArgumentException($"ไม่รู้จักประเภทรูป '{code}'", nameof(code));
            }
        }

        /// <summary>คืน null ถ้าค่าที่ส่งมาไม่ถูกต้อง (ใช้กับข้อมูลที่มาจากฟอร์ม)</summary>
        public static PhotoType? TryParse(string code)
        {
            try
            {
                return Parse(code);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static string ToDisplayName(PhotoType type)
        {
            switch (type)
            {
                case PhotoType.Send:
                    return "รูปตอนส่ง";
                case PhotoType.Receive:
                    return "รูปตอนรับ";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "ไม่รู้จักประเภทรูปนี้");
            }
        }
    }
}
