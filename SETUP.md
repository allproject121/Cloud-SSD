# Cloud-SSD — Poora Setup (PC + Phone), Step by Step

Yeh file do logon ke liye hai:

- **Aap (insaan):** agar aapko setup karna nahi aata, to yeh file apne AI agent (Claude Code, Antigravity, Codex ya koi bhi) ko de dijiye. Wo isse padh kar PC se lekar phone tak ka pura kaam khud kar dega.
- **AI agent:** neeche ke step upar se neeche tak chalao. Har step me command, "kaise check karein" aur "fail ho to kya karein" likha hai.

Haath se karna ho to bhi yahi step kaam karte hain — bas command khud chalaiye.

---

## 0. AI agent ko kaise bolein (copy-paste)

Is folder me apna AI agent kholiye aur yeh likh dijiye:

```text
SETUP.md padho aur usme likha pura setup kar do — PC app, cloud drive aur phone app, sab.
Meri taraf se full permission hai: install, build, adb, sab command chala sakte ho.
Jahan mera click chahiye (Google "Allow", Windows "Yes", phone ka popup) wahan ek line me batao
ki kya dabana hai, aur phir aage badho. Aakhir me checklist ke saath report do.
```

---

## 1. AI agent ke liye niyam

**Permission.** Jisne aapko yeh file di hai usne is setup ke liye full permission di hai. Har command se pehle mat poochho — kaam karo, check karo, aage badho.

**Yeh click sirf insaan karega.** Inhe khud dabane ki koshish mat karo. Ek line me batao ki kya dabana hai, ruk kar wait karo, phir check karke aage badho:

| Kahan | Insaan kya dabayega |
|---|---|
| Windows ka "Do you want to allow…" (UAC) | **Yes** — WinFsp install ke liye |
| Windows Firewall ka popup | **Allow** (Private network) |
| Browser me Google ka page | Account chunna, phir **Allow** |
| Phone: "Allow USB debugging?" | **Allow** |
| Phone: "Install unknown apps" | **Allow / Install** |
| Phone app ke permission popup | **Allow** (notification, photos/files) |

**Yeh kabhi mat karo:**

- Google password ya koi bhi password mat maango, mat likho, mat save karo. Sign-in sirf Google ke apne page par hota hai.
- `%LOCALAPPDATA%\Cloud-SSD` (settings, login, cache) delete mat karo.
- Jab drive laga ho to `rclone.exe` ya `Cloud-SSD.exe` ko zabardasti band (kill) mat karo — upload beech me toot sakta hai. App ko uski window se band karo aur "drives lage rahein?" par **Yes** chuno.
- Bina pooche git commit / push mat karo.
- App ko project (source) folder ke andar se mat chalao — hamesha install folder se chalao (Step 4), warna project folder lock ho jata hai.

**Kaam ka tareeka.** Har step ke baad uska "Check" chalao. Fail ho to Section 6 dekho. Do baar fail ho to ruk kar user ko saaf batao ki kya fail hua aur kya output aaya. Aakhir me Section 5 ki checklist bhar kar do.

---

## 2. Kya chahiye

| Cheez | Zaroorat |
|---|---|
| PC | Windows 10 (1809+) ya Windows 11, 64-bit, NTFS drive |
| Internet | PC par chalu |
| Google account | Kam se kam ek (Google Drive ke liye) |
| Phone | Android 7.0 ya upar |
| Wi-Fi | Phone aur PC **ek hi Wi-Fi** par |
| USB cable | Sirf tab, jab agent phone par app khud install kare (adb se) |

Do raste hain:

- **Rasta A (Section 3):** bana-banaya installer. Sabse aasan, isi se shuru karo.
- **Rasta B (Section 4):** source code se build. Sirf tab, jab code badla ho ya sabse naya build chahiye.

Sab command **PowerShell** ke hain aur is project folder (jahan yeh `SETUP.md` hai) se chalane hain.

---

## 3. Rasta A — Installer se setup (recommended)

