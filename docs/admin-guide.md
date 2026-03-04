# Ripcord — Admin Guide

This guide covers deployment, configuration, and operational tasks for enterprise admins.

## 1) Components

- **Ripcord.App** — WinUI desktop app (user-facing).
- **Ripcord.Engine** — core overwrite/shred, VSS, SSD/TRIM.
- **Ripcord.Broker** — elevated broker, JSON over named pipes (optional but recommended).
- **Ripcord.CLI** — headless operations and CI.
- **Ripcord.Receipts** — receipt models, export, verification.
- **Ripcord.Infrastructure** — logging (Serilog), DI config.

## 2) Supported OS

- Windows 10 20H2+ / Windows 11 (x64, ARM64). Run as standard user; Broker prompts for elevation when needed.

## 3) Install

### Winget (recommended)
```powershell
winget install Ripcord.Ripcord --source winget
