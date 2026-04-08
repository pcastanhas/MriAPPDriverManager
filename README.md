# MriAPPDriverManager

Tools for monitoring and managing stale `MriAPPDriver.exe` processes in MRI Software environments.

---

## Solution Structure

```
MriAPPDriverManager.sln
├── MriAPPDriverShared/          # Shared library (models, DB access, logging, process helpers)
├── MriAPPDriverMonitor/         # Windows Service — auto-kills stale processes
└── MriAPPDriverManager/         # CLI tool — manual inspection and kill
```

---

## Prerequisites

- .NET 8.0 SDK
- Windows (Windows Service + Windows Event Log)
- SQL Server access to the MRI database
- Administrator rights (for service install and Event Log source registration)

---

## Configuration

Both projects have their own `appsettings.json`. Update the connection string in each:

```json
{
  "ConnectionStrings": {
    "MriDatabase": "Server=YOUR_SERVER;Database=YOUR_DATABASE;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

**Monitor-only settings** (`MriAPPDriverMonitor/appsettings.json`):

```json
{
  "MonitorSettings": {
    "PollingIntervalSeconds": 60,
    "ProcessAgeThresholdMinutes": 30
  }
}
```

---

## MriAPPDriverMonitor — Windows Service

### Build & Publish

```bash
dotnet publish MriAPPDriverMonitor -c Release -r win-x64 --self-contained false -o publish\monitor
```

### Install as a Windows Service

Run as Administrator:

```cmd
sc create MriAPPDriverMonitor binPath="C:\path\to\publish\monitor\MriAPPDriverMonitor.exe" start=auto
sc description MriAPPDriverMonitor "Monitors and kills stale MriAPPDriver.exe processes"
sc start MriAPPDriverMonitor
```

### Uninstall

```cmd
sc stop MriAPPDriverMonitor
sc delete MriAPPDriverMonitor
```

### What it does

- Polls every `PollingIntervalSeconds` (default: 60s)
- Kills any `MriAPPDriver.exe` running longer than `ProcessAgeThresholdMinutes` (default: 30 min)
- Looks up the process in `MRI_Server_Messages` to get: Session ID, User ID, Start Time, Report Name
- Writes a log entry to:
  - `log\mriappdriver-YYYY-MM-DD.log` (next to the executable)
  - Windows Event Log → Application → Source: `MriAPPDriverMonitor`

---

## MriAPPDriverManager — CLI Tool

### Build

```bash
dotnet build MriAPPDriverManager -c Release
```

### Usage

```
MriAPPDriverManager <action> [pid]

Actions:
  --running          List all running MriAPPDriver.exe processes with report info
  --info   <pid>     Show detailed info for a specific MriAPPDriver.exe process
  --kill   <pid>     Kill a specific MriAPPDriver.exe process and log the event

Examples:
  MriAPPDriverManager --running
  MriAPPDriverManager --info 12308
  MriAPPDriverManager --kill 12308
```

### Sample Output — `--running`

```
Found 2 running MriAPPDriver.exe process(es):
────────────────────────────────────────────────────────────────────────────────────────────────────
PID      Session ID     User ID          Start Time             Running      Report
────────────────────────────────────────────────────────────────────────────────────────────────────
12308    11554312       JSMITH           2026-04-07 08:12:45    1h 23m       Cash Receipts
8148     11554208       PAPAEFIE         2026-04-07 09:01:10    34m 22s      Batch Fund VII FV WC ...
────────────────────────────────────────────────────────────────────────────────────────────────────
```

### Logging

`--kill` writes to:
- `log\mriappdriver-YYYY-MM-DD.log` next to the executable
- Windows Event Log → Application → Source: `MriAPPDriverManager`

---

## Log File Format

```
2026-04-07 09:45:01 [KILLED] PID=12308  | Session=11554312     | User=JSMITH           | Started=2026-04-07 08:12:45 | Duration=01h 32m 16s | Report=Cash Receipts | KilledBy=Monitor Service
```

---

## Database Query

Both tools query `MRI_Server_Messages` using:

```sql
SELECT TOP 1
    CAST(Session_Id AS VARCHAR(50)) AS SessionId,
    UserId,
    ProcessStartTime                AS StartTime,
    Description                     AS ReportName,
    ComputerName
FROM MRI_Server_Messages
WHERE ServerInfo LIKE '%MriAPPDriverProcessId=<PID>%'
  AND Task IN ('REPORT', 'BATCHREPORT', 'PDFEXPORT')
ORDER BY Posted_Time DESC
```