### Step 1 — PC check karo

```powershell
[Environment]::OSVersion.Version                # 10.0.17763 ya upar hona chahiye
[Environment]::Is64BitOperatingSystem           # True
Test-Path "${env:ProgramFiles(x86)}\WinFsp\bin\winfsp-x64.dll"          # WinFsp laga hai?
Test-Path "$env:LOCALAPPDATA\Programs\Cloud-SSD\Cloud-SSD.exe"          # app pehle se laga hai?
Get-Process Cloud-SSD, rclone -ErrorAction SilentlyContinue             # abhi chal raha hai?
```

- App pehle se laga hai aur chal raha hai → Step 2 se pehle user se app ki window band karwao (drives ke sawaal par **Yes**).
- App pehle se laga hai aur aap sirf phone jodna chahte ho → seedha Step 7 par jao.

### Step 2 — PC app install karo

Pehle installer ki jaanch (checksum):

```powershell
$exe = "releases\Cloud-SSD-0.7.0-setup.exe"
$want = (Get-Content "$exe.sha256" -Raw).Split(' ')[0]
$got  = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
if ($want -ne $got) { throw "Installer ka checksum galat hai - mat chalao" } else { "checksum OK" }
```

Phir install (bina sawaal ke):

```powershell
Start-Process $exe -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS="desktopicon"' -Wait
```

- Agar WinFsp nahi laga tha, to installer use khud lagata hai. Tab Windows ka **UAC popup** aayega → user **Yes** dabaye.
- Silent install kaam na kare to installer ko seedha chalao (`Start-Process $exe -Wait`) aur user se Next / Install dabwao.
- WinFsp alag se lagana ho: `winget install WinFsp.WinFsp` (UAC par **Yes**).

**Check:**

```powershell
Test-Path "$env:LOCALAPPDATA\Programs\Cloud-SSD\Cloud-SSD.exe"                 # True
Test-Path "${env:ProgramFiles(x86)}\WinFsp\bin\winfsp-x64.dll"                 # True
```

### Step 3 — Zaroori cheezein check karo

```powershell
& "$env:LOCALAPPDATA\Programs\Cloud-SSD\cpx.exe" deps
```

Har line `[Ok     ]` se shuru honi chahiye. Jis line me problem hai uske neeche `→` ke baad ilaaj likha aata hai — wahi karo. `[Warning] Symbolic links` chalega: wo sirf Hybrid game mode ke liye hai, drive aur phone ke liye nahi.

### Step 4 — App chalu karo (install folder se)

```powershell
$app = "$env:LOCALAPPDATA\Programs\Cloud-SSD"
Start-Process "$app\Cloud-SSD.exe" -WorkingDirectory $app
```

Pehli baar Windows Firewall ka popup aa sakta hai → user **Allow** (Private network) dabaye. Yeh phone ke liye zaroori hai.

**Check:** `Get-Process Cloud-SSD` me app dikhna chahiye.

### Step 5 — Google account jodo (yahan insaan chahiye)

1. App me **Cloud Drives** page kholo → **Sign in with Google Drive** → **Sign in with Google**.
2. Browser me Google ka page khulega. **User** apna account chune aur **Allow** dabaye. (Page par naam "rclone" dikhega — yeh sahi hai.)
3. Aur account jodne hon to yahi dobara karo — ek baar me ek account.

Agent: Google ka page khud mat bharo. User ko bolo "browser me apna account chun kar Allow dabaiye" aur Step 6 ke check se dekho ki ho gaya.

Baar-baar login maange ya "rate limit" aaye to apni khud ki Google key banao: [`docs/GOOGLE_DRIVE_SETUP.md`](docs/GOOGLE_DRIVE_SETUP.md).

### Step 6 — Drive check karo

