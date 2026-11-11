# Lab 1: Containerising the Multi-Service PGR System with Docker & Preparing for MiniKube

## Overview

The files in the gitlab repository build a multi-container asp.net core application.  Along with the application (that uses a Modular Monolith architecture) there are three databases, postgres, mysql and mongodb.  You will see from the application and database code that the application uses EF Core to persist data in both postgres and mysql, and uses the mongo driver to persist data in mongodb. 

- The ASP.NET Core web application ran on your host operating system (`localhost:5277`).
- Relational domain data was stored in a host **MySQL** database (`localhost:3306`).
- ASP.NET Core Identity authentication and authorization data was stored in a host **PostgreSQL** database (`localhost:5432`).
- Semi-structured research activities were stored in a host **MongoDB** document database (`localhost:27017`).

While running services directly on your host machine is convenient during early development, it introduces significant real-world challenges:
- **"It works on my machine" syndrome**: Differences in OS versions, database patches, system libraries, and local configurations.
- **Dependency bloat**: Installing and maintaining three separate database engines on your development workstation.
- **Port conflicts & environment pollution**: Running multiple projects that require different versions of MySQL or PostgreSQL.
- **No path to cloud deployment**: Modern production environments deploy applications as self-contained images into container orchestrators such as **Kubernetes**.

In this lab, you will **containerise** this entire multi-service application using **Docker** and **Docker Compose**, establishing the foundation for deploying into a local Kubernetes cluster with **MiniKube**.

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
1. **Install, configure, and verify Docker Desktop** on your development machine.
2. **Understand the Docker architecture**: images, containers, the daemon, registries, bridge networks, and volumes.
3. **Extract host-installed database engines into isolated Docker containers** (PostgreSQL, MySQL, and MongoDB) using `docker run` and explicit bridge networking.
4. **Author an optimized, secure multi-stage `Dockerfile`** for a modern ASP.NET Core web application (.NET 10).
5. **Implement `.dockerignore`** to optimize image build contexts and avoid host file leaks.
6. **Master Docker container networking and service discovery**: understand why `localhost` changes to container hostnames.
7. **Orchestrate the entire multi-service stack with Docker Compose (`docker-compose.yml`)**, incorporating container health checks, dependency management, and environment variable overrides.
8. **Verify persistent storage across container lifecycles** using Docker named volumes.
9. **Understand the transition from Docker Compose to Kubernetes & MiniKube** (Pods, Deployments, Services, ConfigMaps, and PVCs).

---

## Part 1: Setting Up & Verifying Docker Desktop

### 1.1 What is Docker Desktop?

**Docker Desktop** is an integrated application providing the Docker Engine (daemon), Docker CLI client, Docker Compose, Docker Scout, container credential management, and an optional single-node Kubernetes cluster.

### 1.2 Installation Instructions

#### macOS:
1. Download the installer from [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop/):
   - Choose **Mac with Apple Silicon** if using an M1/M2/M3/M4 chip.
   - Choose **Mac with Intel chip** if using an older Intel-based Mac.
2. Double-click `Docker.dmg`, drag the **Docker** icon to your **Applications** folder, and launch Docker from Applications.
3. In the setup wizard, choose **Use recommended settings** and grant privileged helper permissions when prompted.

#### Windows:
1. Ensure **Virtual Machine Platform** and **WSL 2** (Windows Subsystem for Linux 2) are enabled:
   ```powershell
   wsl --install
   wsl --update
   ```
2. Download and run the **Docker Desktop for Windows** installer.
3. Ensure the option **Use WSL 2 instead of Hyper-V (recommended)** is checked.
4. Restart your computer if prompted.

