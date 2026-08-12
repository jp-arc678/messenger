# CLAUDE.md — Messenger Document Delivery System (Rebuild)

> ไฟล์นี้คือ context หลักของโปรเจกต์ อ่านทั้งไฟล์ก่อนเริ่มทำงานทุกครั้ง
> ห้ามเดาสิ่งที่ไม่ได้เขียนไว้ ถ้าไม่ชัดเจนให้ถามก่อน (ดูหัวข้อ Open Questions ท้ายไฟล์)

---

## 1. ภาพรวมระบบ (Purpose)

ระบบ web application สำหรับ **แจ้งงานรับ-ส่งเอกสารภายนอก** ให้เจ้าหน้าที่ Messenger
เป็นการ **รื้อระบบเก่า (อายุ ~15 ปี) มาเขียนใหม่ทั้งหมด** สามารถปรับปรุงให้ดีกว่าเดิมได้

Flow หลัก: พนักงาน (User) แจ้งงาน → Messenger รับงาน/ยืนยัน → เดินทางไปส่ง ถ่ายรูปยืนยัน → ปิดงาน → ระบบส่งเมลแจ้งผู้แจ้ง

**Scope:** ใช้ใน **บริษัทเดียว มี 2 สาขา (SDC, SBK)** ใช้ **database ร่วมกัน** แต่ **แยกข้อมูลตามสาขา** แต่ละสาขามี Messenger และ Admin ของตัวเอง

---

## 2. Tech Stack

| ส่วน | เลือกใช้ |
|---|---|
| ภาษา | C# |
| Framework | **ASP.NET MVC 5 (.NET Framework 4.8)** |
| Data access | **Dapper + Stored Procedures** (requirement ระบุใช้ View + stored procedure) |
| Database | SQL Server 2014 |
| Frontend | Razor Views + Bootstrap 5 (responsive, ใช้บนมือถือ/แท็บเล็ตได้) |
| Host | IIS |
| รูปภาพ | เก็บบน **filesystem** (เก็บ path ใน DB, **ไม่เก็บ binary ใน DB**) |
| Unit test | **NUnit** |
| Export Excel | **ClosedXML 0.102.3** (เพิ่มใน Phase 5 ตาม D30 — เป็น dependency ภายนอกตัวเดียวนอกจาก Dapper/MVC) |

หลักการ: **business logic ทั้งหมดอยู่ใน Service layer เท่านั้น** ห้ามอยู่ใน Controller หรือ View

### 2.1 Dev Environment (เครื่องพัฒนาปัจจุบัน — ตรวจสอบแล้ว 2026-08-10)

| รายการ | ค่า |
|---|---|
| Build | MSBuild 17.14 จาก **VS 2022 Build Tools** — `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe` |
| .NET Framework 4.8 | ✅ reference assemblies + `Microsoft.WebApplication.targets` ครบ (build MVC 5 ได้) |
| NuGet restore | ผ่าน `msbuild -t:restore` (ไม่มี `nuget.exe` แยก — ใช้ **PackageReference** ไม่ใช้ packages.config) |
| .NET SDK | 9.0.315 (มีติดตั้ง แต่**ไม่ใช้**กับโปรเจกต์นี้) |
| SQL Server | `localhost` (instance ชื่อ `MSSQLSERVER`, เครื่อง **SDC591**) = SQL Server 2022 · มี `localhost\SQLEXPRESS` อีกตัวแต่ไม่ได้ใช้ |
| SSMS | SSMS 22 |

**Database ที่ใช้ dev:**

| รายการ | ค่า |
|---|---|
| Server | `localhost` (หรือ `SDC591`) — Windows Authentication |
| Database | `MessengerDb` |
| Collation | `Thai_CI_AS` |
| Compatibility level | **120 (= SQL Server 2014)** — ตั้งไว้เพื่อกันการเผลอใช้ T-SQL ที่ SQL 2014 ไม่รองรับ |
| ไฟล์ | `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\MessengerDb.mdf` |
| สคริปต์สร้าง | [src/Database/000_CreateDatabase.sql](src/Database/000_CreateDatabase.sql) (รันซ้ำได้) |
| Connection string (dev) | `Server=localhost;Database=MessengerDb;Integrated Security=SSPI;` |

> เข้าดูเองได้ที่ SSMS → เชื่อมต่อ `localhost` ด้วย Windows Authentication → ฐาน `MessengerDb`

### 2.2 Database Naming Convention (D14 — บังคับใช้ทุก object)

**prefix ติดกับชื่อเลย ไม่มี underscore คั่น** ชื่อหลัง prefix เป็น PascalCase