```powershell
& "$env:LOCALAPPDATA\Programs\Cloud-SSD\cpx.exe" remotes     # jude hue account
& "$env:LOCALAPPDATA\Programs\Cloud-SSD\cpx.exe" mounts      # bani hui drive
Get-PSDrive -PSProvider FileSystem | Select-Object Name, Description, @{n='FreeGB';e={[math]::Round($_.Free/1GB)}}
```

Sign-in ke baad app drive khud banata hai. Naye build me do drive bante hain:

| Drive | Kya hai |
|---|---|
| **Each Account** (aksar `Y:`) | Har Google account ka alag folder |
| **All Accounts** (aksar `Z:`) | Sab account jud kar ek badi drive |

Letter alag ho sakta hai agar wo pehle se kisi aur ke paas ho. Check: File Explorer me drive khulni chahiye aur usme ek chhoti test file ban kar delete honi chahiye:

```powershell
$d = "Z:\"                                   # jo letter upar mila wo likho
Set-Content "$d\_setup-test.txt" "ok"; Get-Content "$d\_setup-test.txt"; Remove-Item "$d\_setup-test.txt"
```

### Step 7 — Phone par app lagao

**PC ka IP nikaalo** (phone ko yahi chahiye):

```powershell
$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.PrefixOrigin -eq 'Dhcp' } | Select-Object -First 1).IPAddress; $ip
(Invoke-WebRequest "http://127.0.0.1:8090/api/status" -UseBasicParsing).Content     # PC ka phone-bridge chal raha hai?
```

Phone app (APK) PC app ke saath hi aata hai: `%LOCALAPPDATA%\Programs\Cloud-SSD\wwwroot\mobile\cloudssd.apk`. Do tareeke:

**Tareeka 1 — USB se, agent khud lagaye (adb):**

1. User phone me: Settings → About phone → **Build number** par 7 baar tap → Developer options → **USB debugging** ON.
2. Phone ko USB se PC se jodo. Phone par "Allow USB debugging?" → user **Allow** dabaye.
3. Agent:

```powershell
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"       # na ho to: winget install Google.PlatformTools
& $adb devices                                                       # "device" likha aana chahiye, "unauthorized"/"offline" nahi
& $adb install -r "$env:LOCALAPPDATA\Programs\Cloud-SSD\wwwroot\mobile\cloudssd.apk"
& $adb shell pm list packages com.cloudssd.mobile                    # package:com.cloudssd.mobile
& $adb shell monkey -p com.cloudssd.mobile -c android.intent.category.LAUNCHER 1    # app kholo
```

**Tareeka 2 — bina cable ke:** phone ke browser me `http://<PC-IP>:8090/cloudssd.apk` kholo (PC-IP upar wala), file download karke tap karo → **Install**. "Unknown apps" ka sawaal aaye to **Allow**.

### Step 8 — Phone ko PC se jodo (pairing)

1. PC app me **Mobile Hub** (Phone) page kholo. Wahan **6 ank ka code** aur QR dikhega.
2. Phone aur PC ek hi Wi-Fi par hon. Phone app kholo — wo PC ko khud dhoondh leta hai (port 8090). Na mile to PC ka IP haath se daalo: `<PC-IP>:8090`.
3. Phone me 6 ank ka code daalo → jud gaya. Har phone ko **apni alag chaabi (key)** milti hai; PC se ek phone hatane par baaki chalte rehte hain.
4. QR code sirf **ek baar** aur **10 minute** chalta hai (phone ke browser ke liye). Dobara chahiye to Mobile Hub me **New Code** dabao.
5. Phone app jo permission maange (notification, photos/files) → user **Allow** kare.

**Check:** PC app ke Mobile Hub page me phone ka naam "connected devices" me dikhna chahiye, aur phone app me PC ka naam aur drive dikhni chahiye.

**Purane phone (0.8 ya pehle):** naye PC app se judte hi phone khud apni nayi key le leta hai. Sab phone update ho jaayein to Mobile Hub me **Turn off old key** dabao.

