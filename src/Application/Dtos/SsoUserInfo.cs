namespace Messenger.Application.Dtos
{
    /// <summary>
    /// ข้อมูลผู้ใช้ที่ได้รับจาก SSO ตาม BR-7
    /// (รหัสพนักงาน, ชื่อ, รหัสแผนก, ชื่อหน่วยงาน, เบอร์ภายใน, e-mail, รหัสสาขา)
    ///
    /// สังเกตว่า "ไม่มี" role อยู่ในนี้ — role เป็นข้อมูลของระบบเราเอง
    /// ไม่ได้มาจาก SSO
    /// </summary>
    public class SsoUserInfo
    {
        public string EmpCode { get; set; }

        public string FullName { get; set; }

        public string DeptCode { get; set; }

        public string UnitName { get; set; }

        public string PhoneExt { get; set; }

        public string Email { get; set; }

        public string BranchCode { get; set; }
    }
}
