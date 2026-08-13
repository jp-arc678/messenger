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
        /// ดึงใบแจ้งงานพร้อม "ประเภทงาน + รายละเอียดต่อประเภท" มาให้ครบในตัว
        ///
        /// spDeliveryRequestList คืน result set 2 ชุด (ใบงาน / ประเภทงานของใบงานทั้งชุด)
        /// จึงยังเป็นการวิ่ง DB รอบเดียว ไม่ใช่ N+1 แบบเรียก spRequestJobTypeListByReq ทีละใบ
        /// </summary>
        public IReadOnlyList<DeliveryRequest> List(RequestListFilter filter)
        {
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

            if (string.IsNullOrWhiteSpace(filter.BranchCode))
                throw new ArgumentException("ต้องระบุรหัสสาขา (BR-6)", nameof(filter));

            using (var connection = _connectionFactory.CreateConnection())
            {
                var parameters = new DynamicParameters();
                parameters.Add("BranchCode", filter.BranchCode);
                parameters.Add("RequesterEmpCode",
                    string.IsNullOrWhiteSpace(filter.RequesterEmpCode) ? null : filter.RequesterEmpCode.Trim());
                parameters.Add("SendDateFrom", filter.SendDateFrom?.Date, DbType.Date);
                parameters.Add("SendDateTo", filter.SendDateTo?.Date, DbType.Date);
                parameters.Add("RequestDateFrom", filter.RequestDateFrom?.Date, DbType.Date);
                parameters.Add("RequestDateTo", filter.RequestDateTo?.Date, DbType.Date);
                parameters.Add("Status",
                    filter.Status.HasValue ? RequestStatuses.ToCode(filter.Status.Value) : null);

                using (var results = connection.QueryMultiple(
                    "dbo.spDeliveryRequestList", parameters,
                    commandType: CommandType.StoredProcedure))
                {
                    var requests = results.Read<DeliveryRequestRow>()
                        .Select(r => r.ToEntity())
                        .ToList();

                    var jobTypesByReq = results.Read<RequestJobTypeRow>()
                        .GroupBy(j => j.ReqId)
                        .ToDictionary(
                            group => group.Key,
                            group => (IList<RequestJobType>)group.Select(j => j.ToEntity()).ToList());

                    // ToEntity() ตั้ง JobTypes จาก JobTypeCodes ของ view ไว้แล้วแต่ไม่มี DetailText
                    // จึงทับด้วยแถวจริงเสมอ ใบที่ไม่มีประเภทงานเลยได้ list ว่างไปตามเดิม
                    foreach (var request in requests)
                    {
                        IList<RequestJobType> jobTypes;
                        request.JobTypes = jobTypesByReq.TryGetValue(request.ReqId, out jobTypes)
                            ? jobTypes
                            : new List<RequestJobType>();
                    }

                    return requests;
                }
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
