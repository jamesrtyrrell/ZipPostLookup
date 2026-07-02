-- ============================================================================
-- ZipPostLookupWorkDB  –  Full build script
-- Generated: 2026-06-03  (all identifiers PascalCase)
-- Reconciled 2026-06-11 against the live DB (Current_db_created.sql):
--   · data.Reference: added AltNameOf + Flagged columns
--   · codes.Discrepancies / codes.ValidationErrors: Code/Name -> ZpCode/PlaceName
--   · codes.Discrepancies: added Notes column (gold-alias reviewer hints)
--   · indexes + ZipCoverage/PendingDiscrepancies views: postal Code/Name -> ZpCode/PlaceName
--   · PlaceName standardised on NVARCHAR(200) across all tables
--   · removed dead objects: data.GetReferenceAdminsJson, data.GetReferenceByCodeWithAdmins,
--     data.GetReferenceWithAdmins procs + pipeline.DiscrepancyFieldSummary view
--   · added Flagged / AltNameOf / Notes migration blocks for existing installs
-- ============================================================================

USE [ZipPostLookupWorkDB]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


-- ============================================================================
-- SECTION 1: SCHEMAS
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'codes')
    EXEC('CREATE SCHEMA [codes]');
GO
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'data')
    EXEC('CREATE SCHEMA [data]');
GO
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'pipeline')
    EXEC('CREATE SCHEMA [pipeline]');
GO
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'Utilities')
    EXEC('CREATE SCHEMA [Utilities]');
GO


-- ============================================================================
-- SECTION 2: SCALAR FUNCTIONS
-- ============================================================================

CREATE OR ALTER FUNCTION [dbo].[CreateClassName](@Text AS VARCHAR(1000))
RETURNS VARCHAR(1000)
AS
BEGIN
    DECLARE @Reset BIT;
    DECLARE @Ret   VARCHAR(1000);
    DECLARE @i     INT;
    DECLARE @c     CHAR(1);

    IF @Text IS NULL RETURN NULL;

    SELECT @Text  = REPLACE(@Text, '_', ' ');
    SELECT @Text  = REPLACE(@Text, '.', ' ');
    SELECT @Reset = 1, @i = 1, @Ret = '';

    WHILE (@i <= LEN(@Text))
        SELECT
            @c    = SUBSTRING(@Text, @i, 1),
            @Ret  = @Ret + CASE WHEN @Reset = 1 THEN UPPER(@c) ELSE LOWER(@c) END,
            @Reset = CASE WHEN @c LIKE '[a-zA-Z]' THEN 0 ELSE 1 END,
            @i    = @i + 1;

    SELECT @Ret = REPLACE(@Ret, ' ', '');
    RETURN @Ret;
END
GO

CREATE OR ALTER FUNCTION [dbo].[ProperCase](@Text AS VARCHAR(1000))
RETURNS VARCHAR(1000)
AS
BEGIN
    DECLARE @Reset BIT;
    DECLARE @Ret   VARCHAR(1000);
    DECLARE @i     INT;
    DECLARE @c     CHAR(1);

    IF @Text IS NULL RETURN NULL;

    SELECT @Reset = 1, @i = 1, @Ret = '';

    WHILE (@i <= LEN(@Text))
        SELECT
            @c    = SUBSTRING(@Text, @i, 1),
            @Ret  = @Ret + CASE WHEN @Reset = 1 THEN UPPER(@c) ELSE LOWER(@c) END,
            @Reset = CASE WHEN @c LIKE '[a-zA-Z]' THEN 0 ELSE 1 END,
            @i    = @i + 1;

    RETURN @Ret;
END
GO


-- ============================================================================
-- SECTION 3: USER-DEFINED TABLE TYPES
-- ============================================================================

-- Used by BulkUpdateCandidateStatus (SqlCandidateRepository).
CREATE TYPE [codes].[StatusUpdateType] AS TABLE (
    [Code]   [nvarchar](20)  NOT NULL,
    [Name]   [nvarchar](100) NOT NULL,
    [Status] [nvarchar](50)  NOT NULL
)
GO

CREATE TYPE [data].[ReferenceAdminType] AS TABLE (
    [ReferenceId]   [bigint]        NULL,
    [AdminLevelId]  [int]           NULL,
    [Value]         [nvarchar](100) NOT NULL,
    [Code]          [nvarchar](50)  NOT NULL
)
GO

-- Legacy — kept for manual DBA use; not used by the C# pipeline.
CREATE TYPE [data].[ReferenceRowType] AS TABLE (
    [Code]          [nvarchar](20)   NOT NULL,
    [Name]          [nvarchar](100)  NOT NULL,
    [Timezone]      [nvarchar](100)  NULL,
    [IsDefault]     [bit]            NULL,
    [Lat]           [nvarchar](20)   NULL,
    [Lng]           [nvarchar](20)   NULL,
    [AdminLevels]   [nvarchar](max)  NULL
)
GO

-- Used by DataTools.ConvertArrayToCodeTableParameter.
CREATE TYPE [Utilities].[Code] AS TABLE (
    [Code] [nvarchar](20) NOT NULL
)
GO


-- ============================================================================
-- SECTION 4: BASE TABLES  (no FK dependencies)
-- ============================================================================

