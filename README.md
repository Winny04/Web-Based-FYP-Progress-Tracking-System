# Web-Based Final Year Project (FYP) Progress Tracking System

## Description
The **Web-Based FYP Progress Tracking System** is a comprehensive web application built with ASP.NET Core MVC. It is designed to streamline, manage, and evaluate the entire lifecycle of Final Year Projects for academic institutions. The platform facilitates seamless collaboration and communication between Students, Supervisors, External Examiners, and Administrators, ensuring project milestones are met efficiently.

---

## Key Features
* **Role-Based Access Control:** Tailored portals and dashboards for Administrators, Supervisors, External Examiners, and Students.
* **Student Portal:**
  * Topic exploration and online Initial Project Allocation Form (IAF) submission.
  * Meeting scheduling and digital signature submission on supervision logbooks.
  * Stage 1 & Stage 2 deliverable uploads, document revision histories, and progress tracking.
  * Real-time direct chat and attachment sharing with assigned supervisors.
* **Supervisor Portal:**
  * Review, accept, or reject student supervision requests.
  * Conduct and record supervision meetings with auto-calculated meeting week numbers.
  * One-click digital logbook signing with instant PDF generation.
  * Milestone feedback submission, rubric-based evaluation, and Excel marksheet grading.
* **External Examiner Portal:**
  * Dedicated examination portal for Stage 1 / Stage 2 defense evaluations and marksheet submissions.
* **Admin Dashboard:**
  * Manage academic sessions, system users, departments, specializations, and default milestone templates.
  * Centralized progress monitoring, schedule overrides, and audit reporting.
* **Real-Time Communication:** Live SignalR messaging and file-sharing between students and supervisors.
* **Automated Notifications:** Email alerts for applications, status updates, and milestone deadlines (powered by MailKit).
* **Document Processing:** Dynamic HTML-to-PDF rendering, signed logbook generation, and Excel marksheet evaluation (powered by PuppeteerSharp, iTextSharp, and NPOI).

---

## Tech Stack
* **Framework:** ASP.NET Core MVC (.NET 10.0 / .NET 6.0+)
* **Database:** Microsoft SQL Server (LocalDB, SQL Express, or Enterprise)
* **ORM:** Entity Framework Core
* **Real-time Engine:** ASP.NET Core SignalR
* **Frontend:** HTML5, CSS3, JavaScript, Bootstrap 5, Metronic UI Components
* **Key Libraries:**
  * **MailKit & MimeKit** (Email communication with offline development mode)
  * **PuppeteerSharp** (Chromium-based high-fidelity PDF rendering)
  * **NPOI & iTextSharp** (Excel marksheet processing and PDF generation)
  * **Quartz.NET** (Automated background task scheduling)

---

## Prerequisites
Before setting up the project locally, ensure you have:
* [.NET SDK](https://dotnet.microsoft.com/download) (.NET 10.0 SDK recommended or compatible runtime)
* [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (SQL Server Express or LocalDB)
* [Visual Studio 2022+](https://visualstudio.microsoft.com/) or [Visual Studio Code](https://code.visualstudio.com/)

---

## Quick Start Guide

Follow these steps to get the project up and running locally:

### 1. Clone the Repository
```bash
git clone https://github.com/Winny04/Web-Based-FYP-Progress-Tracking-System.git
cd Web-Based-FYP-Progress-Tracking-System
```

### 2. Configure Local Settings
Create your local configuration by copying the provided example template:

```bash
# On Windows PowerShell:
Copy-Item FYPTracker/appsettings.example.json FYPTracker/appsettings.Development.json

# On Bash / macOS / Linux:
cp FYPTracker/appsettings.example.json FYPTracker/appsettings.Development.json
```

Open `FYPTracker/appsettings.Development.json` and adjust the connection string to match your local SQL Server instance:
```json
{
  "EnvironmentName": "Development",
  "ConnectionStrings": {
    "Entities": "Server=localhost\\SQLEXPRESS;Database=FYPTracker_Development;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
  },
  "SmtpSettings": {
    "Offline": true,
    "DevRedirectEmail": "your-email@example.com",
    "EmailFrom": "no-reply@example.com",
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "SmtpUser": "your-username"
  },
  "FeatureFlags": {
    "IAFAlternativeTimeRequestEnabled": true
  }
}
```
> **Tip:** Keeping `"Offline": true` in `SmtpSettings` allows all registration, notification, and workflow tests to run locally without connecting to an external mail server.

### 3. Initialize the Database
You can initialize the database schema and default seed data using either method:

* **Option A (SQL Script - Recommended):**
  Open SQL Server Management Studio (SSMS) or Azure Data Studio, connect to your SQL Server instance, and run:
  ```
  FYPTracker/setup_fyp_database.sql
  ```

* **Option B (EF Core Migrations):**
  ```bash
  cd FYPTracker
  dotnet ef database update
  ```

### 4. Build and Run
From the root directory:
```bash
dotnet build
cd FYPTracker
dotnet run --launch-profile Run
```
Or open `FYPTracker.sln` in **Visual Studio** and press **F5** (Run profile).

The web application will be live at:
* **HTTPS:** `https://localhost:7262`
* **HTTP:** `http://localhost:5262`

---

## Default Demo Accounts
When initialized via `setup_fyp_database.sql`, the system provides pre-configured demo accounts for testing all user roles:

| Role | Username | Password | Notes |
| :--- | :--- | :--- | :--- |
| **Admin** | `admin` | `Admin@123` | Full administrative control & system settings |
| **Supervisor** | `dr.lim` | `Student@123` | Faculty of Computer Science & IT supervisor |
| **Supervisor** | `dr.siti` | `Student@123` | Faculty of Engineering supervisor |
| **Student** | `student` | `Student@123` | Active student account |

---

## Running Automated Tests
To run the full suite of unit and workflow verification tests:
```bash
dotnet test
```

---

## Project Structure
```text
Web-Based-FYP-Progress-Tracking-System/
├── FYPTracker/                  # Main ASP.NET Core MVC Application
│   ├── Controllers/             # MVC Controllers (Admin, Student, Supervisor, etc.)
│   ├── Hubs/                    # SignalR Real-Time Chat Hubs
│   ├── Models/                  # EF Core Data & Identity Models
│   ├── Services/                # Domain & Background Services
│   ├── Utils/                   # PDF & Marksheet Helper Utilities
│   ├── Views/                   # Razor Views & Metronic Partial Templates
│   ├── FormTemplates/           # JSON Schemas for dynamic FYP forms
│   ├── appsettings.example.json # Sanitized configuration template
│   └── setup_fyp_database.sql   # Database initialization script
├── FYPTracker.Tests/            # xUnit Test Project
├── FYPTracker.sln               # Visual Studio Solution File
└── README.md                    # Project Documentation & Setup Guide
```

---

## License
This project is open-source and available under the terms of the MIT License.
