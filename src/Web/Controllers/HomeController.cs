using System.Web.Mvc;

namespace Messenger.Web.Controllers
{
    public class HomeController : BaseController
    {
        /// <summary>
        /// หน้าแรกหลัง login — แสดงตัวตนที่ resolve ได้ (สาขา/สิทธิ์ที่จะใช้บังคับ scope)
        /// พร้อมทางลัดไปยังงานที่ role นั้นทำได้
        /// </summary>
        public ActionResult Index()
        {
            return View(CurrentUser);
        }

        /// <summary>
        /// หน้าแสดงข้อผิดพลาดของระบบ — ปลายทางของ customErrors ใน Web.config
        /// ไม่แสดงรายละเอียด exception ให้ผู้ใช้เห็นโดยเด็ดขาด
        /// </summary>
        [AllowAnonymous]
        public ActionResult Error()
        {
            Response.StatusCode = 500;
            Response.TrySkipIisCustomErrors = true;

            return View("Error", new HandleErrorInfo(
                new System.Exception("ระบบทำงานผิดพลาด"), "Home", "Error"));
        }

        /// <summary>ปลายทางของ 404 — ที่อยู่ไม่มีจริง หรือข้อมูลอยู่นอกสาขาของผู้ใช้ (BR-6)</summary>
        [AllowAnonymous]
        public ActionResult NotFound()
        {
            Response.StatusCode = 404;
            Response.TrySkipIisCustomErrors = true;

            return View("NotFound");
        }
    }
}
