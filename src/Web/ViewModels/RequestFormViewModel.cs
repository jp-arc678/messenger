using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using Messenger.Application.Services;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Web.ViewModels
{
    /// <summary>ประเภทงาน 1 ช่องบนฟอร์ม (checkbox + ช่องรายละเอียด) ตาม D18</summary>
    public class JobTypeItemViewModel
    {
        /// <summary>ชื่อ enum ที่ส่งกลับมากับฟอร์ม</summary>
        public string Code { get; set; }

        public bool Selected { get; set; }

        public string DetailText { get; set; }

        public string DisplayName =>
            string.IsNullOrEmpty(Code) ? string.Empty : JobTypes.ToDisplayName(JobTypes.Parse(Code));
    }

    /// <summary>
    /// ฟอร์มสร้าง/แก้ไขใบแจ้งงาน ใช้ร่วมกันทั้งสองหน้า
    ///
    /// วันที่ถูกรับ-ส่งเป็นข้อความรูปแบบ ISO (yyyy-MM-dd) โดยตั้งใจ
    /// เพราะระบบตั้ง culture เป็น th-TH ถ้าปล่อยให้ model binder แปลงเอง
    /// ปี ค.ศ. 2026 จะถูกตีความเป็น พ.ศ. 2026 (= ค.ศ. 1483)
    /// </summary>
    public class RequestFormViewModel
    {
        public const string DateFormat = "yyyy-MM-dd";

        public int ReqId { get; set; }

        public string ReqNo { get; set; }

        /// <summary>rowVersion แปลงเป็น base64 เพื่อฝากไว้ใน hidden field (BR-2)</summary>
        public string RowVersion { get; set; }

        public string RequesterEmpCode { get; set; }

        public string SendDate { get; set; }

        public string ContactName { get; set; }

        public string Address { get; set; }

        public string Phone { get; set; }

        public string Detail { get; set; }

        public bool IsPersonal { get; set; }

        public List<JobTypeItemViewModel> JobTypes { get; set; } = new List<JobTypeItemViewModel>();

        // ---- ข้อมูลประกอบหน้าจอ (ไม่ได้ส่งกลับมาจากฟอร์ม) ----

        public IReadOnlyList<Employee> SelectableRequesters { get; set; } = new List<Employee>();

        /// <summary>
        /// ตัวเลือกผู้แจ้งพร้อมทำเครื่องหมายว่าอันไหนถูกเลือก
        ///
        /// ประกอบเป็น SelectListItem ที่นี่แทนการเขียน &lt;option&gt; เองในหน้า view
        /// เพราะการปล่อยให้ browser เลือกเองจะได้ตัวเลือกแรกของรายการ (ซึ่งเรียงตาม
        /// role จึงเป็น Admin) ทำให้ผู้ใช้สร้างใบงานเป็นชื่อคนอื่นโดยไม่รู้ตัว
        /// </summary>
        public IEnumerable<SelectListItem> RequesterOptions
        {
            get
            {
                return SelectableRequesters.Select(employee => new SelectListItem
                {
                    Value = employee.EmpCode,
                    Text = employee.EmpCode + " · " + employee.FullName + " (" + employee.UnitName + ")",
                    Selected = string.Equals(employee.EmpCode, RequesterEmpCode,
                                             StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        public IReadOnlyList<string> Errors { get; set; } = new List<string>();

        public string StatusDisplayName { get; set; }

        public bool IsEdit => ReqId > 0;

        public string PageTitle => IsEdit ? "แก้ไขใบแจ้งงาน" : "แจ้งงานรับ-ส่งเอกสาร";

        /// <summary>ช่อง checkbox ครบทั้ง 6 ประเภทเสมอ เรียงตาม CLAUDE.md §4</summary>
        public static List<JobTypeItemViewModel> BuildEmptyJobTypes()
        {
            return Domain.Enums.JobTypes.All
                .Select(t => new JobTypeItemViewModel { Code = Domain.Enums.JobTypes.ToCode(t) })
                .ToList();
        }

        /// <summary>เติมค่าที่เคยเลือกไว้ลงในช่อง checkbox ทั้ง 6</summary>
        public static List<JobTypeItemViewModel> BuildJobTypesFrom(IEnumerable<RequestJobType> saved)
        {
            var lookup = (saved ?? Enumerable.Empty<RequestJobType>())
                .ToDictionary(j => j.JobType, j => j.DetailText);

            return Domain.Enums.JobTypes.All
                .Select(t => new JobTypeItemViewModel
                {
                    Code = Domain.Enums.JobTypes.ToCode(t),
                    Selected = lookup.ContainsKey(t),
                    DetailText = lookup.ContainsKey(t) ? lookup[t] : null
                })
                .ToList();
        }

        public static string FormatDate(DateTime date)
        {
            return date.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>คืน null ถ้าข้อความไม่ใช่วันที่รูปแบบ ISO</summary>
        public static DateTime? ParseDate(string text)
        {
            DateTime parsed;
            if (DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out parsed))
            {
                return parsed;
            }

            return null;
        }

        /// <summary>แปลงช่อง checkbox ที่ถูกติ๊ก เป็น input ของ service layer</summary>
        public IReadOnlyList<JobTypeInput> ToJobTypeInputs()
        {
            if (JobTypes == null)
                return new List<JobTypeInput>();

            return JobTypes
                .Where(j => j.Selected && !string.IsNullOrWhiteSpace(j.Code))
                .Select(j => new JobTypeInput
                {
                    JobType = Domain.Enums.JobTypes.Parse(j.Code),
                    DetailText = j.DetailText
                })
                .ToList();
        }

        public byte[] RowVersionBytes()
        {
            if (string.IsNullOrWhiteSpace(RowVersion))
                return null;

            try
            {
                return Convert.FromBase64String(RowVersion);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
