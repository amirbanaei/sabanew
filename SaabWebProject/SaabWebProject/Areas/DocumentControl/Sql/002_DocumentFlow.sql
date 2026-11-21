USE [sistan29azar];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.DF_Type',N'U') IS NULL
BEGIN
CREATE TABLE dbo.DF_Type(
 Id int IDENTITY PRIMARY KEY, Title nvarchar(200) NOT NULL, Description nvarchar(2000) NULL,
 ComponentId int NULL REFERENCES [Contract].tbContractMoalefeDastmozdi(md_ID),
 MonthOffset int NOT NULL CHECK(MonthOffset IN(0,-1)), IsActive bit NOT NULL DEFAULT 1,
 SampleName nvarchar(255) NULL, SampleData varbinary(max) NULL,
 UpdatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), Version rowversion NOT NULL);
CREATE UNIQUE INDEX UX_DF_Type_Title ON dbo.DF_Type(Title);
CREATE TABLE dbo.DF_Stage(
 Id int IDENTITY PRIMARY KEY, TypeId int NOT NULL REFERENCES dbo.DF_Type(Id),
 Position int NOT NULL CHECK(Position>0), Title nvarchar(100) NOT NULL,
 RequireAll bit NOT NULL, UNIQUE(TypeId,Position));
CREATE TABLE dbo.DF_StageUser(
 StageId int NOT NULL REFERENCES dbo.DF_Stage(Id), UserId int NOT NULL REFERENCES [Usser].tbUsers(usr_ID),
 PRIMARY KEY(StageId,UserId));
CREATE TABLE dbo.DF_Request(
 Id int IDENTITY PRIMARY KEY, TypeId int NOT NULL REFERENCES dbo.DF_Type(Id),
 TypeTitle nvarchar(200) NOT NULL, ComponentId int NULL REFERENCES [Contract].tbContractMoalefeDastmozdi(md_ID),
 ContractId int NOT NULL REFERENCES [Contract].tbPeymanContracts(pec_ID),
 OwnerId int NOT NULL REFERENCES [Usser].tbUsers(usr_ID),
 PeriodYear int NOT NULL CHECK(PeriodYear BETWEEN 1300 AND 1600),
 PeriodMonth int NOT NULL CHECK(PeriodMonth BETWEEN 1 AND 12),
 Status int NOT NULL CHECK(Status IN(0,1,2,3)), CurrentStep int NOT NULL,
 Revision int NOT NULL DEFAULT 1, ApprovedAmount decimal(18,2) NULL CHECK(ApprovedAmount>=0),
 CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), UpdatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UNIQUE(TypeId,ContractId,PeriodYear,PeriodMonth));
CREATE TABLE dbo.DF_RequestStep(
 RequestId int NOT NULL REFERENCES dbo.DF_Request(Id), Position int NOT NULL,
 Title nvarchar(100) NOT NULL, RequireAll bit NOT NULL, PRIMARY KEY(RequestId,Position));
CREATE TABLE dbo.DF_RequestReviewer(
 RequestId int NOT NULL, Position int NOT NULL, UserId int NOT NULL REFERENCES [Usser].tbUsers(usr_ID),
 PRIMARY KEY(RequestId,Position,UserId),
 FOREIGN KEY(RequestId,Position) REFERENCES dbo.DF_RequestStep(RequestId,Position));
CREATE TABLE dbo.DF_Revision(
 RequestId int NOT NULL REFERENCES dbo.DF_Request(Id), Revision int NOT NULL,
 Description nvarchar(4000) NOT NULL, ActorId int NOT NULL REFERENCES [Usser].tbUsers(usr_ID),
 CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), PRIMARY KEY(RequestId,Revision));
CREATE TABLE dbo.DF_Review(
 Id int IDENTITY PRIMARY KEY, RequestId int NOT NULL, Revision int NOT NULL, Step int NOT NULL,
 ReviewerId int NOT NULL REFERENCES [Usser].tbUsers(usr_ID),
 Decision int NOT NULL CHECK(Decision IN(0,1,2)), Description nvarchar(4000) NOT NULL,
 Amount decimal(18,2) NULL CHECK(Amount>=0), CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 FOREIGN KEY(RequestId,Revision) REFERENCES dbo.DF_Revision(RequestId,Revision),
 FOREIGN KEY(RequestId,Step) REFERENCES dbo.DF_RequestStep(RequestId,Position),
 UNIQUE(RequestId,Revision,Step,ReviewerId));
CREATE TABLE dbo.DF_File(
 Id int IDENTITY PRIMARY KEY, RequestId int NOT NULL, Revision int NOT NULL,
 ReviewId int NULL REFERENCES dbo.DF_Review(Id), Kind varchar(20) NOT NULL CHECK(Kind IN('excel','attachment','review')),
 FileName nvarchar(255) NOT NULL, ContentType varchar(100) NOT NULL,
 Data varbinary(max) NOT NULL, ActorId int NOT NULL REFERENCES [Usser].tbUsers(usr_ID),
 CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 FOREIGN KEY(RequestId,Revision) REFERENCES dbo.DF_Revision(RequestId,Revision));
CREATE INDEX IX_DF_Request_Owner ON dbo.DF_Request(OwnerId,Status);
CREATE INDEX IX_DF_Reviewer_User ON dbo.DF_RequestReviewer(UserId,RequestId,Position);
CREATE INDEX IX_DF_File_Request ON dbo.DF_File(RequestId,Revision);
END;
COMMIT;
GO