| ชนิด object | prefix | ตัวอย่าง |
|---|---|---|
| Table | `tbl` | `tblBranch`, `tblDeliveryRequest`, `tblReqNoSequence` |
| View | `vw` | `vwEmployeeRole` |
| Stored procedure | `sp` | `spBranchList`, `spEmployeeUpsertFromSso` |
| Function | `fn` | `fnCalcSendDate` |

constraint/index ให้อิงชื่อตารางเต็ม: `PKtblBranch` · `FKtblUserRoleEmployee` · `IXtblDeliveryRequestBranchStatusSendDate` · `CKtblDeliveryRequestStatus` · `UQtblDeliveryRequestReqNo` · `DFtblBranchIsActive`

> **ต้องเรียก stored procedure ด้วย `dbo.` นำหน้าเสมอ** (เช่น `dbo.spBranchList`)
> ไม่ใช่แค่เรื่องความสะอาด — เป็นการเลี่ยงพฤติกรรมของ SQL Server ที่จะไปค้น `master` ก่อน
> เมื่อเจอชื่อขึ้นต้นด้วย `sp` และช่วยให้ execution plan ถูก cache ใช้ซ้ำได้

---

## 3. Architecture & Folder Structure

แบ่งเป็น layer ชัดเจน:

```
/src
  /Web            → Controllers, Views, ViewModels, wwwroot (Bootstrap, JS)
  /Application    → Services (business rules ทั้งหมด), interfaces, DTOs
  /Domain         → Entities, enums (Status, Role, JobType), state machine
  /Infrastructure → Repositories (Dapper → stored procedures), SSO client, EmailSender, FileStorage
  /Database       → DDL scripts, stored procedures, views, seed data (เรียงตาม migration)
/tests
  /UnitTests      → เทสต์ business rules (BR-1..BR-8) และ state machine
```

**Module ตาม domain (8 ก้อน):**
1. Auth + SSO
2. Multi-branch (แยกตามสาขา SDC/SBK, ใช้ DB ร่วมกัน)
3. ฟอร์มแจ้งงาน (User)
4. Messenger workflow (คิว/ยืนยัน/สถานะ)
5. Photo capture & storage
6. Notification (email)
7. Reporting / dashboard
8. Admin / master data

---

## 4. Domain Glossary

**สาขา (Branch):** `SDC`, `SBK` — หน่วยแยกข้อมูล (isolation) ทั้งระบบ

**ประเภทงาน (JobType)** — เลือกได้มากกว่า 1, ระบุรายละเอียดเพิ่มต่อแต่ละอันได้:
`SendDoc` ส่งเอกสาร · `ReceiveDoc` รับเอกสาร · `ReceiveCheck` รับเช็ค · `PlaceBill` วางบิล · `RenewTax` ต่อภาษี · `Other` อื่นๆ

**Role (3 ระดับ):** `A-Admin` · `U-User` · `M-Messenger`

**Status (5 สถานะ):**
`Received` รับแจ้ง · `Delivering` กำลังส่ง · `Paused` พักการส่ง · `Completed` เสร็จงานแล้ว · `Cancelled` ยกเลิก

---

## 5. Role Matrix

| การกระทำ | A-Admin | U-User | M-Messenger |
|---|:---:|:---:|:---:|
| สร้างใบแจ้งงาน | ✅ | ✅ | ✅ |
| แจ้งงานแทนคนอื่น (เลือกผู้แจ้ง) | ✅ | ✅ | ✅ |
| แก้ใบงานตัวเอง (สถานะ Received) | ✅ | ✅ | ✅ |
| แก้ใบงานคนอื่น | ✅ | ❌ | ❌ |
| แก้ได้ทุกช่อง ทุกสถานะ | ✅ | ❌ | ❌ |
| เห็นคิวงานของสาขา | ✅ | เฉพาะใบตัวเอง | ✅ |
| ยืนยัน/จัดลำดับงาน (Received→Delivering) | ✅ | ❌ | ✅ |
| เปลี่ยนสถานะ (pause/complete/cancel) | ✅ | ยกเลิกได้เฉพาะตอน Received (ใบตัวเอง) | ✅ |
| อัปโหลดรูปยืนยัน | ✅ | ❌ | ✅ |
| ดู dashboard/รายงาน | ✅ (ทั้งสาขา) | เฉพาะของตัวเอง | ✅ (ทั้งสาขา) |
| จัดการ master data / role | ✅ | ❌ | ❌ |

