using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Messenger.Application.Abstractions;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;
using Messenger.Infrastructure.Data;

namespace Messenger.Infrastructure.Repositories
{
    /// <summary>
    /// ข้อมูลรูปยืนยัน (metadata + path) ผ่าน stored procedure เท่านั้น
    /// ตัวไฟล์จริงอยู่กับ <see cref="IPhotoFileStorage"/> คนละที่กัน
    /// </summary>
    public class DeliveryPhotoRepository : IDeliveryPhotoRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public DeliveryPhotoRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public int Add(AddPhotoData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            RequireBranch(data.BranchCode);

            using (var connection = _connectionFactory.CreateConnection())
            {
                var parameters = new DynamicParameters();
                parameters.Add("ReqId", data.ReqId);
                parameters.Add("BranchCode", data.BranchCode);
                parameters.Add("PhotoType", PhotoTypes.ToCode(data.PhotoType));
                parameters.Add("FilePath", data.FilePath);
                parameters.Add("FileName", data.FileName);
                parameters.Add("FileSizeBytes", data.FileSizeBytes);
                parameters.Add("CapturedAt", data.CapturedAt);
                parameters.Add("CapturedBy", data.CapturedBy);
                parameters.Add("PhotoId", dbType: DbType.Int32, direction: ParameterDirection.Output);

                connection.Execute("dbo.spDeliveryPhotoAdd", parameters,
                    commandType: CommandType.StoredProcedure);

                return parameters.Get<int>("PhotoId");
            }
        }

        public IReadOnlyList<DeliveryPhoto> ListByRequest(int reqId, string branchCode)
        {
            RequireBranch(branchCode);

            using (var connection = _connectionFactory.CreateConnection())
            {
                var rows = connection.Query<DeliveryPhotoRow>(
                    "dbo.spDeliveryPhotoListByReq",
                    new { ReqId = reqId, BranchCode = branchCode },
                    commandType: CommandType.StoredProcedure);

                return rows.Select(r => r.ToEntity()).ToList();
            }
        }

        public DeliveryPhoto GetById(int photoId, string branchCode)
        {
            RequireBranch(branchCode);

            using (var connection = _connectionFactory.CreateConnection())
            {
                var row = connection.QuerySingleOrDefault<DeliveryPhotoRow>(
                    "dbo.spDeliveryPhotoGetById",
                    new { PhotoId = photoId, BranchCode = branchCode },
                    commandType: CommandType.StoredProcedure);

                return row?.ToEntity();
            }
        }

        public int CountByRequest(int reqId, string branchCode)
        {
            RequireBranch(branchCode);

            using (var connection = _connectionFactory.CreateConnection())
            {
                var parameters = new DynamicParameters();
                parameters.Add("ReqId", reqId);
                parameters.Add("BranchCode", branchCode);
                parameters.Add("PhotoCount", dbType: DbType.Int32, direction: ParameterDirection.Output);

                connection.Execute("dbo.spDeliveryPhotoCountByReq", parameters,
                    commandType: CommandType.StoredProcedure);

                return parameters.Get<int>("PhotoCount");
            }
        }

        public bool Delete(int photoId, string branchCode)
        {
            RequireBranch(branchCode);

            using (var connection = _connectionFactory.CreateConnection())
            {
                var parameters = new DynamicParameters();
                parameters.Add("PhotoId", photoId);
                parameters.Add("BranchCode", branchCode);
                parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                connection.Execute("dbo.spDeliveryPhotoDelete", parameters,
                    commandType: CommandType.StoredProcedure);

                return parameters.Get<int>("RowsAffected") > 0;
            }
        }

        private static void RequireBranch(string branchCode)
        {
            if (string.IsNullOrWhiteSpace(branchCode))
                throw new ArgumentException("ต้องระบุรหัสสาขา (BR-6)", nameof(branchCode));
        }
    }
}
