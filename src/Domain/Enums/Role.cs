using System;

namespace Messenger.Domain.Enums
{
    /// <summary>
    /// สิทธิ์ผู้ใช้ 3 ระดับ (CLAUDE.md §4)
    /// D10 — 1 คนมีได้ 1 role เท่านั้น ห้ามซ้อน และ default คือ User เสมอ
    /// </summary>
    public enum Role
    {
        /// <summary>U-User — พนักงานทั่วไป (ค่าเริ่มต้นของทุกคน)</summary>
        User = 0,

        /// <summary>M-Messenger — เจ้าหน้าที่รับ-ส่งเอกสารประจำสาขา</summary>
        Messenger = 1,

        /// <summary>A-Admin — ผู้ดูแลระบบของสาขาตัวเอง (ไม่ใช่ global)</summary>
        Admin = 2
    }

    /// <summary>
    /// แปลงระหว่าง <see cref="Role"/> กับรหัสตัวอักษรเดียวที่เก็บใน DB (UserRole.RoleCode)
    /// </summary>
    public static class RoleCodes
    {
        public const string Admin = "A";
        public const string User = "U";
        public const string Messenger = "M";

        /// <summary>
        /// แปลง RoleCode จาก DB เป็น enum
        /// ค่าที่ว่าง/ไม่รู้จัก จะได้ <see cref="Role.User"/> เสมอ (D10)
        /// </summary>
        public static Role Parse(string roleCode)
        {
            if (string.IsNullOrWhiteSpace(roleCode))
                return Role.User;

            switch (roleCode.Trim().ToUpperInvariant())
            {
                case Admin:
                    return Role.Admin;
                case Messenger:
                    return Role.Messenger;
                case User:
                    return Role.User;
                default:
                    return Role.User;
            }
        }

        public static string ToCode(Role role)
        {
            switch (role)
            {
                case Role.Admin:
                    return Admin;
                case Role.Messenger:
                    return Messenger;
                case Role.User:
                    return User;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), role, "ไม่รู้จัก role นี้");
            }
        }

        /// <summary>ชื่อ role สำหรับแสดงผลบนหน้าจอ</summary>
        public static string ToDisplayName(Role role)
        {
            switch (role)
            {
                case Role.Admin:
                    return "ผู้ดูแลระบบ (Admin)";
                case Role.Messenger:
                    return "เจ้าหน้าที่รับ-ส่งเอกสาร (Messenger)";
                case Role.User:
                    return "พนักงานทั่วไป (User)";
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), role, "ไม่รู้จัก role นี้");
            }
        }
    }
}
