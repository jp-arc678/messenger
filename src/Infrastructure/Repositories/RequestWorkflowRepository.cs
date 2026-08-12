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
    /// การเปลี่ยนสถานะและคิวงานของ Messenger — เรียกผ่าน stored procedure เท่านั้น
    ///
    /// ไม่มี transaction ฝั่ง C# เพราะ procedure แต่ละตัวครอบ transaction ของตัวเองไว้แล้ว
    /// (การเปลี่ยนสถานะ + audit trail + เหตุผล ต้องสำเร็จหรือล้มพร้อมกันเสมอ)
    /// </summary>
    public class RequestWorkflowRepository : IRequestWorkflowRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public RequestWorkflowRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public bool ChangeStatus(StatusChangeData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            RequireBranch(data.BranchCode);

            using (var connection = _connectionFactory.CreateConnection())
            {
                var parameters = new DynamicParameters();
                parameters.Add("ReqId", data.ReqId);
                parameters.Add("BranchCode", data.BranchCode);
                parameters.Add("FromStatus", RequestStatuses.ToCode(data.FromStatus));
                parameters.Add("ToStatus", RequestStatuses.ToCode(data.ToStatus));
                parameters.Add("ByEmpCode", data.ByEmpCode);
                parameters.Add("ChangedAt", data.ChangedAt);
                parameters.Add("Note", data.Note);
                parameters.Add("Reason", data.Reason);
                parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                connection.Execute("dbo.spDeliveryRequestChangeStatus", parameters,
                    commandType: CommandType.StoredProcedure);

                return parameters.Get<int>("RowsAffected") > 0;
            }
        }

        public MessengerAssignment Confirm(ConfirmAssignmentData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            RequireBranch(data.BranchCode);

            using (var connection = _connectionFactory.CreateConnection())
            {
                var parameters = new DynamicParameters();
                parameters.Add("ReqId", data.ReqId);
                parameters.Add("BranchCode", data.BranchCode);
                parameters.Add("MessengerEmpCode", data.MessengerEmpCode);
                parameters.Add("ByEmpCode", data.ByEmpCode);
                parameters.Add("ConfirmedAt", data.ConfirmedAt);
                parameters.Add("Note", data.Note);
                parameters.Add("SequenceOrder", dbType: DbType.Int32, direction: ParameterDirection.Output);

                connection.Execute("dbo.spMessengerAssignmentConfirm", parameters,
                    commandType: CommandType.StoredProcedure);

                var sequenceOrder = parameters.Get<int?>("SequenceOrder");

                // ไม่มีเลขลำดับกลับมา = ใบงานไม่ได้อยู่สถานะ Received แล้ว (มีคนยืนยันตัดหน้า)
                if (!sequenceOrder.HasValue)
                    return null;

                return new MessengerAssignment
                {
                    ReqId = data.ReqId,
                    MessengerEmpCode = data.MessengerEmpCode,
                    ConfirmedAt = data.ConfirmedAt,
                    SequenceOrder = sequenceOrder.Value
                };
            }
        }

        public bool SwapSequence(int reqIdA, int reqIdB, string branchCode)
        {
            RequireBranch(branchCode);

            using (var connection = _connectionFactory.CreateConnection())
            {
                var parameters = new DynamicParameters();
                parameters.Add("ReqIdA", reqIdA);
                parameters.Add("ReqIdB", reqIdB);
                parameters.Add("BranchCode", branchCode);
                parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                connection.Execute("dbo.spMessengerAssignmentSwapSequence", parameters,
                    commandType: CommandType.StoredProcedure);

                return parameters.Get<int>("RowsAffected") > 0;
            }
        }

        public bool ConfirmReceipt(ReceiptConfirmData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            RequireBranch(data.BranchCode);

            using (var connection = _connectionFactory.CreateConnection())
            {
                var parameters = new DynamicParameters();
                parameters.Add("ReqId", data.ReqId);
                parameters.Add("BranchCode", data.BranchCode);
                parameters.Add("ByEmpCode", data.ByEmpCode);
                parameters.Add("ConfirmedAt", data.ConfirmedAt);
                parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                connection.Execute("dbo.spDeliveryRequestConfirmReceipt", parameters,
                    commandType: CommandType.StoredProcedure);

                return parameters.Get<int>("RowsAffected") > 0;
            }
        }

        public IReadOnlyList<StatusHistoryEntry> ListHistory(int reqId, string branchCode)
        {
            RequireBranch(branchCode);

            using (var connection = _connectionFactory.CreateConnection())
            {
                var rows = connection.Query<StatusHistoryRow>(
                    "dbo.spStatusHistoryListByReq",
                    new { ReqId = reqId, BranchCode = branchCode },
                    commandType: CommandType.StoredProcedure);

                return rows.Select(r => r.ToEntity()).ToList();
            }
        }

        private static void RequireBranch(string branchCode)
        {
            if (string.IsNullOrWhiteSpace(branchCode))
                throw new ArgumentException("ต้องระบุรหัสสาขา (BR-6)", nameof(branchCode));
        }
    }
}