#### Linux:
1. Follow the official distribution guide for [Docker Engine](https://docs.docker.com/engine/install/) and the [Docker Compose plugin](https://docs.docker.com/compose/install/linux/).
2. Add your user to the `docker` group:
   ```bash
   sudo usermod -aG docker $USER
   ```

### 1.3 Recommended Docker Desktop Settings

Open Docker Desktop, navigate to **Settings (Gear Icon)**:
- **General**: Ensure *Start Docker Desktop when you log in* is checked (optional).
- **Resources**:
  - **CPUs**: Allocate at least **2 to 4 vCPUs**.
  - **Memory**: Allocate at least **4 GB RAM** (our stack runs ASP.NET Core, MySQL, PostgreSQL, and MongoDB concurrently).
  - **Virtual disk limit**: Ensure at least **20 GB** is free.
- Click **Apply & restart**.

### 1.4 Verifying Your Docker Environment

Open your terminal and run the following commands to confirm your installation:

```bash
# 1. Check Docker CLI and Daemon versions
docker version

# 2. Check Docker Compose version (V2 compose plugin)
docker compose version

# 3. Inspect system info
docker info

# 4. Run the canonical test image
docker run --rm hello-world
```

> [!NOTE]
> What did `docker run --rm hello-world` do behind the scenes?
> 1. The **Docker client** contacted the **Docker daemon**.
> 2. The daemon checked its local image cache; because `hello-world:latest` was not found locally, it pulled the image from **Docker Hub**.
> 3. The daemon created a new container from that image and executed its default executable (`/hello`).
> 4. The container printed the welcome message to standard output, which was streamed back to your terminal.
> 5. Because the `--rm` flag was supplied, the container was automatically deleted when it finished running.

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

### 2.2 The Three Fundamental Container Concepts You Must Know

Before writing commands, understand these three critical container primitives:

#### 1. The `localhost` Trap (Container Isolation)
Inside a container, `localhost` (or `127.0.0.1`) refers **only to that specific container itself**, NOT to your Mac or Windows host machine, and NOT to other containers!
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
> **Port Conflicts with Host Databases:**
> If you currently have local instances of MySQL (`3306`), PostgreSQL (`5432`), or MongoDB (`27017`) running on your host machine, they will occupy those ports.
> - On macOS, stop them before proceeding:
>   - Stop Postgres.app or run `brew services stop postgresql@16`.
>   - Stop MySQL: `brew services stop mysql` or use the MySQL Preference Pane.
>   - Stop MongoDB: `brew services stop mongodb-community`.
> - Alternatively, in Docker you can map container ports to custom host ports (e.g. `-p 3307:3306`), but using the standard ports makes transitioning to Docker Compose seamless.

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
   docker run -d \
     --name postgres-db \
     --network pgr-network \
     -p 5432:5432 \
     -e POSTGRES_USER=identity \
     -e POSTGRES_PASSWORD='identity$' \
     -e POSTGRES_DB=identity \
     -v postgres_data:/var/lib/postgresql/data \
     --restart unless-stopped \
     postgres:16-alpine
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
   docker run -d \
     --name mysql-db \
     --network pgr-network \
     -p 3306:3306 \
     -e MYSQL_ROOT_PASSWORD='pass1234%uZ' \
     -e MYSQL_DATABASE=student_management_db \
     -v mysql_data:/var/lib/mysql \
     --restart unless-stopped \
     mysql:9.0
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
   docker run -d \
     --name mongo-db \
     --network pgr-network \
     -p 27017:27017 \
     -v mongo_data:/data/db \
     --restart unless-stopped \
     mongo:5.0
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
docker run -d \
  --name pgr-web \
  --network pgr-network \
  -p 5277:8080 \
  -e ConnectionStrings__StudentManagementDatabase="Server=mysql-db;Port=3306;Database=student_management_db;User=root;Password=pass1234%uZ;" \
  -e ConnectionStrings__IdentityDatabase="Host=postgres-db;Port=5432;Database=identity;Username=identity;Password=identity$" \
  -e ConnectionStrings__MongoDb="mongodb://mongo-db:27017" \
  -e ResearchActivityDatabase__DatabaseName="StudentManagement" \
  -e ResearchActivityDatabase__CollectionName="ResearchActivities" \
  pgr-student-management:1.0
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

## Part 7: Looking Ahead to MiniKube & Kubernetes

Now that your application is fully containerised, you are prepared for the next evolution: **Kubernetes orchestration with MiniKube**.

### 7.1 How Docker Compose Maps to Kubernetes

When you move from Docker Compose to Kubernetes, the concepts map directly:

| Docker Compose Concept | Kubernetes Concept | Purpose |
| :--- | :--- | :--- |
| `container` | **Pod** | Smallest deployable unit of computing in Kubernetes (one or more containers). |
| `service` (web, mysql) | **Deployment** | Manages replica sets, declarative updates, health restarts, and rollbacks. |
| `ports: "5277:8080"` | **Service (ClusterIP / NodePort / LoadBalancer)** | Provides stable networking and DNS endpoints inside the cluster. |
| `environment:` | **ConfigMap** & **Secret** | Decouples configuration (URLs) and sensitive credentials (passwords) from images. |
| `volumes:` | **PersistentVolumeClaim (PVC)** | Requests durable, cluster-managed storage volumes for stateful databases. |

### 7.2 Initializing MiniKube Preview

In the upcoming lab session, you will use **MiniKube** to run a local single-node Kubernetes cluster.

To prepare your environment:

1. **Install MiniKube and `kubectl`:**
   - **macOS**: `brew install minikube kubectl`
   - **Windows**: `winget install Kubernetes.minikube` & `winget install Kubernetes.kubectl`
   - **Linux**: See [minikube.sigs.k8s.io/docs/start](https://minikube.sigs.k8s.io/docs/start/)

2. **Start the MiniKube cluster using the Docker driver:**
   ```bash
   minikube start --driver=docker --cpus=4 --memory=6144
   ```

3. **Verify cluster connectivity:**
   ```bash
   kubectl cluster-info
   kubectl get nodes
   ```

4. **Point your local Docker CLI to MiniKube's internal Docker daemon:**
   ```bash
   # On macOS / Linux:
   eval $(minikube docker-env)

   # On Windows PowerShell:
   & minikube -p minikube docker-env --shell powershell | Invoke-Expression
   ```
   *This powerful command allows you to build images directly inside MiniKube without needing to push to a remote registry like Docker Hub!*

---

## Summary & Key Takeaways

1. **Host Isolation**: By containerising MySQL, PostgreSQL, and MongoDB, you eliminated the need to maintain host database servers.
2. **Reproducibility**: Any student or developer on macOS, Windows, or Linux can clone this repository and run `docker compose up -d` to get an identical, working environment in seconds.
3. **Multi-Stage Builds**: Multi-stage `Dockerfile` practices keep production images small, fast, and secure.
4. **Resilient Orchestration**: Health checks and bridge networking ensure reliable inter-service communication.
5. **Kubernetes-Ready**: Your application is now packaged as cloud-native container images ready for deployment into Kubernetes via MiniKube.

