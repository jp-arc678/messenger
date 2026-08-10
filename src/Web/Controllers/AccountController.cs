using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using Messenger.Application.Dtos;
using Messenger.Application.Services;
using Messenger.Web.Security;
using Messenger.Web.ViewModels;

namespace Messenger.Web.Controllers
{
    /// <summary>
    /// การ login/logout
    ///
    /// Controller ไม่มี business logic — หน้าที่มีแค่รับ input, เรียก
    /// <see cref="IAuthService"/> แล้วแปลงผลลัพธ์เป็น cookie + หน้าจอ
    /// </summary>
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private const int TicketLifetimeHours = 8;

        private readonly IAuthService _authService;

        public AccountController(IAuthService authService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        }

        [HttpGet]
        public ActionResult Login(string returnUrl)
        {
            return View(NewLoginModel(returnUrl));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(LoginViewModel model, string returnUrl)
        {
            var empCode = model?.ResolveEmpCode();

            var result = _authService.SignIn(empCode);
            if (!result.Success)
            {
                var failed = NewLoginModel(returnUrl);
                failed.EmpCode = model?.EmpCode;
                failed.SelectedEmpCode = model?.SelectedEmpCode;
                failed.ErrorMessage = result.ErrorMessage;
                return View(failed);
            }

            IssueAuthCookie(result.User);

            if (Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }

        private LoginViewModel NewLoginModel(string returnUrl)
        {
            return new LoginViewModel
            {
                ReturnUrl = returnUrl,
                SelectableUsers = _authService.ListSelectableUsers()
            };
        }

        private void IssueAuthCookie(UserContext user)
        {
            var ticket = new FormsAuthenticationTicket(
                version: 1,
                name: user.EmpCode,
                issueDate: DateTime.Now,
                expiration: DateTime.Now.AddHours(TicketLifetimeHours),
                isPersistent: false,
                userData: UserContextTicket.Serialize(user),
                cookiePath: FormsAuthentication.FormsCookiePath);

            var cookie = new HttpCookie(FormsAuthentication.FormsCookieName, FormsAuthentication.Encrypt(ticket))
            {
                HttpOnly = true,
                Path = FormsAuthentication.FormsCookiePath,
                Secure = FormsAuthentication.RequireSSL
            };

            Response.Cookies.Add(cookie);
        }
    }
}
