# CODEX.md — Cloud-SSD Master Project Index & Codex Architecture Specification

> **App Name:** Cloud-SSD (Virtual Drive: Cloud Drive)  
> **Version:** 0.7.0  
> **Target OS:** Windows 10 (1809+) / Windows 11 x64 (NTFS required)  
> **Engineering Lead:** Codex  
> **Antigravity Action Log:** See [ANTIGRAVITY.md](ANTIGRAVITY.md)  
> **Last Updated:** 2026-11-09  

---

## 📌 1. Project Overview & Quick Reference

**Cloud-SSD** is a cloud-to-SSD virtual storage and intelligent game streaming engine for Windows 10/11 (x64). It keeps PC games in cloud storage (Google Drive), exposes them as a local drive letter (`X:`) in File Explorer via WinFsp and rclone, and caches only the accessed blocks on local NTFS SSD storage with predictive prefetching.

| Attribute | Specification |
|---|---|
| **Official Name** | **Cloud-SSD** |
| **Solution** | `CloudSSD.slnx` |
| **Framework** | .NET 10 (`net10.0-windows10.0.19041.0` / `net10.0`) |
| **Language** | C# (Latest, Nullable enabled) |
| **GUI Framework** | WPF (.NET 10) + CommunityToolkit.Mvvm |
| **Filesystem Engine** | WinFsp (Windows File System Proxy) 2025 |
| **Cloud Backend** | rclone v1.75.2 (VFS sparse cache mode full, RC on 127.0.0.1) |
| **Database** | SQLite (Game library, prediction profiles) |
| **Credential Security** | Windows DPAPI (`SecretStore`) |
| **Primary Cloud Provider**| Google Drive (OAuth2 client flow) |
| **Virtual Mount** | User-mode virtual drive letter (e.g. `X:`) |

---

## 🏗️ 2. Complete Module Map (`src/*`) — Built by Codex

Codex built and structured the 11 core sub-projects:

### 1. `CloudSSD.Core`
- **Domain Models & Settings**: Atomic JSON configuration (`settings.json`).
- **Paths & Units**: Resolves `%LOCALAPPDATA%\Cloud-SSD` as primary data root, units conversion (Mbps vs MB/s).
- **Disk Utilities**: Win32 P/Invoke for `GetCompressedFileSizeW` to track physical allocation of sparse files on NTFS.

### 2. `CloudSSD.Mount`
- **Virtual Drive Supervisor**: Manages `rclone mount` child process with WinFsp user-mode driver.
- **Remote Control & Tailer**: Non-locking `-vv` log tailer (`LogTailer`) parsing real-time block access events.
- **Crash Recovery & Re-attach**: Auto-restart on crash and re-attachment to orphaned mounts.

### 3. `CloudSSD.Cache`
- **Smart Cache Inspector**: Tracks `.vfsMeta` range tables and actual cluster allocation.
- **DiskSafetyGuard**: Protects system disk free space (enforces minimum free reserve, pauses prefetch on pressure).
- **Marker-Guarded Eviction**: Safe cleanup protecting cloud originals and game saves (`.cloudplayx-cache`).

### 4. `CloudSSD.Streaming`
- **Prediction Engine**: `StartupPredictor` (boot profiles), `SequentialPredictor` (linear reads).
- **Priority Scheduler**: P0 (active game read, direct rclone path) yields no contention to P1–P3 prefetch reads.
- **Bandwidth Governance**: Dynamic throttling based on network delay and available capacity.

### 5. `CloudSSD.Games`
- **Scanner & PE Inspector**: Reads Portable Executable (PE) headers and imports to detect missing VC++ / DirectX dependencies.
- **Compatibility Analyzer**: Detects anti-cheat engines (EasyAntiCheat, BattlEye, Vanguard) and DRM systems.
- **Sandbox Launcher**: Spawns games in Windows Job Objects and decodes NTSTATUS exit codes.

### 6. `CloudSSD.Desktop`
- **Modern WPF GUI**: MVVM pattern via `CommunityToolkit.Mvvm`.
- **Views**: Dashboard, Cloud Drives, Game Library, Accelerator, Smart Cache, Live Monitor, Diagnostics.
- **Controls**: Lightweight custom `Sparkline` real-time graph controls.

### 7. `CloudSSD.Diagnostics`
- **DPAPI SecretStore**: Hardware-bound encryption for OAuth tokens.
- **Redaction & Export**: Sanitizes personal data, paths, and tokens into diagnostic zip exports.

### 8. `CloudSSD.Providers`
- Provider descriptors for Google Drive (Tier 1 supported) and generic rclone remotes.

### 9. `CloudSSD.Cli`
- `cpx.exe` headless CLI for mounting, diagnostics, and testing.

### 10. `CloudSSD.SimCloud`
- Local mock server simulating throttled cloud connections, jitter, latency, and packet drops.

### 11. `CloudSSD.App`
- Main host orchestrator and DI wireup.

---

## ⚡ 3. Architecture & Data Flow

```
[User Game Exe]  ---> Reads 'X:\Game\Data.pak' (WinFsp User-mode Filesystem)
                             │
                             ▼
             [rclone mount process with VFS Full Cache]
            /                                          \
  (Cache Miss - Cold Read)                      (Cache Hit - Warm Read)
          │                                                │
          ▼                                                ▼
  [Fetch from Google Drive]                         [Fast read from NTFS SSD]
          │                                                │
          └───────────► [-vv Log Stream] ◄─────────────────┘
                              │
                              ▼
                   [LogTailer + Parser]
                              │
                              ▼
                     [StreamingEngine]
                              │
            ┌─────────────────┴─────────────────┐
            ▼                                   ▼
  [Record to SQLite Profile]          [Prefetch Scheduler (P0-P3)]
```

---

## 📊 4. Performance Benchmarks Summary

*Hardware: i3-10105F, RX 580, 16GB RAM, Windows 11, 70 Mbps / 80ms link:*

| Mode / Condition | Load Time | Data Transferred |
|---|---|---|
| **Local SSD Baseline** | 0.09 – 0.12 s | 54.5 MiB (Local) |
| **Standard rclone mount (cold)** | 56.5 – 65.2 s | 498 – 551 MiB |
| **Cloud-SSD (Cold, no profile)** | 20.3 – 21.0 s | 117 – 120 MiB |
| **Cloud-SSD (Profiled + Prepared)** | 7.7 – 8.2 s | Pre-cached efficiently |
| **Cloud-SSD (Warm / Cached)** | 0.15 – 0.27 s | 0 bytes downloaded |

---

## 🔨 5. Build, Test, and Packaging Commands

```powershell
# Build Solution (Release x64)
dotnet build CloudSSD.slnx -c Release

# Run Automated Tests
dotnet test CloudSSD.slnx -c Debug --logger "console;verbosity=normal"

# Run Desktop UI
dotnet run --project src/CloudSSD.Desktop -c Release
```
