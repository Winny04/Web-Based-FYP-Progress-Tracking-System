USE FYPTracker;
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MeetingRescheduleRequest')
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
        [Status] nvarchar(50) NOT NULL DEFAULT N'Pending',
        [CreatedDate] datetime NOT NULL,
        [RespondedDate] datetime NULL,
        CONSTRAINT [PK_MeetingRescheduleRequest] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MeetingRescheduleRequest_MeetingBooking_MeetingBookingId] FOREIGN KEY ([MeetingBookingId]) REFERENCES [MeetingBooking] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MeetingRescheduleRequest_ApplicationUsers_StudentId] FOREIGN KEY ([StudentId]) REFERENCES [ApplicationUser] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MeetingRescheduleRequest_ApplicationUsers_SupervisorId] FOREIGN KEY ([SupervisorId]) REFERENCES [ApplicationUser] ([Id]) ON DELETE NO ACTION
    );

    CREATE INDEX [IX_MeetingRescheduleRequest_MeetingBookingId] ON [MeetingRescheduleRequest] ([MeetingBookingId]);
    CREATE INDEX [IX_MeetingRescheduleRequest_StudentId] ON [MeetingRescheduleRequest] ([StudentId]);
    CREATE INDEX [IX_MeetingRescheduleRequest_SupervisorId] ON [MeetingRescheduleRequest] ([SupervisorId]);
END
GO
