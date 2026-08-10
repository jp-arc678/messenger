using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Messenger.Application.Abstractions;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Data;

namespace Messenger.Infrastructure.Repositories
{
    /// <summary>
    /// เข้าถึงข้อมูลพนักงานผ่าน stored procedure เท่านั้น (ไม่มี SQL inline)
    /// </summary>
    public class EmployeeRepository : IEmployeeRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public EmployeeRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public Employee GetByEmpCode(string empCode)
        {
            if (string.IsNullOrWhiteSpace(empCode))
                return null;

            using (var connection = _connectionFactory.CreateConnection())
            {
                var row = connection.QuerySingleOrDefault<EmployeeRow>(
                    "dbo.usp_Employee_GetByEmpCode",
                    new { EmpCode = empCode.Trim() },
                    commandType: CommandType.StoredProcedure);

                return row?.ToEntity();
            }
        }

        public Employee UpsertFromSso(SsoUserInfo info)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            using (var connection = _connectionFactory.CreateConnection())
            {
                var row = connection.QuerySingleOrDefault<EmployeeRow>(
                    "dbo.usp_Employee_UpsertFromSso",
                    new
                    {
                        EmpCode = info.EmpCode,
                        FullName = info.FullName,
                        DeptCode = info.DeptCode,
                        UnitName = info.UnitName,
                        PhoneExt = info.PhoneExt,
                        Email = info.Email,
                        BranchCode = info.BranchCode
                    },
                    commandType: CommandType.StoredProcedure);

                return row?.ToEntity();
            }
        }

        public IReadOnlyList<Employee> ListByBranch(string branchCode)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                var rows = connection.Query<EmployeeRow>(
                    "dbo.usp_Employee_ListByBranch",
                    new { BranchCode = string.IsNullOrWhiteSpace(branchCode) ? null : branchCode.Trim() },
                    commandType: CommandType.StoredProcedure);

                return rows.Select(r => r.ToEntity()).ToList();
            }
        }
    }
}
