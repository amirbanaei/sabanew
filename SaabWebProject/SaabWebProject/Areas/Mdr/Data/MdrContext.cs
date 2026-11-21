using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Configuration;
using System.Data.Entity;
using System.Data.Entity.Core.EntityClient;

namespace SaabWebProject.Areas.Mdr.Data
{
    // =====================================================
    // MDR | Phase 0 | DbContext جداگانه برای جدول‌های جدید
    // تا DomainModel.edmx (حدود ۲MB) دست نخورد.
    // اتصال از همان رشته‌ی SaabEntities ساخته می‌شود تا حتماً
    // به همان دیتابیسِ EF برنامه وصل شود (در Web.config رشته‌ی
    // SaabDatabaseConnection به دیتابیس دیگری اشاره می‌کند).
    // هیچ‌وقت دیتابیس یا جدول خودکار نمی‌سازد؛ DDL فقط از MDR_SQL.
    // =====================================================
    public class MdrContext : DbContext
    {
        static MdrContext()
        {
            Database.SetInitializer<MdrContext>(null);
        }

        public MdrContext() : base(BuildConnectionString())
        {
        }

        public DbSet<MdrUserPreference> UserPreferences { get; set; }
        public DbSet<MdrWorkflowDefinition> WorkflowDefinitions { get; set; }
        public DbSet<MdrWorkflowStep> WorkflowSteps { get; set; }
        public DbSet<MdrWorkflowTransition> WorkflowTransitions { get; set; }
        public DbSet<MdrWorkflowInstance> WorkflowInstances { get; set; }
        public DbSet<MdrWorkflowHistory> WorkflowHistory { get; set; }
        public DbSet<MdrSavedSearch> SavedSearches { get; set; }
        public DbSet<MdrDocFolder> DocFolders { get; set; }
        public DbSet<MdrDocument> Documents { get; set; }
        public DbSet<MdrDocumentTag> DocumentTags { get; set; }
        public DbSet<MdrDocumentShare> DocumentShares { get; set; }
        public DbSet<MdrDocumentLink> DocumentLinks { get; set; }
        public DbSet<MdrDocumentHistory> DocumentHistory { get; set; }
        public DbSet<MdrLetter> Letters { get; set; }
        public DbSet<MdrLetterRecipient> LetterRecipients { get; set; }
        public DbSet<MdrLetterHistory> LetterHistory { get; set; }
        public DbSet<MdrRequestType> RequestTypes { get; set; }
        public DbSet<MdrRequest> Requests { get; set; }
        public DbSet<MdrRequestHistory> RequestHistory { get; set; }
        public DbSet<MdrTask> Tasks { get; set; }
        public DbSet<MdrTaskComment> TaskComments { get; set; }
        public DbSet<MdrTaskHistory> TaskHistory { get; set; }
        public DbSet<MdrCase> Cases { get; set; }
        public DbSet<MdrCaseMember> CaseMembers { get; set; }
        public DbSet<MdrCaseLink> CaseLinks { get; set; }
        public DbSet<MdrCaseNote> CaseNotes { get; set; }
        public DbSet<MdrCaseHistory> CaseHistory { get; set; }
        public DbSet<MdrReportSchedule> ReportSchedules { get; set; }
        public DbSet<MdrChatThread> ChatThreads { get; set; }
        public DbSet<MdrChatMember> ChatMembers { get; set; }
        public DbSet<MdrChatMessage> ChatMessages { get; set; }
        public DbSet<MdrChatMention> ChatMentions { get; set; }
        public DbSet<MdrFinDocument> FinDocuments { get; set; }
        public DbSet<MdrFinApprover> FinApprovers { get; set; }
        public DbSet<MdrFinApproval> FinApprovals { get; set; }
        public DbSet<MdrFinHistory> FinHistory { get; set; }
        public DbSet<MdrFinBudget> FinBudgets { get; set; }
        public DbSet<MdrFinPeriod> FinPeriods { get; set; }
        public DbSet<MdrWhStore> WhStores { get; set; }
        public DbSet<MdrWhItem> WhItems { get; set; }
        public DbSet<MdrWhMovement> WhMovements { get; set; }
        public DbSet<MdrWhRequest> WhRequests { get; set; }
        public DbSet<MdrWhRequestLine> WhRequestLines { get; set; }
        public DbSet<MdrAsset> Assets { get; set; }
        public DbSet<MdrAssetEvent> AssetEvents { get; set; }
        public DbSet<MdrSkill> Skills { get; set; }
        public DbSet<MdrCourse> Courses { get; set; }
        public DbSet<MdrCourseEnrollment> CourseEnrollments { get; set; }
        public DbSet<MdrCourseAttendance> CourseAttendance { get; set; }
        public DbSet<MdrSkillProfile> SkillProfiles { get; set; }
        public DbSet<MdrProfileSkill> ProfileSkills { get; set; }
        public DbSet<MdrUserProfile> UserProfiles { get; set; }
        public DbSet<MdrUserSkill> UserSkills { get; set; }
        public DbSet<MdrAiQuery> AiQueries { get; set; }
        public DbSet<MdrAiQuerySource> AiQuerySources { get; set; }
        public DbSet<MdrAiSuggestion> AiSuggestions { get; set; }

        // زیرساخت مشترک و جدول‌های قدیمی سامانه (فقط خواندن) — MdrCoreEntities.cs
        public DbSet<MdrOrganizationUnit> MDR_OrganizationUnits { get; set; }
        public DbSet<MdrAuditLog> MDR_AuditLogs { get; set; }
        public DbSet<MdrFile> MDR_Files { get; set; }
        public DbSet<MdrFileVersion> MDR_FileVersions { get; set; }
        public DbSet<MdrNotification> MDR_Notifications { get; set; }
        public DbSet<MdrUser> tbUsers { get; set; }
        public DbSet<MdrDetailJob> tbdetailjob { get; set; }
        public DbSet<MdrPage> tbPages { get; set; }
        public DbSet<MdrUserPageAccess> tbUserPageAccess { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("dbo");
        }

        private static string BuildConnectionString()
        {
            var entityCs = ConfigurationManager.ConnectionStrings["SaabEntities"].ConnectionString;
            return new EntityConnectionStringBuilder(entityCs).ProviderConnectionString;
        }
    }

    [Table("MDR_UserPreferences")]
    public class MdrUserPreference
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int UserID { get; set; }

        [Required, MaxLength(10)]
        public string ThemeMode { get; set; }

        [Required, MaxLength(10)]
        public string CultureCode { get; set; }

        public System.DateTime UpdatedAt { get; set; }
    }
}
