SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @identityInsertEnabled bit = 0;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock
        @Resource = N'contoso-students-seed',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 30000;
    IF @lockResult < 0
        THROW 51000, 'Could not acquire the Students seed lock.', 1;

    IF OBJECT_ID(N'dbo.Students', N'U') IS NULL
        THROW 51001, 'Apply the Students migration before seeding.', 1;

    IF OBJECT_ID(N'dbo.StudentsSeedHistory', N'U') IS NULL
        CREATE TABLE dbo.StudentsSeedHistory (
            SeedName nvarchar(128) NOT NULL PRIMARY KEY
        );

    IF NOT EXISTS (SELECT 1 FROM dbo.StudentsSeedHistory WHERE SeedName = N'initial-v1')
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.Students WITH (TABLOCKX, HOLDLOCK))
            THROW 51002, 'Students is not empty; refusing to seed existing records.', 1;

        SET IDENTITY_INSERT dbo.Students ON;
        SET @identityInsertEnabled = 1;
        INSERT INTO dbo.Students (Id, FirstName, LastName, EnrollmentDate)
        VALUES
            (1, N'Ana', N'Example', '2020-09-01T00:00:00'),
            (2, N'Ben', N'Sample', '2021-09-01T00:00:00'),
            (3, N'Casey', N'Demo', '2022-09-01T00:00:00');
        SET IDENTITY_INSERT dbo.Students OFF;
        SET @identityInsertEnabled = 0;
        INSERT INTO dbo.StudentsSeedHistory (SeedName) VALUES (N'initial-v1');
    END
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @identityInsertEnabled = 1 SET IDENTITY_INSERT dbo.Students OFF;
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
