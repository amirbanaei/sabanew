using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using SaabWebProject.Models.DomainModels;

namespace SaabWebProject.Areas.Setting.Controllers
{
    public class TaskRequestsController : Controller
    {
        SaabEntities db = new SaabEntities();

        const string UploadDir = "~/Uploads/Tasks/";
        const int MaxFileBytes = 20 * 1024 * 1024;
        static readonly string[] BadExt = { ".exe", ".dll", ".bat", ".cmd", ".js", ".aspx", ".ashx", ".config", ".cshtml", ".ps1", ".vbs", ".msi" };
        static readonly string[] StatusNames = { "جدید", "در حال انجام", "انجام شد", "متوقف" };

        // ===================== کمکی‌ها =====================

        private int? GetCurrentUserId()
        {
            try
            {
                var cookie_user = Request.Cookies[Utility.Base64.Base64Encode("CodeMeli")];
                if (cookie_user == null) return null;
                var nationalcode = Utility.Base64.Base64Decode(cookie_user.Value);
                var user = db.tbUsers.FirstOrDefault(x => x.usr_NationalCode == nationalcode);
                return user == null ? (int?)null : user.usr_ID;
            }
            catch { return null; }
        }

        private bool IsManager(int uid)
        {
            return db.tbTaskManagers.Any(m => m.usr_ID == uid);
        }

        // ⚠️ اگر نام فیلدهای نام/نام‌خانوادگی در tbUsers فرق دارد فقط همین یک متد را اصلاح کن
        private static string UserName(tbUsers u)
        {
            return ((u.usr_Name ?? "") + " " + (u.usr_Family ?? "")).Trim();
        }

        private Dictionary<int, string> UserNames(IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            return db.tbUsers.Where(u => list.Contains(u.usr_ID)).ToList()
                     .ToDictionary(u => u.usr_ID, u => UserName(u));
        }

        private static string Digits(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder();
            foreach (var c in s.Trim())
            {
                if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
                else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ToFa(DateTime d)
        {
            var pc = new PersianCalendar();
            return string.Format("{0:0000}/{1:00}/{2:00}", pc.GetYear(d), pc.GetMonth(d), pc.GetDayOfMonth(d));
        }

        private static DateTime? FromFa(string s)
        {
            try
            {
                var p = Digits(s).Split('/', '-', '.');
                if (p.Length != 3) return null;
                return new PersianCalendar().ToDateTime(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), 0, 0, 0, 0);
            }
            catch { return null; }
        }

        private JsonResult Fail(string msg)
        {
            return Json(new { ok = false, msg = msg }, JsonRequestBehavior.AllowGet);
        }

        private JsonResult Ok(object extra = null)
        {
            return Json(new { ok = true, data = extra }, JsonRequestBehavior.AllowGet);
        }

        // اولویت سراسری: همه‌ی کارها یک صف واحدند (SortOrder کوچکتر = مهم‌تر).
        // کارهای «انجام شد» شماره نمی‌گیرند (rank = 0) تا شماره‌ی صف فقط کارهای باز را بشمارد.
        private Dictionary<int, int> GlobalRanks()
        {
            var all = db.tbTasks.OrderBy(t => t.SortOrder).ThenBy(t => t.TaskID)
                        .Select(t => new { t.TaskID, t.Status }).ToList();
            var ranks = new Dictionary<int, int>();
            int n = 0;
            foreach (var t in all) ranks[t.TaskID] = (t.Status == 2) ? 0 : ++n;
            return ranks;
        }

        private List<object> MapTasks(List<tbTasks> tasks, Dictionary<int, string> names)
        {
            var ids = tasks.Select(t => t.TaskID).ToList();
            var files = db.tbTaskFiles.Where(f => ids.Contains(f.TaskID)).ToList().ToLookup(f => f.TaskID);
            var ranks = GlobalRanks();
            return tasks.Select(t => (object)new
            {
                id = t.TaskID,
                userId = t.usr_ID,
                owner = names.ContainsKey(t.usr_ID) ? names[t.usr_ID] : "",
                title = t.Title,
                description = t.Description,
                deadline = ToFa(t.Deadline),
                daysLeft = (t.Deadline.Date - DateTime.Today).Days,
                status = t.Status,
                statusName = StatusNames[Math.Max(0, Math.Min(3, t.Status))],
                estimate = t.EstimateHours,
                note = t.ManagerNote,
                created = ToFa(t.CreateDate),
                rank = ranks.ContainsKey(t.TaskID) ? ranks[t.TaskID] : 0,
                files = files[t.TaskID].Select(f => new
                {
                    id = f.FileID,
                    name = f.OriginalName,
                    size = f.FileSize
                }).ToList()
            }).ToList();
        }

        // ===================== صفحه ۱ : ثبت مسئله =====================

        public ActionResult Index()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return new HttpUnauthorizedResult();
            ViewBag.IsManager = IsManager(uid.Value);
            return View();
        }

