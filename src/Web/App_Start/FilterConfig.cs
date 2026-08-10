using System.Web.Mvc;

namespace Messenger.Web
{
    public static class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            // บังคับ login ทุกหน้าโดยปริยาย หน้าไหนเปิดสาธารณะต้องใส่ [AllowAnonymous] เอง
            filters.Add(new AuthorizeAttribute());

            filters.Add(new HandleErrorAttribute());
        }
    }
}