-- CurationStatus values mirror CurationStatus enum: NoData | UnderReview | Reviewed | Curated
CREATE TABLE [data].[CountryInfo] (
    [CountryId]         [nvarchar](2)    NOT NULL,
    [CountryName]       [nvarchar](100)  NOT NULL,
    [Enabled]           [bit]            NOT NULL  CONSTRAINT [DF_CountryInfo_Enabled]         DEFAULT (0),
    [HasPostalCodes]    [bit]            NOT NULL  CONSTRAINT [DF_CountryInfo_HasPostalCodes]  DEFAULT (1),
    [CodeRegex]         [nvarchar](255)  NULL,
    [ConstrainedRegex]  [nvarchar](255)  NULL,
    [ConstraintNotes]   [nvarchar](500)  NULL,
    [CodeCount]         [int]            NOT NULL  CONSTRAINT [DF_CountryInfo_CodeCount]       DEFAULT (0),
    [DataCurated]       [bit]            NOT NULL  CONSTRAINT [DF_CountryInfo_DataCurated]     DEFAULT (0),
    [CurationStatus]    [nvarchar](50)   NOT NULL  CONSTRAINT [DF_CountryInfo_CurationStatus]  DEFAULT ('NoData'),
    [Notes]             [nvarchar](max)  NULL,
    [CreatedAt]         [datetimeoffset](7) NOT NULL CONSTRAINT [DF_CountryInfo_CreatedAt]     DEFAULT (SYSUTCDATETIME()),
    [UpdatedAt]         [datetimeoffset](7) NOT NULL CONSTRAINT [DF_CountryInfo_UpdatedAt]     DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_CountryInfo] PRIMARY KEY CLUSTERED ([CountryId] ASC),
    CONSTRAINT [CK_CountryInfo_CurationStatus] CHECK ([CurationStatus] IN (
        'NoData', 'UnderReview', 'Reviewed', 'Curated'
    ))
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- Tracks API call counts per engine to enforce daily/monthly limits across sessions.
-- ResetTime: next midnight UTC for daily APIs; first of next month UTC for monthly APIs.
CREATE TABLE [data].[ApiUsage] (
    [ApiName]      [nvarchar](100)     NOT NULL,
    [UsageDate]    [date]              NOT NULL,
    [CallCount]    [int]               NOT NULL CONSTRAINT [DF_ApiUsage_CallCount]  DEFAULT (0),
    [DailyLimit]   [int]               NULL,
    [MonthlyLimit] [int]               NULL,
    [ResetTime]    [datetimeoffset](7) NULL,
    [UpdatedAt]    [datetimeoffset](7) NOT NULL CONSTRAINT [DF_ApiUsage_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_ApiUsage] PRIMARY KEY CLUSTERED ([ApiName] ASC, [UsageDate] ASC)
) ON [PRIMARY]
GO

