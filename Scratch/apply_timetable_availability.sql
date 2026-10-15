BEGIN TRANSACTION;

-- ===================================================================================
-- 1. SupervisorTimetableEntry (Teaching Timetable) for all 10 Lecturers
-- Session: AcademicSessionId = 1 (Session 2026/2027)
-- Schedule: 3 realistic entries per lecturer (Lecture, Tutorial, Lab)
-- Days: Monday, Tuesday, Friday (zero conflict with Thursday meetings or Wed defenses)
-- ===================================================================================

-- Lecturer 1: Dr. Alice Tan (ID: 9)
INSERT INTO SupervisorTimetableEntry (SupervisorId, AcademicSessionId, DayOfWeek, StartTime, EndTime, Details) VALUES
(9, 1, 'Monday', '09:00:00', '11:00:00', 'CSC3102 Advanced Software Engineering (Lecture)'),
(9, 1, 'Tuesday', '10:00:00', '12:00:00', 'CSC3102 Software Engineering Studio (Lab)'),
(9, 1, 'Friday', '14:00:00', '16:00:00', 'CSC3201 Agile Project Management (Tutorial)');

-- Lecturer 2: Dr. Bernard Lim (ID: 10)
INSERT INTO SupervisorTimetableEntry (SupervisorId, AcademicSessionId, DayOfWeek, StartTime, EndTime, Details) VALUES
(10, 1, 'Monday', '10:00:00', '12:00:00', 'NET2104 Network Architecture & Routing (Lecture)'),
(10, 1, 'Tuesday', '14:00:00', '16:00:00', 'NET2104 Network Packet Simulation (Lab)'),
(10, 1, 'Friday', '09:00:00', '11:00:00', 'SEC3301 Enterprise Network Security (Lecture)');

-- Lecturer 3: Ts. Catherine Wong (ID: 11)
INSERT INTO SupervisorTimetableEntry (SupervisorId, AcademicSessionId, DayOfWeek, StartTime, EndTime, Details) VALUES
(11, 1, 'Monday', '09:00:00', '11:00:00', 'CSC2108 Web Application Frameworks (Lecture)'),
(11, 1, 'Tuesday', '09:00:00', '11:00:00', 'CSC2108 Full-Stack Web Development (Lab)'),
(11, 1, 'Friday', '10:00:00', '12:00:00', 'CSC2205 Mobile Application Development (Lab)');

-- Lecturer 4: Dr. Daniel Raj (ID: 12)
INSERT INTO SupervisorTimetableEntry (SupervisorId, AcademicSessionId, DayOfWeek, StartTime, EndTime, Details) VALUES
(12, 1, 'Monday', '14:00:00', '16:00:00', 'DAT3101 Big Data Analytics & Processing (Lecture)'),
(12, 1, 'Tuesday', '10:00:00', '12:00:00', 'DAT3101 Distributed Systems & Hadoop (Lab)'),
(12, 1, 'Friday', '14:00:00', '16:00:00', 'DAT3202 Data Visualization & Dashboards (Lab)');

-- Lecturer 5: Ir. Evelyn Chong (ID: 13)
INSERT INTO SupervisorTimetableEntry (SupervisorId, AcademicSessionId, DayOfWeek, StartTime, EndTime, Details) VALUES
(13, 1, 'Monday', '10:00:00', '12:00:00', 'ENG2201 Embedded Systems & Microcontrollers (Lecture)'),
(13, 1, 'Tuesday', '14:00:00', '16:00:00', 'ENG2201 Microcontroller Interfacing (Lab)'),
(13, 1, 'Friday', '09:00:00', '11:00:00', 'ENG3302 IoT Sensor Networks & Firmware (Lab)');

-- Lecturer 6: Dr. Farooq Ahmad (ID: 14)
INSERT INTO SupervisorTimetableEntry (SupervisorId, AcademicSessionId, DayOfWeek, StartTime, EndTime, Details) VALUES
(14, 1, 'Monday', '09:00:00', '11:00:00', 'AIE3104 Machine Learning Fundamentals (Lecture)'),
(14, 1, 'Tuesday', '09:00:00', '11:00:00', 'AIE3104 Neural Network Architectures (Lab)'),
(14, 1, 'Friday', '10:00:00', '12:00:00', 'AIE3202 Computer Vision & Pattern Recognition (Lab)');

