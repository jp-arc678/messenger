using Messenger.Domain.Enums;

namespace Messenger.Domain.Entities
{
    /// <summary>
    /// พนักงาน — cache ข้อมูลที่ได้จาก SSO ตาม BR-7
    /// ระบบไม่เก็บ password เอง ข้อมูลทั้งหมดในคลาสนี้มาจาก SSO
    /// ยกเว้น <see cref="Role"/> ซึ่งเป็นสิทธิ์ภายในระบบเราเอง
    /// </summary>
    public class Employee
    {
        /// <summary>รหัสพนักงาน — key หลักที่ใช้อ้างอิงทุกที่ในระบบ</summary>
        public string EmpCode { get; set; }

        public string FullName { get; set; }

        /// <summary>รหัสแผนก</summary>
        public string DeptCode { get; set; }

        /// <summary>ชื่อหน่วยงาน</summary>
        public string UnitName { get; set; }

        /// <summary>เบอร์ภายใน</summary>
        public string PhoneExt { get; set; }

        public string Email { get; set; }

        /// <summary>รหัสสาขา (SDC/SBK) — ตัวกำหนด scope ของข้อมูลทั้งหมด ตาม BR-6</summary>
        public string BranchCode { get; set; }

        public string BranchName { get; set; }

        /// <summary>สิทธิ์ในระบบ — resolve แล้ว (ไม่มีข้อมูล = User ตาม D10)</summary>
        public Role Role { get; set; }

        public bool IsActive { get; set; }
    }
}