> - ทุก role ถูกจำกัด scope ตาม **สาขา (branchCode)** ของตัวเองเสมอ (ดู BR-6)
> - **Admin จำกัดเฉพาะสาขาตัวเอง** (ไม่ใช่ global) และทุก record ของ Admin ต้องระบุว่ามาจากสาขาไหน
> - **แจ้งงานแทนคนอื่น (D17):** ทุก role เลือก `requesterEmpCode` เป็นใครก็ได้ **ที่อยู่สาขาเดียวกับตัวเอง**
>   ค่าเริ่มต้นคือตัวเอง · `createdBy` ยังคงบันทึกคนที่กดสร้างจริงเสมอ (แยกจากผู้แจ้ง)
>   · เมื่อแจ้งแทนคนอื่น ใบงานนั้นถือเป็น "ใบของผู้แจ้ง" — สิทธิ์แก้/ยกเลิกเป็นของผู้แจ้ง ไม่ใช่คนกรอก

---

## 6. Status State Machine

Agent **ห้ามสร้าง transition ที่ไม่มีในตารางนี้** เด็ดขาด ทุกการเปลี่ยนสถานะต้องบันทึกลง StatusHistory (ใคร/เมื่อไหร่/หมายเหตุ)

| จาก | ไป | ใครทำได้ | เงื่อนไข (guard) |
|---|---|---|---|
| *(สร้างใหม่)* | Received | User/Admin/Messenger | คำนวณ sendDate ตาม BR-1, gen เลขใบงานตาม BR-8 |
| Received | Delivering | Messenger/Admin | Messenger ยืนยันวัน-เวลา-ลำดับ → ล็อกการแก้ของ User (BR-2) |
| Received | Cancelled | User(เจ้าของ)/Messenger/Admin | ยกเลิกได้เฉพาะก่อน Messenger ยืนยันรับงาน |
| Delivering | Paused | Messenger/Admin | ต้องระบุเหตุผลพัก |
| Paused | Delivering | Messenger/Admin | — |
| Delivering | Completed | Messenger/Admin | ผ่านเงื่อนไขปิดงาน BR-4 → trigger email (BR-5) |
| Delivering | Cancelled | Messenger/Admin | ต้องระบุเหตุผล |
| Paused | Cancelled | Messenger/Admin | ต้องระบุเหตุผล |
| Completed | — | *(terminal)* | — |
| Cancelled | — | *(terminal)* | — |

---

## 7. Business Rules

**BR-1 — วันที่ส่ง (sendDate) default:**
1. ตั้งต้น sendDate = วันที่บันทึก
2. ถ้าเวลาบันทึก **หลัง 10:00** → sendDate = วันถัดไป
   *(เปรียบเทียบแบบ `>` เท่านั้น: 10:00:00 ตรง = ยังไม่เกิน → sendDate = วันนี้; 10:00:01 = เกิน)*
3. หลังจากข้อ 2 แล้ว ถ้า sendDate ตกวัน **เสาร์/อาทิตย์** → เลื่อนเป็นวันจันทร์
   *(กฎต้อง compose กันได้ เช่น ศุกร์ 11:00 → พรุ่งนี้เสาร์ → เลื่อนเป็นจันทร์)*
   *(ยังไม่ต้องรองรับวันหยุดนักขัตฤกษ์ — ดู D6)*

**ข้อ 2 และ 3 ใช้เฉพาะตอนคำนวณค่า default เท่านั้น** — เมื่อผู้ใช้แก้ `sendDate` เองทีหลัง
(ทำได้เฉพาะสถานะ `Received`) ระบบ **ไม่เลื่อนวันให้อัตโนมัติ** แต่ **ตรวจไม่ให้เลือก** ตาม D16:
- ❌ ห้ามเลือกวันที่**ย้อนหลัง** (ก่อนวันนี้)
- ❌ ห้ามเลือก**วันเสาร์/อาทิตย์**
- ต้องบังคับที่ **service layer** ไม่ใช่แค่ปิดปุ่มบนหน้าจอ

**BR-2 — Edit lock:** ใบงานแก้ได้โดย User เจ้าของ **เฉพาะสถานะ Received เท่านั้น** เมื่อ Messenger ยืนยัน (→ Delivering) จะถูกล็อก, มีเพียง Admin ที่แก้ได้ทุกสถานะ ใช้ **optimistic locking (rowversion)** กันชนกันตอนแก้พร้อมกัน

**BR-3 — รูปภาพ:** ตอนส่งงานถ่ายรูปยืนยัน เลือกประเภทรูป (`send`/`receive`), **resize ฝั่ง client ให้ ≤ 2MB** ก่อนอัปโหลด, เก็บไฟล์บน filesystem เก็บ path ใน DB
**รูปเป็น optional เสมอ** — ไม่ใช่เงื่อนไขบังคับของการปิดงาน (ดู BR-4)

**BR-4 — เงื่อนไขปิดงาน:** ถ้าใบงานมีประเภท `ReceiveDoc` (รับเอกสารกลับมาให้ผู้แจ้ง) ต้อง **กดยืนยันว่ารับของแล้ว** ก่อนจึงเปลี่ยนเป็น Completed ได้ — **ไม่บังคับว่าต้องมีรูป**
ใบงานที่ไม่มี `ReceiveDoc` ปิดงานได้เลยโดยไม่มีเงื่อนไขเพิ่ม