-- Status values mirror RunStatus enum: InProgress | Complete | Failed
CREATE TABLE [pipeline].[Runs] (
    [RunId]             [nvarchar](50)      NOT NULL,
    [CountryId]         [nvarchar](2)       NOT NULL,
    [SourceFilename]    [nvarchar](255)     NOT NULL,
    [Status]            [nvarchar](50)      NOT NULL  CONSTRAINT [DF_Runs_Status]    DEFAULT ('InProgress'),
    [StartedAt]         [datetimeoffset](7) NOT NULL  CONSTRAINT [DF_Runs_StartedAt] DEFAULT (SYSUTCDATETIME()),
    [CompletedAt]       [datetimeoffset](7) NULL,
    [Notes]             [nvarchar](max)     NULL,
    CONSTRAINT [PK_Runs] PRIMARY KEY CLUSTERED ([RunId] ASC),
    CONSTRAINT [CK_Runs_Status] CHECK ([Status] IN ('InProgress', 'Complete', 'Failed')),
    CONSTRAINT [FK_Runs_Country] FOREIGN KEY ([CountryId]) REFERENCES [data].[CountryInfo] ([CountryId])
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO


-- ============================================================================
-- SECTION 5: DEPENDENT TABLES
-- ============================================================================

CREATE TABLE [data].[AdminLevels] (
    [AdminLevelId]  [int]            IDENTITY(1,1) NOT NULL,
    [CountryId]     [nvarchar](2)    NOT NULL,
    [LevelNumber]   [int]            NOT NULL,
    [LevelName]     [nvarchar](100)  NOT NULL,
    [CodeType]      [nvarchar](50)   NULL,
    [Aliases]       [nvarchar](max)  NULL,
    [CreatedAt]     [datetimeoffset](7) NOT NULL CONSTRAINT [DF_AdminLevels_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_AdminLevels] PRIMARY KEY CLUSTERED ([AdminLevelId] ASC),
    CONSTRAINT [UQ_AdminLevels_CountryLevel] UNIQUE NONCLUSTERED ([CountryId] ASC, [LevelNumber] ASC),
    CONSTRAINT [FK_AdminLevels_Country] FOREIGN KEY ([CountryId]) REFERENCES [data].[CountryInfo] ([CountryId])
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- codes.AdminLevels mirrors data.AdminLevels so that SqlCandidateRepository
-- (queries codes.AdminLevels) and SqlReferenceRepository (queries data.AdminLevels)
-- resolve the same AdminLevelIds.
CREATE TABLE [codes].[AdminLevels] (
    [AdminLevelId]  [int]            IDENTITY(1,1) NOT NULL,
    [CountryId]     [nvarchar](2)    NOT NULL,
    [LevelNumber]   [int]            NOT NULL,
    [LevelName]     [nvarchar](100)  NOT NULL,
    [CodeType]      [nvarchar](50)   NULL,
    [Aliases]       [nvarchar](max)  NULL,
    [CreatedAt]     [datetimeoffset](7) NOT NULL CONSTRAINT [DF_CodesAdminLevels_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_CodesAdminLevels] PRIMARY KEY CLUSTERED ([AdminLevelId] ASC),
    CONSTRAINT [UQ_CodesAdminLevels_CountryLevel] UNIQUE NONCLUSTERED ([CountryId] ASC, [LevelNumber] ASC),
    CONSTRAINT [FK_CodesAdminLevels_Country] FOREIGN KEY ([CountryId]) REFERENCES [data].[CountryInfo] ([CountryId])
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

CREATE TABLE [data].[Reference] (
    [ReferenceId]       [bigint]            IDENTITY(1,1) NOT NULL,
    [CountryId]         [nvarchar](2)       NOT NULL,
    [ZpCode]            [nvarchar](20)      NOT NULL,
    [PlaceName]         [nvarchar](200)     NOT NULL,
    [Timezone]          [nvarchar](100)     NULL,
    [IsDefault]         [bit]               NOT NULL  CONSTRAINT [DF_Reference_IsDefault]        DEFAULT (1),
    [Lat]               [nvarchar](20)      NULL      CONSTRAINT [DF_Reference_Lat]              DEFAULT ('---'),
    [Lng]               [nvarchar](20)      NULL      CONSTRAINT [DF_Reference_Lng]              DEFAULT ('---'),
    [TimezoneChecked]   [bit]               NOT NULL  CONSTRAINT [DF_Reference_TimezoneChecked]  DEFAULT (0),
    [NameChecked]       [bit]               NOT NULL  CONSTRAINT [DF_Reference_NameChecked]      DEFAULT (0),
    [CreatedAt]         [datetimeoffset](7) NOT NULL  CONSTRAINT [DF_Reference_CreatedAt]        DEFAULT (SYSUTCDATETIME()),
    [UpdatedAt]         [datetimeoffset](7) NOT NULL  CONSTRAINT [DF_Reference_UpdatedAt]        DEFAULT (SYSUTCDATETIME()),
    [Curated] AS (CONVERT([bit], CASE
                    WHEN [TimezoneChecked] = (1) AND [NameChecked] = (1) THEN (1)
                    ELSE (0)
                  END)) PERSISTED,
    [AltNameOf]         [nvarchar](100)     NULL,
    -- Flagged is a reason code, not just a bool: 0=Valid, 1=Flagged (generic), 2=CommonFake,
    -- 3=Obsolete. Dapper maps 0/1 to the bool model property automatically; an enum is layered
    -- on later. Any non-zero value excludes the code from every export query.
    [Flagged]           [int]               NOT NULL  CONSTRAINT [DF_Reference_Flagged]          DEFAULT (0),
    CONSTRAINT [PK_Reference] PRIMARY KEY CLUSTERED ([ReferenceId] ASC),
    CONSTRAINT [FK_Reference_Country] FOREIGN KEY ([CountryId]) REFERENCES [data].[CountryInfo] ([CountryId]),
    -- Coordinates must be stored as a complete pair: either both Lat and Lng are present,
    -- or both are absent (NULL / '' / '---'). Blocks a stranded lone coordinate from ANY
    -- write path (import guardrail, ZpCode editor, ad-hoc SQL). See DataReference.NormalizeCoordinatePair().
    CONSTRAINT [CK_Reference_CoordPair] CHECK (
        (CASE WHEN [Lat] IS NULL OR [Lat] IN ('', '---') THEN 0 ELSE 1 END)
      = (CASE WHEN [Lng] IS NULL OR [Lng] IN ('', '---') THEN 0 ELSE 1 END)
    )
) ON [PRIMARY]
GO

CREATE TABLE [data].[ReferenceAdmins] (
    [ReferenceAdminId]  [bigint]            IDENTITY(1,1) NOT NULL,
    [ReferenceId]       [bigint]            NOT NULL,
    [AdminLevelId]      [int]               NOT NULL,
    [Value]             [nvarchar](100)     NOT NULL,
    [Code]              [nvarchar](50)      NOT NULL,
    [CreatedAt]         [datetimeoffset](7) NOT NULL CONSTRAINT [DF_ReferenceAdmins_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_ReferenceAdmins] PRIMARY KEY CLUSTERED ([ReferenceAdminId] ASC),
    CONSTRAINT [UQ_ReferenceAdmins_ReferenceLevel] UNIQUE NONCLUSTERED ([ReferenceId] ASC, [AdminLevelId] ASC),
    CONSTRAINT [FK_ReferenceAdmins_Reference]  FOREIGN KEY ([ReferenceId])  REFERENCES [data].[Reference]    ([ReferenceId]),
    CONSTRAINT [FK_ReferenceAdmins_AdminLevel] FOREIGN KEY ([AdminLevelId]) REFERENCES [data].[AdminLevels]  ([AdminLevelId])
) ON [PRIMARY]
GO

-- Gold-certified codes: curated + admin1 + timezone + lat/lng all present.
-- ZpCodes certified as ground-truth source of truth. PK (CountryId, ZpCode) — fast EXISTS check.
-- ChecksVersion tracks which integrity check set signed off (invalidate on new checks if needed).
CREATE TABLE [data].[GoldCode] (
    [CountryId]     [nvarchar](2)       NOT NULL,
    [ZpCode]        [nvarchar](20)      NOT NULL,
    [GoldAt]        [datetimeoffset](7) NOT NULL CONSTRAINT [DF_GoldCode_GoldAt]        DEFAULT (SYSUTCDATETIME()),
    [ChecksVersion] [int]               NOT NULL CONSTRAINT [DF_GoldCode_ChecksVersion] DEFAULT (1),
    CONSTRAINT [PK_GoldCode] PRIMARY KEY CLUSTERED ([CountryId] ASC, [ZpCode] ASC),
    CONSTRAINT [FK_GoldCode_Country] FOREIGN KEY ([CountryId]) REFERENCES [data].[CountryInfo] ([CountryId])
) ON [PRIMARY]
GO

-- Status values mirror CandidateStatus enum: Pending | Discrepancy | Clean | Rejected | Unfound
CREATE TABLE [codes].[Candidate] (
    [CandidateId]       [bigint]            IDENTITY(1,1) NOT NULL,
    [CountryId]         [nvarchar](2)       NOT NULL,
    [RunId]             [nvarchar](50)      NOT NULL,
    [RecordNumber]      [int]               NULL,
    [ZpCode]            [nvarchar](20)      NOT NULL,
    [PlaceName]         [nvarchar](200)     NOT NULL,
    [Timezone]          [nvarchar](100)     NULL,
    [IsDefault]         [bit]               NOT NULL  CONSTRAINT [DF_Candidate_IsDefault]  DEFAULT (0),
    [Lat]               [nvarchar](20)      NULL      CONSTRAINT [DF_Candidate_Lat]        DEFAULT ('---'),
    [Lng]               [nvarchar](20)      NULL      CONSTRAINT [DF_Candidate_Lng]        DEFAULT ('---'),
    [Status]            [nvarchar](50)      NOT NULL  CONSTRAINT [DF_Candidate_Status]     DEFAULT ('Pending'),
    [CreatedAt]         [datetimeoffset](7) NOT NULL  CONSTRAINT [DF_Candidate_CreatedAt]  DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_Candidate] PRIMARY KEY CLUSTERED ([CandidateId] ASC),
    CONSTRAINT [CK_Candidate_Status] CHECK ([Status] IN (
        'Pending', 'Discrepancy', 'Clean', 'Rejected', 'Unfound'
    )),
    CONSTRAINT [FK_Candidate_Country] FOREIGN KEY ([CountryId]) REFERENCES [data].[CountryInfo] ([CountryId]),
    CONSTRAINT [FK_Candidate_Run]     FOREIGN KEY ([RunId])     REFERENCES [pipeline].[Runs]    ([RunId])
) ON [PRIMARY]
GO

CREATE TABLE [codes].[CandidateAdmins] (
    [CandidateAdminId]  [bigint]            IDENTITY(1,1) NOT NULL,
    [CandidateId]       [bigint]            NOT NULL,
    [AdminLevelId]      [int]               NOT NULL,
    [Value]             [nvarchar](100)     NOT NULL,
    [Code]              [nvarchar](50)      NOT NULL,
    [CreatedAt]         [datetimeoffset](7) NOT NULL CONSTRAINT [DF_CandidateAdmins_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_CandidateAdmins] PRIMARY KEY CLUSTERED ([CandidateAdminId] ASC),
    CONSTRAINT [FK_CandidateAdmins_Candidate]  FOREIGN KEY ([CandidateId])  REFERENCES [codes].[Candidate]   ([CandidateId]),
    CONSTRAINT [FK_CandidateAdmins_AdminLevel] FOREIGN KEY ([AdminLevelId]) REFERENCES [data].[AdminLevels]  ([AdminLevelId])
) ON [PRIMARY]
GO

CREATE TABLE [codes].[Discrepancies] (
    [DiscrepancyId]     [bigint]            IDENTITY(1,1) NOT NULL,
    [CountryId]         [nvarchar](2)       NOT NULL,
    [RunId]             [nvarchar](50)      NOT NULL,
    [ZpCode]            [nvarchar](20)      NOT NULL,
    [PlaceName]         [nvarchar](100)     NOT NULL,
    [AdminLevelId]      [int]               NULL,
    [FieldName]         [nvarchar](100)     NOT NULL,
    [RefValue]          [nvarchar](max)     NULL,
    [InValue]           [nvarchar](max)     NULL,
    [OverrideValue]     [nvarchar](max)     NULL,
    [AcceptIncoming]    [bit]               NULL,
    [Process]           [bit]               NOT NULL  CONSTRAINT [DF_Discrepancies_Process]   DEFAULT (0),
    [CreatedAt]         [datetimeoffset](7) NOT NULL  CONSTRAINT [DF_Discrepancies_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [ResolvedAt]        [datetimeoffset](7) NULL,
    [Notes]             [nvarchar](500)     NULL,
    CONSTRAINT [PK_Discrepancies] PRIMARY KEY CLUSTERED ([DiscrepancyId] ASC),
    CONSTRAINT [UQ_Discrepancies_Field] UNIQUE NONCLUSTERED
        ([CountryId] ASC, [RunId] ASC, [ZpCode] ASC, [PlaceName] ASC, [FieldName] ASC),
    CONSTRAINT [FK_Discrepancies_Country]    FOREIGN KEY ([CountryId])    REFERENCES [data].[CountryInfo] ([CountryId]),
    CONSTRAINT [FK_Discrepancies_Run]        FOREIGN KEY ([RunId])        REFERENCES [pipeline].[Runs]    ([RunId]),
    CONSTRAINT [FK_Discrepancies_AdminLevel] FOREIGN KEY ([AdminLevelId]) REFERENCES [data].[AdminLevels] ([AdminLevelId])
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

CREATE TABLE [codes].[ValidationErrors] (
    [ValidationErrorId] [bigint]            IDENTITY(1,1) NOT NULL,
    [CountryId]         [nvarchar](2)       NOT NULL,
    [RunId]             [nvarchar](50)      NOT NULL,
    [RecordNumber]      [int]               NULL,
    [ZpCode]            [nvarchar](20)      NULL,
    [PlaceName]         [nvarchar](100)     NULL,
    [ErrorType]         [nvarchar](100)     NOT NULL,
    [ErrorMessage]      [nvarchar](max)     NOT NULL,
    [Severity]          [nvarchar](50)      NOT NULL  CONSTRAINT [DF_ValidationErrors_Severity]  DEFAULT ('Error'),
    [CreatedAt]         [datetimeoffset](7) NOT NULL  CONSTRAINT [DF_ValidationErrors_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_ValidationErrors] PRIMARY KEY CLUSTERED ([ValidationErrorId] ASC),
    CONSTRAINT [FK_ValidationErrors_Country] FOREIGN KEY ([CountryId]) REFERENCES [data].[CountryInfo] ([CountryId]),
    CONSTRAINT [FK_ValidationErrors_Run]     FOREIGN KEY ([RunId])     REFERENCES [pipeline].[Runs]    ([RunId])
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

CREATE TABLE [pipeline].[Decisions] (
    [DecisionId]        [bigint]            IDENTITY(1,1) NOT NULL,
    [CountryId]         [nvarchar](2)       NOT NULL,
    [RunId]             [nvarchar](50)      NOT NULL,
    [ZpCode]            [nvarchar](20)      NOT NULL,
    [PlaceName]         [nvarchar](200)     NOT NULL,
    [AcceptIncoming]    [bit]               NOT NULL,
    [DecidedBy]         [nvarchar](100)     NULL,
    [Notes]             [nvarchar](max)     NULL,
    [CreatedAt]         [datetimeoffset](7) NOT NULL CONSTRAINT [DF_Decisions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_Decisions] PRIMARY KEY CLUSTERED ([DecisionId] ASC),
    CONSTRAINT [FK_Decisions_Country] FOREIGN KEY ([CountryId]) REFERENCES [data].[CountryInfo] ([CountryId]),
    CONSTRAINT [FK_Decisions_Run]     FOREIGN KEY ([RunId])     REFERENCES [pipeline].[Runs]    ([RunId])
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ============================================================================
-- Migration: add MonthlyLimit + ResetTime to data.ApiUsage (existing installs).
-- Run these on any DB created before 2026-06-06. Safe to re-run.
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE  object_id = OBJECT_ID(N'data.ApiUsage') AND name = N'MonthlyLimit')
BEGIN
    ALTER TABLE [data].[ApiUsage] ADD [MonthlyLimit] [int] NULL;
    PRINT 'data.ApiUsage: MonthlyLimit column added.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE  object_id = OBJECT_ID(N'data.ApiUsage') AND name = N'ResetTime')
BEGIN
    ALTER TABLE [data].[ApiUsage] ADD [ResetTime] [datetimeoffset](7) NULL;
    PRINT 'data.ApiUsage: ResetTime column added.';
END
GO

-- ============================================================================
-- Migration: add AltNameOf + Flagged to data.Reference (existing installs).
-- Run on any DB created before 2026-06-07. Safe to re-run.
-- AltNameOf links an alternate place name to its canonical row; Flagged excludes
-- a code from every export query.
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE  object_id = OBJECT_ID(N'data.Reference') AND name = N'AltNameOf')
BEGIN
    ALTER TABLE [data].[Reference] ADD [AltNameOf] [nvarchar](100) NULL;
    PRINT 'data.Reference: AltNameOf column added.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE  object_id = OBJECT_ID(N'data.Reference') AND name = N'Flagged')
BEGIN
    ALTER TABLE [data].[Reference] ADD [Flagged] [int] NOT NULL
        CONSTRAINT [DF_Reference_Flagged] DEFAULT (0);
    PRINT 'data.Reference: Flagged column added.';
END
GO

-- ============================================================================
-- Migration: widen data.Reference.Flagged from bit to int (existing installs).
-- Run on any DB created before 2026-06-14. Safe to re-run. Lets Flagged carry a
-- reason code (0=Valid, 1=Flagged, 2=CommonFake, 3=Obsolete) rather than a plain
-- bool. Existing 0/1 values are preserved; Dapper still maps them to the bool model
-- property. The default constraint is dropped/re-added because ALTER COLUMN cannot
-- run while it is attached.
-- ============================================================================
IF EXISTS (SELECT 1 FROM sys.columns c
           JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID(N'data.Reference')
             AND c.name = N'Flagged' AND t.name = N'bit')
BEGIN
    DECLARE @dfFlagged sysname = (
        SELECT dc.name FROM sys.default_constraints dc
        JOIN sys.columns c ON c.object_id = dc.parent_object_id
                          AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'data.Reference') AND c.name = N'Flagged');

    IF @dfFlagged IS NOT NULL
        EXEC('ALTER TABLE [data].[Reference] DROP CONSTRAINT [' + @dfFlagged + ']');

    ALTER TABLE [data].[Reference] ALTER COLUMN [Flagged] [int] NOT NULL;
    ALTER TABLE [data].[Reference] ADD CONSTRAINT [DF_Reference_Flagged] DEFAULT (0) FOR [Flagged];
    PRINT 'data.Reference: Flagged widened from bit to int.';
END
GO

-- ============================================================================
-- Migration: add CK_Reference_CoordPair to data.Reference (existing installs).
-- Run on any DB created before 2026-06-14. Safe to re-run.
-- Enforces the lat/lng pairing rule at the data layer so no write path can strand a
-- lone coordinate. PRECONDITION: no existing row may violate it — blank any half-pairs
-- first (UPDATE data.Reference SET Lat='---', Lng='---' WHERE one is set and the other blank).
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE  name = N'CK_Reference_CoordPair'
                 AND  parent_object_id = OBJECT_ID(N'data.Reference'))
BEGIN
    ALTER TABLE [data].[Reference] WITH CHECK ADD CONSTRAINT [CK_Reference_CoordPair] CHECK (
        (CASE WHEN [Lat] IS NULL OR [Lat] IN ('', '---') THEN 0 ELSE 1 END)
      = (CASE WHEN [Lng] IS NULL OR [Lng] IN ('', '---') THEN 0 ELSE 1 END)
    );
    PRINT 'data.Reference: CK_Reference_CoordPair constraint added.';
END
GO

-- ============================================================================
-- Migration: add Notes to codes.Discrepancies (existing installs).
-- Run on any DB created before 2026-06-10. Safe to re-run.
-- Notes carries reviewer hints set at import time (e.g. gold-alias warnings).
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE  object_id = OBJECT_ID(N'codes.Discrepancies') AND name = N'Notes')
BEGIN
    ALTER TABLE [codes].[Discrepancies] ADD [Notes] [nvarchar](500) NULL;
    PRINT 'codes.Discrepancies: Notes column added.';
END
GO

-- ============================================================================
-- SECTION 6: INDEXES
-- ============================================================================

CREATE NONCLUSTERED INDEX [IX_Candidate_Country_Status]
    ON [codes].[Candidate] ([CountryId], [Status])
    INCLUDE ([ZpCode], [PlaceName]);
GO

CREATE NONCLUSTERED INDEX [IX_Candidate_Country_Code]
    ON [codes].[Candidate] ([CountryId], [ZpCode]);
GO

CREATE NONCLUSTERED INDEX [IX_CandidateAdmins_Candidate_Level]
    ON [codes].[CandidateAdmins] ([CandidateId], [AdminLevelId])
    INCLUDE ([Value], [Code]);
GO

CREATE NONCLUSTERED INDEX [IX_CandidateAdmins_AdminLevel]
    ON [codes].[CandidateAdmins] ([AdminLevelId])
    WHERE [AdminLevelId] IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX [IX_Discrepancies_Process_Country]
    ON [codes].[Discrepancies] ([Process], [CountryId])
    INCLUDE ([RunId], [ZpCode], [PlaceName], [FieldName], [AcceptIncoming]);
GO

CREATE NONCLUSTERED INDEX [IX_Discrepancies_Country_Run_Field]
    ON [codes].[Discrepancies] ([CountryId], [RunId], [FieldName])
    INCLUDE ([Process], [AcceptIncoming]);
GO

CREATE NONCLUSTERED INDEX [IX_Discrepancies_AdminLevel]
    ON [codes].[Discrepancies] ([AdminLevelId])
    WHERE [AdminLevelId] IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX [IX_Reference_Country_Code]
    ON [data].[Reference] ([CountryId], [ZpCode])
    INCLUDE ([ReferenceId], [PlaceName], [Timezone], [IsDefault], [Lat], [Lng]);
GO

CREATE NONCLUSTERED INDEX [IX_Reference_Curated]
    ON [data].[Reference] ([Curated], [CountryId])
    INCLUDE ([ZpCode], [PlaceName]);
GO

CREATE NONCLUSTERED INDEX [IX_ReferenceAdmins_Ref_Level]
    ON [data].[ReferenceAdmins] ([ReferenceId], [AdminLevelId])
    INCLUDE ([Value], [Code]);
GO

CREATE NONCLUSTERED INDEX [IX_Runs_Country]
    ON [pipeline].[Runs] ([CountryId]);
GO

CREATE NONCLUSTERED INDEX [IX_Runs_Status]
    ON [pipeline].[Runs] ([Status])
    INCLUDE ([CountryId], [StartedAt], [CompletedAt]);
GO

CREATE NONCLUSTERED INDEX [IX_Decisions_Country_Code]
    ON [pipeline].[Decisions] ([CountryId], [ZpCode], [PlaceName]);
GO

CREATE NONCLUSTERED INDEX [IX_ValidationErrors_Country_Run]
    ON [codes].[ValidationErrors] ([CountryId], [RunId])
    INCLUDE ([ZpCode], [PlaceName], [ErrorType], [Severity]);
GO


-- ============================================================================
-- SECTION 7: VIEWS
-- ============================================================================

CREATE VIEW [pipeline].[ZipCoverage] AS
    SELECT
        r.CountryId,
        r.ZpCode,
        COUNT(*)                                            AS NameCount,
        SUM(CASE WHEN r.Lat <> '---' AND r.Lat <> ''
                 THEN 1 ELSE 0 END)                         AS HasCoords,
        SUM(CASE WHEN r.TimezoneChecked = 1
                 THEN 1 ELSE 0 END)                         AS TimezoneVerified
    FROM [data].[Reference] r
    GROUP BY r.CountryId, r.ZpCode;
GO

CREATE VIEW [pipeline].[PendingDiscrepancies] AS
    SELECT
        d.CountryId,
        d.RunId,
        d.ZpCode,
        d.PlaceName,
        d.FieldName,
        d.RefValue,
        d.InValue,
        d.CreatedAt
    FROM [codes].[Discrepancies] d
    WHERE d.Process = 0
      AND d.AcceptIncoming IS NULL;
GO

CREATE VIEW [pipeline].[CountrySummary] AS
    SELECT
        ci.CountryId,
        ci.CountryName,
        ci.CodeCount                                              AS RefCount,
        COUNT(c.CandidateId)                                      AS CandidateCount,
        SUM(CASE WHEN c.Status = 'Clean'       THEN 1 ELSE 0 END) AS CleanCount,
        SUM(CASE WHEN c.Status = 'Discrepancy' THEN 1 ELSE 0 END) AS DiscrepancyCount,
        SUM(CASE WHEN d.Process = 0            THEN 1 ELSE 0 END) AS UnresolvedDiscrepancies,
        ci.DataCurated,
        ci.CurationStatus
    FROM [data].[CountryInfo] ci
    LEFT JOIN [codes].[Candidate]     c ON c.CountryId = ci.CountryId
    LEFT JOIN [codes].[Discrepancies] d ON d.CountryId = ci.CountryId
    GROUP BY ci.CountryId, ci.CountryName, ci.CodeCount, ci.DataCurated, ci.CurationStatus;
GO

-- Deduplicated open Name-discrepancy backlog: one row per distinct (CountryId, ZpCode, InValue)
-- across all RunIds, with classification columns. codes.Discrepancies has a per-RunId unique key,
-- so the same code+name conflict recurs once per import run; this view collapses those to a single
-- working row for review and the cleanup phases (see PROJECTS "Gold Name-Discrepancy Backlog").
--   InValueBlank        — InValue is NULL / '' / '---' (nothing to keep → reject)
--   InValueAlreadyARow  — InValue already exists as a data.Reference PlaceName for the code (moot → close)
-- A non-blank, not-already-a-row InValue is a candidate alias for review/promotion.
CREATE OR ALTER VIEW [pipeline].[OpenNameDiscrepancies] AS
    SELECT
        d.CountryId,
        d.ZpCode,
        d.InValue,
        MIN(d.RefValue)                                          AS RefValue,
        COUNT(*)                                                 AS DuplicateRows,
        MIN(d.CreatedAt)                                         AS FirstSeen,
        MAX(d.CreatedAt)                                         AS LastSeen,
        CAST(CASE WHEN d.InValue IS NULL OR LTRIM(RTRIM(d.InValue)) IN ('', '---')
                  THEN 1 ELSE 0 END AS BIT)                      AS InValueBlank,
        CAST(CASE WHEN EXISTS (
                      SELECT 1 FROM [data].[Reference] r
                      WHERE r.CountryId = d.CountryId
                        AND r.ZpCode    = d.ZpCode
                        AND r.PlaceName = d.InValue)
                  THEN 1 ELSE 0 END AS BIT)                      AS InValueAlreadyARow
    FROM [codes].[Discrepancies] d
    WHERE d.FieldName  = 'Name'
      AND d.ResolvedAt IS NULL
    GROUP BY d.CountryId, d.ZpCode, d.InValue;
GO


-- ============================================================================
-- SECTION 8: STORED PROCEDURES
-- ============================================================================

-- Deletes all pipeline and reference data for a country in FK-safe order.
CREATE OR ALTER PROCEDURE [pipeline].[ResetCountry]
    @CountryId NVARCHAR(2)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DELETE ca
        FROM [codes].[CandidateAdmins] ca
        INNER JOIN [codes].[Candidate] c ON ca.CandidateId = c.CandidateId
        WHERE c.CountryId = @CountryId;

        DELETE FROM [codes].[Discrepancies]    WHERE CountryId = @CountryId;
        DELETE FROM [codes].[ValidationErrors] WHERE CountryId = @CountryId;
        DELETE FROM [codes].[Candidate]        WHERE CountryId = @CountryId;
        DELETE FROM [pipeline].[Decisions]     WHERE CountryId = @CountryId;

        DELETE ra
        FROM [data].[ReferenceAdmins] ra
        INNER JOIN [data].[Reference] r ON ra.ReferenceId = r.ReferenceId
        WHERE r.CountryId = @CountryId;

        DELETE FROM [data].[Reference]    WHERE CountryId = @CountryId;
        DELETE FROM [data].[AdminLevels]  WHERE CountryId = @CountryId;

        UPDATE [data].[CountryInfo]
        SET    CodeCount = 0,
               UpdatedAt = SYSUTCDATETIME()
        WHERE  CountryId = @CountryId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

-- Utility: generates a Dapper POCO class from any table in the database.
CREATE OR ALTER PROCEDURE [dbo].[GenerateDapperClass]
    @SchemaName  NVARCHAR(128),
    @TableName   NVARCHAR(128),
    @Namespace   NVARCHAR(256) = 'YourApp.Database.Models'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FullTableName NVARCHAR(256)  = @SchemaName + '.' + @TableName;
    DECLARE @ClassName     NVARCHAR(1000) = dbo.CreateClassName(@FullTableName);
    DECLARE @Result        NVARCHAR(MAX)  = '';

    SET @Result =
        'using Dapper.Contrib.Extensions;'                              + CHAR(13)+CHAR(10) +
        ''                                                              + CHAR(13)+CHAR(10) +
        'namespace ' + @Namespace + ';'                                 + CHAR(13)+CHAR(10) +
        ''                                                              + CHAR(13)+CHAR(10) +
        '[Table("' + @FullTableName + '")]'                             + CHAR(13)+CHAR(10) +
        'public class ' + @ClassName                                    + CHAR(13)+CHAR(10) +
        '{'                                                             + CHAR(13)+CHAR(10);

    SELECT @Result = @Result +
        CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN '    [Key]' + CHAR(13)+CHAR(10) ELSE '' END +
        CASE WHEN cc.definition  IS NOT NULL THEN '    [Write(false)]' + CHAR(13)+CHAR(10) ELSE '' END +
        '    public ' +
        CASE WHEN c.IS_NULLABLE = 'YES' AND ct.cs_type NOT IN ('string','byte[]')
             THEN ct.cs_type + '?' ELSE ct.cs_type END +
        ' ' + c.COLUMN_NAME +
        CASE
            WHEN c.COLUMN_NAME LIKE '%At' AND ct.cs_type = 'DateTimeOffset'
                THEN ' { get; set; } = DateTimeOffset.UtcNow;'
            WHEN ct.cs_type = 'bool' AND c.COLUMN_DEFAULT = '((1))'
                THEN ' { get; set; } = true;'
            WHEN ct.cs_type = 'string' AND c.IS_NULLABLE = 'NO'
                THEN ' { get; set; } = string.Empty;'
            ELSE ' { get; set; }'
        END + CHAR(13)+CHAR(10)
    FROM INFORMATION_SCHEMA.COLUMNS c
    INNER JOIN (VALUES
        ('bigint','long'),('int','int'),('smallint','short'),('tinyint','byte'),
        ('bit','bool'),('decimal','decimal'),('numeric','decimal'),('money','decimal'),
        ('smallmoney','decimal'),('float','double'),('real','float'),
        ('datetime','DateTime'),('datetime2','DateTime'),('datetimeoffset','DateTimeOffset'),
        ('date','DateOnly'),('time','TimeOnly'),('smalldatetime','DateTime'),
        ('char','string'),('nchar','string'),('varchar','string'),('nvarchar','string'),
        ('text','string'),('ntext','string'),('uniqueidentifier','Guid'),
        ('binary','byte[]'),('varbinary','byte[]'),('image','byte[]'),
        ('xml','string'),('sql_variant','object')
    ) AS ct(sql_type, cs_type) ON ct.sql_type = c.DATA_TYPE
    LEFT JOIN sys.computed_columns cc
        ON cc.object_id = OBJECT_ID(@FullTableName) AND cc.name = c.COLUMN_NAME
    LEFT JOIN (
        SELECT dbo.CreateClassName(ku.COLUMN_NAME) AS COLUMN_NAME
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
            ON ku.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
            AND ku.TABLE_SCHEMA   = tc.TABLE_SCHEMA
            AND ku.TABLE_NAME     = tc.TABLE_NAME
        WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
          AND tc.TABLE_SCHEMA    = @SchemaName
          AND tc.TABLE_NAME      = @TableName
        GROUP BY ku.COLUMN_NAME HAVING COUNT(*) = 1
    ) pk ON pk.COLUMN_NAME = c.COLUMN_NAME
    WHERE c.TABLE_SCHEMA = @SchemaName AND c.TABLE_NAME = @TableName
    ORDER BY c.ORDINAL_POSITION;

    SET @Result = @Result + '}' + CHAR(13)+CHAR(10);
    PRINT @Result;
    SELECT @Result AS GeneratedClass;
END;
GO


-- ============================================================================
-- SECTION 9: SEED DATA
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM [data].[CountryInfo] WHERE CountryId = 'US')
    INSERT INTO [data].[CountryInfo] (CountryId, CountryName) VALUES ('US', 'United States');

