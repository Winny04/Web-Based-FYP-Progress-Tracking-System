# Web-Based Final Year Project (FYP) Progress Tracking System

## Description
The **Web-Based FYP Progress Tracking System** is a comprehensive web application built with ASP.NET Core MVC. It is designed to streamline, manage, and evaluate the entire lifecycle of Final Year Projects for academic institutions. The platform facilitates seamless collaboration and communication between Students, Supervisors, External Examiners, and Administrators, ensuring project milestones are met efficiently.

## Features
* **Role-Based Access Control:** Dedicated dashboards and tailored functionalities for Students, Supervisors, External Examiners, and Administrators.
* **Student Portal:**
  * Register for topics, submit Initial Project Allocation Forms (IAF), and sign digital meeting logbooks.
  * Upload progress reports, deliverables, and Stage 1 / Stage 2 milestones.
  * Interactive messaging and chat attachments with assigned supervisors.
* **Supervisor Portal:**
  * Review, accept, or reject student project applications.
  * Conduct and record supervision meetings with auto-calculated meeting week numbers.
  * Digitally sign meeting logs with instantaneous PDF generation.
  * Provide milestone feedback, rubrics assessment, and marksheet uploads.
* **Admin Dashboard:**
  * Manage academic sessions, users, departments, specializations, and default milestone templates.
  * System-wide progress monitoring, schedule overrides, and automated reporting.
* **Real-Time Communication:** Live SignalR messaging and file-sharing between students and supervisors.
* **Email Notifications:** Automated email notifications for proposals, status updates, and milestones (via MailKit).
* **Document Processing:** Dynamic HTML-to-PDF rendering, signed logbook generation, and Excel marksheet evaluation (via PuppeteerSharp, iTextSharp, and NPOI).

## Tech Stack
* **Framework:** ASP.NET Core MVC (.NET 10.0 / .NET 6.0+)
* **Database:** SQL Server (LocalDB, SQL Express, or Full SQL Server)
* **ORM:** Entity Framework Core
* **Real-time Engine:** ASP.NET Core SignalR
* **Frontend:** HTML5, CSS3, JavaScript, Bootstrap 5, Metronic UI Components
* **Key Libraries:**
  * **MailKit & MimeKit** (Email communication)
  * **PuppeteerSharp** (High-fidelity PDF document rendering)
  * **NPOI & iTextSharp** (Marksheet Excel & PDF handling)
  * **Quartz.NET** (Background scheduling)

---

## Prerequisites
To run this project locally, ensure you have installed:
* [.NET SDK](https://dotnet.microsoft.com/download) (.NET 10.0 SDK recommended or compatible runtime)
* [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (SQL Server Express or LocalDB)
* An IDE such as [Visual Studio 2022+](https://visualstudio.microsoft.com/) or [Visual Studio Code](https://code.visualstudio.com/)

---

## Getting Started

Follow these steps to set up and run the system locally from GitHub:

### 1. Clone the repository
```bash
git clone https://github.com/Winny04/Web-Based-FYP-Progress-Tracking-System.git
cd Web-Based-FYP-Progress-Tracking-System
git checkout latest
```

### 2. Configure `appsettings.json`
Copy the provided sample configuration template to create your local development settings:
```bash
cp FYPTracker/appsettings.example.json FYPTracker/appsettings.Development.json
```
*(On Windows PowerShell, use: `Copy-Item FYPTracker/appsettings.example.json FYPTracker/appsettings.Development.json`)*

Open `FYPTracker/appsettings.Development.json` and adjust the database connection string to point to your local SQL Server instance:
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
> **Tip:** Setting `"Offline": true` in `SmtpSettings` allows you to run and test all workflows without needing an active SMTP mail server.

### 3. Database Setup
You can initialize the database using either of the following methods:

- **Option A (SQL Script - Recommended):**
  Open SQL Server Management Studio (SSMS) or Azure Data Studio, connect to your local SQL Server, and execute:
  ```
  FYPTracker/setup_fyp_database.sql
  ```

- **Option B (EF Core Migrations):**
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
Or open `FYPTracker.sln` in **Visual Studio** and select the **Run** profile (`F5`).

The application will be accessible at:
* **HTTPS:** `https://localhost:7262`
* **HTTP:** `http://localhost:5262`

---

## Running Unit & Integration Tests
To execute the automated test suite:
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
├── FYPTracker.sln               # Solution File
└── README.md                    # Project Documentation & Setup Guide
```

---

## License
This project is open-source and available under the terms of the MIT License.
