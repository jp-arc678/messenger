using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Messenger.Application.Services;
using Messenger.Domain.Entities;
using Messenger.Domain.Workflow;
using Messenger.Web.ViewModels;

namespace Messenger.Web.Controllers
{
    /// <summary>
    /// ใบแจ้งงาน — สร้าง / แก้ไข / ดู / รายการ
    ///
    /// Controller ไม่มี business rule เลย หน้าที่มีแค่
    /// แปลงฟอร์ม → command → เรียก service → แปลงผลลัพธ์เป็นหน้าจอ
    /// การตัดสินสิทธิ์ สาขา และความถูกต้องทั้งหมดอยู่ที่ service layer
    /// </summary>
    public class RequestsController : BaseController
    {
        private readonly IDeliveryRequestService _service;
        private readonly IRequestWorkflowService _workflow;

        public RequestsController(IDeliveryRequestService service, IRequestWorkflowService workflow)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        [HttpGet]
        public ActionResult Index(string sendDateFrom, string sendDateTo,
                                  string requestDateFrom, string requestDateTo, string status)
        {
            var query = new RequestListQuery
            {
                SendDateFrom = RequestFormViewModel.ParseDate(sendDateFrom),
                SendDateTo = RequestFormViewModel.ParseDate(sendDateTo),
                RequestDateFrom = RequestFormViewModel.ParseDate(requestDateFrom),
                RequestDateTo = RequestFormViewModel.ParseDate(requestDateTo),
                Status = RequestListViewModel.ParseStatus(status)
            };

            var model = new RequestListViewModel
            {
                Requests = _service.List(CurrentUser, query),
                SendDateFrom = sendDateFrom,
                SendDateTo = sendDateTo,
                RequestDateFrom = requestDateFrom,
                RequestDateTo = requestDateTo,
                Status = status,
                SeesWholeBranch = CurrentUser.IsAdmin || CurrentUser.IsMessenger,
                BranchCode = CurrentUser.BranchCode,
                BranchName = CurrentUser.BranchName,
                Message = TempData["Message"] as string,
                ErrorMessage = TempData["Error"] as string
            };

            return View(model);
        }

        [HttpGet]
        public ActionResult Details(int id)
        {
            var result = _service.Get(id, CurrentUser);
            if (!result.Success)
                return HttpNotFoundWithMessage(result.FirstError);

            var history = _workflow.GetHistory(id, CurrentUser);

            var model = new RequestDetailsViewModel
            {
                Request = result.Value,
                CanEdit = _service.CanEdit(result.Value, CurrentUser),
                Actions = _workflow.AvailableActions(result.Value, CurrentUser),
                History = history.Success ? history.Value : new List<StatusHistoryEntry>(),
                Message = TempData["Message"] as string,
                ErrorMessage = TempData["Error"] as string
            };

            return View(model);
        }