        public JsonResult MyList()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Fail("ابتدا وارد شوید");
            var tasks = db.tbTasks.Where(t => t.usr_ID == uid.Value)
                          .OrderBy(t => t.SortOrder).ThenBy(t => t.TaskID).ToList();
            return Ok(MapTasks(tasks, UserNames(new[] { uid.Value })));
        }

        [HttpPost]
        public JsonResult Save(int? id, string title, string description, string deadline)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Fail("ابتدا وارد شوید");

            if (string.IsNullOrWhiteSpace(title)) return Fail("عنوان مسئله را وارد کنید");
            var dl = FromFa(deadline);
            if (dl == null) return Fail("تاریخ را به شکل 1405/07/20 وارد کنید");

            tbTasks t;
            if (id.HasValue && id.Value > 0)
            {
                t = db.tbTasks.Find(id.Value);
                if (t == null) return Fail("یافت نشد");
                if (t.usr_ID != uid.Value && !IsManager(uid.Value)) return Fail("اجازه ویرایش ندارید");
                t.UpdateDate = DateTime.Now;
            }
            else
            {
                t = new tbTasks
                {
                    usr_ID = uid.Value,
                    Status = 0,
                    CreateDate = DateTime.Now,
                    // کار جدید ته صف سراسری می‌رود
                    SortOrder = (db.tbTasks.Select(x => (int?)x.SortOrder).Max() ?? 0) + 1
                };
                db.tbTasks.Add(t);
            }

            t.Title = title.Trim();
            t.Description = description;
            t.Deadline = dl.Value;
            db.SaveChanges();

            // آپلود فایل‌ها
            var dir = Server.MapPath(UploadDir);
            Directory.CreateDirectory(dir);
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var f = Request.Files[i];
                if (f == null || f.ContentLength == 0) continue;
                var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
                if (BadExt.Contains(ext)) return Fail("نوع فایل «" + ext + "» مجاز نیست");
                if (f.ContentLength > MaxFileBytes) return Fail("حجم هر فایل حداکثر ۲۰ مگابایت است");

