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
    /// เข้าถึงข้อมูลใบแจ้งงานผ่าน stored procedure เท่านั้น
    ///
    /// การสร้างและการแก้ไขถูกครอบด้วย transaction เดียว เพราะแต่ละงานกินหลาย
    /// statement (ขอเลขใบงาน + ใบงาน + ประเภทงาน + audit trail) ถ้าพังกลางทาง
    /// แล้วไม่ rollback จะเหลือเลขใบงานที่ถูกจองแต่ไม่มีใบงานจริง
    /// </summary>
    public class DeliveryRequestRepository : IDeliveryRequestRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public DeliveryRequestRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public CreatedRequest Create(CreateRequestData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (data.ReqNoFactory == null)
                throw new ArgumentException("ต้องระบุวิธีประกอบเลขใบงาน", nameof(data));

            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    // BR-8 — จองเลขลำดับของ (สาขา + เดือน) ไว้ภายใน transaction นี้
                    var sequenceParams = new DynamicParameters();
                    sequenceParams.Add("BranchCode", data.BranchCode);
                    sequenceParams.Add("YyMm", data.YyMm);
                    sequenceParams.Add("NextNumber", dbType: DbType.Int32, direction: ParameterDirection.Output);

                    connection.Execute("dbo.spReqNoNext", sequenceParams, transaction,
                        commandType: CommandType.StoredProcedure);

                    var runningNumber = sequenceParams.Get<int>("NextNumber");
                    var reqNo = data.ReqNoFactory(runningNumber);

                    var requestParams = new DynamicParameters();
                    requestParams.Add("ReqNo", reqNo);
                    requestParams.Add("BranchCode", data.BranchCode);
                    requestParams.Add("RequesterEmpCode", data.RequesterEmpCode);
                    requestParams.Add("RequestDateTime", data.RequestDateTime);
                    requestParams.Add("SendDate", data.SendDate.Date, DbType.Date);
                    requestParams.Add("ContactName", data.ContactName);
                    requestParams.Add("Address", data.Address);
                    requestParams.Add("Phone", data.Phone);
                    requestParams.Add("Detail", data.Detail);
                    requestParams.Add("IsPersonal", data.IsPersonal);
                    requestParams.Add("CreatedBy", data.CreatedBy);
                    requestParams.Add("ReqId", dbType: DbType.Int32, direction: ParameterDirection.Output);

                    connection.Execute("dbo.spDeliveryRequestCreate", requestParams, transaction,
                        commandType: CommandType.StoredProcedure);

                    var reqId = requestParams.Get<int>("ReqId");

                    InsertJobTypes(connection, transaction, reqId, data.JobTypes);

                    transaction.Commit();

                    return new CreatedRequest { ReqId = reqId, ReqNo = reqNo };
                }
            }
        }

        public bool Update(UpdateRequestData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("ReqId", data.ReqId);
                    parameters.Add("BranchCode", data.BranchCode);
                    parameters.Add("RequesterEmpCode", data.RequesterEmpCode);
                    parameters.Add("SendDate", data.SendDate.Date, DbType.Date);
                    parameters.Add("ContactName", data.ContactName);
                    parameters.Add("Address", data.Address);
                    parameters.Add("Phone", data.Phone);
                    parameters.Add("Detail", data.Detail);
                    parameters.Add("IsPersonal", data.IsPersonal);
                    parameters.Add("UpdatedBy", data.UpdatedBy);
                    parameters.Add("RowVersion", data.RowVersion, DbType.Binary, size: 8);
                    parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                    connection.Execute("dbo.spDeliveryRequestUpdate", parameters, transaction,
                        commandType: CommandType.StoredProcedure);

                    if (parameters.Get<int>("RowsAffected") == 0)
                    {
                        // BR-2 — rowVersion ไม่ตรง หรือใบงานอยู่คนละสาขา
                        transaction.Rollback();
                        return false;
                    }

                    // ประเภทงานใช้วิธีลบทิ้งแล้วใส่ชุดใหม่ เพราะจำนวนน้อย (สูงสุด 6 แถว)
                    // และทำให้ไม่ต้องเทียบทีละรายการว่าอะไรเพิ่ม/ลบ/แก้
                    connection.Execute("dbo.spRequestJobTypeClear", new { ReqId = data.ReqId }, transaction,
                        commandType: CommandType.StoredProcedure);

                    InsertJobTypes(connection, transaction, data.ReqId, data.JobTypes);

                    transaction.Commit();
                    return true;
                }
            }
        }

        public DeliveryRequest GetById(int reqId, string branchCode)
        {
            if (string.IsNullOrWhiteSpace(branchCode))
                throw new ArgumentException("ต้องระบุรหัสสาขา (BR-6)", nameof(branchCode));

            using (var connection = _connectionFactory.CreateConnection())
            {
                var row = connection.QuerySingleOrDefault<DeliveryRequestRow>(
                    "dbo.spDeliveryRequestGetById",
                    new { ReqId = reqId, BranchCode = branchCode },
                    commandType: CommandType.StoredProcedure);

                if (row == null)
                    return null;

                var request = row.ToEntity();

                var jobTypes = connection.Query<RequestJobTypeRow>(
                    "dbo.spRequestJobTypeListByReq",
                    new { ReqId = reqId },
                    commandType: CommandType.StoredProcedure);

                request.JobTypes = jobTypes.Select(j => j.ToEntity()).ToList();

                return request;
            }
        }

        /// <summary>
        /// หมายเหตุ : รายการนี้ "ไม่" ดึงประเภทงานมาด้วย เพื่อเลี่ยง query แบบ N+1
        /// หน้าจอที่ต้องเห็นประเภทงานให้เปิดดูรายใบผ่าน <see cref="GetById"/>
        /// </summary>
        public IReadOnlyList<DeliveryRequest> List(string branchCode, string requesterEmpCode,
                                                   DateTime? sendDateFrom, DateTime? sendDateTo)
        {
            if (string.IsNullOrWhiteSpace(branchCode))
                throw new ArgumentException("ต้องระบุรหัสสาขา (BR-6)", nameof(branchCode));

            using (var connection = _connectionFactory.CreateConnection())
            {
                var parameters = new DynamicParameters();
                parameters.Add("BranchCode", branchCode);
                parameters.Add("RequesterEmpCode",
                    string.IsNullOrWhiteSpace(requesterEmpCode) ? null : requesterEmpCode.Trim());
                parameters.Add("SendDateFrom", sendDateFrom?.Date, DbType.Date);
                parameters.Add("SendDateTo", sendDateTo?.Date, DbType.Date);

                var rows = connection.Query<DeliveryRequestRow>(
                    "dbo.spDeliveryRequestList", parameters,
                    commandType: CommandType.StoredProcedure);

                return rows.Select(r => r.ToEntity()).ToList();
            }
        }

        private static void InsertJobTypes(IDbConnection connection, IDbTransaction transaction,
                                           int reqId, IReadOnlyList<RequestJobType> jobTypes)
        {
            if (jobTypes == null)
                return;

            foreach (var jobType in jobTypes)
            {
                connection.Execute("dbo.spRequestJobTypeAdd",
                    new
                    {
                        ReqId = reqId,
                        JobType = Domain.Enums.JobTypes.ToCode(jobType.JobType),
                        DetailText = jobType.DetailText
                    },
                    transaction,
                    commandType: CommandType.StoredProcedure);
            }
        }
    }
}
