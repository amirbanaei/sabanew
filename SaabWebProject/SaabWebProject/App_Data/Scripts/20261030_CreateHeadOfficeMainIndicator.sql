IF OBJECT_ID(N'dbo.tbHeadOfficeMainIndicator', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbHeadOfficeMainIndicator
    (
        IndicatorId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_tbHeadOfficeMainIndicator PRIMARY KEY,
        Title NVARCHAR(150) NOT NULL,
        Unit NVARCHAR(50) NOT NULL,
        IsDeleted BIT NOT NULL
            CONSTRAINT DF_tbHeadOfficeMainIndicator_IsDeleted DEFAULT (0),
        CreatedAt DATETIME2(0) NOT NULL
            CONSTRAINT DF_tbHeadOfficeMainIndicator_CreatedAt DEFAULT (SYSDATETIME()),
        CreatedBy INT NULL,
        UpdatedAt DATETIME2(0) NULL,
        UpdatedBy INT NULL,
        DeletedAt DATETIME2(0) NULL,
        DeletedBy INT NULL
    );

    CREATE UNIQUE INDEX UX_tbHeadOfficeMainIndicator_ActiveTitle
        ON dbo.tbHeadOfficeMainIndicator(Title)
        WHERE IsDeleted = 0;
END