                var stored = Guid.NewGuid().ToString("N") + ext;
                f.SaveAs(Path.Combine(dir, stored));
                db.tbTaskFiles.Add(new tbTaskFiles
                {
                    TaskID = t.TaskID,
                    OriginalName = Path.GetFileName(f.FileName),
                    StoredName = stored,
                    FileSize = f.ContentLength
                });
            }
            db.SaveChanges();
            return Ok(new { id = t.TaskID });
        }

        [HttpPost]
        public JsonResult Delete(int id)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Fail("ابتدا وارد شوید");
            var t = db.tbTasks.Find(id);
            if (t == null) return Fail("یافت نشد");
            if (t.usr_ID != uid.Value && !IsManager(uid.Value)) return Fail("اجازه حذف ندارید");

            foreach (var f in db.tbTaskFiles.Where(x => x.TaskID == id).ToList())
                DeletePhysical(f.StoredName);

            db.tbTaskFiles.RemoveRange(db.tbTaskFiles.Where(x => x.TaskID == id));
            db.tbTasks.Remove(t);
            db.SaveChanges();
            return Ok();
        }

        [HttpPost]
        public JsonResult RemoveFile(int id)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Fail("ابتدا وارد شوید");
            var f = db.tbTaskFiles.Find(id);
            if (f == null) return Fail("یافت نشد");
            var t = db.tbTasks.Find(f.TaskID);
            if (t == null || (t.usr_ID != uid.Value && !IsManager(uid.Value))) return Fail("اجازه ندارید");

            DeletePhysical(f.StoredName);
            db.tbTaskFiles.Remove(f);
            db.SaveChanges();
            return Ok();
        }

        private void DeletePhysical(string stored)
        {
            try
            {
                var p = Path.Combine(Server.MapPath(UploadDir), stored);
                if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
            }
            catch { }
        }

        public ActionResult DownloadFile(int id)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return new HttpUnauthorizedResult();
            var f = db.tbTaskFiles.Find(id);
            if (f == null) return HttpNotFound();
            var t = db.tbTasks.Find(f.TaskID);
            if (t == null || (t.usr_ID != uid.Value && !IsManager(uid.Value))) return new HttpUnauthorizedResult();

            var p = Path.Combine(Server.MapPath(UploadDir), f.StoredName);
            if (!System.IO.File.Exists(p)) return HttpNotFound();
            return File(p, "application/octet-stream", f.OriginalName);
        }

        // ===================== صفحه ۲ : مدیریت و اولویت‌بندی =====================

        public ActionResult Manage()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return new HttpUnauthorizedResult();
            if (!IsManager(uid.Value)) return new HttpStatusCodeResult(403, "دسترسی ندارید");
            return View();
        }

        // همه‌ی کارهای همه‌ی کاربران، به ترتیب اولویت سراسری
        public JsonResult ManageAll()
        {
            var uid = GetCurrentUserId();
            if (uid == null || !IsManager(uid.Value)) return Fail("دسترسی ندارید");
            var tasks = db.tbTasks.OrderBy(t => t.SortOrder).ThenBy(t => t.TaskID).ToList();
            return Ok(MapTasks(tasks, UserNames(tasks.Select(t => t.usr_ID))));
        }

        [HttpPost]
        public JsonResult UpdateManage(int id, decimal? estimateHours, int status, string managerNote, string deadline)
        {
            var uid = GetCurrentUserId();
            if (uid == null || !IsManager(uid.Value)) return Fail("دسترسی ندارید");
            var t = db.tbTasks.Find(id);
            if (t == null) return Fail("یافت نشد");
            if (status < 0 || status > 3) return Fail("وضعیت نامعتبر");
            if (estimateHours.HasValue && estimateHours.Value < 0) return Fail("تخمین زمان نامعتبر");

            if (!string.IsNullOrWhiteSpace(deadline))
            {
                var dl = FromFa(deadline);
                if (dl == null) return Fail("تاریخ را به شکل 1405/07/20 وارد کنید");
                t.Deadline = dl.Value;
            }
            t.EstimateHours = estimateHours;
            t.Status = status;
            t.ManagerNote = managerNote;
            t.UpdateDate = DateTime.Now;
            db.SaveChanges();
            return Ok();
        }

        [HttpPost]
        public JsonResult Reorder(int[] ids)
        {
            var uid = GetCurrentUserId();
            if (uid == null || !IsManager(uid.Value)) return Fail("دسترسی ندارید");
            if (ids == null || ids.Length == 0) return Fail("لیست خالی است");

            // ۱) کل صف سراسری را یکتا و پشت‌سرهم شماره‌گذاری می‌کنیم (۱..N)
            var all = db.tbTasks.OrderBy(t => t.SortOrder).ThenBy(t => t.TaskID).ToList();
            for (int i = 0; i < all.Count; i++) all[i].SortOrder = i + 1;

            // ۲) «جایگاه‌های» فعلیِ کارهای ارسال‌شده را به ترتیب جدید به آن‌ها می‌دهیم.
            //    کارهای دیگر (مثلاً فیلتر‌شده یا مخفی) سر جای خودشان می‌مانند.
            var picked = ids.Distinct()
                            .Select(id => all.FirstOrDefault(t => t.TaskID == id))
                            .Where(t => t != null).ToList();
            var slots = picked.Select(t => t.SortOrder).OrderBy(s => s).ToList();
            for (int i = 0; i < picked.Count; i++) picked[i].SortOrder = slots[i];

            db.SaveChanges();
            return Ok();
        }

        // ----- مدیریت دسترسی مدیرها -----

        public JsonResult ManagerList()
        {
            var uid = GetCurrentUserId();
            if (uid == null || !IsManager(uid.Value)) return Fail("دسترسی ندارید");
            var ms = db.tbTaskManagers.ToList();
            var names = UserNames(ms.Select(m => m.usr_ID));
            return Ok(ms.Select(m => new
            {
                id = m.ManagerID,
                name = names.ContainsKey(m.usr_ID) ? names[m.usr_ID] : "؟",
                self = m.usr_ID == uid.Value
            }).ToList());
        }

        [HttpPost]
        public JsonResult GrantAccess(string nationalCode)
        {
            var uid = GetCurrentUserId();
            if (uid == null || !IsManager(uid.Value)) return Fail("دسترسی ندارید");
            nationalCode = Digits(nationalCode);
            var u = db.tbUsers.FirstOrDefault(x => x.usr_NationalCode == nationalCode);
            if (u == null) return Fail("کاربری با این کد ملی پیدا نشد");
            if (db.tbTaskManagers.Any(m => m.usr_ID == u.usr_ID)) return Fail("این کاربر قبلاً دسترسی دارد");
            db.tbTaskManagers.Add(new tbTaskManagers { usr_ID = u.usr_ID });
            db.SaveChanges();
            return Ok();
        }

        [HttpPost]
        public JsonResult RevokeAccess(int id)
        {
            var uid = GetCurrentUserId();
            if (uid == null || !IsManager(uid.Value)) return Fail("دسترسی ندارید");
            var m = db.tbTaskManagers.Find(id);
            if (m == null) return Fail("یافت نشد");
            if (m.usr_ID == uid.Value) return Fail("نمی‌توانید دسترسی خودتان را بگیرید");
            db.tbTaskManagers.Remove(m);
            db.SaveChanges();
            return Ok();
        }

        // ===================== صفحه ۳ : برد عمومی (فقط مشاهده) =====================

        public ActionResult Board()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return new HttpUnauthorizedResult();
            ViewBag.IsManager = IsManager(uid.Value);
            return View();
        }

        public JsonResult BoardData()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Fail("ابتدا وارد شوید");

            // یک لیست تخت به ترتیب اولویت سراسری؛ گروه‌بندی بر اساس کاربر سمت فرانت انجام می‌شود
            var tasks = db.tbTasks.OrderBy(t => t.SortOrder).ThenBy(t => t.TaskID).ToList();
            var names = UserNames(tasks.Select(t => t.usr_ID));
            var ranks = GlobalRanks();
            return Ok(tasks.Select(t => new
            {
                id = t.TaskID,
                rank = ranks.ContainsKey(t.TaskID) ? ranks[t.TaskID] : 0,
                userId = t.usr_ID,
                owner = names.ContainsKey(t.usr_ID) ? names[t.usr_ID] : "؟",
                title = t.Title,
                deadline = ToFa(t.Deadline),
                daysLeft = (t.Deadline.Date - DateTime.Today).Days,
                status = t.Status,
                statusName = StatusNames[Math.Max(0, Math.Min(3, t.Status))],
                estimate = t.EstimateHours,
                note = t.ManagerNote
            }).ToList());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}