**BR-5 — Email:** เมื่อจบ process (→ Completed) ส่งเมลแจ้งพนักงานผู้แจ้งอัตโนมัติ *(รายละเอียด SMTP ยัง TBD — ดู D5; ทำผ่าน interface `IEmailSender` แล้ว mock ไปก่อน)*

**BR-6 — Branch isolation:** ทุก query/หน้าจอต้อง filter ตาม **สาขา (branchCode)** ของผู้ใช้เสมอ Messenger/User เห็นเฉพาะงานสาขาตัวเอง ทั้ง 2 สาขาใช้ **DB เดียวร่วมกัน** ต้องบังคับ isolation ที่ระดับ **repository/service ไม่ใช่แค่ UI**

**BR-7 — SSO:** ข้อมูลผู้ใช้มาจาก Single Sign-On: รหัสพนักงาน, ชื่อ, รหัสแผนก, ชื่อหน่วยงาน, เบอร์ภายใน, e-mail, **รหัสสาขา (branchCode)** พนักงานทุกคนใช้ระบบได้ ระบบไม่เก็บ password เอง (เฟส 0 ทำเป็น stub/mock ก่อน — ดู D3)

**BR-8 — เลขใบงาน (reqNo):** รูปแบบ `MSG-{BRANCH}-{YYMM}-{NNNN}`
- `{BRANCH}` = `SDC` หรือ `SBK`
- `{YYMM}` = ปี 2 หลัก + เดือน 2 หลัก
- `{NNNN}` = running 4 หลัก, **แยกลำดับตาม (สาขา + YYMM)** และ **reset ทุกเดือน**
- ตัวอย่าง: `MSG-SDC-2608-0001`, `MSG-SBK-2608-0001`

---

## 8. Data Model (core entities)

> ชื่อตารางจริงใน DB ใช้ prefix ตาม §2.2 (`tblBranch`, `tblEmployee`, ...)

- **Branch** → `tblBranch` (branchCode `SDC`/`SBK`, name)
- **Employee** → `tblEmployee` (empCode, name, deptCode, unitName, phoneExt, email, **branchCode**) — cache จาก SSO
- **UserRole** → `tblUserRole` (empCode, branchCode, roleCode) — **1 คนมีได้ 1 role เท่านั้น ห้ามซ้อน** (บังคับด้วย PK ที่ empCode), คนใหม่/คนที่ยังไม่มีแถว = `U-User` เสมอ
- **DeliveryRequest** → `tblDeliveryRequest` (reqNo `MSG-{BRANCH}-{YYMM}-{NNNN}`, **branchCode**, requesterEmpCode, requestDateTime, sendDate, contactName, address, phone, detail, status, isPersonal, receiptConfirmed (BR-4), rowVersion, createdBy/At, updatedBy/At)
  - **บังคับกรอก (NOT NULL + CHECK ห้ามเป็นช่องว่างล้วน) ตาม D15:** `contactName`, `address`, `detail`
  - **ไม่บังคับ:** `phone`
  - `requesterEmpCode` = ผู้แจ้ง (อาจไม่ใช่คนกรอก ดู D17) · `createdBy` = คนที่กดสร้างจริง
  - `isPersonal` = แยกงานฝากส่วนตัว vs งานบริษัท **ใช้เพื่อ filter/รายงานเท่านั้น ไม่มีผลกับ flow หรือ business rule ใด ๆ**
- **RequestJobType** → `tblRequestJobType` (reqId, jobType, detailText) — 1 ใบมีได้หลายประเภท
- **ReqNoSequence** → `tblReqNoSequence` (branchCode, yymm, lastNumber) — สำหรับ gen running ตาม BR-8
- **MessengerAssignment** → `tblMessengerAssignment` (reqId, messengerEmpCode, confirmedAt, sequenceOrder, route, distanceKm, returnToOffice)
  - แต่ละสาขามี Messenger ประจำ **คนเดียว** → **เปลี่ยนตัว Messenger กลางคันไม่ได้**
  - `sequenceOrder` = ลำดับการวิ่งงาน **ต่อวัน (ต่อสาขา)** ไม่ต้องแยกต่อ Messenger
- **DeliveryPhoto** → `tblDeliveryPhoto` (reqId, photoType `send`/`receive`, filePath, capturedAt, capturedBy)
- **StatusHistory** → `tblStatusHistory` (reqId, fromStatus, toStatus, byEmpCode, changedAt, note) — audit trail
- **PauseReason / CancelReason** → `tblPauseReason` / `tblCancelReason` (reqId, reason, byEmpCode, at)

---

## 9. Development Phases

