using System.Web.Mvc;
using Messenger.Application.Dtos;
using Messenger.Web.Security;

namespace Messenger.Web.Controllers
{
    /// <summary>
    /// Controller ฐานที่ให้ทุกหน้าเข้าถึงผู้ใช้ปัจจุบันได้
    ///
    /// สำคัญ : <see cref="CurrentUser"/> เป็นแหล่งเดียวของ branchCode ที่จะถูกส่ง
    /// ลงไปให้ service layer ใช้บังคับ BR-6 — ห้ามรับ branchCode จาก querystring
    /// หรือ form ของผู้ใช้เด็ดขาด
    /// </summary>
    public abstract class BaseController : Controller
    {
        protected UserContext CurrentUser => MessengerPrincipal.CurrentUser;
    }
}
