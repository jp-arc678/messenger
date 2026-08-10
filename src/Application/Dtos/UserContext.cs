using Messenger.Domain.Enums;

namespace Messenger.Application.Dtos
{
    /// <summary>
    /// ตัวตนของผู้ใช้ที่ login อยู่ ณ ขณะนั้น — เป็นแหล่งอ้างอิงเดียว
    /// ของทั้ง "ตัวตน" และ "ขอบเขตสาขา" ที่ service layer ใช้บังคับ BR-6
    /// </summary>
    public class UserContext
    {
        public string EmpCode { get; set; }

        public string FullName { get; set; }

        public string DeptCode { get; set; }

        public string UnitName { get; set; }

        public string PhoneExt { get; set; }

        public string Email { get; set; }

        /// <summary>สาขาของผู้ใช้ — ทุก query ต้อง filter ด้วยค่านี้ (BR-6)</summary>
        public string BranchCode { get; set; }

        public string BranchName { get; set; }

        public Role Role { get; set; }

        public bool IsAdmin => Role == Role.Admin;

        public bool IsMessenger => Role == Role.Messenger;

        public string RoleDisplayName => RoleCodes.ToDisplayName(Role);
    }
}
