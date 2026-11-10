# CLOUD-SSD (5TB) - Virtual Local Disk System

Combine multiple Google Drive accounts (e.g. 5–6 accounts = 5TB storage) into a **single Native Local Disk (`Z:\`)** on Windows 10/11 x64.

---

### Features:
1. **Appears as a Real Local Disk:**
   - Mounts as a native Fixed Drive (`DRIVE_FIXED`) in My PC (File Explorer).
   - Looks, feels, and works like an internal hard drive partition.
2. **Combines Multiple Accounts:**
   - Multiple Google Accounts (e.g., `Drive1`, `Drive2`, `Drive3`) are merged into a single `Z:\` drive using rclone Union.
   - When you save or copy a file to `Z:\`, it automatically saves to the account with the most free space!
3. **Laptop SSD Protection (Zero Bloat):**
   - Maximum local SSD cache limit: **15 GB**.
   - Minimum free disk safety reserve: **10 GB**.
   - Your laptop SSD will never run out of space.
4. **Silent Auto-Start on Boot:**
   - Option to automatically and quietly mount `Z:\` when Windows starts, without any black popup windows.

---

### How to Use:

1. **Open the Manager:**
   - Double-click **`CloudSSD-Manager.bat`** (or the **"Cloud-SSD Manager"** icon on your Desktop).
2. **Add Your Accounts:**
   - Choose **Option [1]** to add your first Google account.
   - Give it a name (e.g. `Account1`).
   - A browser window will open automatically. Sign in and click **Allow**.
   - Repeat for your other Google accounts (`Account2`, `Account3`, etc.).
3. **Mount the 5TB Local Disk:**
   - Choose **Option [4]** (or double-click **`Mount-Disk.bat`** / Desktop shortcut).
   - `Z:\` drive will open in File Explorer!
4. **Auto-Start on Boot:**
   - Choose **Option [6]** in the Manager to enable auto-mount whenever your laptop starts up.

---

### Folder Structure:
- `CloudSSD-Manager.bat` - Main Control Center
- `Mount-Disk.bat` - 1-Click Mount
- `Unmount-Disk.bat` - 1-Click Unmount
- `bin\` - Standalone portable rclone engine
- `config\` - Saved Google Drive credentials (`rclone.conf`)
- `logs\` - Mount and operation logs
- `scripts\` - Automated PowerShell helper scripts
