using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Abstractions;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Application.Services
{
    /// <summary>
    /// Business logic ของใบแจ้งงาน
    ///
    /// กฎที่บังคับในคลาสนี้ :
    /// - BR-1  คำนวณ sendDate เริ่มต้น (ผ่าน <see cref="SendDateCalculator"/>)
    /// - BR-2  แก้ได้เฉพาะสถานะ Received และเฉพาะเจ้าของ ยกเว้น Admin + optimistic locking
    /// - BR-6  ทุก operation ใช้ branchCode จาก UserContext เท่านั้น ไม่รับจากฟอร์ม
    /// - BR-8  ประกอบเลขใบงาน (ผ่าน <see cref="ReqNoGenerator"/>)
    /// - D15   ContactName / Address / Detail บังคับกรอก
    /// - D16   sendDate ที่ผู้ใช้เลือกเอง ห้ามย้อนหลัง ห้ามเสาร์-อาทิตย์
    /// - D17   แจ้งแทนคนอื่นได้ แต่ต้องเป็นคนในสาขาเดียวกัน
    /// - D18   ต้องเลือกประเภทงานอย่างน้อย 1 ประเภท
    /// </summary>
    public class DeliveryRequestService : IDeliveryRequestService
    {
        private readonly IDeliveryRequestRepository _requests;
        private readonly IEmployeeRepository _employees;
        private readonly IClock _clock;

        public DeliveryRequestService(IDeliveryRequestRepository requests,
                                      IEmployeeRepository employees,
                                      IClock clock)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _employees = employees ?? throw new ArgumentNullException(nameof(employees));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public DateTime GetDefaultSendDate()
        {
            return SendDateCalculator.CalculateDefault(_clock.Now);
        }

        public IReadOnlyList<Employee> GetSelectableRequesters(UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            // BR-6 — เลือกได้เฉพาะคนในสาขาตัวเอง
            var employees = _employees.ListByBranch(user.BranchCode) ?? new List<Employee>();

            // กันกรณีที่ cache พนักงานไม่มีตัวผู้ใช้ปัจจุบัน (เช่น ข้อมูลถูกล้างไป
            // แต่ cookie login เดิมยังไม่หมดอายุ) ถ้าปล่อยให้รายการไม่มีตัวเอง
            // ช่อง "ผู้แจ้ง" จะตกไปเป็นคนแรกของรายการ แล้วใบงานจะถูกบันทึก
            // เป็นชื่อคนอื่นโดยผู้ใช้ไม่รู้ตัว
            var containsSelf = employees.Any(e =>
                string.Equals(e.EmpCode, user.EmpCode, StringComparison.OrdinalIgnoreCase));

            if (containsSelf)
                return employees;

            var self = new Employee
            {
                EmpCode = user.EmpCode,
                FullName = user.FullName,
                DeptCode = user.DeptCode,
                UnitName = user.UnitName,
                PhoneExt = user.PhoneExt,
                Email = user.Email,
                BranchCode = user.BranchCode,
                BranchName = user.BranchName,
                Role = user.Role,
                IsActive = true
            };

            var withSelf = new List<Employee> { self };
            withSelf.AddRange(employees);
            return withSelf;
        }

        public ServiceResult<DeliveryRequest> Create(CreateRequestCommand command, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            if (command == null)
                return ServiceResult<DeliveryRequest>.Fail("ไม่มีข้อมูลใบแจ้งงาน");

            var now = _clock.Now;

            // BR-1 — ถ้าผู้ใช้ไม่ได้เลือกวันเอง ให้ระบบคำนวณให้
            var sendDate = command.SendDate?.Date ?? SendDateCalculator.CalculateDefault(now);

            var requesterEmpCode = string.IsNullOrWhiteSpace(command.RequesterEmpCode)
                ? user.EmpCode
                : command.RequesterEmpCode.Trim();

            var errors = ValidateCommonFields(
                requesterEmpCode: requesterEmpCode,
                sendDate: sendDate,
                userPickedSendDate: command.SendDate.HasValue,
                contactName: command.ContactName,
                address: command.Address,
                detail: command.Detail,
                jobTypes: command.JobTypes,
                user: user);

            if (errors.Count > 0)
                return ServiceResult<DeliveryRequest>.Fail(errors);

            var data = new CreateRequestData
            {
                BranchCode = user.BranchCode,
                RequesterEmpCode = requesterEmpCode,
                RequestDateTime = now,
                SendDate = sendDate,
                ContactName = command.ContactName.Trim(),
                Address = command.Address.Trim(),
                Phone = Normalize(command.Phone),
                Detail = command.Detail.Trim(),
                IsPersonal = command.IsPersonal,
                CreatedBy = user.EmpCode,
                JobTypes = ToJobTypeEntities(command.JobTypes),
                YyMm = ReqNoGenerator.ToYyMm(now),

                // BR-8 — repository จะเรียกฟังก์ชันนี้ด้วยเลขลำดับที่ DB จองให้
                ReqNoFactory = running => ReqNoGenerator.Build(user.BranchCode, now, running)
            };

            var created = _requests.Create(data);
            if (created == null)
                return ServiceResult<DeliveryRequest>.Fail("บันทึกใบแจ้งงานไม่สำเร็จ");

            var saved = _requests.GetById(created.ReqId, user.BranchCode);
            return ServiceResult<DeliveryRequest>.Ok(saved);
        }

        public ServiceResult<DeliveryRequest> Update(UpdateRequestCommand command, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            if (command == null)
                return ServiceResult<DeliveryRequest>.Fail("ไม่มีข้อมูลใบแจ้งงาน");

            // BR-6 — อ่านด้วย branchCode ของผู้ใช้ ใบงานต่างสาขาจะหาไม่เจอตั้งแต่ต้น
            var existing = _requests.GetById(command.ReqId, user.BranchCode);
            if (existing == null)
                return ServiceResult<DeliveryRequest>.Fail("ไม่พบใบแจ้งงานนี้ในสาขาของคุณ");

            // BR-2 — ด่านสิทธิ์/สถานะ
            if (!CanEdit(existing, user))
                return ServiceResult<DeliveryRequest>.Fail(BuildCannotEditMessage(existing, user));

            if (command.RowVersion == null || command.RowVersion.Length == 0)
                return ServiceResult<DeliveryRequest>.Fail("ข้อมูลรุ่นของใบแจ้งงานหายไป กรุณาเปิดฟอร์มใหม่");

            var requesterEmpCode = string.IsNullOrWhiteSpace(command.RequesterEmpCode)
                ? existing.RequesterEmpCode
                : command.RequesterEmpCode.Trim();

            var errors = ValidateCommonFields(
                requesterEmpCode: requesterEmpCode,
                sendDate: command.SendDate.Date,
                // ตอนแก้ไข ผู้ใช้เป็นคนกำหนดวันเองเสมอ จึงต้องผ่านเงื่อนไข D16 ทุกครั้ง
                userPickedSendDate: true,
                contactName: command.ContactName,
                address: command.Address,
                detail: command.Detail,
                jobTypes: command.JobTypes,
                user: user);

            if (errors.Count > 0)
                return ServiceResult<DeliveryRequest>.Fail(errors);

            var data = new UpdateRequestData
            {
                ReqId = command.ReqId,
                BranchCode = user.BranchCode,
                RequesterEmpCode = requesterEmpCode,
                SendDate = command.SendDate.Date,
                ContactName = command.ContactName.Trim(),
                Address = command.Address.Trim(),
                Phone = Normalize(command.Phone),
                Detail = command.Detail.Trim(),
                IsPersonal = command.IsPersonal,
                UpdatedBy = user.EmpCode,
                RowVersion = command.RowVersion,
                JobTypes = ToJobTypeEntities(command.JobTypes)
            };

            if (!_requests.Update(data))
            {
                // BR-2 — rowVersion ไม่ตรง แปลว่ามีคนแก้ใบนี้ไปแล้วระหว่างที่เรากรอกฟอร์ม
                return ServiceResult<DeliveryRequest>.Conflict(
                    "ใบแจ้งงานนี้ถูกแก้ไขโดยผู้ใช้อื่นไปแล้ว กรุณาเปิดข้อมูลล่าสุดแล้วแก้ใหม่อีกครั้ง");
            }

            var saved = _requests.GetById(command.ReqId, user.BranchCode);
            return ServiceResult<DeliveryRequest>.Ok(saved);
        }

        public ServiceResult<DeliveryRequest> Get(int reqId, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var request = _requests.GetById(reqId, user.BranchCode);
            if (request == null)
                return ServiceResult<DeliveryRequest>.Fail("ไม่พบใบแจ้งงานนี้ในสาขาของคุณ");

            // §5 — U-User เห็นเฉพาะใบของตัวเอง
            if (!CanView(request, user))
                return ServiceResult<DeliveryRequest>.Fail("คุณไม่มีสิทธิ์ดูใบแจ้งงานนี้");

            return ServiceResult<DeliveryRequest>.Ok(request);
        }

        public IReadOnlyList<DeliveryRequest> List(UserContext user, DateTime? sendDateFrom, DateTime? sendDateTo)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            // §5 — Admin/Messenger เห็นทั้งสาขา, User เห็นเฉพาะใบตัวเอง
            var requesterFilter = SeesWholeBranch(user) ? null : user.EmpCode;

            return _requests.List(user.BranchCode, requesterFilter, sendDateFrom, sendDateTo);
        }

        public bool CanEdit(DeliveryRequest request, UserContext user)
        {
            if (request == null || user == null)
                return false;

            // BR-6 — ใบงานต่างสาขาแตะไม่ได้ ไม่ว่า role ไหน
            if (!SameBranch(request.BranchCode, user.BranchCode))
                return false;

            // §5 — Admin แก้ได้ทุกช่อง ทุกสถานะ (ในสาขาตัวเอง)
            if (user.IsAdmin)
                return true;

            // BR-2 — role อื่นแก้ได้เฉพาะใบของตัวเอง และเฉพาะตอนยังไม่ถูกยืนยัน
            return IsOwner(request, user) && request.Status == RequestStatus.Received;
        }

        private bool CanView(DeliveryRequest request, UserContext user)
        {
            if (!SameBranch(request.BranchCode, user.BranchCode))
                return false;

            return SeesWholeBranch(user) || IsOwner(request, user);
        }

        private static bool SeesWholeBranch(UserContext user)
        {
            return user.IsAdmin || user.IsMessenger;
        }

        private static bool IsOwner(DeliveryRequest request, UserContext user)
        {
            return string.Equals(request.RequesterEmpCode, user.EmpCode, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameBranch(string left, string right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildCannotEditMessage(DeliveryRequest request, UserContext user)
        {
            if (!IsOwner(request, user))
                return "คุณแก้ไขได้เฉพาะใบแจ้งงานที่ตัวเองเป็นผู้แจ้งเท่านั้น";

            return $"ใบแจ้งงานอยู่ในสถานะ \"{request.StatusDisplayName}\" จึงแก้ไขไม่ได้แล้ว " +
                   "(แก้ได้เฉพาะสถานะ \"รับแจ้ง\" ก่อน Messenger ยืนยันรับงาน)";
        }

        /// <summary>กฎที่ใช้ร่วมกันทั้งตอนสร้างและตอนแก้ไข</summary>
        private List<string> ValidateCommonFields(string requesterEmpCode,
                                                  DateTime sendDate,
                                                  bool userPickedSendDate,
                                                  string contactName,
                                                  string address,
                                                  string detail,
                                                  IReadOnlyList<JobTypeInput> jobTypes,
                                                  UserContext user)
        {
            var errors = new List<string>();

            // D15 — ฟิลด์บังคับกรอก
            if (string.IsNullOrWhiteSpace(contactName))
                errors.Add("กรุณาระบุชื่อผู้ติดต่อ");

            if (string.IsNullOrWhiteSpace(address))
                errors.Add("กรุณาระบุที่อยู่");

            if (string.IsNullOrWhiteSpace(detail))
                errors.Add("กรุณาระบุรายละเอียดงาน");

            // D18 — ต้องเลือกประเภทงานอย่างน้อย 1 ประเภท
            if (jobTypes == null || jobTypes.Count == 0)
            {
                errors.Add("กรุณาเลือกประเภทงานอย่างน้อย 1 ประเภท");
            }
            else if (jobTypes.Select(j => j.JobType).Distinct().Count() != jobTypes.Count)
            {
                errors.Add("เลือกประเภทงานซ้ำกัน");
            }

            // D16 — เงื่อนไขวันที่ส่งที่ผู้ใช้เลือกเอง
            if (userPickedSendDate)
            {
                var dateError = SendDateCalculator.ValidateUserPickedDate(sendDate, _clock.Today);
                if (dateError != null)
                    errors.Add(dateError);
            }

            // D17 + BR-6 — ผู้แจ้งต้องเป็นคนที่มีอยู่จริงและอยู่สาขาเดียวกับผู้ใช้
            var requesterError = ValidateRequester(requesterEmpCode, user);
            if (requesterError != null)
                errors.Add(requesterError);

            return errors;
        }

        private string ValidateRequester(string requesterEmpCode, UserContext user)
        {
            if (string.IsNullOrWhiteSpace(requesterEmpCode))
                return "กรุณาระบุผู้แจ้ง";

            var isSelf = string.Equals(requesterEmpCode, user.EmpCode, StringComparison.OrdinalIgnoreCase);

            // ตรวจกับ DB เสมอ แม้จะแจ้งในนามตัวเอง เพราะคอลัมน์ RequesterEmpCode
            // มี foreign key ไปที่ tblEmployee ถ้าไม่มีแถวนั้นจริง การบันทึกจะพัง
            // เป็น exception ระดับ DB แทนที่จะได้ข้อความที่ผู้ใช้เข้าใจ
            var requester = _employees.GetByEmpCode(requesterEmpCode);
            if (requester == null)
            {
                if (isSelf)
                {
                    return "ข้อมูลผู้ใช้ของคุณไม่มีอยู่ในระบบแล้ว " +
                           "กรุณาออกจากระบบแล้วเข้าสู่ระบบใหม่อีกครั้ง";
                }

                return $"ไม่พบพนักงานรหัส '{requesterEmpCode}'";
            }

            if (!SameBranch(requester.BranchCode, user.BranchCode))
                return "เลือกผู้แจ้งได้เฉพาะพนักงานในสาขาเดียวกันเท่านั้น";

            if (!requester.IsActive)
                return $"พนักงานรหัส '{requesterEmpCode}' ถูกปิดการใช้งานแล้ว";

            return null;
        }

        private static IReadOnlyList<RequestJobType> ToJobTypeEntities(IReadOnlyList<JobTypeInput> inputs)
        {
            if (inputs == null)
                return new List<RequestJobType>();

            return inputs
                .Select(i => new RequestJobType
                {
                    JobType = i.JobType,
                    DetailText = Normalize(i.DetailText)
                })
                .ToList();
        }

        /// <summary>ตัดช่องว่างหน้าหลัง และเปลี่ยนข้อความว่างเปล่าเป็น null</summary>
        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim();
        }
    }
}
