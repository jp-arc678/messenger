using System.Threading;
using System.Web.Mvc;
using System.Web.Security;
using Messenger.Application.Services;

namespace Messenger.Web.Security
{
    /// <summary>
    /// อ่านสาขา/สิทธิ์ของผู้ใช้ใหม่จาก DB ทุก request (Phase 6 — hardening ของ BR-6)
    ///
    /// ปัญหาที่แก้ : ตัวตนของผู้ใช้ถูกเก็บไว้ใน Forms Authentication ticket ซึ่งอยู่ได้ 8 ชั่วโมง
    /// ถ้าเชื่อค่าใน ticket อย่างเดียว คนที่ถูกย้ายสาขาจะยังเห็นข้อมูลสาขาเดิมได้ทั้งวัน
    /// และคนที่เพิ่งถูกลดสิทธิ์จาก Admin ก็ยังใช้สิทธิ์ Admin ได้จนกว่าจะ logout
    ///
    /// ราคาที่จ่าย : query พนักงาน 1 ครั้งต่อ 1 request ของ MVC (ไม่รวมไฟล์ static)
    /// ซึ่งเป็นราคาที่ยอมรับได้สำหรับระบบภายในองค์กร และคุ้มกับการที่ BR-6 ไม่มีช่องโหว่เวลา
    ///
    /// วางเป็น global filter ต่อจาก <see cref="AuthorizeAttribute"/> จึงทำงานเฉพาะ
    /// request ที่ผ่านการ login มาแล้วเท่านั้น
    /// </summary>
    public class RefreshUserContextAttribute : FilterAttribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationContext filterContext)
        {
            var principal = filterContext.HttpContext.User as MessengerPrincipal;
            if (principal == null)
                return;   // ยังไม่ได้ login — ปล่อยให้ AuthorizeAttribute จัดการ

            var authService = DependencyResolver.Current.GetService<IAuthService>();
            if (authService == null)
                return;

            var fresh = authService.ResolveCurrent(principal.User.EmpCode);

            if (fresh == null)
            {
                // พนักงานถูกลบ/ปิดการใช้งาน/สาขาถูกปิด → ไล่ออกจากระบบทันที
                FormsAuthentication.SignOut();
                filterContext.HttpContext.Session?.Abandon();

                filterContext.Result = new RedirectToRouteResult(
                    new System.Web.Routing.RouteValueDictionary
                    {
                        { "controller", "Account" },
                        { "action", "Login" },
                        { "reason", "inactive" }
                    });

                return;
            }

            var refreshed = new MessengerPrincipal(fresh);
            filterContext.HttpContext.User = refreshed;
            Thread.CurrentPrincipal = refreshed;
        }
    }
}
