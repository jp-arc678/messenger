using System.Collections.Generic;
using Messenger.Application.Dtos;

namespace Messenger.Application.Abstractions
{
    /// <summary>
    /// ช่องทางคุยกับระบบ Single Sign-On (BR-7)
    ///
    /// D3 — ยังไม่ทราบ contract จริงของ SSO จึงใช้ implementation แบบ stub
    /// ไปก่อนใน Phase 0 เมื่อได้ contract จริงให้เปลี่ยนเฉพาะ implementation
    /// โดยไม่ต้องแก้ service layer
    /// </summary>
    public interface ISsoClient
    {
        /// <summary>
        /// ดึงข้อมูลผู้ใช้จาก SSO ด้วยรหัสพนักงาน
        /// คืน null ถ้า SSO ไม่รู้จักรหัสนี้
        /// </summary>
        SsoUserInfo GetUserInfo(string empCode);

        /// <summary>
        /// รายชื่อผู้ใช้ทั้งหมดที่ SSO รู้จัก
        /// ใช้กับหน้าจอ mock login ของ Phase 0 เท่านั้น
        /// SSO จริงจะไม่มี operation นี้
        /// </summary>
        IReadOnlyList<SsoUserInfo> ListKnownUsers();
    }
}