-- Lecturer 7: Ms. Grace Teo (ID: 15)
INSERT INTO SupervisorTimetableEntry (SupervisorId, AcademicSessionId, DayOfWeek, StartTime, EndTime, Details) VALUES
(15, 1, 'Monday', '14:00:00', '16:00:00', 'CSC1101 Object-Oriented Programming in C# (Lecture)'),
(15, 1, 'Tuesday', '10:00:00', '12:00:00', 'CSC1101 OOP Practical Workshop (Lab)'),
(15, 1, 'Friday', '14:00:00', '16:00:00', 'CSC1203 Data Structures & Algorithms (Tutorial)');

-- Lecturer 8: Dr. Haris bin Omar (ID: 16)
INSERT INTO SupervisorTimetableEntry (SupervisorId, AcademicSessionId, DayOfWeek, StartTime, EndTime, Details) VALUES
(16, 1, 'Monday', '10:00:00', '12:00:00', 'CYB3102 Ethical Hacking & Penetration Testing (Lecture)'),
(16, 1, 'Tuesday', '14:00:00', '16:00:00', 'CYB3102 Exploit Analysis & Forensics (Lab)'),
(16, 1, 'Friday', '09:00:00', '11:00:00', 'CYB3204 Cloud Infrastructure Hardening (Lecture)');

-- Lecturer 9: Ts. Irene Lee (ID: 17)
INSERT INTO SupervisorTimetableEntry (SupervisorId, AcademicSessionId, DayOfWeek, StartTime, EndTime, Details) VALUES
(17, 1, 'Monday', '09:00:00', '11:00:00', 'UXD2105 Human-Computer Interaction & UX (Lecture)'),
(17, 1, 'Tuesday', '09:00:00', '11:00:00', 'UXD2105 User Research & Prototyping (Lab)'),
(17, 1, 'Friday', '10:00:00', '12:00:00', 'UXD2201 Design Systems & Usability Testing (Lab)');

-- Lecturer 10: Dr. Jeremy Ko (ID: 18)
INSERT INTO SupervisorTimetableEntry (SupervisorId, AcademicSessionId, DayOfWeek, StartTime, EndTime, Details) VALUES
(18, 1, 'Monday', '14:00:00', '16:00:00', 'CSC3305 Cloud Native Architecture & Microservices (Lecture)'),
(18, 1, 'Tuesday', '10:00:00', '12:00:00', 'CSC3305 Kubernetes & Container Orchestration (Lab)'),
(18, 1, 'Friday', '14:00:00', '16:00:00', 'CSC3401 DevOps Engineering & CI/CD Pipelines (Lab)');


-- ===================================================================================
-- 2. SupervisorAvailabilities (Weekly Default Consultation Slots & Future Date Overrides)
-- ScheduleType = 'WeeklyDefault': Dedicated recurring student consultation windows
-- ScheduleType = 'DateOverride': Future specific dates (Conference attendance / Leave)
-- ===================================================================================

-- Lecturer 1: Dr. Alice Tan (ID: 9)
INSERT INTO SupervisorAvailabilities (SupervisorId, ScheduleType, DayOfWeek, SpecificDate, StartTime, EndTime, IsAvailable) VALUES
(9, 'WeeklyDefault', 'Tuesday', NULL, '14:00:00', '16:00:00', 1),
(9, 'WeeklyDefault', 'Friday', NULL, '10:00:00', '12:00:00', 1),
(9, 'DateOverride', NULL, '2026-11-20', '08:00:00', '17:00:00', 0);

-- Lecturer 2: Dr. Bernard Lim (ID: 10)
INSERT INTO SupervisorAvailabilities (SupervisorId, ScheduleType, DayOfWeek, SpecificDate, StartTime, EndTime, IsAvailable) VALUES
(10, 'WeeklyDefault', 'Tuesday', NULL, '09:00:00', '11:00:00', 1),
(10, 'WeeklyDefault', 'Friday', NULL, '14:00:00', '16:00:00', 1),
(10, 'DateOverride', NULL, '2026-11-20', '08:00:00', '17:00:00', 0);

