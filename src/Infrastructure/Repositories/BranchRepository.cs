using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Messenger.Application.Abstractions;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Data;

namespace Messenger.Infrastructure.Repositories
{
    /// <summary>เข้าถึงข้อมูลสาขาผ่าน stored procedure</summary>
    public class BranchRepository : IBranchRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public BranchRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public IReadOnlyList<Branch> ListActive()
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                var branches = connection.Query<Branch>(
                    "dbo.usp_Branch_List",
                    commandType: CommandType.StoredProcedure);

                // BranchCode เป็น CHAR(3) จึงถูก pad ช่องว่างมาจาก DB ต้อง trim ก่อนใช้เทียบ
                return branches
                    .Select(b =>
                    {
                        b.BranchCode = b.BranchCode?.Trim();
                        return b;
                    })
                    .ToList();
            }
        }
    }
}