IF NOT EXISTS (SELECT 1 FROM [data].[CountryInfo] WHERE CountryId = 'CA')
    INSERT INTO [data].[CountryInfo] (CountryId, CountryName) VALUES ('CA', 'Canada');

IF NOT EXISTS (SELECT 1 FROM [data].[CountryInfo] WHERE CountryId = 'MX')
    INSERT INTO [data].[CountryInfo] (CountryId, CountryName) VALUES ('MX', 'Mexico');
GO

IF NOT EXISTS (SELECT 1 FROM [data].[AdminLevels] WHERE CountryId = 'US' AND LevelNumber = 1)
    INSERT INTO [data].[AdminLevels] (CountryId, LevelNumber, LevelName, CodeType, Aliases)
    VALUES ('US', 1, 'State', 'usps', N'["Territory", "District"]');

IF NOT EXISTS (SELECT 1 FROM [data].[AdminLevels] WHERE CountryId = 'US' AND LevelNumber = 2)
    INSERT INTO [data].[AdminLevels] (CountryId, LevelNumber, LevelName, CodeType, Aliases)
    VALUES ('US', 2, 'County', 'fips', N'["Parish", "Borough", "Census Area"]');

IF NOT EXISTS (SELECT 1 FROM [data].[AdminLevels] WHERE CountryId = 'CA' AND LevelNumber = 1)
    INSERT INTO [data].[AdminLevels] (CountryId, LevelNumber, LevelName, CodeType, Aliases)
    VALUES ('CA', 1, 'Province', NULL, N'["Territory"]');

