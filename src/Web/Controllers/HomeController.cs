using System.Web.Mvc;

namespace Messenger.Web.Controllers
{
    public class HomeController : BaseController
    {
        /// <summary>
        /// หน้าแรกหลัง login — เฟส 0 ยังไม่มีเมนูงานจริง
        /// แสดงตัวตนที่ resolve ได้ เพื่อยืนยันว่า SSO + role + สาขา ทำงานถูก
        /// </summary>
        public ActionResult Index()
        {
            return View(CurrentUser);
        }
    }
}
