# Lab: Containerising the Multi-Service PGR System with Docker

## Overview

The files in the gitlab repository build a multi-container asp.net core application. Along with the application (that uses a Modular Monolith architecture) there are three databases: postgres, mysql and mongodb. You will see from the application and database code that the application uses EF Core to persist data in both postgres and mysql, and uses the mongo driver to persist data in mongodb.

- The ASP.NET Core web application ran on your host operating system (`localhost:5277`).
- Relational domain data was stored in a host **MySQL** database (`localhost:3306`).
- ASP.NET Core Identity authentication and authorization data was stored in a host **PostgreSQL** database (`localhost:5432`).
- Semi-structured research activities were stored in a host **MongoDB** document database (`localhost:27017`).

While running services directly on your host machine is convenient during early development, it introduces significant real-world challenges:
- **"It works on my machine" syndrome**: Differences in OS versions, database patches, system libraries, and local configurations.
- **Dependency bloat**: Installing and maintaining three separate database engines on your development workstation.
- **Port conflicts & environment pollution**: Running multiple projects that require different versions of MySQL or PostgreSQL.
- **No path to cloud deployment**: Modern production environments deploy applications as self-contained images into container orchestrators.

In this lab, you will **containerise** this entire multi-service application using **Docker** and **Docker Compose**. Once complete, an optional [Bonus Lab](./bonus-lab-minikube-scaling.md) explores deploying and horizontally scaling the application in **Kubernetes** using **MiniKube**.

```
+---------------------------------------------------------------------------------------+
|                                    DOCKER HOST                                        |
|                                                                                       |
|  +--------------------+   pgr-network (Docker Bridge Network)                         |
|  |   Host Browser     |                                                               |
|  |  localhost:5277    |                                                               |
|  +---------+----------+                                                               |
|            | (Port 5277:8080)                                                         |
|            v                                                                          |
|  +--------------------+         +--------------------+         +--------------------+ |
|  |     web (App)      |-------->|   postgres-db      |         |     mysql-db       | |
|  |  ASP.NET Core 10   |         |  PostgreSQL 16     |         |     MySQL 9.0      | |
|  |  (Multi-stage)     |         |  (Identity DB)     |         |    (Domain DB)     | |
|  +---------+----------+         +---------+----------+         +---------+----------+ |
|            |                              |                              |            |
|            |                              v                              v            |
|            |                     [ postgres_data ]              [ mysql_data ]        |
|            |                     (Docker Volume)                (Docker Volume)       |
|            |                                                                          |
|            |                    +--------------------+                                |
|            +------------------->|     mongo-db       |                                |
|                                 |    MongoDB 5.0     |                                |
|                                 |   (Research DB)    |                                |
|                                 +---------+----------+                                |
|                                           |                                           |
|                                           v                                           |
|                                    [ mongo_data ]                                     |
|                                    (Docker Volume)                                    |
+---------------------------------------------------------------------------------------+
```

---

## Learning Objectives

By the end of this lab, you will be able to:
1. **Install, configure, and verify Docker Desktop** on your development machine (Dev Portal VM or macOS).
2. **Master the core distinction between Docker Images and Containers**.
3. **Extract host-installed database engines into isolated Docker containers** (PostgreSQL, MySQL, and MongoDB) using `docker run` and explicit bridge networking.
4. **Author an optimized, secure multi-stage `Dockerfile`** for a modern ASP.NET Core web application (.NET 10).
5. **Implement `.dockerignore`** to optimize image build contexts and avoid host file leaks.
6. **Master Docker container networking and service discovery**: understand why `localhost` changes to container hostnames.
7. **Orchestrate the entire multi-service stack with Docker Compose (`docker-compose.yml`)**, incorporating container health checks, dependency management, and environment variable overrides.
8. **Verify persistent storage across container lifecycles** using Docker named volumes.

---

## Part 1: Setting Up & Verifying Docker Desktop

### 1.1 Understanding Your Lab Environment: Why Use a Cloud Dev VM?

Running Docker and Kubernetes requires low-level system virtualisation (such as WSL 2 or Hyper-V) and daemon privileges that require **local administrative rights**. On standard institutional/college lab computers, student accounts intentionally do **not** have administrator permissions, preventing direct local installation of Docker Desktop or modifying system virtualisation features.

