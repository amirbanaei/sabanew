using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using SaabWebProject.Areas.Mdr.Data;

namespace SaabWebProject.Areas.Mdr.Core
{
    // =====================================================
    // MDR
    // Phase 1   : زیرساخت مشترک
    // Step 1.5  : مدیریت فایل و پیوست
    // Part 1.5.2: کلاس مشترک مدیریت فایل
    // -----------------------------------------------------
    // همه‌ی بخش‌های سامانه برای پیوست فقط از این کلاس
    // استفاده کنند (آپلود، نسخه جدید، لیست، دانلود، حذف).
    //
    // نمونه استفاده در Controller:
    //
    //   var result = FileManager.Upload(
    //       Request.Files[0],
    //       AuditLogger.Modules.OrganizationStructure,
    //       "MDR_OrganizationUnits",
    //       unitId);
    //
    //   if (!result.Success) { ... result.Message ... }
    //
    // قواعد:
    //   - فقط پسوندهای مجاز + بررسی محتوای واقعی فایل
    //   - حداکثر حجم: FileSettings.MaxFileSizeBytes
    //   - نام ذخیره‌شده روی سرور GUID است (نه نام کاربر)
    //   - محل ذخیره: ~/App_Data/MDR_Files (از مرورگر مستقیم قابل دسترسی نیست)
    //   - هر جایگزینی = نسخه جدید؛ نسخه‌های قبلی پاک نمی‌شوند
    //   - حذف نرم است؛ فایل فیزیکی باقی می‌ماند
    //   - همه‌ی عملیات در Audit Log ثبت می‌شود
    // =====================================================

    public static class FileManager
    {
        // =================================================
        // تنظیمات
        // =================================================

        public static class FileSettings
        {
            // حداکثر حجم هر فایل: ۲۰ مگابایت
            public static long MaxFileSizeBytes = 20L * 1024 * 1024;

            // محل ذخیره (داخل App_Data؛ IIS اجازه‌ی دانلود مستقیم نمی‌دهد)
            public static string StorageRoot = "~/App_Data/MDR_Files";

            // نام جدول فایل‌ها (برای لاگ)
            public const string FilesTable = "MDR_Files";
        }


        // -------------------------------------------------
        // پسوندهای مجاز + امضای (Signature) ابتدای فایل
        // امضای null یعنی فایل متنی است (بررسی جدا دارد)
        // -------------------------------------------------

        private static readonly Dictionary<string, byte[][]> AllowedTypes =
            new Dictionary<string, byte[][]>(StringComparer.OrdinalIgnoreCase)
            {
                // اسناد
                { ".pdf",  new[] { Bytes(0x25, 0x50, 0x44, 0x46) } },                     // %PDF
                { ".docx", new[] { Bytes(0x50, 0x4B, 0x03, 0x04) } },                     // ZIP
                { ".xlsx", new[] { Bytes(0x50, 0x4B, 0x03, 0x04) } },
                { ".pptx", new[] { Bytes(0x50, 0x4B, 0x03, 0x04) } },
                { ".doc",  new[] { Bytes(0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1) } }, // Office قدیمی
                { ".xls",  new[] { Bytes(0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1) } },
                { ".ppt",  new[] { Bytes(0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1) } },
                { ".txt",  null },
                { ".csv",  null },

                // تصویر
                { ".jpg",  new[] { Bytes(0xFF, 0xD8, 0xFF) } },
                { ".jpeg", new[] { Bytes(0xFF, 0xD8, 0xFF) } },
                { ".png",  new[] { Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A) } },
                { ".gif",  new[] { Bytes(0x47, 0x49, 0x46, 0x38) } },                     // GIF8
                { ".bmp",  new[] { Bytes(0x42, 0x4D) } },                                 // BM

                // فشرده
                { ".zip",  new[] { Bytes(0x50, 0x4B, 0x03, 0x04), Bytes(0x50, 0x4B, 0x05, 0x06) } },
                { ".rar",  new[] { Bytes(0x52, 0x61, 0x72, 0x21, 0x1A, 0x07) } }          // Rar!
            };


        // متن پسوندهای مجاز برای نمایش در پیام خطا / راهنمای صفحه
        public static string AllowedExtensionsText
        {
            get { return string.Join("، ", AllowedTypes.Keys); }
        }