> **สำคัญมาก:** ทำทีละเฟสตามลำดับ **ห้ามข้าม ห้ามเขียนโค้ดของเฟสถัดไปล่วงหน้า**
> จบแต่ละเฟสแล้ว **หยุดรอ approval** ก่อนเริ่มเฟสใหม่ (ดูหัวข้อ 10)

**Phase 0 — Foundation**
Goal: โครงโปรเจกต์ + DB schema + auth 3 roles + SSO stub + branch seed (SDC, SBK)
Done when: รันโปรเจกต์เปล่าขึ้น, login ด้วย mock SSO ได้ (มี branchCode), DDL สร้างตารางหลักครบ, role resolve ถูก

**Phase 1 — Core: ฟอร์มแจ้งงาน (User)**
Goal: สร้าง/แก้/ดู ใบแจ้งงาน, 6 ประเภทงาน (multi-select + detail), กฎ BR-1, BR-2, gen เลขใบงาน BR-8
Done when: สร้างใบงานได้, reqNo ถูกตาม BR-8, sendDate คำนวณถูกตาม BR-1, edit-lock ทำงานตาม BR-2, มี unit test คลุม BR-1 + BR-2 + BR-8

**Phase 2 — Messenger workflow**
Goal: หน้าคิวงาน, ค้นตามวันบันทึก/วันส่ง (ช่วงวันที่), ยืนยัน + จัดลำดับ, เปลี่ยนสถานะตาม state machine
Done when: Messenger ยืนยันงาน (Received→Delivering) ได้, pause/resume/cancel ทำงานตามตาราง §6, มี unit test คลุม state machine

**Phase 3 — Photo**
Goal: ถ่าย/อัปโหลดรูป, resize client-side ≤2MB, เก็บ filesystem, เงื่อนไขปิดงาน BR-4
Done when: อัปโหลดรูป + เลือกประเภทได้, ไฟล์ >2MB ถูกย่อก่อนส่ง, ใบที่มี `ReceiveDoc` ปิดงานได้เฉพาะเมื่อ**กดยืนยันรับของแล้ว**
*(แก้ถ้อยคำเดิม "ปิดงานได้เฉพาะเมื่อรูปครบ" ให้ตรงกับ D9/BR-4 ที่ตัดสินไว้แล้วว่า **รูปไม่ใช่เงื่อนไขปิดงาน**)*

**Phase 4 — Notification**
Goal: ส่งเมลตอน Completed (BR-5) ผ่าน `IEmailSender` (mock ก่อน)
Done when: ปิดงานแล้ว trigger เมลถึงผู้แจ้ง, มี template + ปรับ config ได้

**Phase 5 — Reporting / Dashboard**
Goal: รายงานสรุปรายวัน + export
Done when: หน้าสรุปรายวันแสดงจำนวนงาน/สถานะ/ต่อ Messenger (แยกตามสาขา), export ได้

**Phase 6 — Branch isolation + Hardening + Responsive polish + UAT**
Goal: บังคับ BR-6 ครบทุกจุด, ตรวจ authorization, ทดสอบบนมือถือ/แท็บเล็ต
Done when: ผู้ใช้ต่างสาขา (SDC/SBK) เห็นข้อมูลแยกกันสมบูรณ์บน DB เดียว, ผ่าน checklist UAT

---

## 10. Working Agreement (กติกาการทำงานของ Agent)

1. **ทำทีละเฟสตามลำดับ** เริ่มทุกงานด้วยการบอกว่า "กำลังทำ Phase N"
2. **ห้ามเขียนโค้ดของเฟสถัดไป** แม้จะเห็นว่าต้องใช้ในอนาคต
3. จบเฟส → รัน/ตรวจให้ผ่าน → สรุปสิ่งที่ทำ + วิธีทดสอบ → **หยุดรอคำว่า "approved ไป Phase N+1"** ห้ามไปต่อเอง
4. Business rule ที่ยุ่ง (BR-1, BR-2, BR-8, state machine) **ต้องเขียน unit test เสมอ**
5. **ห้ามสร้าง status transition** ที่ไม่มีใน §6
6. Business logic อยู่ใน Service layer เท่านั้น
7. งาน DB ทุกอย่างต้องเป็น **DDL/migration + stored procedure script ที่รันได้จริง** ห้าม assume schema เงียบๆ
8. จะเพิ่ม dependency / library ใหม่ ต้องถามก่อน
9. เจอความกำกวม → หยุดถาม อย่าเดา (เพิ่มเข้า Open Questions)

---

## 11. Enhancements (ทำเมื่อ core เสร็จ — อย่าเพิ่งทำในเฟส 0-6 เว้นแต่สั่ง)

