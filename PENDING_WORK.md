# Cloud-SSD Pending Work & Roadmap (`PENDING_WORK.md`)

## 🎯 Active Status: v0.7.0 Complete & Released

All core requirements and major new features have been fully implemented, tested, and packaged:
- [x] Complete project-wide rebrand: `CloudPlayX` ➔ `Cloud-SSD`
- [x] Primary executable rebranded: `Cloud-SSD.exe` (Assembly Name: `Cloud-SSD`)
- [x] Fixed installer busy mount error (auto-unmount & process kill in Inno Setup)
- [x] Mobile Hub (Offline Phone Bridge) embedded async HTTP server (`LocalMobileBridgeService.cs`)
- [x] Pure C# Zero-Dependency QR Code Generator (`QrCodeGenerator.cs`)
- [x] Desktop UI (`MobileHubView.xaml`, `MobileHubViewModel.cs`) integrated with live pairing and drop zone
- [x] Mobile PWA Client (`wwwroot/mobile`: 5 tabs - Home, Files, Gaming, Vault, Settings)
- [x] 100% Offline Service Worker (`sw.js`) and Web App Manifest (`manifest.json`)
- [x] 62/62 Unit & Integration tests passing cleanly with 0 errors
- [x] ReadyToRun release binaries & setup installer built:
  - `Cloud-SSD-0.7.0-setup.exe` (~67.5 MB / 70.8 MB)
  - `Cloud-SSD-0.7.0-win-x64.zip` (~89.3 MB / 93.7 MB)

---

## 📋 Next Priorities & Future Enhancements

### Phase 1: Real-World Phone Verification
- [ ] Connect a physical Android phone to the PC running Cloud-SSD via local Wi-Fi or Phone Hotspot.
- [ ] Verify instant camera QR code scanning and pairing token authorization.
- [ ] Test 4K video playback seeking and range requests directly on mobile Chrome.
- [ ] Test IndexedDB "Save to Offline Vault" for offline access when PC is shutdown.

### Phase 2: Live Gaming HUD Expansion
- [ ] Expand `/api/hud` with real-time MSI Afterburner / RTSS or Windows Performance Counters integration for per-core CPU and GPU temperature telemetry.
- [ ] Add emulator-specific auto-sync profiles (AetherSX2, PPSSPP, RetroArch save states).
