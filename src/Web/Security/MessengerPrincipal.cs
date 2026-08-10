using System;
using System.Security.Principal;
using System.Web;
using Messenger.Application.Dtos;
using Messenger.Domain.Enums;

namespace Messenger.Web.Security
{
    /// <summary>
    /// ตัวตนของผู้ใช้ที่ผูกกับ request ปัจจุบัน
    /// ห่อ <see cref="UserContext"/> ไว้ให้ controller/view หยิบไปใช้ได้สะดวก
    ///
    /// <see cref="IsInRole"/> รองรับทั้งรหัสย่อ ("A"/"U"/"M") และชื่อเต็ม
    /// ("Admin"/"User"/"Messenger") เพื่อให้ใช้กับ [Authorize(Roles = "...")] ได้
    /// </summary>
    public class MessengerPrincipal : IPrincipal
    {
        public MessengerPrincipal(UserContext user)
        {
            User = user ?? throw new ArgumentNullException(nameof(user));
            Identity = new GenericIdentity(user.EmpCode, "MessengerForms");
        }

        public IIdentity Identity { get; }

        public UserContext User { get; }

        public bool IsInRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return false;

            var wanted = role.Trim();

            return string.Equals(wanted, RoleCodes.ToCode(User.Role), StringComparison.OrdinalIgnoreCase)
                || string.Equals(wanted, User.Role.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// ผู้ใช้ปัจจุบันของ request นี้ — คืน null ถ้ายังไม่ได้ login
        /// </summary>
        public static UserContext CurrentUser
        {
            get
            {
                var principal = HttpContext.Current?.User as MessengerPrincipal;
                return principal?.User;
            }
        }
    }
}