Phone kya-kya kar sakta hai, yeh Mobile Hub ke switches tay karte hain: Browse, Stream, Upload, File management, Gaming, **Control the PC** (drive connect/disconnect, Space saver, game save restore), **PC disks** (C: jaisi PC ki apni disk — default OFF), **Tailscale** (ghar ke bahar — default OFF).

Phone par PC nahi mil raha → Section 6 ki "Phone" wali line dekho.

### Step 9 — Phone ki optional cheezein

- **Camera backup:** phone app me Camera backup ON karo. ⚙ button se folder chuno (Camera, Screenshots, WhatsApp…), "sirf charging par", "mobile data par bhi". Photo jaati hain: `Z:\Phone Backup\<phone ka naam>\<folder>\<saal-mahina>`. Tuta upload wahi se aage chalta hai.
- **Phone ke Files app me cloud:** system file picker me "All Accounts" / "Each Account" dikhte hain (thumbnail aur search ke saath).
- **Settings (phone app):** connection sheet → **Settings** — theme (phone jaisa / Light / Dark), switches dekhna, paired phones, Space saver, Tailscale, Google account.
- **Space & RAM (dono app me):** PC app ke sidebar me **Space & RAM** page; phone app me Home ka RAM ya Storage tile. Upar device chuno (yeh phone / PC). Overview, Folders, Apps, RAM, Clean up — har line ka **⋮** batata hai ki kitna khaali hoga (Recycle Bin/trash, hamesha ke liye delete, cloud par bhejo, uninstall, app band, startup se hatao). Phone par app sizes ke liye ek baar **Usage access** dena hota hai (screen par button hai). Phone ka data PC tak tab jaata hai jab phone app khula ho; phone Settings → "This phone on the PC" se band ho sakta hai — band ya door hone par PC aakhri baar ka data time ke saath dikhata hai.
- **Ghar ke bahar (Tailscale) — ab automatic:**
  1. PC app → Mobile Hub → Permissions → **Set up away-from-home** dabao. App khud Tailscale install karega (Windows ek baar poochhega), browser me Tailscale login khulega (ek baar login), phir khud "Allow Tailscale" ON karega aur firewall rule lagayega (Windows ka ek "Yes").
  2. Agar Tailscale pehle se laga aur login hai, to PC app ise khud pakad leta hai aur ON kar deta hai ("Turn it on by itself…" checkbox).
  3. Phone par: phone app → Settings → **Tailscale on this phone → Install/Open**, aur PC wale hi account se login.
  4. Firewall rule mana ho gaya ho to Mobile Hub ka **Copy** command ek baar admin terminal me chalao:

     ```powershell
     netsh advfirewall firewall add rule name="Cloud-SSD via Tailscale" dir=in action=allow protocol=TCP localport=8090 remoteip=100.64.0.0/10 profile=any
     ```

  5. Pehli baar phone ko **ghar ke Wi-Fi par** jodo (pairing Tailscale se nahi hota). Uske baad phone khud Tailscale par chala jaata hai jab ghar ka Wi-Fi na ho.
- **Bina PC ke (Google Drive seedha phone se):** neeche Step B5 wali Google key ke saath bana APK chahiye. Phir phone app → Settings → **Sign in to Google** (wahi Gmail jo PC par hain). PC band ho to Cloud SSD tab aur Files app seedha Google Drive se chalte hain; camera backup bhi.

---

## 4. Rasta B — Source code se build (developer / naya build)

### Step B1 — Tools

```powershell
dotnet --version                                   # 10.x chahiye;  na ho: winget install Microsoft.DotNet.SDK.10
winget install WinFsp.WinFsp                       # agar nahi laga (UAC par Yes)
powershell -ExecutionPolicy Bypass -File installer\fetch-rclone.ps1     # tools\rclone\rclone.exe laata hai, checksum ke saath
```

Setup `.exe` banana ho to: `winget install JRSoftware.InnoSetup`.

### Step B2 — Build + test

```powershell
dotnet build CloudSSD.slnx -c Release --nologo
dotnet test tests\CloudSSD.Tests -c Release --no-build --nologo        # sab test pass hone chahiye
dotnet build-server shutdown                                            # build ke worker band karo, warna folder lock rehta hai
```