        /// <summary>
        /// เปลี่ยนสถานะใบงาน 1 ครั้ง (§6) — ใช้ร่วมกันทั้งหน้ารายละเอียดและหน้าคิวงาน
        ///
        /// controller ไม่ตัดสินอะไรเลยว่าเปลี่ยนได้ไหม ส่งต่อให้ service ทั้งหมด
        /// แล้วแปลงผลลัพธ์เป็นข้อความบนหน้าจอเท่านั้น
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChangeStatus(int id, RequestAction statusAction, string reason,
                                         string returnTo, string queueDate)
        {
            var result = _workflow.Apply(id, statusAction, reason, CurrentUser);

            if (result.Success)
            {
                TempData["Message"] = $"{ActionName(statusAction)}ใบแจ้งงาน {result.Value.ReqNo} เรียบร้อยแล้ว";
            }
            else
            {
                TempData["Error"] = string.Join(" · ", result.Errors);
            }

            if (string.Equals(returnTo, "queue", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Queue", new { date = queueDate });

            return RedirectToAction("Details", new { id });
        }

        [HttpGet]
        public ActionResult Create()
        {
            var model = new RequestFormViewModel
            {
                RequesterEmpCode = CurrentUser.EmpCode,
                SendDate = RequestFormViewModel.FormatDate(_service.GetDefaultSendDate()),
                JobTypes = RequestFormViewModel.BuildEmptyJobTypes(),
                SelectableRequesters = _service.GetSelectableRequesters(CurrentUser)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(RequestFormViewModel form)
        {
            NormalizeForm(form);

            var sendDate = RequestFormViewModel.ParseDate(form.SendDate);
            if (sendDate == null)
                return RedisplayCreate(form, "รูปแบบวันที่ส่งไม่ถูกต้อง");

            var command = new CreateRequestCommand
            {
                RequesterEmpCode = form.RequesterEmpCode,
                SendDate = sendDate,
                ContactName = form.ContactName,
                Address = form.Address,
                Phone = form.Phone,
                Detail = form.Detail,
                IsPersonal = form.IsPersonal,
                JobTypes = form.ToJobTypeInputs()
            };

            var result = _service.Create(command, CurrentUser);
            if (!result.Success)
                return RedisplayCreate(form, result.Errors);

            TempData["Message"] = $"สร้างใบแจ้งงาน {result.Value.ReqNo} เรียบร้อยแล้ว";
            return RedirectToAction("Details", new { id = result.Value.ReqId });
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            var result = _service.Get(id, CurrentUser);
            if (!result.Success)
                return HttpNotFoundWithMessage(result.FirstError);

            var request = result.Value;

            if (!_service.CanEdit(request, CurrentUser))
            {
                TempData["Message"] = "ใบแจ้งงานนี้แก้ไขไม่ได้แล้ว";
                return RedirectToAction("Details", new { id });
            }

            return View(ToForm(request));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(RequestFormViewModel form)
        {
            NormalizeForm(form);

            var sendDate = RequestFormViewModel.ParseDate(form.SendDate);
            if (sendDate == null)
                return RedisplayEdit(form, "รูปแบบวันที่ส่งไม่ถูกต้อง");

            var command = new UpdateRequestCommand
            {
                ReqId = form.ReqId,
                RequesterEmpCode = form.RequesterEmpCode,
                SendDate = sendDate.Value,
                ContactName = form.ContactName,
                Address = form.Address,
                Phone = form.Phone,
                Detail = form.Detail,
                IsPersonal = form.IsPersonal,
                RowVersion = form.RowVersionBytes(),
                JobTypes = form.ToJobTypeInputs()
            };

            var result = _service.Update(command, CurrentUser);
            if (!result.Success)
                return RedisplayEdit(form, result.Errors);

            TempData["Message"] = $"บันทึกการแก้ไขใบแจ้งงาน {result.Value.ReqNo} เรียบร้อยแล้ว";
            return RedirectToAction("Details", new { id = form.ReqId });
        }

        // ---------------- helpers ----------------

        /// <summary>ชื่อการกระทำสำหรับข้อความยืนยัน — อ่านจากตาราง §6 ที่เดียว</summary>
        private static string ActionName(RequestAction action)
        {
            return RequestStateMachine.All
                       .Where(t => t.Action == action)
                       .Select(t => t.DisplayName)
                       .FirstOrDefault() ?? "เปลี่ยนสถานะ";
        }

        private RequestFormViewModel ToForm(DeliveryRequest request)
        {
            return new RequestFormViewModel
            {
                ReqId = request.ReqId,
                ReqNo = request.ReqNo,
                RowVersion = request.RowVersion == null ? null : Convert.ToBase64String(request.RowVersion),
                RequesterEmpCode = request.RequesterEmpCode,
                SendDate = RequestFormViewModel.FormatDate(request.SendDate),
                ContactName = request.ContactName,
                Address = request.Address,
                Phone = request.Phone,
                Detail = request.Detail,
                IsPersonal = request.IsPersonal,
                JobTypes = RequestFormViewModel.BuildJobTypesFrom(request.JobTypes),
                SelectableRequesters = _service.GetSelectableRequesters(CurrentUser),
                StatusDisplayName = request.StatusDisplayName
            };
        }

        /// <summary>
        /// ฟอร์มที่ POST กลับมาจะไม่มีข้อมูลประกอบหน้าจอ (รายชื่อผู้แจ้ง, ช่อง checkbox ที่ไม่ได้ติ๊ก)
        /// จึงต้องเติมกลับก่อนแสดงผลซ้ำ
        /// </summary>
        private void NormalizeForm(RequestFormViewModel form)
        {
            if (form.JobTypes == null || form.JobTypes.Count == 0)
                form.JobTypes = RequestFormViewModel.BuildEmptyJobTypes();
        }

        private ActionResult RedisplayCreate(RequestFormViewModel form, params string[] errors)
        {
            return RedisplayCreate(form, (IReadOnlyList<string>)errors);
        }

        private ActionResult RedisplayCreate(RequestFormViewModel form, IReadOnlyList<string> errors)
        {
            form.Errors = errors;
            form.SelectableRequesters = _service.GetSelectableRequesters(CurrentUser);
            return View("Create", form);
        }

        private ActionResult RedisplayEdit(RequestFormViewModel form, params string[] errors)
        {
            return RedisplayEdit(form, (IReadOnlyList<string>)errors);
        }

        private ActionResult RedisplayEdit(RequestFormViewModel form, IReadOnlyList<string> errors)
        {
            form.Errors = errors;
            form.SelectableRequesters = _service.GetSelectableRequesters(CurrentUser);

            // ดึงสถานะล่าสุดมาแสดงหัวฟอร์ม (ถ้ายังอ่านได้)
            var current = _service.Get(form.ReqId, CurrentUser);
            if (current.Success)
            {
                form.ReqNo = current.Value.ReqNo;
                form.StatusDisplayName = current.Value.StatusDisplayName;
            }

            return View("Edit", form);
        }

        private ActionResult HttpNotFoundWithMessage(string message)
        {
            Response.StatusCode = 404;
            return View("RequestNotFound", model: message);
        }
    }
}