To ensure an unconstrained, consistent, and pre-configured developer experience, this lab uses two access routes:

- **Option A (Default & Primary Setup for College PCs)**: Access a cloud-hosted virtual development machine (Microsoft Dev Box / Dev VM) via the **Microsoft Dev Portal** ([https://devportal.microsoft.com/](https://devportal.microsoft.com/)). This VM comes with administrator privileges, Docker Desktop pre-installed, and all necessary tooling ready to go.
- **Option B (Alternative for Personal Laptops)**: If you are using your own **personal Mac (macOS)** laptop in class, you have local administrator rights and can run Docker Desktop natively on macOS.

---

### 1.2 Option A (Default): Accessing the Dev Portal VM & Installing Docker (AMD64)

If you are working on a college lab PC (or any computer where you do not have administrative rights), follow these steps to connect to your cloud Dev VM and install Docker:

1. **Sign In to the Microsoft Dev Portal:**
   - Open your web browser (Chrome, Edge, or Firefox) and navigate to:
     $$\rightarrow\text{ }\textbf{\url{https://devportal.microsoft.com/}}$$
   - Sign in using your **college/university Microsoft 365 credentials** (e.g. `your.name@college.ac.uk`).

2. **Access Your Assigned Dev Box (Virtual Machine):**
   - On the Dev Portal dashboard, locate your project workspace and the **Dev Box** provisioned for this module.
   - If your Dev Box is currently *Stopped*, click **Start** and allow 1–2 minutes for the virtual machine to boot.

3. **Connect to the Dev Box:**
   - **Method 1 (Browser Web Client — Fastest):** Click **Open in browser**. A full interactive Windows desktop session will open directly inside your browser tab.
   - **Method 2 (Remote Desktop / Windows App):** Click the drop-down next to Open and select **Open in Remote Desktop app** or **Open in Windows App** for a full-screen, multi-monitor client experience.

4. **Verify System Architecture in PowerShell:**
   - Inside the Dev Box, open **PowerShell** (search for *PowerShell* in the Start Menu).
   - Check the VM processor architecture by running:
     ```powershell
     $env:PROCESSOR_ARCHITECTURE
     ```
   - This should return `AMD64`, confirming a 64-bit x86/Intel/AMD architecture.

5. **Install Docker Desktop (AMD64):**
   The Dev Box VM comes with administrator privileges, but Docker Desktop is not pre-installed. Choose either of the following methods to install it:

   - **Method 1 (Recommended — Quickest via winget):**
     In PowerShell, execute the Windows Package Manager command:
     ```powershell
     winget install -e --id Docker.DockerDesktop
     ```
     Press `Y` to accept the license agreements if prompted. Wait for the installer to download and run.

   - **Method 2 (Direct Installer Download):**
     Open Microsoft Edge inside the Dev Box and download the official **AMD64** installer directly:
     $$\rightarrow\text{ }\textbf{\url{https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe}}$$
     Run `Docker Desktop Installer.exe`.

   > [!IMPORTANT]
   > **Critical Installer Configuration Options:**
   > During installation (or in the installer dialog), ensure you select the following options:
   > 1. Select **All Users Installation (Requires Password)** (installs for all users on the machine).
   > 2. Ensure **WSL 2** is selected (Use WSL 2 instead of Hyper-V).
   > 3. Once installation completes, click **Close and restart** (to reboot the Dev Box and register the WSL 2 subsystem).

6. **Reconnect and Update WSL:**
   - After the Dev Box restarts, reconnect to your VM via the Dev Portal (`https://devportal.microsoft.com/`).
   - Open **PowerShell** or **Terminal** as Administrator (or standard user) and immediately run:
     ```powershell
     wsl.exe --update
     ```
     *(This ensures the latest WSL 2 Linux kernel is installed so the Docker engine can start reliably).*

7. **Launch and Initialise Docker Desktop:**
   - Open the **Start Menu**, search for **Docker Desktop**, and launch it.
   - Accept the Docker Subscription Service Agreement if prompted.
   - Wait until the status bar in the bottom-left corner of Docker Desktop turns green: **"Engine running"** (or the Docker whale icon in the system tray shows active).

8. **Open Terminal and Clone Your Code:**
   - In PowerShell or VS Code inside the Dev Box, navigate to your workspace directory and clone the repository:
     ```powershell
     git clone <your-repository-url>
     cd container-lab-start
     ```

---

### 1.3 Option B (Alternative): Local Setup on Personal Mac (macOS)

If you are using your own personal Mac laptop where you have administrator privileges, you can run Docker Desktop natively on macOS:

1. **Download Docker Desktop for Mac:**
   - Visit [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop/).
   - Choose **Mac with Apple Silicon** if using an M1, M2, M3, or M4 Mac.
   - Choose **Mac with Intel chip** if using an older Intel Mac.
2. **Install and Launch:**
   - Open `Docker.dmg`, drag the **Docker** icon to your **Applications** folder, and launch Docker from Applications or Spotlight.
   - When prompted, grant privileged helper permissions (required to configure network filters and virtualisation).
3. **Recommended Resource Allocation:**
   - In Docker Desktop, click the **Settings (Gear Icon)** $\rightarrow$ **Resources**:
     - **CPUs**: Allocate at least **2 to 4 vCPUs**.
     - **Memory**: Allocate at least **4 GB RAM** (our stack runs ASP.NET Core, MySQL, PostgreSQL, and MongoDB concurrently).
     - **Disk**: Ensure at least **20 GB** is free.
   - Click **Apply & restart**.

---

### 1.4 Verifying Your Docker Environment (Both Setups)

Whether working inside the **Microsoft Dev Portal VM** or locally on a **Mac**, open your terminal (**PowerShell** on the Dev Box, or **Terminal / zsh** on Mac) and execute:

```bash
# 1. Check Docker CLI and Daemon versions
docker version

# 2. Check Docker Compose version (V2 compose plugin)
docker compose version

# 3. Inspect system info and runtime resources
docker info

# 4. Run the canonical test image
docker run --rm hello-world
```

If you see the message:
```text
Hello from Docker!
This message shows that your installation appears to be working correctly.
```
Your Docker engine is active, your user account has container execution permissions, and you are ready to begin containerising the multi-database stack!

> [!NOTE]
> **What did `docker run --rm hello-world` do behind the scenes?**
> 1. The **Docker client** contacted the **Docker daemon**.
> 2. The daemon checked its local image cache; because `hello-world:latest` was not found locally, it pulled the image from **Docker Hub**.
> 3. The daemon created a new container from that image and executed its default executable (`/hello`).
> 4. The container printed the welcome message to standard output, which was streamed back to your terminal.
> 5. Because the `--rm` flag was supplied, the container was automatically deleted when it finished running.

> [!TIP]
> **One-Line CLI Commands:**
> All Docker CLI commands in this guide are formatted as single continuous lines without backslashes (`\`) or backticks (``` ` ```). This makes them easy to copy and paste directly into PowerShell on the Dev Box VM or Terminal on macOS without line-break or syntax errors.

---

## Part 2: Architecture & Migration Strategy

### 2.1 The Baseline Architecture (Before)

In the previous exercise, your application was configured with three local database instances:

| Service | Host Location | Database Name | Default Port | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **ASP.NET Core Web** | Host OS process | N/A | `5277` (HTTP) | Web frontend & modular domain logic |
| **MySQL** | Host OS process | `student_management_db` | `3306` | Students, Supervisors, Enrolments, Applications |
| **PostgreSQL** | Host OS process | `identity` | `5432` | ASP.NET Core Identity authentication & roles |
| **MongoDB** | Host OS process | `StudentManagement` | `27017` | Research activity documents |

### 2.2 Image vs. Container: Understanding the Core Difference

One of the most essential concepts to master in Docker is the distinction between an **Image** and a **Container**. If you have ever wondered why an image appears in your image list but isn't serving web traffic, or why you can have multiple containers running from one single image, this distinction is key:

| Analogy | Docker Image (Blueprint / Template) | Docker Container (Live Runtime Instance) |
| :--- | :--- | :--- |
| **Object-Oriented Programming** | `Class` (the type definition) | `Object` instance in memory (created with `new`) |
| **Cooking & Baking** | Recipe printed in a cookbook | Active dish cooking in the oven |
| **Building Construction** | Architectural blueprint on paper | Physical building you can walk inside |
| **Operating Systems** | `.exe` or executable binary on disk | Active process with a PID in Task Manager |

#### What is an Image?
- An **Image** is an **immutable, read-only snapshot or package** that contains everything required to run your service: base OS files (e.g. Alpine Linux), system libraries, runtime engines (e.g. .NET 10 Runtime), environment configurations, and compiled application binaries.
- Images are built once using a `Dockerfile` (`docker build`) or pulled from a registry like Docker Hub (`docker pull mysql:9.0`).
- An image is static on disk and **never executes code on its own**. It consumes zero CPU and zero active RAM.
- You can inspect your available local images at any time with:
  ```bash
  docker images
  ```

#### What is a Container?
- A **Container** is an **isolated, running process instance** instantiated from an image using `docker run` or `docker compose up`.
- When Docker creates a container from an image, it provisions:
  1. An active process tree with isolated Linux namespaces (Process IDs, network stack, filesystem mount points).
  2. Its own private virtual network interface and internal IP address.
  3. A thin, temporary **Read-Write layer** on top of the image's immutable read-only layers. Any file modifications, database writes, or logs created during execution live in this layer.
- **One Image $\rightarrow$ Multiple Containers:** You can spin up as many independent containers as needed from a single image. For instance, from the single `pgr-student-management:1.0` image, you could run three identical web instances (`web-1`, `web-2`, and `web-3`) concurrently!
- You can inspect active containers with:
  ```bash
  docker ps
  ```

```
+-------------------------------------------------------------------------------+
|                                 DOCKER IMAGE                                  |
|                    (Immutable, Read-Only Template on Disk)                    |
|       [ Base Linux OS ]  -->  [ .NET 10 Runtime ]  -->  [ Compiled App ]       |
+-------------------------------------------------------------------------------+
                       |                                   |
                 docker run                          docker run
                       |                                   |
                       v                                   v
+---------------------------------------------+ +---------------------------------------------+
|                CONTAINER 1                  | |                CONTAINER 2                  |
| [ Read-Write Ephemeral Layer (Writes/Logs) ]| | [ Read-Write Ephemeral Layer (Writes/Logs) ]|
| [ Read-Only Image Layers (Shared)          ]| | [ Read-Only Image Layers (Shared)          ]|
| Isolated Process Space (PID 1420)           | | Isolated Process Space (PID 1890)           |
| Virtual Network IP: 172.18.0.4              | | Virtual Network IP: 172.18.0.5              |
+---------------------------------------------+ +---------------------------------------------+
```

---

### 2.3 Critical Container Concepts: Networking, Storage & Configuration

Before writing commands, understand these three critical container primitives:

#### 1. The `localhost` Trap (Container Isolation)
Inside a container, `localhost` (or `127.0.0.1`) refers **only to that specific container itself**, NOT to your host machine, and NOT to other containers!
- When your web application runs inside a container, connecting to `localhost:3306` fails because there is no MySQL service running inside the *web* container.
- To communicate, containers must join a **shared Docker network** and address each other by their **container names** (e.g. `Server=mysql-db;Port=3306...`).

#### 2. Container Ephemerality & Volumes
Containers are designed to be immutable and disposable. When a container is deleted (`docker rm`), any data written inside its default writable layer is **permanently destroyed**.
- Database data must NEVER be stored inside the container filesystem.
- Instead, we mount a **Docker Named Volume** (e.g. `mysql_data:/var/lib/mysql`) into the container. Even if the container is destroyed and recreated, the volume persists on your host drive.

#### 3. Environment Variable Hierarchy in ASP.NET Core
In ASP.NET Core, any configuration value in `appsettings.json` can be overridden by environment variables using double underscores (`__`):
- `ConnectionStrings:StudentManagementDatabase` in JSON $\rightarrow$ `ConnectionStrings__StudentManagementDatabase` in Docker environment variables.
- `ConnectionStrings:IdentityDatabase` in JSON $\rightarrow$ `ConnectionStrings__IdentityDatabase`.
- `ConnectionStrings:MongoDb` in JSON $\rightarrow$ `ConnectionStrings__MongoDb`.

---

## Part 3: Containerising the Databases Step-by-Step (Manual CLI)

To build a solid mental model of how containers work before using automated orchestration tools, we will first create the database containers individually using the `docker` CLI.

> [!WARNING]
> **Port Conflicts with Host Databases (Mac Users Only):**
> - **Dev Portal VM Users (Default):** Your cloud VM is a dedicated container host with no host-level database servers running, so ports `3306`, `5432`, and `27017` are completely free. You can proceed directly to Step 3.1!
> - **Personal Mac Users:** If you previously had local instances of MySQL (`3306`), PostgreSQL (`5432`), or MongoDB (`27017`) running directly on macOS, they will occupy those ports. Stop them before proceeding:
>   - Stop Postgres.app or run `brew services stop postgresql@16`.
>   - Stop MySQL: `brew services stop mysql` or use the MySQL System Settings pane.
>   - Stop MongoDB: `brew services stop mongodb-community`.

### Step 3.1: Create a Dedicated Bridge Network

Docker containers can only discover each other by name if they are on a **user-defined bridge network** (the default `bridge` network does not support automatic DNS resolution).

```bash
docker network create pgr-network
```

Verify the network was created:
```bash
docker network ls
```

---

### Step 3.2: Containerise PostgreSQL (Identity DB)

We will use the lightweight official `postgres:16-alpine` image.

1. **Create a persistent named volume:**
   ```bash
   docker volume create postgres_data
   ```

2. **Launch the PostgreSQL container:**
   ```bash
   docker run -d --name postgres-db --network pgr-network -p 5432:5432 -e POSTGRES_USER=identity -e POSTGRES_PASSWORD='identity$' -e POSTGRES_DB=identity -v postgres_data:/var/lib/postgresql/data --restart unless-stopped postgres:16-alpine
   ```

   **Explanation of flags:**
   - `-d`: Run in detached mode (in the background).
   - `--name postgres-db`: Names the container so other containers can reach it at hostname `postgres-db`.
   - `--network pgr-network`: Attaches the container to our shared bridge network.
   - `-p 5432:5432`: Forwards host port `5432` to container port `5432`.
   - `-e ...`: Supplies required PostgreSQL initialisation environment variables.
   - `-v postgres_data:/var/lib/postgresql/data`: Mounts our persistent volume.
   - `--restart unless-stopped`: Restarts the container automatically if Docker reboots.

3. **Verify PostgreSQL is running and healthy:**
   ```bash
   # Check running containers
   docker ps -f name=postgres-db

   # Run a query inside the container via psql
   docker exec -it postgres-db psql -U identity -d identity -c "\conninfo"
   ```

---

### Step 3.3: Containerise MySQL (Relational Domain DB)

We will use the official `mysql:9.0` (or `mysql:8.4-lts`) image.

1. **Create a persistent named volume:**
   ```bash
   docker volume create mysql_data
   ```

2. **Launch the MySQL container:**
   ```bash
   docker run -d --name mysql-db --network pgr-network -p 3306:3306 -e MYSQL_ROOT_PASSWORD='pass1234%uZ' -e MYSQL_DATABASE=student_management_db -v mysql_data:/var/lib/mysql --restart unless-stopped mysql:9.0
   ```

3. **Verify MySQL is running:**
   ```bash
   # Watch the logs until you see "ready for connections"
   docker logs -f mysql-db
   # (Press Ctrl+C to exit log follow mode)

   # Execute a query inside the container
   docker exec -it mysql-db mysql -u root -p'pass1234%uZ' -e "SHOW DATABASES;"
   ```

---

### Step 3.4: Containerise MongoDB (Research Activities NoSQL)

We will use the official `mongo:5.0` image.

1. **Create a persistent named volume:**
   ```bash
   docker volume create mongo_data
   ```

2. **Launch the MongoDB container:**
   ```bash
   docker run -d --name mongo-db --network pgr-network -p 27017:27017 -v mongo_data:/data/db --restart unless-stopped mongo:5.0
   ```

3. **Verify MongoDB is running:**
   ```bash
   docker exec -it mongo-db mongo --eval "db.adminCommand('ping')"
   ```

---

## Part 4: Containerising the ASP.NET Core Application

### 4.1 Understanding Multi-Stage Docker Builds

A common beginner mistake is using a single Docker image containing the full .NET SDK to run the application in production. The .NET SDK image is over **800 MB** and contains compilers, NuGet caches, and command-line tools that should never be present in a production runtime.

A **multi-stage build** solves this by using separate stages:
1. **`build` stage**: Uses `mcr.microsoft.com/dotnet/sdk:10.0` to restore dependencies and compile the code.
2. **`publish` stage**: Uses the SDK to package binaries in `Release` configuration.
3. **`final` stage**: Copies *only* the compiled binaries into a slim `mcr.microsoft.com/dotnet/aspnet:10.0` runtime image (~**220 MB**). The compiler, source files, and temporary caches are discarded.

```
+--------------------------------------------------------------------------------+
| STAGE 1: build & publish (SDK Image ~850MB)                                    |
| [ Source Code ] + [ dotnet restore ] + [ dotnet publish -c Release ]           |
|                                                                                |
|                                  | Only compiled DLLs & static assets copied   |
|                                  v                                             |
| STAGE 2: final runtime (ASP.NET Runtime Image ~220MB)                          |
| [ ASP.NET Core 10 Runtime ] + [ Published App DLLs ]                           |
+--------------------------------------------------------------------------------+
```

### 4.2 Creating `.dockerignore`

Before creating a `Dockerfile`, create a `.dockerignore` file in the repository root. This prevents copying temporary build artifacts (`bin/`, `obj/`), version control history (`.git/`), and IDE files into the Docker build context.

Create `.dockerignore` in the repository root:

```gitignore
**/.git
**/.github
**/.vs
**/.vscode
**/bin
**/obj
**/test-results
**/*.user
**/*.suo
**/*.db
**/*.db-shm
**/*.db-wal
**/node_modules
README.md
LabResources/
credentials.md
```

> [!IMPORTANT]
> Why is ignoring `bin/` and `obj/` critical?
> If you have previously compiled your project on your macOS or Windows host, copying host `obj/` and `bin/` folders into a Linux container causes compiler conflicts and architecture mismatches. Ignoring them ensures a 100% clean, reproducible build inside the container.

---

### 4.3 Writing the `Dockerfile`

Create a file named `Dockerfile` in the repository root:

```dockerfile
# ==============================================================================
# Stage 1: Base Runtime Environment
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# ==============================================================================
# Stage 2: SDK Build & Restore
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Step 1: Copy only project file first to maximize Docker layer caching
COPY ["PgrStudentManagement.Web/PgrStudentManagement.Web.csproj", "PgrStudentManagement.Web/"]

# Step 2: Restore NuGet packages (this layer is cached unless .csproj changes)
RUN dotnet restore "PgrStudentManagement.Web/PgrStudentManagement.Web.csproj"

# Step 3: Copy the remaining source files
COPY . .

# Step 4: Build the application
WORKDIR "/src/PgrStudentManagement.Web"
RUN dotnet build "PgrStudentManagement.Web.csproj" -c Release -o /app/build

# ==============================================================================
# Stage 3: Publish Binaries
# ==============================================================================
FROM build AS publish
RUN dotnet publish "PgrStudentManagement.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==============================================================================
# Stage 4: Final Lightweight Production Image
# ==============================================================================
FROM base AS final
WORKDIR /app

# Copy published artifacts from Stage 3
COPY --from=publish /app/publish .

# Run as non-root user for container security
USER $APP_UID

ENTRYPOINT ["dotnet", "PgrStudentManagement.Web.dll"]
```

---

### 4.4 Building the Docker Image

Run the following command from the repository root:

```bash
docker build -t pgr-student-management:1.0 .
```

Verify the image exists in your local image cache:
```bash
docker images pgr-student-management
```

Notice the size: it is approximately **250 MB**, compared to over 1 GB if the SDK had been kept.

---

### 4.5 Running the Web Application Container

Now launch the web application container attached to `pgr-network`. Notice how we supply environment variables to point connection strings to `mysql-db`, `postgres-db`, and `mongo-db` instead of `localhost`:

```bash
docker run -d --name pgr-web --network pgr-network -p 5277:8080 -e ConnectionStrings__StudentManagementDatabase="Server=mysql-db;Port=3306;Database=student_management_db;User=root;Password=pass1234%uZ;" -e ConnectionStrings__IdentityDatabase="Host=postgres-db;Port=5432;Database=identity;Username=identity;Password=identity$" -e ConnectionStrings__MongoDb="mongodb://mongo-db:27017" -e ResearchActivityDatabase__DatabaseName="StudentManagement" -e ResearchActivityDatabase__CollectionName="ResearchActivities" pgr-student-management:1.0
```

Inspect the container logs to watch the application start and seed databases:
```bash
docker logs -f pgr-web
```

Open your browser to [http://localhost:5277](http://localhost:5277):
1. Navigate to **Students** (`/Students`) $\rightarrow$ verify data is fetched from the MySQL container.
2. Navigate to **Academic Supervisors** (`/AcademicSupervisors`) $\rightarrow$ verify it redirects to `/Account/Login`.
3. Sign in with `supervisor@pgr.ac.uk` / `Password123!` $\rightarrow$ verify authentication succeeds via the PostgreSQL container.
4. Navigate to a student's **Research Activity Log** (`/ResearchActivities?studentNumber=S100001`) $\rightarrow$ verify documents are loaded from the MongoDB container.

Clean up the manually launched containers before moving to Docker Compose:
```bash
docker rm -f pgr-web mysql-db postgres-db mongo-db
```
*(Don't worry: your data in `mysql_data`, `postgres_data`, and `mongo_data` remains safe!)*

---

## Part 5: Multi-Container Orchestration with Docker Compose

Running multiple `docker run` commands manually with long strings of flags is error-prone. **Docker Compose** lets you define the entire multi-service stack in a single, declarative YAML configuration file (`docker-compose.yml`).

### 5.1 Authoring `docker-compose.yml`

Create a file named `docker-compose.yml` in the repository root:

```yaml
services:
  # ============================================================================
  # 1. MySQL Domain Relational Database
  # ============================================================================
  mysql-db:
    image: mysql:9.0
    container_name: mysql-db
    restart: unless-stopped
    environment:
      MYSQL_ROOT_PASSWORD: pass1234%uZ
      MYSQL_DATABASE: student_management_db
    ports:
      - "3306:3306"
    volumes:
      - mysql_data:/var/lib/mysql
    healthcheck:
      test: ["CMD", "mysqladmin", "ping", "-h", "localhost", "-u", "root", "-ppass1234%uZ"]
      interval: 5s
      timeout: 5s
      retries: 10
      start_period: 15s
    networks:
      - pgr-network

  # ============================================================================
  # 2. PostgreSQL Identity & Authorization Database
  # ============================================================================
  postgres-db:
    image: postgres:16-alpine
    container_name: postgres-db
    restart: unless-stopped
    environment:
      POSTGRES_USER: identity
      POSTGRES_PASSWORD: identity$
      POSTGRES_DB: identity
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U identity -d identity"]
      interval: 5s
      timeout: 5s
      retries: 10
      start_period: 10s
    networks:
      - pgr-network

  # ============================================================================
  # 3. MongoDB Research Activities Document Database
  # ============================================================================
  mongo-db:
    image: mongo:5.0
    container_name: mongo-db
    restart: unless-stopped
    ports:
      - "27017:27017"
    volumes:
      - mongo_data:/data/db
    healthcheck:
      test: ["CMD", "mongo", "--eval", "db.adminCommand('ping')"]
      interval: 5s
      timeout: 5s
      retries: 10
      start_period: 10s
    networks:
      - pgr-network

  # ============================================================================
  # 4. ASP.NET Core Web Application
  # ============================================================================
  web:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: pgr-web
    restart: unless-stopped
    ports:
      - "5277:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__StudentManagementDatabase=Server=mysql-db;Port=3306;Database=student_management_db;User=root;Password=pass1234%uZ;
      - ConnectionStrings__IdentityDatabase=Host=postgres-db;Port=5432;Database=identity;Username=identity;Password=identity$
      - ConnectionStrings__MongoDb=mongodb://mongo-db:27017
      - ResearchActivityDatabase__DatabaseName=StudentManagement
      - ResearchActivityDatabase__CollectionName=ResearchActivities
    depends_on:
      mysql-db:
        condition: service_healthy
      postgres-db:
        condition: service_healthy
      mongo-db:
        condition: service_healthy
    networks:
      - pgr-network

# ==============================================================================
# Persistent Named Volumes
# ==============================================================================
volumes:
  mysql_data:
  postgres_data:
  mongo_data:

# ==============================================================================
# Networks
# ==============================================================================
networks:
  pgr-network:
    driver: bridge
```

> [!TIP]
> **Why are `healthcheck` and `condition: service_healthy` so important?**
> In Docker Compose, `depends_on: [mysql-db]` only waits until the MySQL container is *started*, not until MySQL is actually *ready to accept connections*. A database takes several seconds to initialise. Using health checks guarantees the ASP.NET Core web container will NOT start until MySQL, PostgreSQL, and MongoDB report that their listening sockets are healthy and ready!

---

### 5.2 Managing the Stack with Docker Compose

Here are the essential Docker Compose commands you will use daily:

```bash
# 1. Build images and start all 4 containers in the background (detached mode)
docker compose up -d --build

# 2. View running containers, their ports, and health status
docker compose ps

# 3. Follow logs across all services in real time
docker compose logs -f

# 4. Follow logs for only the web application
docker compose logs -f web

# 5. Execute commands inside a running service
docker compose exec mysql-db mysql -u root -p'pass1234%uZ' -e "SELECT * FROM student_management_db.Students;"

# 6. Stop all containers (data in volumes is preserved)
docker compose stop

# 7. Start stopped containers
docker compose start

# 8. Tear down containers and networks (data in volumes is still preserved)
docker compose down

# 9. Tear down containers, networks, AND remove volumes (full reset)
docker compose down -v
```

---

## Part 6: Verification Checklist

Execute the following verification tasks to validate your containerised solution:

| # | Task | Expected Result | Checked |
| :-: | :--- | :--- | :-: |
| 1 | `docker compose up -d` | All 4 services start; `docker compose ps` shows `(healthy)` | [ ] |
| 2 | Visit `http://localhost:5277` | Home Dashboard renders with HTTP 200 | [ ] |
| 3 | Visit `http://localhost:5277/Students` | Students list renders from `mysql-db` container | [ ] |
| 4 | Visit `http://localhost:5277/AcademicSupervisors` | Redirects to `/Account/Login` (secured route) | [ ] |
| 5 | Login with `supervisor@pgr.ac.uk` / `Password123!` | Authenticates via `postgres-db`, redirects to Supervisors | [ ] |
| 6 | Visit `http://localhost:5277/ResearchActivities?studentNumber=S100001` | Displays research reflections from `mongo-db` container | [ ] |
| 7 | Run `docker compose down && docker compose up -d` | Data persists (no data loss occurs) | [ ] |

---

## Summary & Key Takeaways

1. **Host Isolation**: By containerising MySQL, PostgreSQL, and MongoDB, you eliminated the need to install or maintain host database servers.
2. **Reproducibility**: Any student or developer on the Dev Portal VM or macOS can clone this repository and run `docker compose up -d` to get an identical, working environment in seconds.
3. **Multi-Stage Builds**: Multi-stage `Dockerfile` practices keep production images small (~250MB vs ~1GB), fast to download, and secure.
4. **Resilient Orchestration**: Health checks and bridge networking ensure reliable inter-service communication and database readiness.
5. **Persistence**: Docker named volumes ensure your relational and NoSQL databases survive container restarts and rebuilds.

---

## Next Steps: Bonus Lab — Scaling with Kubernetes & MiniKube

Now that your application is fully containerised and orchestrated with Docker Compose, you are ready to explore cloud-native container orchestration!

Proceed to the [**Bonus Lab: Scaling the PGR System with Kubernetes & MiniKube**](./bonus-lab-minikube-scaling.md) to learn how to:
- Install and configure **MiniKube** and `kubectl`.
- Convert Docker Compose services into Kubernetes **Pods**, **Deployments**, **Services**, and **PersistentVolumeClaims**.
- **Case Study**: Horizontally scale the stateless web tier to handle peak application submission surges during the annual PhD admissions intake window.