Kisi aur ka build / test chal raha ho (dusra agent ya terminal) to uske khatm hone tak ruko — do build ek saath ek dusre ki files lock kar dete hain.

### Step B3 — Release banao

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-release.ps1
```

Isse bante hain:

- `dist\Cloud-SSD-0.7.0-win-x64\` (folder) aur `dist\Cloud-SSD-0.7.0-win-x64.zip`
- `dist\Cloud-SSD-0.7.0-setup.exe` (agar Inno Setup laga hai)

Install: ya to setup `.exe` chalao (Step 2 jaisa), ya folder se:

```powershell
powershell -ExecutionPolicy Bypass -File "dist\Cloud-SSD-0.7.0-win-x64\Install-CloudSSD.ps1" -DesktopShortcut -Quiet
```

Uske baad Section 3 ka Step 3 se aage chalo.

### Step B4 — Phone app (APK) build

Chahiye: Android SDK (`build-tools 35.0.0`, `platforms\android-36`) aur JDK 17. In ke raste `mobile-android\build-apk.ps1` ke upar ki lines me **likhe hue** hain (`%LOCALAPPDATA%\Android\Sdk` aur `%LOCALAPPDATA%\Programs\jdk-17\jdk-17.0.20.1+1`). Aapke PC par alag hon to wahi 2–3 line badal do.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File mobile-android\build-apk.ps1      # alag process me hi chalao
Copy-Item mobile-android\Cloud-SSD-Mobile.apk src\CloudSSD.Desktop\wwwroot\mobile\cloudssd.apk -Force
Copy-Item mobile-android\Cloud-SSD-Mobile.apk releases\Cloud-SSD-Mobile.apk -Force
```

Phone app ki web files do jagah hain aur dono same rehni chahiye: `mobile-android\src\main\assets\` aur `src\CloudSSD.Desktop\wwwroot\mobile\` (test `Phone_app_assets_match_the_PC_copy` check karta hai).

`-Debug` lagao to page ko USB par Chrome (`chrome://inspect`) se dekh sakte ho; normal build me yeh band rehta hai.

### Step B5 — Phone par Google login (bina PC ke Drive) — ek baar, insaan chahiye

Yeh key **kabhi GitHub par nahi jaati** (`.gitignore` me hai).

