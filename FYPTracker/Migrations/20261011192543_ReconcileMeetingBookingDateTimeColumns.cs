using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileMeetingBookingDateTimeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[MeetingBooking]', N'U') IS NULL
BEGIN
    THROW 51001, 'Target table [dbo].[MeetingBooking] does not exist.', 1;
END

-- Validate or add StartDateTime
IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns c
    INNER JOIN sys.tables t ON c.object_id = t.object_id
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'dbo' AND t.name = 'MeetingBooking' AND c.name = 'StartDateTime'
)
BEGIN
    ALTER TABLE [dbo].[MeetingBooking] ADD [StartDateTime] datetime2(7) NULL;
END
ELSE
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM sys.columns c
        INNER JOIN sys.tables t ON c.object_id = t.object_id
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        INNER JOIN sys.types typ ON c.user_type_id = typ.user_type_id
        WHERE s.name = 'dbo' 
          AND t.name = 'MeetingBooking' 
          AND c.name = 'StartDateTime'
          AND typ.name = 'datetime2'
          AND c.scale = 7
          AND c.is_nullable = 1
    )
    BEGIN
        THROW 51002, 'Existing column [dbo].[MeetingBooking].[StartDateTime] does not match expected definition datetime2(7) NULL.', 1;
    END
END

-- Validate or add EndDateTime
IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns c
    INNER JOIN sys.tables t ON c.object_id = t.object_id
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'dbo' AND t.name = 'MeetingBooking' AND c.name = 'EndDateTime'
)
BEGIN
    ALTER TABLE [dbo].[MeetingBooking] ADD [EndDateTime] datetime2(7) NULL;
END
ELSE
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM sys.columns c
        INNER JOIN sys.tables t ON c.object_id = t.object_id
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        INNER JOIN sys.types typ ON c.user_type_id = typ.user_type_id
        WHERE s.name = 'dbo' 
          AND t.name = 'MeetingBooking' 
          AND c.name = 'EndDateTime'
          AND typ.name = 'datetime2'
          AND c.scale = 7
          AND c.is_nullable = 1
    )
    BEGIN
        THROW 51003, 'Existing column [dbo].[MeetingBooking].[EndDateTime] does not match expected definition datetime2(7) NULL.', 1;
    END
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CRITICAL NON-DESTRUCTIVE DOWN STRATEGY:
            // MeetingBooking.StartDateTime and MeetingBooking.EndDateTime contain historical and active
            // academic meeting records (datetime2(7) NULL). Dropping these columns would permanently destroy
            // meeting data. Therefore, Down() intentionally leaves both columns and their data intact.
            migrationBuilder.Sql(@"
-- Non-destructive rollback: MeetingBooking.StartDateTime and MeetingBooking.EndDateTime are intentionally preserved
-- to protect existing academic meeting records.
PRINT 'Non-destructive Down(): [dbo].[MeetingBooking] datetime columns preserved.';
");
        }
    }
}
