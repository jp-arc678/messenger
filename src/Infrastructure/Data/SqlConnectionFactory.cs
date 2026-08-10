using System;
using System.Data;
using System.Data.SqlClient;

namespace Messenger.Infrastructure.Data
{
    /// <summary>
    /// Implementation จริงที่ต่อเข้า SQL Server
    /// connection string ถูกส่งเข้ามาจาก composition root (ชั้น Web)
    /// เพื่อไม่ให้ Infrastructure ผูกกับ System.Configuration โดยตรง
    /// </summary>
    public class SqlConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public SqlConnectionFactory(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("ต้องระบุ connection string", nameof(connectionString));

            _connectionString = connectionString;
        }

        public IDbConnection CreateConnection()
        {
            return new SqlConnection(_connectionString);
        }
    }
}