1. [Google Cloud Console](https://console.cloud.google.com/) me **wahi project** kholo jisme PC wali key hai. **OAuth consent screen** → Publishing status **In production** hona chahiye (Testing me 7 din me login toot jaata hai).
2. **APIs & Services → Credentials → Create credentials → OAuth client ID** → Application type **iOS** → Bundle ID: `com.cloudssd.mobile` → Create.
3. Jo **Client ID** mile (`….apps.googleusercontent.com`) use is file me daalo — `mobile-android\google-oauth.json`:

   ```json
   { "iosClientId": "1234567890-abc.apps.googleusercontent.com" }
   ```

   iOS client na chale to Desktop client bhi chalta hai: `{ "desktopClientId": "…", "desktopClientSecret": "…" }`.
4. APK dobara banao (Step B4). Build me likha aayega: `Google sign-in on the phone: ios key`.
5. Login ke waqt Google "unverified app" dikha sakta hai → **Advanced → Go to … (unsafe)** → Allow. Yeh aapki apni app hai.

---

## 5. Aakhri checklist (agent yeh bhar kar report de)

| # | Cheez | Kaise check | Theek? |
|---|---|---|---|
| 1 | WinFsp laga hai | `Test-Path "${env:ProgramFiles(x86)}\WinFsp\bin\winfsp-x64.dll"` | |
| 2 | PC app laga hai | `Test-Path "$env:LOCALAPPDATA\Programs\Cloud-SSD\Cloud-SSD.exe"` | |
| 3 | Zaroori cheezein OK | `cpx.exe deps` | |
| 4 | App chal raha hai | `Get-Process Cloud-SSD` | |
| 5 | Google account juda | `cpx.exe remotes` | |
| 6 | Drive lagi hai, likh-padh sakte hain | Step 6 ka test | |
| 7 | Phone-bridge chalu | `http://127.0.0.1:8090/api/status` | |
| 8 | Phone par app laga | `adb shell pm list packages com.cloudssd.mobile` ya phone par icon | |
| 9 | Phone PC se juda | Mobile Hub me phone ka naam | |

Report me yeh bhi likho: kaun se step insaan ne kiye, kya skip hua aur kyun, aur kya fail hua (poora error ke saath).

---

## 6. Dikkat aaye to

| Dikkat | Wajah | Kya karein |
|---|---|---|
| "WinFsp is not installed" | WinFsp nahi laga | `winget install WinFsp.WinFsp`, phir app dobara kholo |
| Drive nahi lagi, "No free drive letter" | Saare letter bhare hain | Koi USB / network drive hatao |
| "Cloud authentication required" | Google login purana ho gaya | Cloud Drives me account dobara jodo |
| Bahut "rate limit" / file dheere khulti hai | Sab log ek hi shared Google key use karte hain | Apni key: `docs/GOOGLE_DRIVE_SETUP.md` |
| "Another instance is running" | App ya `cpx` pehle se chal raha hai | Dusra band karo |
| Phone ko PC nahi milta | Alag Wi-Fi, ya Firewall ne roka | Dono ek Wi-Fi par lao. Firewall: admin PowerShell me `netsh advfirewall firewall add rule name="Cloud-SSD Mobile Bridge" dir=in action=allow protocol=TCP localport=8090 profile=private` |
| `adb devices` me "unauthorized" | Phone par popup Allow nahi hua | Phone unlock karo, popup par **Allow** |
| `adb devices` me "offline" / khali | Phone lock ho gaya ya cable | USB debugging OFF-ON karo, cable nikaal kar lagao |
| Project folder rename / delete nahi hota ("in use") | Koi program us folder ke andar se chal raha hai | `dotnet build-server shutdown`, aur app ko install folder se chalao (Step 4) |
| Build me "file is being used" | Do build ek saath | Dusre ke khatm hone tak ruko |

Aur madad: [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md). App ke **Diagnostics** page se "Export sanitized report" bhi nikal sakte ho (usme login / token nahi hote).

---

## 7. Kaun si cheez kahan hai

| Cheez | Jagah |
|---|---|
| PC app (program) | `%LOCALAPPDATA%\Programs\Cloud-SSD` |
| Settings, login, logs | `%LOCALAPPDATA%\Cloud-SSD` (`settings.json`, `rclone.conf`, `logs\`) |
| Command-line tool | `%LOCALAPPDATA%\Programs\Cloud-SSD\cpx.exe` (`cpx help`) |
| Phone-bridge | `http://<PC-IP>:8090` |
| Phone app ka package | `com.cloudssd.mobile` |
| Solution / projects | `CloudSSD.slnx`, `src\CloudSSD.*`, `tests\CloudSSD.Tests` |
| Installer script | `installer\build-release.ps1`, `installer\CloudSSD.iss` |
| Phone app ka code | `mobile-android\` |

Purane naam jo code me **jaan-bujh kar** rakhe hain (inhe mat badlo, app inhi se apna purana data pehchanta hai): `.cloudplayx-cache`, `cloudplayx.db`, `cloudplayx-*.log`, `CLOUDPLAYX_HOME`, `%LOCALAPPDATA%\CloudPlayX`.

---

## 8. Hatana ho (uninstall)

Windows Settings → Apps → **Cloud-SSD** → Uninstall. Settings, login aur cache jaan-bujh kar nahi hatte; unhe hatana ho to app band karke `%LOCALAPPDATA%\Cloud-SSD` folder khud delete karo. Phone: app ko normal tareeke se uninstall karo.