Audit trail เต็มรูปแบบ · แจ้งเตือนผ่าน LINE (Notify/OA) เสริมอีเมล · ลายเซ็นดิจิทัลผู้รับ · QR/barcode ต่อใบงาน · SLA/aging flag งานเกินกำหนด (เตือนงานแจ้งหลัง 9:00) · Dashboard KPI (เวลาเฉลี่ยจนเสร็จ, ยอดยกเลิก) · PWA + GPS tag บนรูป

---

## 12. Open Questions / Decisions

- **D1 — Framework:** ✅ ASP.NET MVC 5 (.NET Framework 4.8)
- **D2 — Scope/Admin:** ✅ บริษัทเดียว 2 สาขา (SDC, SBK) ใช้ DB ร่วมกัน, isolation ตามสาขา, Admin จำกัดเฉพาะสาขาตัวเอง + ระบุสาขาต้นสังกัด
- **D3 — SSO:** ⏳ ยังไม่แน่ใจ contract — ใช้ mock ไปก่อน (เฟส 0)
- **D4 — เลขใบงาน:** ✅ `MSG-{BRANCH}-{YYMM}-{NNNN}` running 4 หลักแยกตามสาขา+เดือน reset รายเดือน (ดู BR-8)
- **D5 — Email:** ⏳ ยังไม่แน่ใจ SMTP ของบริษัท — ทำผ่าน `IEmailSender` แล้ว โดยเฟส 4 ใช้โหมด
  pickup directory เขียนไฟล์ `.eml` แทนการส่งจริง เปลี่ยนเป็น SMTP จริงได้ด้วยการแก้ config (ดู D28)
- **D6 — Holiday:** ✅ ยังไม่ต้องรองรับวันหยุดนักขัตฤกษ์ (BR-1 คิดแค่เสาร์-อาทิตย์)
- **D7 — ใครยกเลิกงานได้:** ✅ **Messenger และ Admin ยกเลิกได้ทุกสถานะที่ยังไม่ terminal** (Received/Delivering/Paused) · **User ยกเลิกได้เฉพาะใบตัวเองตอนสถานะ Received** (ก่อน Messenger รับงาน) — ปรับ §5/§6 ให้ตรงกันแล้ว
- **D8 — BR-1 เกณฑ์เวลา:** ✅ ใช้ `>` เท่านั้น (10:00:00 ตรง ยังนับเป็นวันนี้)
- **D9 — BR-4 เงื่อนไขปิดงาน:** ✅ **แก้กฎใหม่** — `ReceiveDoc` ต้อง**กดยืนยันรับของ**อย่างเดียวพอ **ไม่บังคับรูป**; รูปทั้งหมดเป็น optional
- **D10 — Role:** ✅ 1 คน = 1 role ห้ามซ้อน, default = `U-User` เสมอ (คนใหม่เข้าระบบเริ่มที่ User ทันที)
- **D11 — Messenger:** ✅ แต่ละสาขามี Messenger ประจำคนเดียว, เปลี่ยนตัวกลางคันไม่ได้, `sequenceOrder` เป็นลำดับ**ต่อวัน**
- **D12 — Test framework:** ✅ **NUnit**
- **D13 — Dev DB:** ✅ สร้าง `MessengerDb` บน `localhost` (Windows Auth) แล้ว — รายละเอียดดู §2.1
- **D14 — Naming convention:** ✅ table = `tbl`, view = `vw`, stored procedure = `sp`, function = `fn` — **prefix ติดชื่อเลย ไม่มี underscore** (ดู §2.2)
- **D15 — ฟิลด์บังคับกรอก:** ✅ `contactName`, `address`, `detail` = บังคับ (NOT NULL + CHECK) · `phone` = ไม่บังคับ
- **D16 — sendDate ที่ผู้ใช้แก้เอง:** ✅ ห้ามย้อนหลัง + ห้ามเสาร์/อาทิตย์ · กฎเลื่อนเป็นวันจันทร์ของ BR-1 ใช้เฉพาะตอนคำนวณ default
- **D17 — แจ้งงานแทนคนอื่น:** ✅ ทุก role ทำได้ แต่เลือกได้เฉพาะคนใน **สาขาเดียวกัน** (BR-6) · default = ตัวเอง · `createdBy` บันทึกคนกรอกจริงเสมอ
- **D18 — ฟอร์มประเภทงาน:** ✅ checkbox 6 ประเภท ติ๊กแล้วมีช่องกรอกรายละเอียดโผล่ต่อแต่ละประเภท
  - ✅ **บังคับเลือกอย่างน้อย 1 ประเภท** — ใบงานที่ไม่มีประเภทงานเลย บันทึกไม่ได้
