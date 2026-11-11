# Postgraduate Research (PGR) Student Management System

A modular ASP.NET Core web application demonstrating a **modular monolith** architecture for managing Postgraduate Research (PGR) students, academic supervisory teams, PhD admissions, and student research activities.

---

## Architecture Overview

The application is structured as a **modular monolith** divided into four distinct domain modules, each containing its own controllers, models, data access layer (DAOs/repositories), seeders, and services. Modules communicate across boundaries using lightweight **contracts (interfaces)** rather than referencing each other's data layer directly:

```
PgrStudentManagement.Web/Modules/
├── Students/               # Student records, enrolments, progression
│   └── Contracts/          # IStudentLookup (consumed by Supervisors & Applications)
├── AcademicSupervisors/    # Staff, role eligibility, supervisory teams
│   └── Contracts/          # ISupervisorLookup (consumed by Applications)
├── PhdApplications/        # Prospective PhD proposals, review, admissions lifecycle
└── ResearchActivities/     # Conferences, publications, research documents
```

### 1. Students Module ([`Modules/Students`](./PgrStudentManagement.Web/Modules/Students))
- Handles student records, programme enrolments, mode of study, and progression milestones (thesis titles, expected dates, submissions).
- **Persistence**: Relational SQLite database (`student-management.db`) via Entity Framework Core (`Students` and `Enrolments` tables).
- **Public Contract**: Exposes `IStudentLookup` (returning `StudentSummary`), allowing other modules to verify student existence and full names without coupling to the `Students` persistence schema.

### 2. Academic Supervisors Module ([`Modules/AcademicSupervisors`](./PgrStudentManagement.Web/Modules/AcademicSupervisors))
- Manages supervisor records, role eligibility (Director of Studies, First Supervisor, Second Supervisor, Adviser), and supervisory team assignments.
- Enforces institutional rules (e.g. Director of Studies must be institution-employed, maximum supervisor thresholds, single DoS/First Supervisor per student).
- **Persistence**: Relational SQLite database via EF Core (`AcademicSupervisors`, `AcademicSupervisorEligibleRoles`, `SupervisoryAssignments`).
- **Public Contract**: Exposes `ISupervisorLookup` (returning `SupervisorSummary`), allowing other modules to look up supervisor names and check Main Supervisor eligibility.

### 3. PhD Applications Module ([`Modules/PhdApplications`](./PgrStudentManagement.Web/Modules/PhdApplications))
- **Purpose**: Manages the admissions intake and proposal evaluation pipeline for prospective PhD candidates before they become enrolled students.
- **Domain Model (`PhdApplication`)**:
  - **Identity**: Natural primary key `ApplicationReference` (e.g. `APP-2026-001`).
  - **Applicant Details**: First name, last name, email address.
  - **Proposed Study**: Programme code (e.g. `PHD-COMP`, `PHD-DESIGN`), mode of study (`Full-time` / `Part-time`), proposed cohort start date.
  - **Research Proposal**: Working thesis title and research abstract/summary.
  - **Funding**: Enumerated `FundingType` (`SelfFunded`, `Studentship`, `ResearchCouncil`, `ExternalSponsor`).
  - **Proposed Supervision**: Optional `ProposedSupervisorStaffNumber` foreign key referencing `AcademicSupervisors.StaffNumber`.
  - **Admissions Lifecycle**: `ApplicationStatus` (`Submitted`, `UnderReview`, `InterviewScheduled`, `OfferMade`, `OfferAccepted`, `Rejected`, `Withdrawn`, `Enrolled`).
  - **Decisions & Enrollment**: Submission date, decision timestamp, and optional `EnrolledStudentNumber` foreign key referencing `Students.StudentNumber` once an offer is accepted and the applicant matriculates.
- **Cross-Module Integration**:
  - Uses `ISupervisorLookup` to verify that any proposed supervisor exists and is eligible to act as a Main Supervisor (`DirectorOfStudies` or `FirstSupervisor`).
  - Uses `IStudentLookup` to validate matriculated student records when an application transitions to `Enrolled`.
- **Persistence**: Relational SQLite database via EF Core (`PhdApplications` table).
- **UI / UX & Accessibility Starting Point**: The module's starter views ([`Create.cshtml`](./PgrStudentManagement.Web/Views/PhdApplications/Create.cshtml) and [`EditStatus.cshtml`](./PgrStudentManagement.Web/Views/PhdApplications/EditStatus.cshtml)) are intentionally stripped of form validation and defaulted to plain text inputs, providing a real-world scenario for students to practice modern UI/UX, input types, and accessible form design (see [Lab Resources](#lab-resources-ui--ux-and-accessibility)).

### 4. Research Activities Module ([`Modules/ResearchActivities`](./PgrStudentManagement.Web/Modules/ResearchActivities))
- Tracks student research activities, conference presentations, published papers, and research milestones.
- **Persistence**: Stored in a NoSQL **MongoDB** database (`StudentManagement` database, `ResearchActivities` collection).

---

## Database Architecture & Persistence

The application uses **MySQL** for relational domain models, **PostgreSQL** for ASP.NET Core Identity authentication and authorization, and **MongoDB** for flexible semi-structured research activity documents.

