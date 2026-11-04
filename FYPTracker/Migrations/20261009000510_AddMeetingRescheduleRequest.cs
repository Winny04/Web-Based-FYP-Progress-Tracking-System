using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingRescheduleRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FYPProjectStatusHistories_ApplicationUser_ChangedById",
                table: "FYPProjectStatusHistories");

            migrationBuilder.AlterColumn<int>(
                name: "ChangedById",
                table: "FYPProjectStatusHistories",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.Sql(@"
                IF OBJECT_ID('MeetingRescheduleRequest', 'U') IS NULL
                BEGIN
                    CREATE TABLE [MeetingRescheduleRequest] (
                        [Id] int NOT NULL IDENTITY,
                        [MeetingBookingId] int NOT NULL,
                        [StudentId] int NOT NULL,
                        [SupervisorId] int NOT NULL,
                        [OriginalDate] date NOT NULL,
                        [OriginalTime] nvarchar(10) NULL,
                        [ProposedDate] date NOT NULL,
                        [ProposedTime] nvarchar(10) NULL,
                        [Reason] nvarchar(1000) NOT NULL,
                        [SupervisorResponse] nvarchar(max) NULL,
                        [Status] nvarchar(50) NOT NULL,
                        [CreatedDate] datetime NOT NULL,
                        [RespondedDate] datetime NULL,
                        CONSTRAINT [PK_MeetingRescheduleRequest] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_MeetingRescheduleRequest_ApplicationUser_StudentId] FOREIGN KEY ([StudentId]) REFERENCES [ApplicationUser] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_MeetingRescheduleRequest_ApplicationUser_SupervisorId] FOREIGN KEY ([SupervisorId]) REFERENCES [ApplicationUser] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_MeetingRescheduleRequest_MeetingBooking_MeetingBookingId] FOREIGN KEY ([MeetingBookingId]) REFERENCES [MeetingBooking] ([Id]) ON DELETE CASCADE
                    );

                    CREATE INDEX [IX_MeetingRescheduleRequest_MeetingBookingId] ON [MeetingRescheduleRequest] ([MeetingBookingId]);
                    CREATE INDEX [IX_MeetingRescheduleRequest_StudentId] ON [MeetingRescheduleRequest] ([StudentId]);
                    CREATE INDEX [IX_MeetingRescheduleRequest_SupervisorId] ON [MeetingRescheduleRequest] ([SupervisorId]);
                END
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_FYPProjectStatusHistories_ApplicationUser_ChangedById",
                table: "FYPProjectStatusHistories",
                column: "ChangedById",
                principalTable: "ApplicationUser",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FYPProjectStatusHistories_ApplicationUser_ChangedById",
                table: "FYPProjectStatusHistories");

            migrationBuilder.DropTable(
                name: "MeetingRescheduleRequest");

            migrationBuilder.AlterColumn<int>(
                name: "ChangedById",
                table: "FYPProjectStatusHistories",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FYPProjectStatusHistories_ApplicationUser_ChangedById",
                table: "FYPProjectStatusHistories",
                column: "ChangedById",
                principalTable: "ApplicationUser",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
