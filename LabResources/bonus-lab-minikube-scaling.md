# Bonus Lab: Scaling the PGR System with Kubernetes & MiniKube

## Overview

In [Lab: Containerising the Multi-Service PGR System with Docker](./lab-instructions.md), you packaged the **PGR Student Management Portal** and its three databases (**MySQL**, **PostgreSQL**, and **MongoDB**) into Docker containers and orchestrated them using **Docker Compose**.

Docker Compose is great for local development, testing, and single-host deployments. However, in enterprise production environments, modern cloud-native systems require **container orchestration**:
- **Automatic horizontal scaling** when user demand surges.
- **Self-healing and auto-restart** if a container fails or runs out of memory.
- **Zero-downtime rolling updates** when deploying new software versions.
- **Service discovery and intelligent traffic load-balancing** across pools of redundant container instances.

In this bonus lab, you will deploy the containerised PGR system into a local **Kubernetes** cluster running on **MiniKube**, and walk through a real-world case study: **horizontally scaling the stateless web tier to handle peak admissions traffic**.

---

## The Case Study: The Annual PhD Admissions Deadline Surge

### The Scenario
Every year in October and January, the Graduate Research School opens its application intake window for prospective PhD candidates applying for funded doctoral studentships.

In the final **72 hours before the scholarship deadline**:
- Thousands of prospective students worldwide log in simultaneously to draft proposals, submit applications, upload research abstracts, and check status (`/PhdApplications/Create` and `/PhdApplications/Details`).
- Academic supervisors log in concurrently to review submitted applications and verify eligibility (`/AcademicSupervisors`).

### The Bottleneck in Docker Compose
Under our baseline Docker Compose setup, we run exactly **one container instance** of `pgr-web`. Under a 10x traffic surge:
1. The single container's CPU and thread pool become saturated.
2. Incoming HTTP requests are queued in the operating system backlog.
3. Users experience slow response times, 504 Gateway Timeouts, or dropped connections.

### The Architectural Solution: Horizontal Pod Scaling in Kubernetes
Notice an essential characteristic of our architecture: **the ASP.NET Core web application is completely stateless**:
- **Domain data** is persisted in **MySQL**.
- **Authentication & session claims** are verified against **PostgreSQL**.
- **Research logs** are stored in **MongoDB**.

Because the web application stores no session state in local container memory or files, we can run **multiple identical instances** of the web container side by side!

In Kubernetes:
1. We define a **Deployment** that manages a pool of identical application instances called **Pods**.
2. We define a Kubernetes **Service** that provides a single, stable IP address and automatically distributes incoming HTTP traffic across all healthy Pods using round-robin load balancing.
3. During the deadline rush, we scale from **1 Pod $\rightarrow$ 5 Pods** in seconds. Once the deadline passes, we scale back down to conserve compute resources.

```
                                  KUBERNETES CLUSTER (MiniKube)
                                  
                               +----------------------------------+
                               |     pgr-web-service (Service)    |
                               |    Load Balancer / NodePort      |
                               +-----------------+----------------+
                                                 |
                   +-----------------------------+-----------------------------+
                   |                             |                             |
                   v                             v                             v
       +-----------------------+     +-----------------------+     +-----------------------+
       |     pgr-web Pod 1     |     |     pgr-web Pod 2     |     |     pgr-web Pod 3     |
       |  (pgr-student-mgmt)   |     |  (pgr-student-mgmt)   |     |  (pgr-student-mgmt)   |
       +-----------+-----------+     +-----------+-----------+     +-----------+-----------+
                   |                             |                             |
                   +----------------------+      |      +----------------------+
                                          |      |      |
                                          v      v      v
                                   [ Internal Cluster Network ]
                                          |      |      |
                   +----------------------+      |      +----------------------+
                   |                             |                             |
                   v                             v                             v
       +-----------------------+     +-----------------------+     +-----------------------+
       |      mysql-db Pod     |     |    postgres-db Pod    |     |      mongo-db Pod     |
       |   (MySQL 9.0 + PVC)   |     |  (PostgreSQL 16 + PVC)|     |   (MongoDB 5.0 + PVC) |
       +-----------------------+     +-----------------------+     +-----------------------+
```

