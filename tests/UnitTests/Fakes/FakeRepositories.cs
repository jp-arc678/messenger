using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Abstractions;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.UnitTests.Fakes
{
    /// <summary>สาขาปลอม — ตั้งต้นด้วย SDC และ SBK เหมือนของจริง</summary>
    public class FakeBranchRepository : IBranchRepository
    {
        private readonly List<Branch> _branches;

        public FakeBranchRepository(params string[] branchCodes)
        {
            var codes = branchCodes.Length > 0 ? branchCodes : new[] { "SDC", "SBK" };
            _branches = codes
                .Select(c => new Branch { BranchCode = c, BranchName = "สาขา " + c, IsActive = true })
                .ToList();
        }

        public IReadOnlyList<Branch> ListActive()
        {
            return _branches;
        }
    }

    /// <summary>
    /// พนักงานปลอมที่จำลองพฤติกรรมของ spEmployeeUpsertFromSso :
    /// คนที่ยังไม่เคยมี role จะได้ 'U' ตาม D10
    /// </summary>
    public class FakeEmployeeRepository : IEmployeeRepository
    {
        private readonly Dictionary<string, Employee> _employees =
            new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _roleCodes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int UpsertCallCount { get; private set; }

        /// <summary>ตั้งค่า RoleCode ที่ "มีอยู่แล้วใน DB" ของพนักงานคนหนึ่ง</summary>
        public FakeEmployeeRepository WithExistingRole(string empCode, string roleCode)
        {
            _roleCodes[empCode] = roleCode;
            return this;
        }

        public Employee GetByEmpCode(string empCode)
        {
            return _employees.TryGetValue(empCode ?? string.Empty, out var employee) ? employee : null;
        }

        public Employee UpsertFromSso(SsoUserInfo info)
        {
            UpsertCallCount++;

            // จำลอง SP : ถ้ายังไม่มีแถวใน UserRole ให้ใส่ 'U'
            if (!_roleCodes.ContainsKey(info.EmpCode))
                _roleCodes[info.EmpCode] = RoleCodes.User;

            var employee = new Employee
            {
                EmpCode = info.EmpCode,
                FullName = info.FullName,
                DeptCode = info.DeptCode,
                UnitName = info.UnitName,
                PhoneExt = info.PhoneExt,
                Email = info.Email,
                BranchCode = info.BranchCode,
                BranchName = "สาขา " + info.BranchCode,
                Role = RoleCodes.Parse(_roleCodes[info.EmpCode]),
                IsActive = true
            };

            _employees[info.EmpCode] = employee;
            return employee;
        }

        public IReadOnlyList<Employee> ListByBranch(string branchCode)
        {
            return _employees.Values
                .Where(e => branchCode == null || string.Equals(e.BranchCode, branchCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
