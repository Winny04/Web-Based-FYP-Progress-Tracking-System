# Web-Based Final Year Project (FYP) Progress Tracking System

## Description
The **Web-Based FYP Progress Tracking System** is a comprehensive web application built with ASP.NET Core MVC. It is designed to streamline, manage, and evaluate the entire lifecycle of Final Year Projects for academic institutions. The platform facilitates seamless collaboration and communication between Students, Supervisors, and Administrators, ensuring project milestones are met efficiently.

## Features
* **Role-Based Access Control:** Dedicated dashboards and tailored functionalities for Students, Supervisors, and Administrators.
* **Student Portal:**
  * Register for topics and submit project proposals.
  * Upload progress reports and milestones.
  * View feedback and evaluations from supervisors.
* **Supervisor Portal:**
  * Review, accept, or reject student proposals.
  * Monitor assigned students' progress.
  * Provide continuous feedback, grading, and document reviews.
* **Admin Dashboard:**
  * Manage user accounts (Students, Supervisors) and system configurations.
  * Oversee the overall FYP timeline and processes.
* **Email Notifications:** Automated email alerts for important updates and submissions (via MailKit).
* **Scheduled Tasks:** Background jobs for deadlines and reminders (via Quartz).
* **Document Generation:** Generate and export reports in PDF and Excel formats (via iTextSharp and NPOI).

## Tech Stack
* **Framework:** ASP.NET Core MVC (.NET 6.0)
* **Database:** SQL Server
* **ORM:** Entity Framework Core (EF Core 6.0)
* **Frontend:** HTML, CSS, JavaScript, Bootstrap 5, CoreUI Icons
* **Other Libraries:**
  * AutoMapper (Object mapping)
  * MailKit & MimeKit (Email services)
  * Quartz.NET (Task scheduling)
  * iTextSharp & NPOI (Document processing)
  * PuppeteerSharp (PDF generation from HTML)

## Prerequisites
To run this project locally, ensure you have the following installed:
* [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
* [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) or SQL Server Express
* A suitable IDE like [Visual Studio 2022](https://visualstudio.microsoft.com/) or [Visual Studio Code](https://code.visualstudio.com/)

## Getting Started

Follow these steps to set up the project on your local machine:

1. **Clone the repository:**
   ```bash
   git clone https://github.com/Winny04/Web-Based-FYP-Progress-Tracking-System.git
   cd Web-Based-FYP-Progress-Tracking-System
   ```

2. **Database Setup:**
   * Open SQL Server Management Studio (SSMS).
   * Execute the `setup_fyp_database.sql` script located in the `FYPTracker` directory to create the database schema and initial data.
   * *Alternatively*, you can use Entity Framework Core tools to update the database:
     ```bash
     cd FYPTracker
     dotnet ef database update
     ```

3. **Configure the Application:**
   * Open `appsettings.json` (or `appsettings.Development.json`) in the `FYPTracker` folder.
   * Update the `"Entities"` ConnectionString to match your local SQL Server instance.
     ```json
     "ConnectionStrings": {
       "Entities": "Server=YOUR_SERVER_NAME;Database=FYPTracker;Trusted_Connection=True;MultipleActiveResultSets=true"
     }
     ```

4. **Run the Application:**
   * Open the `.sln` file in Visual Studio and press `F5` (or click "Start") to build and run the app.
   * *Alternatively*, run via .NET CLI:
     ```bash
     dotnet build
     dotnet run
     ```
   * The application will typically start at `https://localhost:7198` (check `Properties/launchSettings.json` for exact ports).

## Usage
Upon navigating to the homepage, users will be prompted to log in. Depending on the account type (Student, Supervisor, Admin), the system will redirect to the respective dashboard where the user can begin managing their FYP tasks.

## License
This project is open-source and available under the terms of the MIT License. Feel free to fork and customize it as needed for your institution.
