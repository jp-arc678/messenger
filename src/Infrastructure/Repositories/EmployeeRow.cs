using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Infrastructure.Repositories
{
    /// <summary>
    /// รูปร่างของแถวที่ stored procedure ส่งกลับมา (ตรงกับ view dbo.vwEmployeeRole)
    /// แยกจาก entity เพราะ DB เก็บ role เป็นตัวอักษรเดียว แต่ domain ใช้ enum
    /// </summary>
    internal class EmployeeRow
    {
        public string EmpCode { get; set; }
        public string FullName { get; set; }
        public string DeptCode { get; set; }
        public string UnitName { get; set; }
        public string PhoneExt { get; set; }
        public string Email { get; set; }
        public string BranchCode { get; set; }
        public string BranchName { get; set; }
        public string RoleCode { get; set; }
        public bool IsActive { get; set; }

        public Employee ToEntity()
        {
            return new Employee
            {
                EmpCode = EmpCode,
                FullName = FullName,
                DeptCode = DeptCode,
                UnitName = UnitName,
                PhoneExt = PhoneExt,
                Email = Email,
                BranchCode = BranchCode == null ? null : BranchCode.Trim(),
                BranchName = BranchName,
                // D10 — RoleCode ที่ว่างหรือไม่รู้จัก จะ resolve เป็น User เสมอ
                Role = RoleCodes.Parse(RoleCode),
                IsActive = IsActive
            };
        }
    }
}