---

## Part 1: Prerequisites & Installing MiniKube and `kubectl`

### 1.1 What are MiniKube and `kubectl`?
- **kubectl**: The official command-line tool used to communicate with the Kubernetes API server and control the cluster.
- **MiniKube**: An official Kubernetes tool that sets up an optimized, single-node Kubernetes cluster inside a local container or virtual machine on your computer.

### 1.2 Installation & PATH Setup on the Dev Portal VM (Windows AMD64)

On the Microsoft Dev Portal VM, your user directory is `C:\Users\student` (`$HOME`). When installed or downloaded, `minikube.exe` and `kubectl.exe` reside in `C:\Users\student`. Because Windows does not look in user home folders by default, you must add `C:\Users\student` to your Windows PATH:

1. **Install or Download MiniKube and `kubectl`:**
   In PowerShell, run:
   ```powershell
   winget install -e --id Kubernetes.minikube
   winget install -e --id Kubernetes.kubectl
   ```
   *(Or if downloaded directly into `C:\Users\student`, ensure `minikube.exe` and `kubectl.exe` are present).*

2. **Add `C:\Users\student` (`$HOME`) to Your PATH:**
   In PowerShell, run these two commands to register `C:\Users\student` for the current session and permanently for future sessions:
   ```powershell
   $env:Path += ";$HOME"
   [Environment]::SetEnvironmentVariable("Path", [Environment]::GetEnvironmentVariable("Path", "User") + ";$HOME", "User")
   ```

> [!TIP]
> **Why is this necessary?**
> Windows only recognizes commands without their full file path (like `minikube` instead of `C:\Users\student\minikube.exe`) if the folder containing the `.exe` is listed in your system or user `PATH` environment variable. The command above permanently registers `C:\Users\student` so you can run `minikube` and `kubectl` from anywhere in your project workspace!

> [!IMPORTANT]
> **Always Use Native PowerShell Directly (Do NOT Use the VS Code Terminal):**
> Execute all commands in a standalone **PowerShell** window (or **Windows Terminal**) opened from the Start Menu, **not** inside VS Code’s integrated terminal.
> - **Why?** If VS Code is open while you run `winget` or update `$env:Path`, opening a new terminal tab inside VS Code **will not inherit the updated PATH**. VS Code caches the environment of the parent process, causing `minikube` or `kubectl` to report `'command not recognized'`.
> - **Profile Confusion:** VS Code often defaults new terminal tabs to `cmd.exe` (Command Prompt) or `Git Bash`, where PowerShell syntax (such as `$env:Path += ";$HOME"`) is invalid.
> - **Recommended Workflow:** Keep **VS Code** open for editing Kubernetes YAML manifests and source code, and use a dedicated native **PowerShell** window at the repository root (`container-lab-start`) to run all cluster commands!

---

#### On Personal Mac (macOS — Alternative):
Open **Terminal** and run:
```bash
brew install minikube kubectl
```

---

### 1.3 Verify Installation
In your terminal, confirm both CLI tools are accessible:
```bash
minikube version
kubectl version --client
```

Both commands should now output their respective version strings (e.g. `minikube version: v1.35.x` and `Client Version: v1.32.x`).

---

## Part 2: Starting Your MiniKube Cluster

Make sure **Docker Desktop** is running (green *"Engine running"* icon). Then start MiniKube using Docker as the container driver:

```bash
minikube start --driver=docker --cpus=4 --memory=6144
```

MiniKube will pull its Kubernetes node image, create a container named `minikube`, and configure the Kubernetes control plane.

Verify cluster health and node status:
```bash
kubectl cluster-info
kubectl get nodes
```
You should see:
```text
NAME       STATUS   ROLES           AGE   VERSION
minikube   Ready    control-plane   1m    v1.32.x
```

---

## Part 3: Loading Your Image into MiniKube (`minikube image load`)

In production, Kubernetes pulls images from cloud registries (like Docker Hub or Azure Container Registry). In local development, pushing a 250MB image across the internet just to pull it back down to your own machine is slow and requires account credentials.

