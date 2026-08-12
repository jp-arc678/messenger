using System.Web.Mvc;
using Messenger.Web.Security;

namespace Messenger.Web
{
    public static class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            // บังคับ login ทุกหน้าโดยปริยาย หน้าไหนเปิดสาธารณะต้องใส่ [AllowAnonymous] เอง
            filters.Add(new AuthorizeAttribute());

            // ต่อจากนั้นอ่านสาขา/สิทธิ์ล่าสุดจาก DB ทับค่าที่ค้างอยู่ใน cookie (BR-6)
            filters.Add(new RefreshUserContextAttribute());

            filters.Add(new HandleErrorAttribute());
        }
    }
}
