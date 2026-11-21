SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.DC_ModuleUser', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_ModuleUser
    (
        UserId      INT NOT NULL,
        RoleCode    VARCHAR(20) NOT NULL, -- Admin, Manager
        IsActive    BIT NOT NULL CONSTRAINT DF_DC_ModuleUser_Active DEFAULT(1),
        CONSTRAINT PK_DC_ModuleUser PRIMARY KEY(UserId, RoleCode),
        CONSTRAINT CK_DC_ModuleUser_Role CHECK(RoleCode IN ('Admin','Manager'))
    );
END;

IF OBJECT_ID(N'dbo.DC_DocumentDefinition', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_DocumentDefinition
    (
        Id                      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DC_DocumentDefinition PRIMARY KEY,
        Title                   NVARCHAR(250) NOT NULL,
        Alias                   NVARCHAR(150) NULL,
        AllowedExtensions       NVARCHAR(300) NOT NULL CONSTRAINT DF_DC_Doc_Ext DEFAULT(N'.pdf'),
        MaxFileSizeMb           INT NOT NULL CONSTRAINT DF_DC_Doc_Size DEFAULT(20),
        StatementMonthOffset    SMALLINT NOT NULL CONSTRAINT DF_DC_Doc_MonthOffset DEFAULT(0),
        IsPeriodic              BIT NOT NULL CONSTRAINT DF_DC_Doc_Periodic DEFAULT(1),
        IsActive                BIT NOT NULL CONSTRAINT DF_DC_Doc_Active DEFAULT(1),
        CreatedBy               INT NOT NULL,
        CreatedAt               DATETIME2(0) NOT NULL CONSTRAINT DF_DC_Doc_CreatedAt DEFAULT(SYSDATETIME()),
        ModifiedAt              DATETIME2(0) NULL
    );
END;

IF OBJECT_ID(N'dbo.DC_DocumentContract', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_DocumentContract
    (
        DocumentDefinitionId INT NOT NULL,
        ContractId           INT NOT NULL,
        CONSTRAINT PK_DC_DocumentContract PRIMARY KEY(DocumentDefinitionId, ContractId),
        CONSTRAINT FK_DC_DocumentContract_Document FOREIGN KEY(DocumentDefinitionId) REFERENCES dbo.DC_DocumentDefinition(Id),
        CONSTRAINT FK_DC_DocumentContract_Contract FOREIGN KEY(ContractId) REFERENCES [Contract].[tbPeymanContracts](pec_ID)
    );
END;

IF OBJECT_ID(N'dbo.DC_ResultTitle', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_ResultTitle
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DC_ResultTitle PRIMARY KEY,
        Title           NVARCHAR(200) NOT NULL,
        ResultBehavior  TINYINT NOT NULL, -- 1=Advance, 2=Stay, 3=ReturnPrevious
        ColorCode       VARCHAR(20) NULL,
        RequiresComment BIT NOT NULL CONSTRAINT DF_DC_Result_Comment DEFAULT(0),
        IsFinalApproval BIT NOT NULL CONSTRAINT DF_DC_Result_Final DEFAULT(0),
        IsActive        BIT NOT NULL CONSTRAINT DF_DC_Result_Active DEFAULT(1),
        CreatedAt       DATETIME2(0) NOT NULL CONSTRAINT DF_DC_Result_CreatedAt DEFAULT(SYSDATETIME()),
        CONSTRAINT CK_DC_Result_Behavior CHECK(ResultBehavior IN (1,2,3))
    );
END;

IF OBJECT_ID(N'dbo.DC_IntegrationEndpoint', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_IntegrationEndpoint
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DC_IntegrationEndpoint PRIMARY KEY,
        Title           NVARCHAR(200) NOT NULL,
        Direction       TINYINT NOT NULL, -- 1=Source, 2=ApprovedOutput
        ProcedureName   NVARCHAR(256) NOT NULL,
        IsActive        BIT NOT NULL CONSTRAINT DF_DC_Endpoint_Active DEFAULT(1),
        CONSTRAINT CK_DC_Endpoint_Direction CHECK(Direction IN (1,2)),
        CONSTRAINT UQ_DC_Endpoint UNIQUE(Direction, ProcedureName)
    );
END;

IF OBJECT_ID(N'dbo.DC_Workflow', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_Workflow
    (
        Id                      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DC_Workflow PRIMARY KEY,
        ContractId              INT NOT NULL,
        DocumentDefinitionId    INT NOT NULL,
        Alias                   NVARCHAR(200) NOT NULL,
        SourceEndpointId        INT NULL,
        OutputEndpointId        INT NULL,
        IsActive                BIT NOT NULL CONSTRAINT DF_DC_Workflow_Active DEFAULT(1),
        CreatedBy               INT NOT NULL,
        CreatedAt               DATETIME2(0) NOT NULL CONSTRAINT DF_DC_Workflow_CreatedAt DEFAULT(SYSDATETIME()),
        ModifiedAt              DATETIME2(0) NULL,
        RowVersion              ROWVERSION NOT NULL,
        CONSTRAINT FK_DC_Workflow_Contract FOREIGN KEY(ContractId) REFERENCES [Contract].[tbPeymanContracts](pec_ID),
        CONSTRAINT FK_DC_Workflow_Document FOREIGN KEY(DocumentDefinitionId) REFERENCES dbo.DC_DocumentDefinition(Id),
        CONSTRAINT FK_DC_Workflow_Source FOREIGN KEY(SourceEndpointId) REFERENCES dbo.DC_IntegrationEndpoint(Id),
        CONSTRAINT FK_DC_Workflow_Output FOREIGN KEY(OutputEndpointId) REFERENCES dbo.DC_IntegrationEndpoint(Id),
        CONSTRAINT UQ_DC_Workflow UNIQUE(ContractId, DocumentDefinitionId)
    );
END;

IF OBJECT_ID(N'dbo.DC_WorkflowStep', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_WorkflowStep
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DC_WorkflowStep PRIMARY KEY,
        WorkflowId      INT NOT NULL,
        Title           NVARCHAR(200) NOT NULL,
        Alias           NVARCHAR(120) NULL,
        SortOrder       INT NOT NULL,
        AllowedHours    INT NOT NULL,
        IsUploadStep    BIT NOT NULL CONSTRAINT DF_DC_Step_Upload DEFAULT(0),
        IsFinalStep     BIT NOT NULL CONSTRAINT DF_DC_Step_Final DEFAULT(0),
        CONSTRAINT FK_DC_Step_Workflow FOREIGN KEY(WorkflowId) REFERENCES dbo.DC_Workflow(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_DC_Step_Order UNIQUE(WorkflowId, SortOrder),
        CONSTRAINT CK_DC_Step_Hours CHECK(AllowedHours > 0)
    );
END;

IF OBJECT_ID(N'dbo.DC_WorkflowStepUser', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_WorkflowStepUser
    (
        WorkflowStepId INT NOT NULL,
        UserId         INT NOT NULL,
        AccessType     TINYINT NOT NULL, -- 1=Action, 2=ViewOnly
        CONSTRAINT PK_DC_StepUser PRIMARY KEY(WorkflowStepId, UserId, AccessType),
        CONSTRAINT FK_DC_StepUser_Step FOREIGN KEY(WorkflowStepId) REFERENCES dbo.DC_WorkflowStep(Id) ON DELETE CASCADE,
        CONSTRAINT CK_DC_StepUser_Access CHECK(AccessType IN (1,2))
    );
END;

IF OBJECT_ID(N'dbo.DC_WorkflowStepResult', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_WorkflowStepResult
    (
        WorkflowStepId INT NOT NULL,
        ResultTitleId  INT NOT NULL,
        CONSTRAINT PK_DC_StepResult PRIMARY KEY(WorkflowStepId, ResultTitleId),
        CONSTRAINT FK_DC_StepResult_Step FOREIGN KEY(WorkflowStepId) REFERENCES dbo.DC_WorkflowStep(Id) ON DELETE CASCADE,
        CONSTRAINT FK_DC_StepResult_Title FOREIGN KEY(ResultTitleId) REFERENCES dbo.DC_ResultTitle(Id)
    );
END;

IF OBJECT_ID(N'dbo.DC_DocumentInstance', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_DocumentInstance
    (
        Id                      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DC_DocumentInstance PRIMARY KEY,
        ContractId              INT NOT NULL,
        DocumentDefinitionId    INT NOT NULL,
        WorkflowId              INT NOT NULL,
        StatementNo             NVARCHAR(100) NULL,
        PeriodYear              SMALLINT NOT NULL,
        PeriodMonth             TINYINT NOT NULL,
        CurrentStepId           INT NULL,
        Status                  VARCHAR(30) NOT NULL CONSTRAINT DF_DC_Instance_Status DEFAULT('Draft'),
        ApprovedTotal           DECIMAL(20,2) NULL,
        DueAt                   DATETIME2(0) NULL,
        IsOpen                  BIT NOT NULL CONSTRAINT DF_DC_Instance_Open DEFAULT(1),
        CreatedBy               INT NOT NULL,
        CreatedAt               DATETIME2(0) NOT NULL CONSTRAINT DF_DC_Instance_CreatedAt DEFAULT(SYSDATETIME()),
        ClosedAt                DATETIME2(0) NULL,
        RowVersion              ROWVERSION NOT NULL,
        CONSTRAINT FK_DC_Instance_Contract FOREIGN KEY(ContractId) REFERENCES [Contract].[tbPeymanContracts](pec_ID),
        CONSTRAINT FK_DC_Instance_Document FOREIGN KEY(DocumentDefinitionId) REFERENCES dbo.DC_DocumentDefinition(Id),
        CONSTRAINT FK_DC_Instance_Workflow FOREIGN KEY(WorkflowId) REFERENCES dbo.DC_Workflow(Id),
        CONSTRAINT FK_DC_Instance_CurrentStep FOREIGN KEY(CurrentStepId) REFERENCES dbo.DC_WorkflowStep(Id),
        CONSTRAINT CK_DC_Instance_Month CHECK(PeriodMonth BETWEEN 1 AND 12),
        CONSTRAINT UQ_DC_Instance_Period UNIQUE(ContractId, DocumentDefinitionId, PeriodYear, PeriodMonth)
    );
END;

IF OBJECT_ID(N'dbo.DC_DocumentFile', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_DocumentFile
    (
        Id                  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DC_DocumentFile PRIMARY KEY,
        DocumentInstanceId  BIGINT NOT NULL,
        OriginalName        NVARCHAR(260) NOT NULL,
        StoredName          NVARCHAR(260) NOT NULL,
        Extension           NVARCHAR(20) NOT NULL,
        ContentType         NVARCHAR(150) NULL,
        FileSize            BIGINT NOT NULL,
        Sha256              CHAR(64) NOT NULL,
        UploadedBy          INT NOT NULL,
        UploadedAt          DATETIME2(0) NOT NULL CONSTRAINT DF_DC_File_UploadedAt DEFAULT(SYSDATETIME()),
        IsCurrent           BIT NOT NULL CONSTRAINT DF_DC_File_Current DEFAULT(1),
        CONSTRAINT FK_DC_File_Instance FOREIGN KEY(DocumentInstanceId) REFERENCES dbo.DC_DocumentInstance(Id)
    );
END;

IF OBJECT_ID(N'dbo.DC_DocumentAction', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_DocumentAction
    (
        Id                  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DC_DocumentAction PRIMARY KEY,
        DocumentInstanceId  BIGINT NOT NULL,
        WorkflowStepId      INT NULL,
        ActionType          VARCHAR(40) NOT NULL,
        ResultTitleId       INT NULL,
        Comment             NVARCHAR(MAX) NULL,
        ApprovedTotal       DECIMAL(20,2) NULL,
        DiscrepanciesJson   NVARCHAR(MAX) NULL,
        FromStatus          VARCHAR(30) NULL,
        ToStatus            VARCHAR(30) NULL,
        PerformedBy         INT NOT NULL,
        PerformedAt         DATETIME2(0) NOT NULL CONSTRAINT DF_DC_Action_At DEFAULT(SYSDATETIME()),
        CONSTRAINT FK_DC_Action_Instance FOREIGN KEY(DocumentInstanceId) REFERENCES dbo.DC_DocumentInstance(Id),
        CONSTRAINT FK_DC_Action_Step FOREIGN KEY(WorkflowStepId) REFERENCES dbo.DC_WorkflowStep(Id),
        CONSTRAINT FK_DC_Action_Result FOREIGN KEY(ResultTitleId) REFERENCES dbo.DC_ResultTitle(Id)
    );
END;

IF OBJECT_ID(N'dbo.DC_DocumentStepState', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DC_DocumentStepState
    (
        DocumentInstanceId BIGINT NOT NULL,
        WorkflowStepId     INT NOT NULL,
        Status             VARCHAR(25) NOT NULL,
        StartedAt          DATETIME2(0) NULL,
        DueAt              DATETIME2(0) NULL,
        CompletedAt        DATETIME2(0) NULL,
        ReturnCount        INT NOT NULL CONSTRAINT DF_DC_StepState_Return DEFAULT(0),
        CONSTRAINT PK_DC_StepState PRIMARY KEY(DocumentInstanceId, WorkflowStepId),
        CONSTRAINT FK_DC_StepState_Instance FOREIGN KEY(DocumentInstanceId) REFERENCES dbo.DC_DocumentInstance(Id),
        CONSTRAINT FK_DC_StepState_Step FOREIGN KEY(WorkflowStepId) REFERENCES dbo.DC_WorkflowStep(Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_DC_Instance_Dashboard' AND object_id=OBJECT_ID(N'dbo.DC_DocumentInstance'))
    CREATE INDEX IX_DC_Instance_Dashboard ON dbo.DC_DocumentInstance(ContractId, Status, DueAt) INCLUDE(DocumentDefinitionId, CurrentStepId, PeriodYear, PeriodMonth);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_DC_Action_History' AND object_id=OBJECT_ID(N'dbo.DC_DocumentAction'))
    CREATE INDEX IX_DC_Action_History ON dbo.DC_DocumentAction(DocumentInstanceId, PerformedAt DESC);

IF NOT EXISTS (SELECT 1 FROM dbo.DC_ResultTitle)
BEGIN
    INSERT dbo.DC_ResultTitle(Title, ResultBehavior, ColorCode, RequiresComment, IsFinalApproval)
    VALUES
      (N'تأیید', 1, '#16a34a', 0, 1),
      (N'تأیید مشروط', 1, '#f59e0b', 1, 0),
      (N'نیازمند اصلاح', 2, '#ea580c', 1, 0),
      (N'عدم پذیرش', 3, '#dc2626', 1, 0);
END;

COMMIT TRANSACTION;