        // =================================================
        // خروجی عملیات
        // =================================================

        public class FileOperationResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }

            public long? FileID { get; set; }
            public Guid? FileGuid { get; set; }
            public int? VersionNo { get; set; }

            internal static FileOperationResult Fail(string message)
            {
                return new FileOperationResult { Success = false, Message = message };
            }
        }


        // اطلاعات یک پیوست برای لیست
        public class FileInfoItem
        {
            public long FileID { get; set; }
            public Guid FileGuid { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string Category { get; set; }
            public int CurrentVersionNo { get; set; }

            public string OriginalFileName { get; set; }
            public string Extension { get; set; }
            public long FileSize { get; set; }

            public string PersianDate { get; set; }
            public string CreatedByName { get; set; }
            public string LastPersianDate { get; set; }
            public string LastUploadedByName { get; set; }
        }


        // اطلاعات یک نسخه برای تاریخچه
        public class FileVersionItem
        {
            public int VersionNo { get; set; }
            public string OriginalFileName { get; set; }
            public string Extension { get; set; }
            public long FileSize { get; set; }
            public string ChangeNote { get; set; }
            public string PersianDate { get; set; }
            public string Time { get; set; }
            public string UploadedByName { get; set; }
            public bool IsCurrent { get; set; }
        }


        // فایل آماده‌ی دانلود
        public class DownloadInfo
        {
            public string PhysicalPath { get; set; }
            public string DownloadName { get; set; }
            public string ContentType { get; set; }
        }


        // =================================================
        // MDR 1.5.2.1 — آپلود پیوست جدید
        // =================================================

        public static FileOperationResult Upload(
            HttpPostedFileBase file,
            string moduleName,
            string entityName,
            object entityId,
            string title = null,
            string description = null,
            string category = null)
        {
            if (string.IsNullOrWhiteSpace(moduleName) ||
                string.IsNullOrWhiteSpace(entityName) ||
                entityId == null ||
                string.IsNullOrWhiteSpace(entityId.ToString()))
            {
                return FileOperationResult.Fail("رکورد مقصد پیوست مشخص نیست.");
            }

            PreparedFile prepared;
            string error = Prepare(file, out prepared);

            if (error != null)
            {
                return FileOperationResult.Fail(error);
            }

            string physicalPath = null;

            try
            {
                using (var db = new MdrContext())
                {
                    var user = GetCurrentUser(db);
                    DateTime now = DateTime.Now;
                    string persianDate = ToPersianDate(now);

                    // ۱) ذخیره‌ی فایل فیزیکی
                    physicalPath = SavePhysical(prepared, now);

                    using (var transaction = db.Database.BeginTransaction())
                    {
                        // ۲) رکورد پیوست
                        var entity = new MdrFile
                        {
                            FileGuid = Guid.NewGuid(),

                            ModuleName = Cut(moduleName, 100),
                            EntityName = Cut(entityName, 100),
                            EntityID = Cut(entityId.ToString(), 50),
                            Category = Cut(Clean(category), 100),

                            Title = Cut(Clean(title) ?? prepared.DisplayNameWithoutExtension, 300),
                            Description = Cut(Clean(description), 1000),

                            CurrentVersionNo = 1,

                            CreatedAt = now,
                            PersianDate = persianDate,
                            CreatedByUserID = user.UserID,
                            CreatedByName = user.FullName,

                            IsDeleted = false
                        };

                        db.MDR_Files.Add(entity);
                        db.SaveChanges();

                        // ۳) نسخه‌ی ۱
                        db.MDR_FileVersions.Add(
                            NewVersion(entity.FileID, 1, prepared, physicalPath, now, persianDate, user, null));

                        db.SaveChanges();
                        transaction.Commit();

                        // ۴) لاگ
                        AuditLogger.Log(
                            AuditLogger.Actions.Create,
                            moduleName,
                            "آپلود پیوست «" + entity.Title + "» برای " + entityName + " #" + entity.EntityID,
                            entityName: FileSettings.FilesTable,
                            entityId: entity.FileID,
                            entityTitle: entity.Title,
                            newValues: new
                            {
                                entity.Title,
                                entity.Category,
                                OwnerEntity = entityName,
                                OwnerEntityID = entity.EntityID,
                                FileName = prepared.DisplayName,
                                FileSize = prepared.Size,
                                VersionNo = 1
                            });

                        return new FileOperationResult
                        {
                            Success = true,
                            Message = "فایل با موفقیت آپلود شد.",
                            FileID = entity.FileID,
                            FileGuid = entity.FileGuid,
                            VersionNo = 1
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                // اگر ثبت در دیتابیس خطا داد، فایل فیزیکی یتیم نماند
                DeletePhysicalQuietly(physicalPath);

                LogError(moduleName, "خطا در آپلود پیوست", ex);

                return FileOperationResult.Fail("خطا در آپلود فایل: " + ex.Message);
            }
        }


        // =================================================
        // MDR 1.5.2.2 — نسخه‌ی جدید (جایگزینی فایل)
        // =================================================

        public static FileOperationResult AddVersion(
            Guid fileGuid,
            HttpPostedFileBase file,
            string changeNote = null)
        {
            PreparedFile prepared;
            string error = Prepare(file, out prepared);

            if (error != null)
            {
                return FileOperationResult.Fail(error);
            }

            string physicalPath = null;
            string moduleName = null;

            try
            {
                using (var db = new MdrContext())
                {
                    var entity = db.MDR_Files
                        .FirstOrDefault(x => x.FileGuid == fileGuid && !x.IsDeleted);

                    if (entity == null)
                    {
                        return FileOperationResult.Fail("پیوست موردنظر پیدا نشد.");
                    }

                    moduleName = entity.ModuleName;

                    var user = GetCurrentUser(db);
                    DateTime now = DateTime.Now;
                    string persianDate = ToPersianDate(now);

                    int oldVersionNo = entity.CurrentVersionNo;

                    int newVersionNo =
                        (db.MDR_FileVersions
                            .Where(x => x.FileID == entity.FileID)
                            .Max(x => (int?)x.VersionNo) ?? 0) + 1;

                    physicalPath = SavePhysical(prepared, now);

                    using (var transaction = db.Database.BeginTransaction())
                    {
                        db.MDR_FileVersions.Add(
                            NewVersion(entity.FileID, newVersionNo, prepared, physicalPath,
                                       now, persianDate, user, changeNote));

                        entity.CurrentVersionNo = newVersionNo;
                        entity.UpdatedAt = now;
                        entity.UpdatedByUserID = user.UserID;
                        entity.UpdatedByName = user.FullName;

                        db.SaveChanges();
                        transaction.Commit();
                    }

                    AuditLogger.Log(
                        AuditLogger.Actions.Update,
                        entity.ModuleName,
                        "نسخه‌ی جدید پیوست «" + entity.Title + "» (نسخه " + newVersionNo + ")",
                        entityName: FileSettings.FilesTable,
                        entityId: entity.FileID,
                        entityTitle: entity.Title,
                        oldValues: new { VersionNo = oldVersionNo },
                        newValues: new
                        {
                            VersionNo = newVersionNo,
                            FileName = prepared.DisplayName,
                            FileSize = prepared.Size,
                            ChangeNote = Clean(changeNote)
                        });

                    return new FileOperationResult
                    {
                        Success = true,
                        Message = "نسخه‌ی جدید با موفقیت ثبت شد.",
                        FileID = entity.FileID,
                        FileGuid = entity.FileGuid,
                        VersionNo = newVersionNo
                    };
                }
            }
            catch (Exception ex)
            {
                DeletePhysicalQuietly(physicalPath);

                LogError(moduleName, "خطا در ثبت نسخه‌ی جدید پیوست", ex);

                return FileOperationResult.Fail("خطا در ثبت نسخه‌ی جدید: " + ex.Message);
            }
        }


        // =================================================
        // MDR 1.5.2.3 — لیست پیوست‌های یک رکورد
        // =================================================

        public static List<FileInfoItem> GetFiles(
            string entityName,
            object entityId)
        {
            if (string.IsNullOrWhiteSpace(entityName) || entityId == null)
            {
                return new List<FileInfoItem>();
            }

            string id = entityId.ToString();

            using (var db = new MdrContext())
            {
                var files = db.MDR_Files
                    .AsNoTracking()
                    .Where(x => x.EntityName == entityName &&
                                x.EntityID == id &&
                                !x.IsDeleted)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList();

                if (files.Count == 0)
                {
                    return new List<FileInfoItem>();
                }

                var fileIds = files.Select(x => x.FileID).ToList();

                // نسخه‌های فعلی، با یک کوئری
                var versions = db.MDR_FileVersions
                    .AsNoTracking()
                    .Where(v => fileIds.Contains(v.FileID))
                    .ToList();

                return files.Select(f =>
                {
                    var current = versions.FirstOrDefault(
                        v => v.FileID == f.FileID && v.VersionNo == f.CurrentVersionNo);

                    return new FileInfoItem
                    {
                        FileID = f.FileID,
                        FileGuid = f.FileGuid,
                        Title = f.Title,
                        Description = f.Description,
                        Category = f.Category,
                        CurrentVersionNo = f.CurrentVersionNo,

                        OriginalFileName = current != null ? current.OriginalFileName : null,
                        Extension = current != null ? current.Extension : null,
                        FileSize = current != null ? current.FileSize : 0,

                        PersianDate = f.PersianDate,
                        CreatedByName = f.CreatedByName,
                        LastPersianDate = current != null ? current.PersianDate : f.PersianDate,
                        LastUploadedByName = current != null ? current.UploadedByName : f.CreatedByName
                    };
                }).ToList();
            }
        }


        // =================================================
        // MDR 1.5.2.4 — تاریخچه‌ی نسخه‌های یک پیوست
        // =================================================

        // includeDeleted = true فقط برای صفحه مدیریت فایل‌ها
        public static List<FileVersionItem> GetVersions(
            Guid fileGuid,
            bool includeDeleted = false)
        {
            using (var db = new MdrContext())
            {
                var entity = db.MDR_Files
                    .AsNoTracking()
                    .FirstOrDefault(x => x.FileGuid == fileGuid &&
                                         (includeDeleted || !x.IsDeleted));

                if (entity == null)
                {
                    return new List<FileVersionItem>();
                }

                return db.MDR_FileVersions
                    .AsNoTracking()
                    .Where(v => v.FileID == entity.FileID)
                    .OrderByDescending(v => v.VersionNo)
                    .ToList()
                    .Select(v => new FileVersionItem
                    {
                        VersionNo = v.VersionNo,
                        OriginalFileName = v.OriginalFileName,
                        Extension = v.Extension,
                        FileSize = v.FileSize,
                        ChangeNote = v.ChangeNote,
                        PersianDate = v.PersianDate,
                        Time = v.CreatedAt.ToString("HH:mm"),
                        UploadedByName = v.UploadedByName,
                        IsCurrent = v.VersionNo == entity.CurrentVersionNo
                    })
                    .ToList();
            }
        }


        // =================================================
        // MDR 1.5.2.5 — آماده‌سازی دانلود
        // versionNo = null یعنی نسخه‌ی فعلی
        // خروجی null یعنی فایل پیدا نشد
        // =================================================

        // includeDeleted = true فقط برای صفحه مدیریت فایل‌ها
        public static DownloadInfo GetDownload(
            Guid fileGuid,
            int? versionNo = null,
            bool includeDeleted = false)
        {
            using (var db = new MdrContext())
            {
                var entity = db.MDR_Files
                    .AsNoTracking()
                    .FirstOrDefault(x => x.FileGuid == fileGuid &&
                                         (includeDeleted || !x.IsDeleted));

                if (entity == null)
                {
                    return null;
                }

                int targetVersion = versionNo ?? entity.CurrentVersionNo;

                var version = db.MDR_FileVersions
                    .AsNoTracking()
                    .FirstOrDefault(v => v.FileID == entity.FileID &&
                                         v.VersionNo == targetVersion);

                if (version == null)
                {
                    return null;
                }

                string physicalPath = GetPhysicalPath(version.RelativePath);

                if (physicalPath == null || !File.Exists(physicalPath))
                {
                    LogError(entity.ModuleName,
                        "فایل فیزیکی پیوست #" + entity.FileID + " نسخه " + targetVersion + " پیدا نشد",
                        null);

                    return null;
                }

                AuditLogger.Log(
                    AuditLogger.Actions.View,
                    entity.ModuleName,
                    "دانلود پیوست «" + entity.Title + "» (نسخه " + targetVersion + ")",
                    entityName: FileSettings.FilesTable,
                    entityId: entity.FileID,
                    entityTitle: entity.Title);

                return new DownloadInfo
                {
                    PhysicalPath = physicalPath,
                    DownloadName = version.OriginalFileName,
                    ContentType = string.IsNullOrWhiteSpace(version.ContentType)
                        ? MimeMapping.GetMimeMapping(version.OriginalFileName)
                        : version.ContentType
                };
            }
        }


        // =================================================
        // MDR 1.5.2.6 — حذف (نرم)
        // =================================================

        public static FileOperationResult Delete(Guid fileGuid)
        {
            string moduleName = null;

            try
            {
                using (var db = new MdrContext())
                {
                    var entity = db.MDR_Files
                        .FirstOrDefault(x => x.FileGuid == fileGuid && !x.IsDeleted);

                    if (entity == null)
                    {
                        return FileOperationResult.Fail("پیوست موردنظر پیدا نشد.");
                    }

                    moduleName = entity.ModuleName;

                    var user = GetCurrentUser(db);

                    entity.IsDeleted = true;
                    entity.DeletedAt = DateTime.Now;
                    entity.DeletedByUserID = user.UserID;
                    entity.DeletedByName = user.FullName;

                    db.SaveChanges();

                    AuditLogger.Log(
                        AuditLogger.Actions.Delete,
                        entity.ModuleName,
                        "حذف پیوست «" + entity.Title + "» از " + entity.EntityName + " #" + entity.EntityID,
                        entityName: FileSettings.FilesTable,
                        entityId: entity.FileID,
                        entityTitle: entity.Title,
                        oldValues: new
                        {
                            entity.Title,
                            entity.Category,
                            OwnerEntity = entity.EntityName,
                            OwnerEntityID = entity.EntityID,
                            entity.CurrentVersionNo
                        });

                    return new FileOperationResult
                    {
                        Success = true,
                        Message = "پیوست حذف شد.",
                        FileID = entity.FileID,
                        FileGuid = entity.FileGuid
                    };
                }
            }
            catch (Exception ex)
            {
                LogError(moduleName, "خطا در حذف پیوست", ex);

                return FileOperationResult.Fail("خطا در حذف پیوست: " + ex.Message);
            }
        }


        // =================================================
        // MDR 1.5.5.1 — بازگردانی پیوست حذف‌شده
        // (فایل فیزیکی هیچ‌وقت پاک نشده؛ فقط علامت حذف برداشته می‌شود)
        // =================================================

        public static FileOperationResult Restore(Guid fileGuid)
        {
            string moduleName = null;

            try
            {
                using (var db = new MdrContext())
                {
                    var entity = db.MDR_Files
                        .FirstOrDefault(x => x.FileGuid == fileGuid && x.IsDeleted);

                    if (entity == null)
                    {
                        return FileOperationResult.Fail("پیوست حذف‌شده‌ای با این مشخصات پیدا نشد.");
                    }

                    moduleName = entity.ModuleName;

                    var user = GetCurrentUser(db);

                    var oldValues = new
                    {
                        IsDeleted = true,
                        entity.DeletedAt,
                        entity.DeletedByName
                    };

                    entity.IsDeleted = false;
                    entity.DeletedAt = null;
                    entity.DeletedByUserID = null;
                    entity.DeletedByName = null;

                    entity.UpdatedAt = DateTime.Now;
                    entity.UpdatedByUserID = user.UserID;
                    entity.UpdatedByName = user.FullName;

                    db.SaveChanges();

                    AuditLogger.Log(
                        AuditLogger.Actions.Update,
                        entity.ModuleName,
                        "بازگردانی پیوست حذف‌شده «" + entity.Title + "» برای " +
                            entity.EntityName + " #" + entity.EntityID,
                        entityName: FileSettings.FilesTable,
                        entityId: entity.FileID,
                        entityTitle: entity.Title,
                        oldValues: oldValues,
                        newValues: new { IsDeleted = false });

                    return new FileOperationResult
                    {
                        Success = true,
                        Message = "پیوست بازگردانی شد.",
                        FileID = entity.FileID,
                        FileGuid = entity.FileGuid
                    };
                }
            }
            catch (Exception ex)
            {
                LogError(moduleName, "خطا در بازگردانی پیوست", ex);

                return FileOperationResult.Fail("خطا در بازگردانی پیوست: " + ex.Message);
            }
        }


        // =================================================
        // داخلی: آماده‌سازی و اعتبارسنجی فایل
        // =================================================

        private class PreparedFile
        {
            public byte[] Content;
            public long Size;
            public string Extension;
            public string ContentType;
            public string DisplayName;
            public string DisplayNameWithoutExtension;
            public string Hash;
        }


        // خروجی: null یعنی بدون خطا؛ در غیر این صورت پیام خطا
        private static string Prepare(
            HttpPostedFileBase file,
            out PreparedFile prepared)
        {
            prepared = null;

            if (file == null || file.ContentLength <= 0)
            {
                return "فایلی انتخاب نشده یا فایل خالی است.";
            }

            if (file.ContentLength > FileSettings.MaxFileSizeBytes)
            {
                return "حجم فایل بیشتر از حد مجاز (" +
                       (FileSettings.MaxFileSizeBytes / 1024 / 1024) + " مگابایت) است.";
            }

            string displayName = SanitizeFileName(file.FileName);

            if (displayName == null)
            {
                return "نام فایل معتبر نیست.";
            }

            string extension = Path.GetExtension(displayName).ToLowerInvariant();

            if (string.IsNullOrEmpty(extension) || !AllowedTypes.ContainsKey(extension))
            {
                return "نوع فایل مجاز نیست. پسوندهای مجاز: " + AllowedExtensionsText;
            }

            byte[] content;

            using (var memory = new MemoryStream())
            {
                file.InputStream.Position = 0;
                file.InputStream.CopyTo(memory);
                content = memory.ToArray();
            }

            if (content.Length == 0)
            {
                return "فایل خالی است.";
            }

            if (!HasValidSignature(content, extension))
            {
                return "محتوای فایل با پسوند آن (" + extension + ") مطابقت ندارد.";
            }

            prepared = new PreparedFile
            {
                Content = content,
                Size = content.Length,
                Extension = extension,
                ContentType = MimeMapping.GetMimeMapping(displayName),
                DisplayName = displayName,
                DisplayNameWithoutExtension = Path.GetFileNameWithoutExtension(displayName),
                Hash = ComputeSha256(content)
            };

            return null;
        }


        private static bool HasValidSignature(byte[] content, string extension)
        {
            byte[][] signatures = AllowedTypes[extension];

            // فایل متنی: نباید بایت صفر داشته باشد (یعنی باینری نیست)
            if (signatures == null)
            {
                int length = Math.Min(content.Length, 8192);

                for (int i = 0; i < length; i++)
                {
                    if (content[i] == 0)
                    {
                        return false;
                    }
                }

                return true;
            }

            foreach (var signature in signatures)
            {
                if (content.Length < signature.Length)
                {
                    continue;
                }

                bool match = true;

                for (int i = 0; i < signature.Length; i++)
                {
                    if (content[i] != signature[i])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return true;
                }
            }

            return false;
        }


        // نام نمایشی امن: بدون مسیر، بدون کاراکتر غیرمجاز
        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            // بعضی مرورگرهای قدیمی مسیر کامل می‌فرستند
            string name = fileName.Replace('\\', '/');
            int slash = name.LastIndexOf('/');

            if (slash >= 0)
            {
                name = name.Substring(slash + 1);
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder();

            foreach (char c in name)
            {
                builder.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
            }

            name = builder.ToString().Trim().Trim('.');

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            // حداکثر ۲۰۰ کاراکتر، با حفظ پسوند
            if (name.Length > 200)
            {
                string ext = Path.GetExtension(name);
                name = name.Substring(0, 200 - ext.Length) + ext;
            }

            return name;
        }


        // =================================================
        // داخلی: ذخیره‌ی فیزیکی
        // =================================================

        // مسیر: ~/App_Data/MDR_Files/1405/07/{guid}.pdf
        private static string SavePhysical(PreparedFile prepared, DateTime now)
        {
            var pc = new PersianCalendar();

            string folder = string.Format(
                "{0:0000}/{1:00}",
                pc.GetYear(now),
                pc.GetMonth(now));

            string storedName = Guid.NewGuid().ToString("N") + prepared.Extension;
            string relativePath = folder + "/" + storedName;

            string physicalPath = GetPhysicalPath(relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath));
            File.WriteAllBytes(physicalPath, prepared.Content);

            return physicalPath;
        }


        private static MdrFileVersion NewVersion(
            long fileId,
            int versionNo,
            PreparedFile prepared,
            string physicalPath,
            DateTime now,
            string persianDate,
            CurrentUserInfo user,
            string changeNote)
        {
            string storedName = Path.GetFileName(physicalPath);

            return new MdrFileVersion
            {
                FileID = fileId,
                VersionNo = versionNo,

                OriginalFileName = Cut(prepared.DisplayName, 260),
                Extension = Cut(prepared.Extension, 20),
                ContentType = Cut(prepared.ContentType, 150),
                FileSize = prepared.Size,

                StoredFileName = Cut(storedName, 100),
                RelativePath = Cut(ToRelativePath(physicalPath), 300),

                FileHash = prepared.Hash,
                ChangeNote = Cut(Clean(changeNote), 500),

                CreatedAt = now,
                PersianDate = persianDate,
                UploadedByUserID = user.UserID,
                UploadedByName = user.FullName
            };
        }


        private static string GetRootPath()
        {
            return HttpContext.Current != null
                ? HttpContext.Current.Server.MapPath(FileSettings.StorageRoot)
                : System.Web.Hosting.HostingEnvironment.MapPath(FileSettings.StorageRoot);
        }


        // مسیر نسبی → مسیر فیزیکی، با جلوگیری از خروج از پوشه‌ی ذخیره
        private static string GetPhysicalPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            string root = Path.GetFullPath(GetRootPath())
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            string full = Path.GetFullPath(
                Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full
                : null;
        }


        private static string ToRelativePath(string physicalPath)
        {
            string root = Path.GetFullPath(GetRootPath())
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return physicalPath
                .Substring(root.Length)
                .Replace(Path.DirectorySeparatorChar, '/');
        }


        private static void DeletePhysicalQuietly(string physicalPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(physicalPath) && File.Exists(physicalPath))
                {
                    File.Delete(physicalPath);
                }
            }
            catch
            {
            }
        }


        // =================================================
        // داخلی: کاربر جاری
        // همان روش AuditLogger: کوکی CodeMeli → tbUsers
        // =================================================

        private class CurrentUserInfo
        {
            public int? UserID;
            public string FullName;
        }


        private static CurrentUserInfo GetCurrentUser(MdrContext db)
        {
            var info = new CurrentUserInfo();

            try
            {
                var cookie = HttpContext.Current?.Request?.Cookies[
                    MdrBase64.Base64Encode("CodeMeli")];

                if (cookie == null || string.IsNullOrWhiteSpace(cookie.Value))
                {
                    return info;
                }

                string nationalCode = MdrBase64.Base64Decode(cookie.Value);

                var user = db.tbUsers
                    .AsNoTracking()
                    .Where(u => u.usr_NationalCode.ToString() == nationalCode)
                    .Select(u => new { u.usr_ID, u.usr_Name, u.usr_Family })
                    .FirstOrDefault();

                if (user != null)
                {
                    info.UserID = user.usr_ID;
                    info.FullName = Cut(Clean((user.usr_Name ?? "") + " " + (user.usr_Family ?? "")), 200);
                }
            }
            catch
            {
            }

            return info;
        }


        // =================================================
        // Helpers
        // =================================================

        private static void LogError(string moduleName, string description, Exception ex)
        {
            AuditLogger.Log(
                AuditLogger.Actions.Error,
                moduleName ?? "Files",
                description + (ex != null ? ": " + ex.Message : ""),
                entityName: FileSettings.FilesTable,
                isSuccess: false);
        }


        private static string ComputeSha256(byte[] content)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(content);
                var builder = new StringBuilder(64);

                foreach (byte b in hash)
                {
                    builder.Append(b.ToString("x2"));
                }

                return builder.ToString();
            }
        }


        private static string ToPersianDate(DateTime date)
        {
            var pc = new PersianCalendar();

            return string.Format(
                "{0:0000}/{1:00}/{2:00}",
                pc.GetYear(date),
                pc.GetMonth(date),
                pc.GetDayOfMonth(date));
        }


        // فاصله‌های اضافه حذف؛ رشته‌ی خالی → null
        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return System.Text.RegularExpressions.Regex
                .Replace(value, @"\s+", " ")
                .Trim();
        }


        private static string Cut(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength);
        }


        private static byte[] Bytes(params int[] values)
        {
            return values.Select(v => (byte)v).ToArray();
        }
    }
}