- **D19 — การแสดงวันที่:** ✅ แสดงเป็น **ค.ศ.** ทั้งระบบ (ตั้ง `culture="en-US"` ใน Web.config)
  เหตุผล: culture `th-TH` ใช้ปฏิทินพุทธศักราช ทำให้ทั้งการแสดงผลและการอ่านค่าจากช่องเลือกวันที่คลาดไป 543 ปี
  และทำให้ `{YYMM}` ใน BR-8 ผิดด้วย
- **D20 — BR-4 กับการปิดงานใน Phase 2:** ✅ เฟส 2 ปิดงานได้ทุกใบโดย**ยังไม่ตรวจ** `receiptConfirmed`
  ตาม §9 ที่จัด BR-4 ไว้ใน Phase 3 พร้อมเรื่องรูป
  · **ปิดใน Phase 3 แล้ว** — เพิ่มปุ่ม "ยืนยันรับของแล้ว" + guard ตอนปิดงาน และแทนที่เทสต์ที่ตรึง
  พฤติกรรมเดิมด้วยเทสต์ชุด BR-4 ที่บังคับกฎจริง
- **D21 — ลำดับวิ่งงาน (sequenceOrder):** ✅ ระบบให้เลขอัตโนมัติตอนยืนยันรับงาน
  (= MAX ของ **สาขา + วันที่ส่ง** นั้น + 1) ไม่ต้องกรอกเอง · จัดลำดับใหม่ด้วยปุ่มเลื่อนขึ้น/ลงในหน้าคิวงาน
  ซึ่งสลับเลขกับใบที่อยู่ติดกันแบบ atomic
- **D22 — Admin ยืนยันรับงานแทน:** ✅ ผู้รับงานที่บันทึกใน `tblMessengerAssignment` คือ
  **Messenger ประจำสาขา** เสมอ (D11) ไม่ใช่ Admin ที่กด · ถ้าสาขายังไม่มี Messenger หรือมีมากกว่า 1 คน
  จะยืนยันไม่ได้พร้อมข้อความบอกเหตุผล
- **D23 — ช่วงเวลาที่จัดการรูปได้:** ✅ อัปโหลด/ลบรูปได้เฉพาะ **Messenger/Admin** และเฉพาะตอนใบงานอยู่สถานะ
  `Delivering` หรือ `Paused` เท่านั้น · สถานะ `Received` ยังไม่มีใครรับงาน และ `Completed`/`Cancelled` ปิดจบไปแล้ว
  · การ **ดู** รูปใช้สิทธิ์เดียวกับการดูใบงาน (ผู้แจ้งดูรูปใบตัวเองได้ แต่อัปโหลด/ลบไม่ได้)
  · ปุ่ม "ยืนยันรับของแล้ว" (BR-4) ใช้เงื่อนไขเวลาชุดเดียวกันนี้
- **D24 — ลบรูป:** ✅ ลบได้ก่อนปิดงาน (เงื่อนไขเดียวกับ D23) โดย Messenger/Admin · ลบแถวใน DB แล้วลบไฟล์จริงตาม
  ถ้าลบไฟล์พลาดจะเหลือแค่ไฟล์กำพร้าที่ไม่มีใครเห็น ดีกว่ากรณีกลับกันที่หน้าจอจะโชว์รูปที่เปิดไม่ได้
- **D25 — ที่เก็บไฟล์รูป:** ✅ เก็บ **นอก web root** · path ตั้งที่ appSetting `PhotoStorageRoot` ใน Web.config
  (ค่าว่าง = `~\App_Data\Photos` สำหรับเครื่อง dev · production ให้ชี้ path เต็ม เช่น `D:\MessengerPhotos`
  เพื่อไม่ให้ deploy ทับแล้วรูปหาย) · DB เก็บ path **แบบสัมพัทธ์** กับ root เพื่อให้ย้ายที่เก็บได้โดยไม่ต้องแก้ข้อมูล
  · เปิดรูปต้องผ่าน `/Photos/Show/{id}` ที่ตรวจสาขา + สิทธิ์ก่อนเสมอ (BR-6) ห้ามเปิดไฟล์ตรงผ่าน URL
- **D26 — ส่งอีเมลไม่สำเร็จ:** ✅ **ไม่ย้อนสถานะ** — งานที่ปิดไปแล้วยังปิดอยู่ ระบบขึ้น "คำเตือน"
  บนหน้าจอแทน (สีเหลือง ไม่ใช่ error สีแดง) ผ่าน `ServiceResult.Warnings`
  เหตุผล: SMTP ล่มไม่ควรทำให้ Messenger ปิดงานไม่ได้ทั้งวัน
- **D27 — ผู้รับอีเมลตอนปิดงาน:** ✅ `To` = **ผู้แจ้ง** (requesterEmpCode) · `Cc` = **คนที่กดสร้างใบงาน**
  (createdBy) เฉพาะเมื่อแจ้งแทนคนอื่นและอีเมลไม่ซ้ำกัน (D17) · ผู้แจ้งไม่มีอีเมล = ไม่ส่ง + ขึ้นคำเตือน
