using System;
using System.Collections.Generic;
using Messenger.Application.Abstractions;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;

namespace Messenger.Application.Services
{
    /// <summary>
    /// จัดการการ login และการ resolve สิทธิ์
    ///
    /// ลำดับการทำงานของ <see cref="SignIn"/> :
    ///   1. ถาม SSO ว่ารู้จักรหัสพนักงานนี้ไหม (BR-7)
    ///   2. ตรวจว่าสาขาที่ SSO ส่งมาใช้งานได้จริง (BR-6)
    ///   3. cache ข้อมูลลงตาราง Employee + ให้ role เริ่มต้นเป็น User ถ้าเป็นคนใหม่ (D10)
    ///   4. คืน UserContext ที่มีทั้งตัวตนและสาขา ให้ชั้นบนเอาไปใช้บังคับ scope
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly ISsoClient _sso;
        private readonly IEmployeeRepository _employees;
        private readonly IBranchRepository _branches;

        public AuthService(ISsoClient sso, IEmployeeRepository employees, IBranchRepository branches)
        {
            _sso = sso ?? throw new ArgumentNullException(nameof(sso));
            _employees = employees ?? throw new ArgumentNullException(nameof(employees));
            _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        }

        public SignInResult SignIn(string empCode)
        {
            if (string.IsNullOrWhiteSpace(empCode))
                return SignInResult.Fail("กรุณาระบุรหัสพนักงาน");

            empCode = empCode.Trim();

            var ssoUser = _sso.GetUserInfo(empCode);
            if (ssoUser == null)
                return SignInResult.Fail($"ระบบ SSO ไม่พบรหัสพนักงาน '{empCode}'");

            if (string.IsNullOrWhiteSpace(ssoUser.BranchCode))
                return SignInResult.Fail($"ระบบ SSO ไม่ได้ส่งรหัสสาขาของพนักงาน '{empCode}' มาด้วย");

            if (!IsKnownBranch(ssoUser.BranchCode))
                return SignInResult.Fail($"รหัสสาขา '{ssoUser.BranchCode}' ไม่มีอยู่ในระบบ หรือถูกปิดใช้งาน");

            // BR-7 — cache ข้อมูลจาก SSO ลง DB, คนใหม่จะได้ role User อัตโนมัติ (D10)
            var employee = _employees.UpsertFromSso(ssoUser);
            if (employee == null)
                return SignInResult.Fail($"ไม่สามารถบันทึกข้อมูลพนักงาน '{empCode}' ลงระบบได้");

            if (!employee.IsActive)
                return SignInResult.Fail($"พนักงาน '{empCode}' ถูกปิดการใช้งานในระบบ");

            return SignInResult.Ok(ToUserContext(employee));
        }

        public IReadOnlyList<SsoUserInfo> ListSelectableUsers()
        {
            return _sso.ListKnownUsers();
        }

        private bool IsKnownBranch(string branchCode)
        {
            var branches = _branches.ListActive();
            if (branches == null)
                return false;

            foreach (var branch in branches)
            {
                if (string.Equals(branch.BranchCode, branchCode, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static UserContext ToUserContext(Employee employee)
        {
            return new UserContext
            {
                EmpCode = employee.EmpCode,
                FullName = employee.FullName,
                DeptCode = employee.DeptCode,
                UnitName = employee.UnitName,
                PhoneExt = employee.PhoneExt,
                Email = employee.Email,
                BranchCode = employee.BranchCode,
                BranchName = employee.BranchName,
                Role = employee.Role
            };
        }
    }
}
