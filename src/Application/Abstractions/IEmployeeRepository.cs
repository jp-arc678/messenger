using System.Collections.Generic;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;

namespace Messenger.Application.Abstractions
{
    /// <summary>
    /// เข้าถึงข้อมูลพนักงาน + role ที่ resolve แล้ว
    /// implementation เรียก stored procedure ผ่าน Dapper เท่านั้น
    /// </summary>
    public interface IEmployeeRepository
    {
        /// <summary>คืน null ถ้าไม่พบพนักงานรหัสนี้</summary>
        Employee GetByEmpCode(string empCode);

        /// <summary>
        /// บันทึก/อัปเดต cache ข้อมูลพนักงานจาก SSO (BR-7)
        /// พร้อมให้ role เริ่มต้นเป็น User ถ้ายังไม่เคยมี (D10)
        /// แล้วคืนข้อมูลที่ resolve role แล้วกลับมา
        /// </summary>
        Employee UpsertFromSso(SsoUserInfo info);

        /// <summary>
        /// รายชื่อพนักงานที่ยังใช้งานอยู่
        /// ส่ง null ใน <paramref name="branchCode"/> เพื่อดึงทุกสาขา
        /// (ใช้กับหน้า mock login เท่านั้น — หน้าจอจริงต้อง filter สาขาตาม BR-6)
        /// </summary>
        IReadOnlyList<Employee> ListByBranch(string branchCode);
    }
}