Instead of needing remote registries or complex shell redirection, MiniKube provides a dedicated, native command to transfer images directly from Docker Desktop into MiniKube: **`minikube image load`**.

### 3.1 Build Your Image with Docker Desktop
First, make sure your application image is built locally using standard Docker Desktop (run this from your repository root):

```bash
docker build -t pgr-student-management:1.0 .
```

### 3.2 Load the Image Directly into MiniKube
Now, transfer the image into MiniKube with a single command:

```bash
minikube image load pgr-student-management:1.0
```

### 3.3 Verify the Image is in MiniKube
Confirm that MiniKube has received the image:

```bash
minikube image ls --format table
```
*(On Windows PowerShell, you can also filter with: `minikube image ls | Select-String pgr`).*

You should see `docker.io/library/pgr-student-management:1.0` in the output!

> [!TIP]
> Notice the flag `imagePullPolicy: IfNotPresent` in our Kubernetes manifests (`k8s/pgr-web.yaml`). This tells Kubernetes: *"Since `pgr-student-management:1.0` was loaded into MiniKube's local cache, use it immediately without attempting to download from Docker Hub!"*

---

## Part 4: Authoring the Kubernetes Manifests

Create a folder named `k8s` in your repository root:
```bash
mkdir k8s
```

We will create two manifest files:
1. `k8s/pgr-databases.yaml`: Provisions persistent storage, Deployments, and internal ClusterIP Services for MySQL, PostgreSQL, and MongoDB.
2. `k8s/pgr-web.yaml`: Provisions the ASP.NET Core web Deployment and external Service.

### 4.1 Authoring `k8s/pgr-databases.yaml`

Create `k8s/pgr-databases.yaml` and add the database definitions:

```yaml
# ==============================================================================
# Persistent Volume Claims (Durable Storage)
# ==============================================================================
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: mysql-pvc
spec:
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 1Gi
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: postgres-pvc
spec:
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 1Gi
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: mongo-pvc
spec:
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 1Gi

# ==============================================================================
# 1. MySQL Domain Relational Database
# ==============================================================================
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mysql-db
  labels:
    app: mysql-db
spec:
  replicas: 1
  selector:
    matchLabels:
      app: mysql-db
  template:
    metadata:
      labels:
        app: mysql-db
    spec:
      containers:
        - name: mysql
          image: mysql:9.0
          env:
            - name: MYSQL_ROOT_PASSWORD
              value: "pass1234%uZ"
            - name: MYSQL_DATABASE
              value: "student_management_db"
          ports:
            - containerPort: 3306
          volumeMounts:
            - name: mysql-storage
              mountPath: /var/lib/mysql
      volumes:
        - name: mysql-storage
          persistentVolumeClaim:
            claimName: mysql-pvc
---
apiVersion: v1
kind: Service
metadata:
  name: mysql-service
spec:
  selector:
    app: mysql-db
  ports:
    - port: 3306
      targetPort: 3306

# ==============================================================================
# 2. PostgreSQL Identity Database
# ==============================================================================
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: postgres-db
  labels:
    app: postgres-db
spec:
  replicas: 1
  selector:
    matchLabels:
      app: postgres-db
  template:
    metadata:
      labels:
        app: postgres-db
    spec:
      containers:
        - name: postgres
          image: postgres:16-alpine
          env:
            - name: POSTGRES_USER
              value: "identity"
            - name: POSTGRES_PASSWORD
              value: "identity$"
            - name: POSTGRES_DB
              value: "identity"
          ports:
            - containerPort: 5432
          volumeMounts:
            - name: postgres-storage
              mountPath: /var/lib/postgresql/data
      volumes:
        - name: postgres-storage
          persistentVolumeClaim:
            claimName: postgres-pvc
---
apiVersion: v1
kind: Service
metadata:
  name: postgres-service
spec:
  selector:
    app: postgres-db
  ports:
    - port: 5432
      targetPort: 5432

# ==============================================================================
# 3. MongoDB Research Activities Document Database
# ==============================================================================
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mongo-db
  labels:
    app: mongo-db
spec:
  replicas: 1
  selector:
    matchLabels:
      app: mongo-db
  template:
    metadata:
      labels:
        app: mongo-db
    spec:
      containers:
        - name: mongo
          image: mongo:5.0
          ports:
            - containerPort: 27017
          volumeMounts:
            - name: mongo-storage
              mountPath: /data/db
      volumes:
        - name: mongo-storage
          persistentVolumeClaim:
            claimName: mongo-pvc
---
apiVersion: v1
kind: Service
metadata:
  name: mongo-service
spec:
  selector:
    app: mongo-db
  ports:
    - port: 27017
      targetPort: 27017
```