- **D28 — ช่องทางส่งเมลตอนยังไม่มี SMTP (ต่อจาก D5):** ✅ ใช้ `SmtpClient` ของ .NET
  โหมด **pickup directory** เขียนไฟล์ `.eml` ลง `~\App_Data\Mail` ตอน dev (เปิดอ่านด้วยโปรแกรมเมลได้)
  · ได้ SMTP จริงเมื่อไหร่ ตั้ง `EmailPickupDirectory` = `none` แล้วแก้ `<system.net><mailSettings>`
  ใน Web.config **ไม่ต้องแก้โค้ด**
  · เนื้อเมลมาจาก template `~\App_Data\EmailTemplates\RequestCompleted.html` แก้แล้วมีผลทันที
  ไม่ต้อง build ใหม่ (ลบไฟล์ = ใช้ template สำรองในโค้ด) · บรรทัดแรก `Subject:` = หัวเรื่อง
  · ค่าที่แทรกลง template ถูก HTML-encode เสมอ และหัวเรื่องถูกตัดขึ้นบรรทัดใหม่ทิ้ง (กัน header injection)
- **D29 — ช่วงเวลาของรายงาน:** ✅ เลือก **ช่วงวันที่** ได้ (ค่าเริ่มต้น = วันนี้วันเดียว) · แกนของรายงานคือ
  **วันที่ส่ง** ไม่ใช่วันที่แจ้ง เพราะเป็นวันที่ Messenger ออกวิ่งงานจริง (BR-1 ทำให้สองวันนี้ต่างกันได้)
  · ตารางรายวันออกครบทุกวันในช่วง รวมวันที่ไม่มีงาน (แสดง 0) · จำกัดช่วงไม่เกิน 366 วัน
- **D30 — รูปแบบไฟล์ export:** ✅ **Excel .xlsx** ผ่าน **ClosedXML** (อนุมัติเพิ่ม dependency แล้วตาม §10 ข้อ 8)
  · ไฟล์มี 2 ชีต: "รายการใบงาน" (ข้อมูลดิบไว้ pivot) และ "สรุป" (ตัวเลขชุดเดียวกับหน้าจอ)
  · **ต้องมี** `<add assembly="netstandard ..." />` ใน `<compilation>` ของ Web.config
  ไม่งั้นทุกหน้าเว็บพังด้วย CS0012 เพราะ ClosedXML เป็น netstandard2.0 (มี binding redirect ประกอบด้วย)
- **D31 — เนื้อหาไฟล์ export:** ✅ **รายการใบงานแบบละเอียด** 1 แถว = 1 ใบงาน
  (เลขใบงาน/สาขา/วันที่ส่ง/ลำดับ/วันที่บันทึก/สถานะ/ผู้แจ้ง/หน่วยงาน/ผู้รับงาน/ประเภทงาน/ผู้ติดต่อ/เบอร์/ที่อยู่)
  · ตัวเลขสรุปกับรายการมาจากชุดข้อมูลเดียวกันเสมอ จึงไม่มีทางไม่ตรงกัน
- **D32 — สาขา/สิทธิ์ต้องอ่านใหม่จาก DB ทุก request:** ✅ เดิมเชื่อค่าใน Forms Auth ticket (อายุ 8 ชม.)
  ทำให้คนที่ถูก**ย้ายสาขา**ยังเห็นข้อมูลสาขาเดิมได้ทั้งวัน และคนที่ถูกลดสิทธิ์ยังใช้สิทธิ์เดิมได้
  — เป็นช่องโหว่เชิงเวลาของ BR-6 · ตอนนี้ `RefreshUserContextAttribute` (global filter)
  เรียก `IAuthService.ResolveCurrent()` ทุก request แล้วสร้าง principal ใหม่จากข้อมูล DB
  · พนักงานที่ถูกปิดการใช้งาน/สาขาถูกปิด = ถูก sign out ทันที
  · ราคา: query พนักงาน 1 ครั้งต่อ request ของ MVC (ยอมรับได้สำหรับระบบภายใน)
- **D33 — วิธียืนยันว่า BR-6 รัดกุมจริง:** ✅ ไม่เชื่อการ "ซ่อนปุ่มบนหน้าจอ" เป็นหลักฐาน
  ต้องผ่าน `tools\Test-BranchIsolation.ps1` ที่ยิง POST/GET ตรงไปที่ action ด้วย id ของสาขาอื่น
  ครบทุก endpoint (32 ข้อ) · **ทุกครั้งที่เพิ่ม action ใหม่ ต้องเพิ่มเคสในสคริปต์นี้ด้วย**
