using System.Collections.Generic;
using Messenger.Application.Dtos;

namespace Messenger.Application.Services
{
    /// <summary>ผลลัพธ์การ login</summary>
    public class SignInResult
    {
        public bool Success { get; private set; }

        public string ErrorMessage { get; private set; }

        public UserContext User { get; private set; }

        public static SignInResult Ok(UserContext user)
        {
            return new SignInResult { Success = true, User = user };
        }

        public static SignInResult Fail(string message)
        {
            return new SignInResult { Success = false, ErrorMessage = message };
        }
    }

    /// <summary>
    /// Business logic ของการ login + resolve สิทธิ์
    /// (business rule ทั้งหมดอยู่ที่ service layer เท่านั้น — CLAUDE.md §2)
    /// </summary>
    public interface IAuthService
    {
        /// <summary>
        /// login ด้วยรหัสพนักงาน : ถาม SSO → cache ลง Employee (BR-7)
        /// → resolve role (D10) → คืน <see cref="UserContext"/>
        /// </summary>
        SignInResult SignIn(string empCode);

        /// <summary>
        /// อ่านตัวตน + สาขา + role ล่าสุดจาก DB ของผู้ที่ login อยู่แล้ว (ไม่ถาม SSO ซ้ำ)
        ///
        /// ใช้ทุก request เพื่อไม่ให้ระบบเชื่อค่าสาขา/สิทธิ์ที่ค้างอยู่ใน cookie :
        /// ถ้า Admin ย้ายสาขา เปลี่ยน role หรือปิดการใช้งานพนักงาน ผลต้องมีทันที
        /// ไม่ใช่รอ cookie หมดอายุ (BR-6 + §5)
        ///
        /// คืน null เมื่อไม่พบพนักงานคนนี้แล้ว หรือถูกปิดการใช้งาน — ผู้เรียกต้องบังคับออกจากระบบ
        /// </summary>
        UserContext ResolveCurrent(string empCode);

        /// <summary>
        /// รายชื่อผู้ใช้ที่เลือก login ได้ในหน้าจอ mock SSO ของ Phase 0
        /// </summary>
        IReadOnlyList<SsoUserInfo> ListSelectableUsers();
    }
}