---

### 4.2 Authoring `k8s/pgr-web.yaml`

Create `k8s/pgr-web.yaml`. Notice how the connection strings point to Kubernetes internal DNS names (`mysql-service`, `postgres-service`, `mongo-service`), and `replicas` is initially set to `1`:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: pgr-web
  labels:
    app: pgr-web
spec:
  replicas: 1
  selector:
    matchLabels:
      app: pgr-web
  template:
    metadata:
      labels:
        app: pgr-web
    spec:
      containers:
        - name: web
          image: pgr-student-management:1.0
          imagePullPolicy: IfNotPresent
          ports:
            - containerPort: 8080
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: "Development"
            - name: ConnectionStrings__StudentManagementDatabase
              value: "Server=mysql-service;Port=3306;Database=student_management_db;User=root;Password=pass1234%uZ;"
            - name: ConnectionStrings__IdentityDatabase
              value: "Host=postgres-service;Port=5432;Database=identity;Username=identity;Password=identity$"
            - name: ConnectionStrings__MongoDb
              value: "mongodb://mongo-service:27017"
            - name: ResearchActivityDatabase__DatabaseName
              value: "StudentManagement"
            - name: ResearchActivityDatabase__CollectionName
              value: "ResearchActivities"
          resources:
            requests:
              memory: "128Mi"
              cpu: "100m"
            limits:
              memory: "512Mi"
              cpu: "500m"
---
apiVersion: v1
kind: Service
metadata:
  name: pgr-web-service
spec:
  type: NodePort
  selector:
    app: pgr-web
  ports:
    - port: 8080
      targetPort: 8080
      nodePort: 30080
```

---

## Part 5: Deploying and Testing the Baseline Stack

### 5.1 Apply the Manifests to Kubernetes
Execute:
```bash
kubectl apply -f k8s/
```

### 5.2 Monitor Pod Provisioning
Watch the pods as Kubernetes starts containers and mounts storage:
```bash
kubectl get pods -w
```
*(Press `Ctrl+C` once all 4 pods display `STATUS: Running` and `READY: 1/1`).*

### 5.3 Access the Web Application
Because MiniKube runs inside Docker, use MiniKube's service tunnel to open the application in your default browser:
```bash
minikube service pgr-web-service
```
This opens the running application in your browser and displays the tunnel URL (e.g. `http://127.0.0.1:58432`). Verify you can navigate to **Students**, **Academic Supervisors** (login), and **Research Activities**.

---

## Part 6: Hands-On Case Study: Scaling the Web Tier

Now that the system is operational on Kubernetes, let's execute the **PhD Admissions Surge Case Study**.

### Phase 1: Inspect the Baseline (Single Pod)
Check the active pods for the web application:
```bash
kubectl get pods -l app=pgr-web
```
Output:
```text
NAME                       READY   STATUS    RESTARTS   AGE
pgr-web-7bc944cb9d-k8x7m   1/1     Running   0          3m
```
Exactly one Pod is servicing all user requests.

---

### Phase 2: The Admission Surge — Scaling from 1 to 5 Replicas
The admissions deadline is approaching! Scale the deployment horizontally to 5 replicas:

```bash
kubectl scale deployment pgr-web --replicas=5
```

Watch the ReplicaSet spin up four brand-new worker Pods in real time:
```bash
kubectl get pods -l app=pgr-web -w
```
Within **3 to 5 seconds**, all 5 Pods are initialized, connected to the backend databases, and reported as `Running`:

```text
NAME                       READY   STATUS    RESTARTS   AGE
pgr-web-7bc944cb9d-k8x7m   1/1     Running   0          5m
pgr-web-7bc944cb9d-4fl9a   1/1     Running   0          4s
pgr-web-7bc944cb9d-8x2b1   1/1     Running   0          4s
pgr-web-7bc944cb9d-m3q09   1/1     Running   0          4s
pgr-web-7bc944cb9d-pz89k   1/1     Running   0          4s
```

---

### Phase 3: Verifying Intelligent Traffic Distribution (EndpointSlice Discovery)

How does Kubernetes know where to send incoming user traffic?

Kubernetes uses the **EndpointSlice API** (`discovery.k8s.io`) for scalable service discovery. When you scaled the deployment to 5 replicas, Kubernetes automatically discovered all 5 Pods and registered their internal network endpoints into an `EndpointSlice`.

Inspect the discovery endpoint slices associated with `pgr-web-service`:
```bash
kubectl get endpointslice -l kubernetes.io/service-name=pgr-web-service
```
Output:
```text
NAME                    ADDRESSTYPE   PORTS   ENDPOINTS                               AGE
pgr-web-service-7xk9p   IPv4          8080    10.244.0.12,10.244.0.13,10.244.0.14...   6m
```

To view the detailed discovery metadata showing each Pod's private IP, port, and readiness status:
```bash
kubectl describe endpointslice -l kubernetes.io/service-name=pgr-web-service
```
You will see all 5 Pod IPs registered with `Ready: true`. When HTTP requests arrive at `pgr-web-service`, the cluster network proxy (kube-proxy) distributes traffic evenly across all 5 Pods via round-robin load balancing.

Follow logs across all 5 web Pods simultaneously:
```bash
kubectl logs -l app=pgr-web --tail=5 -f
```
Refresh your browser or navigate between `/Students` and `/PhdApplications`. You will see log messages generated by different Pod instances handling your requests!

---

### Phase 4: Simulating Failure & Self-Healing Resilience
What happens if one of your web container instances crashes under heavy load?

Delete one of the running web Pods to simulate an unhandled process crash or out-of-memory failure:
```bash
# Copy one of your web pod names from 'kubectl get pods'
kubectl delete pod <pgr-web-pod-name>
```

Immediately query the pods:
```bash
kubectl get pods -l app=pgr-web
```
Notice what happened:
1. The crashed Pod entered `Terminating`.
2. The Kubernetes **Deployment Controller** immediately observed: *"Desired replicas: 5; Actual healthy replicas: 4"*.
3. Kubernetes instantly spawned a new replacement Pod to maintain the desired capacity of 5. **Zero downtime occurred!**

---

### Phase 5: Post-Deadline Scale-Down
The 31 January admissions deadline has passed. User traffic returns to normal baseline levels. To conserve compute resources, scale the web deployment back down to 2 replicas:

```bash
kubectl scale deployment pgr-web --replicas=2
```

Inspect the pods:
```bash
kubectl get pods -l app=pgr-web
```
Kubernetes gracefully terminates 3 of the Pods, leaving 2 healthy replicas running.

---

## Part 7: Clean-Up

When you have finished the lab:

1. **Delete all Kubernetes resources in the cluster:**
   ```bash
   kubectl delete -f k8s/
   ```

2. **Stop the MiniKube cluster:**
   ```bash
   minikube stop
   ```

Because we used `minikube image load` instead of overriding terminal environment variables, your PowerShell/Terminal session remains cleanly attached to Docker Desktop with zero clean-up required!

---

## Summary & Key Takeaways

1. **Stateless vs. Stateful Tiers**: By separating our stateless web controllers from our stateful database persistence layers, our application can scale horizontally across multiple container instances with zero data corruption.
2. **Kubernetes Deployments**: Manage pools of identical Pods and maintain the desired replica count through self-healing controllers.
3. **Kubernetes Services**: Decouple service consumers from ephemeral Pod IPs by providing stable DNS names (`mysql-service`, `pgr-web-service`) and round-robin load balancing.
4. **On-Demand Scalability**: Single CLI commands (`kubectl scale`) allow engineering teams to respond to real-world business spikes (such as admissions deadlines or exam results days) in seconds.

