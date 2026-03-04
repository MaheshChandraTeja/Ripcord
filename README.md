# Ripcord

![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D4)
![Runtime](https://img.shields.io/badge/.NET-8.0-512BD4)
![UI](https://img.shields.io/badge/UI-WinUI%203-0A5A9C)
![Mode](https://img.shields.io/badge/mode-Local%20First-2E8B57)

## Secure, Verifiable Data Erasure for Windows

Ripcord is a secure data‑destruction platform for Windows designed for environments where deletion must be **deterministic, auditable, and cryptographically verifiable**.

It provides controlled workflows for:

• secure file shredding  
• shadow copy removal  
• free‑space sanitization  
• cryptographic receipt generation  

Ripcord supports both **operator‑friendly UI workflows** and **automation‑ready CLI operations**, making it suitable for enterprise IT operations, compliance environments, and security‑sensitive systems.

---

# Safety Notice

Ripcord performs **irreversible operations** including:

- multi‑pass overwrite
- file destruction
- shadow copy removal
- free‑space wiping

Always test commands using:

```
--dry-run
```

before executing in production environments.

---

# Why Ripcord

Most deletion tools optimize for convenience or raw low‑level control.  
Ripcord is designed for **controlled destruction with verifiable evidence.**

Core design goals:

- deterministic erase workflows
- cryptographic receipts and attestations
- optional elevation isolation through broker execution
- local‑first architecture with zero cloud dependency
- CLI automation and GUI workflows

---

# Core Capabilities

## File and Directory Shredding

Secure deletion workflows with configurable overwrite profiles.

Features include:

- multi‑pass overwrite strategies
- recursive directory shredding
- NTFS Alternate Data Stream cleanup
- filename randomization to reduce residual metadata
- optional post‑overwrite deletion
- structured journaling and operation receipts

---

## Shadow Copy (VSS) Operations

Manage Windows Volume Shadow Copies safely.

Capabilities:

- enumerate snapshots by volume
- purge by age or client policy
- delete individual snapshot IDs
- dry‑run planning for safe execution

---

## Free Space Wiping

Sanitize unused storage areas on a volume.

Features include:

- filesystem capability detection
- controlled free‑space filling
- reserved disk protection
- optional TRIM support through native helpers
- automatic artifact cleanup

---

## Receipt and Attestation System

Ripcord generates **cryptographically verifiable evidence of destruction.**

Artifacts include:

- canonical JSON receipts
- SHA‑256 integrity hash binding
- optional RSA / X.509 signatures
- Merkle root verification
- exportable audit reports (HTML or PDF)

---

# Operation Modes

Ripcord supports three execution environments.

**Ripcord.App**  
WinUI desktop interface for operator workflows

**Ripcord.CLI**  
command‑line interface for automation and scripting

**Ripcord.Broker**  
optional elevated broker service enabling privilege‑isolated operations

---

# Architecture

```
Ripcord
  WinUI App (Ripcord.App)
  CLI (Ripcord.CLI)
        |
        v
  Broker (Ripcord.Broker)
        |
        v
  Engine (Ripcord.Engine)
     - Shred
     - VSS Operations
     - Free Space / TRIM
     - Validation + Journaling
        |
        v
  Receipts (Ripcord.Receipts)
     - Canonical JSON
     - Attestation
     - Verification
     - Export
```

---

# Repository Layout

```
src/
  Ripcord.App/             WinUI desktop interface
  Ripcord.Broker/          Elevated broker service
  Ripcord.CLI/             Command-line interface
  Ripcord.Engine/          Core erase engine
  Ripcord.Engine.Native/   Native helpers
  Ripcord.Infrastructure/  Config and logging
  Ripcord.Receipts/        Receipt and verification system

tests/
  Engine.Tests/
  E2E.Tests/
  Perf.Benchmarks/

docs/
  architecture-overview.md
  receipts-spec.md
  admin-guide.md
```

---

# Built‑In Erase Profiles

| Profile | Description |
|-------|-------------|
| zero | single pass zero fill |
| nist-clear | NIST 800‑88 clear |
| dod-5220-3pass | DoD 5220.22‑M |
| schneier-7 | Schneier seven pass |

---

# Platform Requirements

- Windows 10 (20H2+) or Windows 11
- .NET SDK 8.0
- Windows SDK and WinAppSDK
- Visual C++ toolchain (optional)

---

# Build

Restore and build the solution.

```
dotnet restore Ripcord.sln
dotnet build Ripcord.sln -c Debug
```

---

# Run the Desktop App

```
dotnet run --project src/Ripcord.App/Ripcord.App.csproj -c Debug -p:Platform=x64
```

---

# Run the Broker

```
dotnet run --project src/Ripcord.Broker/Ripcord.Broker.csproj -c Debug -p:Platform=x64
```

---

# Run CLI

```
dotnet run --project src/Ripcord.CLI/Ripcord.CLI.csproj -- --help
```

---

# CLI Examples

### Shred directory

```
ripcord shred "C:\Sensitive" --recurse --delete-after
```

### Dry‑run preview

```
ripcord shred "C:\Sensitive\file.docx" --dry-run
```

### List shadow copies

```
ripcord shadowpurge --list --volume "C:\"
```

### Purge snapshots older than 30 days

```
ripcord shadowpurge --volume "C:\" --older-than-days 30
```

### Free‑space wipe

```
ripcord freespace "C:\" --reserve-mb 2048
```

### Verify receipts

```
ripcord receipts --verify
```

---

# Security Model

## Broker Isolation

The broker process enforces security controls including:

- user‑scoped named pipes
- same‑user authorization
- command allow‑lists
- path validation
- UNC path denial

---

# Logging and Audit Evidence

Default paths:

```
Receipts
C:\ProgramData\Ripcord\Receipts

Logs
C:\ProgramData\Ripcord\Logs
```

Receipts contain:

- canonical JSON structure
- SHA‑256 integrity hash
- optional certificate signatures

---

# Testing

Run unit tests:

```
dotnet test tests/Engine.Tests
```

Run integration tests:

```
dotnet test tests/E2E.Tests
```

Run performance benchmarks:

```
dotnet run --project tests/Perf.Benchmarks -c Release
```

---

# Current Scope

Ripcord currently focuses on **Windows secure data destruction workflows**.

Limitations:

- Windows‑only implementation
- some VSS operations require elevation
- storage‑dependent TRIM behavior

---

# Documentation

```
docs/
architecture-overview.md
receipts-spec.md
admin-guide.md
```

---

## 🏢 About

**Built by**  
**Mahesh Chandra Teja Garnepudi**  
**Sagarika Srivastava**

**Organization**  
**Kairais Tech**  
https://www.kairais.com

---

## 📄 License

Vyre is proprietary software.  
Third‑party dependencies are licensed under their respective terms and used in compliance.