-- Lecturer 3: Ts. Catherine Wong (ID: 11)
INSERT INTO SupervisorAvailabilities (SupervisorId, ScheduleType, DayOfWeek, SpecificDate, StartTime, EndTime, IsAvailable) VALUES
(11, 'WeeklyDefault', 'Tuesday', NULL, '14:00:00', '16:00:00', 1),
(11, 'WeeklyDefault', 'Friday', NULL, '14:00:00', '16:00:00', 1),
(11, 'DateOverride', NULL, '2026-11-20', '08:00:00', '17:00:00', 0);

-- Lecturer 4: Dr. Daniel Raj (ID: 12)
INSERT INTO SupervisorAvailabilities (SupervisorId, ScheduleType, DayOfWeek, SpecificDate, StartTime, EndTime, IsAvailable) VALUES
(12, 'WeeklyDefault', 'Tuesday', NULL, '14:00:00', '16:00:00', 1),
(12, 'WeeklyDefault', 'Friday', NULL, '10:00:00', '12:00:00', 1),
(12, 'DateOverride', NULL, '2026-11-20', '08:00:00', '17:00:00', 0);

-- Lecturer 5: Ir. Evelyn Chong (ID: 13)
INSERT INTO SupervisorAvailabilities (SupervisorId, ScheduleType, DayOfWeek, SpecificDate, StartTime, EndTime, IsAvailable) VALUES
(13, 'WeeklyDefault', 'Tuesday', NULL, '09:00:00', '11:00:00', 1),
(13, 'WeeklyDefault', 'Friday', NULL, '14:00:00', '16:00:00', 1),
(13, 'DateOverride', NULL, '2026-11-20', '08:00:00', '17:00:00', 0);

-- Lecturer 6: Dr. Farooq Ahmad (ID: 14)
INSERT INTO SupervisorAvailabilities (SupervisorId, ScheduleType, DayOfWeek, SpecificDate, StartTime, EndTime, IsAvailable) VALUES
(14, 'WeeklyDefault', 'Tuesday', NULL, '14:00:00', '16:00:00', 1),
(14, 'WeeklyDefault', 'Friday', NULL, '14:00:00', '16:00:00', 1),
(14, 'DateOverride', NULL, '2026-11-20', '08:00:00', '17:00:00', 0);

-- Lecturer 7: Ms. Grace Teo (ID: 15)
INSERT INTO SupervisorAvailabilities (SupervisorId, ScheduleType, DayOfWeek, SpecificDate, StartTime, EndTime, IsAvailable) VALUES
(15, 'WeeklyDefault', 'Tuesday', NULL, '14:00:00', '16:00:00', 1),
(15, 'WeeklyDefault', 'Friday', NULL, '10:00:00', '12:00:00', 1),
(15, 'DateOverride', NULL, '2026-11-20', '08:00:00', '17:00:00', 0);

-- Lecturer 8: Dr. Haris bin Omar (ID: 16)
INSERT INTO SupervisorAvailabilities (SupervisorId, ScheduleType, DayOfWeek, SpecificDate, StartTime, EndTime, IsAvailable) VALUES
(16, 'WeeklyDefault', 'Tuesday', NULL, '09:00:00', '11:00:00', 1),
(16, 'WeeklyDefault', 'Friday', NULL, '14:00:00', '16:00:00', 1),
(16, 'DateOverride', NULL, '2026-11-20', '08:00:00', '17:00:00', 0);

-- Lecturer 9: Ts. Irene Lee (ID: 17)
INSERT INTO SupervisorAvailabilities (SupervisorId, ScheduleType, DayOfWeek, SpecificDate, StartTime, EndTime, IsAvailable) VALUES
(17, 'WeeklyDefault', 'Tuesday', NULL, '14:00:00', '16:00:00', 1),
(17, 'WeeklyDefault', 'Friday', NULL, '14:00:00', '16:00:00', 1),
(17, 'DateOverride', NULL, '2026-11-20', '08:00:00', '17:00:00', 0);

-- Lecturer 10: Dr. Jeremy Ko (ID: 18)
INSERT INTO SupervisorAvailabilities (SupervisorId, ScheduleType, DayOfWeek, SpecificDate, StartTime, EndTime, IsAvailable) VALUES
(18, 'WeeklyDefault', 'Tuesday', NULL, '14:00:00', '16:00:00', 1),
(18, 'WeeklyDefault', 'Friday', NULL, '10:00:00', '12:00:00', 1),
(18, 'DateOverride', NULL, '2026-11-20', '08:00:00', '17:00:00', 0);

COMMIT TRANSACTION;
