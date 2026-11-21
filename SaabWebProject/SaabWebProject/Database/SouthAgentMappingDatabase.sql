/* Execute this script in the existing jonobi10aban database.
   It creates only a table; it does not create a second database. */
USE [jonobi10aban];
GO

IF OBJECT_ID(N'dbo.SouthOfficerUserMap', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SouthOfficerUserMap
    (
        ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SouthOfficerUserMap PRIMARY KEY,
        FKusr INT NOT NULL,
        codeusrsystem NVARCHAR(50) NOT NULL,
        officer_id INT NOT NULL,
        AgentName NVARCHAR(250) NULL,
        CityName NVARCHAR(100) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_SouthOfficerUserMap_IsActive DEFAULT (1),
        UpdatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_SouthOfficerUserMap_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_SouthOfficerUserMap_tbUsers FOREIGN KEY(FKusr) REFERENCES [Usser].[tbUsers]([usr_ID])
    );
    CREATE UNIQUE INDEX UX_SouthOfficerUserMap_ActiveCode ON dbo.SouthOfficerUserMap(codeusrsystem) WHERE IsActive = 1;
    CREATE UNIQUE INDEX UX_SouthOfficerUserMap_ActiveOfficerId ON dbo.SouthOfficerUserMap(officer_id) WHERE IsActive = 1;
END;
GO