IF NOT EXISTS (SELECT 1 FROM [data].[AdminLevels] WHERE CountryId = 'MX' AND LevelNumber = 1)
    INSERT INTO [data].[AdminLevels] (CountryId, LevelNumber, LevelName, CodeType, Aliases)
    VALUES ('MX', 1, 'Estado', NULL, N'[]');

IF NOT EXISTS (SELECT 1 FROM [codes].[AdminLevels] WHERE CountryId = 'US' AND LevelNumber = 1)
    INSERT INTO [codes].[AdminLevels] (CountryId, LevelNumber, LevelName, CodeType, Aliases)
    VALUES ('US', 1, 'State', 'usps', N'["Territory", "District"]');

IF NOT EXISTS (SELECT 1 FROM [codes].[AdminLevels] WHERE CountryId = 'US' AND LevelNumber = 2)
    INSERT INTO [codes].[AdminLevels] (CountryId, LevelNumber, LevelName, CodeType, Aliases)
    VALUES ('US', 2, 'County', 'fips', N'["Parish", "Borough", "Census Area"]');

IF NOT EXISTS (SELECT 1 FROM [codes].[AdminLevels] WHERE CountryId = 'CA' AND LevelNumber = 1)
    INSERT INTO [codes].[AdminLevels] (CountryId, LevelNumber, LevelName, CodeType, Aliases)
    VALUES ('CA', 1, 'Province', NULL, N'["Territory"]');