### 1. MySQL Domain Relational Database (`student_management_db`)
- **Connection**: `Server=localhost;Port=3306;Database=student_management_db;User=root;Password=pass1234%uZ;` (configured in `appsettings.json` under `ConnectionStrings:StudentManagementDatabase`).
- **Tables**:
  - `Students`: Core student demographic and research milestone records.
  - `Enrolments`: Programme history and enrolment statuses (`Active`, `Suspended`, `Completed`).
  - `AcademicSupervisors`: Staff records and employment types (`InstitutionEmployed`, `External`).
  - `AcademicSupervisorEligibleRoles`: Many-to-many relationship mapping supervisors to permissible supervisory roles.
  - `SupervisoryAssignments`: Student-supervisor team pairings with start/end dates and role types.
  - `PhdApplications`: Candidate applications, proposals, proposed supervisors, and status history.
- **Automatic Schema Creation & Seeding**: When the application boots, `Program.cs` automatically invokes `context.Database.EnsureCreatedAsync()`, ensuring all schema tables are created and seeded if they do not yet exist.

### 2. PostgreSQL Identity Database (`identity`)
- **Connection**: `Host=localhost;Port=5432;Database=identity;Username=identity;Password=identity$` (configured in `appsettings.json` under `ConnectionStrings:IdentityDatabase`).
- **Purpose**: Backing store for ASP.NET Core Identity, securing the **Academic Supervisors** module with authentication and role-based authorization.
- **Tables**: Standard EF Core Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`).
- **Default Seeded Account**:
  - **Email**: `supervisor@pgr.ac.uk`
  - **Password**: `Password123!`
  - (Referenced in [`credentials.md`](./credentials.md))

### 3. MongoDB Research Activities Database (`StudentManagement`)
- **Connection**: `mongodb://localhost:27017` (configured in `appsettings.json` under `ConnectionStrings:MongoDb` and `ResearchActivityDatabase`).
- **Database**: `StudentManagement`
- **Collection**: `ResearchActivities`
- **Document Model**: Flexible document schema storing activity metadata, typed activity discriminator, and arbitrary key-value details (reflections, discussion points, action items, presentation abstracts).
- **Seeding**: Automatically seeded via `ResearchActivityDataSeeder` on boot if no documents exist for the primary test student.

---

## How to Run the Application

### 1. Prerequisites
Ensure your local database instances are running:
- **MySQL Server** on `localhost:3306` with database `student_management_db` accessible by user `root`.
- **PostgreSQL Server** (e.g., Postgres.app) on `localhost:5432` with database `identity` accessible by user `identity`.
- **MongoDB Server** on `localhost:27017` with database `StudentManagement`.

### 2. Build the Solution
From the repository root:
```bash
dotnet build
```

### 3. Run the Web Project
Launch the web application:
```bash
dotnet run --project PgrStudentManagement.Web
```
Or with hot reload:
```bash
dotnet watch --project PgrStudentManagement.Web
```

By default, the application listens on:
- HTTP: `http://localhost:5277`
- HTTPS: `https://localhost:7178`

### 4. Application Startup Sequence
When [PgrStudentManagement.Web/Program.cs](PgrStudentManagement.Web/Program.cs) executes:
1. **Service Registration**: Registers MVC controllers, MySQL EF Core `StudentManagementDbContext`, PostgreSQL EF Core `AppIdentityDbContext`, ASP.NET Core Identity services, DAOs, repositories, and cross-module services (`IStudentLookup`, `ISupervisorLookup`).
2. **Database Provisioning**:
   - Creates and updates MySQL domain schema via `context.Database.EnsureCreatedAsync()`.
   - Creates PostgreSQL Identity schema via `identityContext.Database.EnsureCreatedAsync()`.
3. **Ordered Data Seeding**:
   - `StudentDataSeeder` (Students)
   - `EnrolmentDataSeeder` (Enrolments referencing `StudentNumber`)
   - `AcademicSupervisorDataSeeder` (Supervisors and supervisory assignments)
   - `PhdApplicationDataSeeder` (PhD applications referencing supervisors and students)
   - `ResearchActivityDataSeeder` (Research activity records)
   - `AppIdentityDataSeeder` (Default supervisor user and roles in PostgreSQL)
4. **Middleware Pipeline**: Configures routing, `app.UseAuthentication()`, `app.UseAuthorization()`, and static assets.

### 4. Running the Tests
To run unit and integration tests across all modules:
```bash
dotnet test
```

---

### 5. Containerisation & Orchestration Labs

- **Core Lab — Containerising with Docker & Docker Compose**: [`LabResources/lab-instructions.md`](LabResources/lab-instructions.md)
  - **Environment Options**: Setup via Microsoft Dev Portal VM ([https://devportal.microsoft.com/](https://devportal.microsoft.com/)) on Windows AMD64 or native macOS.
  - **Concepts**: Docker Images vs. Containers, port mapping, bridge networks, and volume persistence.
  - **Multi-Service Extraction**: Extracting MySQL, PostgreSQL (Identity), and MongoDB into isolated containers.
  - **Docker Compose**: Orchestration, health checks, dependency management, and environment variable overrides.
- **Bonus Lab — Horizontal Scaling with Kubernetes & MiniKube**: [`LabResources/bonus-lab-minikube-scaling.md`](LabResources/bonus-lab-minikube-scaling.md)
  - **Case Study**: Scaling the stateless ASP.NET Core web tier during the **annual PhD application deadline surge**.
  - **Kubernetes Architecture**: Pods, Deployments, ClusterIP & NodePort Services, and PersistentVolumeClaims.
  - **Hands-On Scaling & Self-Healing**: Scaling from 1 $\rightarrow$ 5 replicas, observing traffic load distribution, and simulating container crash self-healing.