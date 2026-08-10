using System.Collections.Generic;
using Messenger.Application.Dtos;

namespace Messenger.Web.ViewModels
{
    /// <summary>
    /// หน้าจอ login ของ Phase 0 — เป็นตัวแทน SSO ชั่วคราว (D3)
    /// เมื่อเชื่อม SSO จริงแล้ว หน้านี้จะถูกแทนที่ด้วยการ redirect ไป SSO
    /// </summary>
    public class LoginViewModel
    {
        /// <summary>รหัสพนักงานที่พิมพ์เอง (มีค่าจะถูกใช้ก่อน)</summary>
        public string EmpCode { get; set; }

        /// <summary>รหัสพนักงานที่เลือกจากรายการ mock SSO</summary>
        public string SelectedEmpCode { get; set; }

        public string ReturnUrl { get; set; }

        public string ErrorMessage { get; set; }

        public IReadOnlyList<SsoUserInfo> SelectableUsers { get; set; } = new List<SsoUserInfo>();

        /// <summary>รหัสที่จะใช้ login จริง — ช่องพิมพ์เองมาก่อนรายการที่เลือก</summary>
        public string ResolveEmpCode()
        {
            return string.IsNullOrWhiteSpace(EmpCode) ? SelectedEmpCode : EmpCode;
        }
    }
}