IF NOT EXISTS (SELECT 1 FROM [codes].[AdminLevels] WHERE CountryId = 'MX' AND LevelNumber = 1)
    INSERT INTO [codes].[AdminLevels] (CountryId, LevelNumber, LevelName, CodeType, Aliases)
    VALUES ('MX', 1, 'Estado', NULL, N'[]');
GO

-- ============================================================================
-- Migration: add data.GoldCode table (existing installs before 2026-06-09).
-- Run on any DB created before this date. Safe to re-run.
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables
               WHERE  schema_id = SCHEMA_ID('data') AND name = 'GoldCode')
BEGIN
    CREATE TABLE [data].[GoldCode] (
        [CountryId]     [nvarchar](2)       NOT NULL,
        [ZpCode]        [nvarchar](20)      NOT NULL,
        [GoldAt]        [datetimeoffset](7) NOT NULL CONSTRAINT [DF_GoldCode_GoldAt]        DEFAULT (SYSUTCDATETIME()),
        [ChecksVersion] [int]               NOT NULL CONSTRAINT [DF_GoldCode_ChecksVersion] DEFAULT (1),
        CONSTRAINT [PK_GoldCode] PRIMARY KEY CLUSTERED ([CountryId] ASC, [ZpCode] ASC),
        CONSTRAINT [FK_GoldCode_Country] FOREIGN KEY ([CountryId]) REFERENCES [data].[CountryInfo] ([CountryId])
    );
    PRINT 'data.GoldCode: table created.';
END
GO
