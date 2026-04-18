# Deploy pe HoloLens 2 — Ghid Complet

Acest document explică pas cu pas cum faci deploy la proiectul EchoRealm din Unity pe un HoloLens 2 fizic, prin Visual Studio 2026 Community.

---

## 0. Pregătire HoloLens (o singură dată)

### 0.1. Activează Developer Mode pe HoloLens
1. Pornește HoloLens, pune-l pe cap.
2. Start (👐 gestul) → **Settings** → **Update & Security** → **For developers**.
3. Activează **Developer Mode** (toggle ON).
4. Activează **Device Portal** (toggle ON). Îți va afișa un mesaj cu adresa IP și un PIN.

### 0.2. Conectare la aceeași rețea Wi-Fi
HoloLens-ul și PC-ul **trebuie să fie pe aceeași rețea Wi-Fi** (ideal rețea privată, nu „Guest").

Pe HoloLens: **Settings → Network & Internet → Wi-Fi** → conectează la aceeași rețea ca PC-ul.

### 0.3. Notează IP-ul HoloLens
Pe HoloLens: **Settings → Network & Internet → Wi-Fi → Advanced options** → vezi `IPv4 Address` (ex: `192.168.1.47`). Notează-l.

### 0.4. Pair device cu Visual Studio (prima dată)
1. Pe HoloLens: **Settings → Update & Security → For developers → Pair** → apare un **PIN de 6 cifre**.
2. În Visual Studio pe PC: **Debug → [Dispozitivul tău HoloLens] → Remote Machine** (se va cere PIN la primul deploy).

---

## 1. Setări Unity înainte de Build

Deschide **`EchoRealm.unity`** (MainScene) și:

### 1.1. Platform switch
**File → Build Settings:**
- Platform: **Universal Windows Platform** → **Switch Platform** (dacă nu e deja).
- Architecture: **ARM 64-bit**
- Build Type: **D3D Project**
- Target SDK Version: **Latest installed**
- Minimum Platform Version: **10.0.19041.0** sau mai nou
- Build and Run on: **Local Machine** (nu contează — faci deploy manual din VS)
- Build configuration: **Release** (pentru test rapid poți pune **Debug** temporar)

### 1.2. Player Settings (verifică)
**Edit → Project Settings → Player → UWP tab:**

**Publishing Settings → Capabilities** (bifate):
- ✅ InternetClient
- ✅ InternetClientServer (pentru Photon)
- ✅ PrivateNetworkClientServer (pentru Ollama pe LAN)
- ✅ Microphone (pentru speech recognition)
- ✅ SpatialPerception (pentru QR + mesh)
- ✅ WebCam (pentru QR scanning prin OpenXR)
- ✅ GazeInput (pentru eye tracking)

**XR Plug-in Management → UWP tab:**
- ✅ OpenXR bifat
- OpenXR → Feature groups → Microsoft HoloLens bifat
- OpenXR → Interaction Profiles → ✅ Microsoft Hand Interaction Profile, ✅ Eye Gaze Interaction Profile

### 1.3. Scenes In Build
**File → Build Settings → Scenes In Build:**
- Asigură-te că **MainScene.unity** e bifată și are index 0.

---

## 2. Build din Unity

1. **File → Build Settings → Build**.
2. Alege un folder — **NU-l pune în `Assets/`**. Recomandat:
   ```
   C:\Users\Samuel\Desktop\DisertatieVatavu2026\Builds\HoloLens\
   ```
3. Unity generează un proiect Visual Studio (durează 3–10 min prima dată).
4. La final, se deschide **automat** folder-ul de build. Vei vedea:
   ```
   Builds/HoloLens/
   ├── EchoRealm.sln        ← asta deschizi în Visual Studio
   ├── EchoRealm/
   └── Il2CppOutputProject/
   ```

---

## 3. Deploy din Visual Studio 2026

### 3.1. Deschide soluția
Double-click pe `EchoRealm.sln`. Așteaptă să se încarce complet (bara de jos arată „Ready").

### 3.2. Setări de build în VS
În toolbar-ul de sus:
- **Solution Configuration:** `Release` (sau `Debug` pentru prima verificare)
- **Solution Platform:** `ARM64`
- **Startup target:** click pe săgeata lângă butonul verde → **Remote Machine**

### 3.3. Configurare Remote Machine (prima oară)
1. **Project → EchoRealm Properties → Debugging:**
   - **Debugger to launch:** Remote Machine
   - **Machine Name:** IP-ul HoloLens (ex: `192.168.1.47`)
   - **Authentication Mode:** Universal (Unencrypted Protocol)
   - Apply → OK.

### 3.4. Deploy
- **Build → Deploy Solution** (NU „Run"). Prima dată durează ~5–15 min (copie ~200MB).
- La prompt pentru PIN: introdu PIN-ul de pe HoloLens (de la 0.4).
- Când vezi „Deploy succeeded", pune HoloLens pe cap.

### 3.5. Rulare
Pe HoloLens: **Start menu (👐) → All apps → EchoRealm** → tap pentru launch.

> Pentru iterare rapidă: **F5** (Debug → Start Debugging) face build + deploy + launch + attach debugger automat. Dar e mai lent decât Deploy simplu.

---

## 4. Debugging în timp real

### 4.1. Log-uri live (Device Portal)
1. În browser pe PC: `https://<IP-HoloLens>` (ex: `https://192.168.1.47`).
2. Accept certificat self-signed.
3. Login cu user/parolă setate pe HoloLens (sau PIN).
4. **Views → Logging** → vezi `Debug.Log` live.

### 4.2. Alternative: Visual Studio Output Window
Dacă ai dat **F5** (nu Deploy), VS atașează debugger-ul — toate `Debug.Log` apar în **Output → Debug**.

### 4.3. Device Portal — alte funcții utile:
- **Mixed Reality Capture** → filmare video din perspectiva HoloLens (excelent pentru demo-uri)
- **3D View** → vezi mesh-ul spațial scanat
- **Performance** → FPS, CPU/GPU/RAM în timp real
- **File Explorer** → acces la fișierele appului (ex: pentru export SessionLogger)

---

## 5. Flow rapid pentru iterare (după prima dată)

```
1. Modifici cod în Unity / VS Code
2. Unity: File → Build Settings → Build (overwrite folder existent)
3. VS: File → Reload All (dacă .sln e deschis)
4. VS: Build → Deploy Solution  (~30–60s pe iterații mici)
5. Start app pe HoloLens
```

💡 **Tip:** În Build Settings bifează **"Build and Run"** doar la nevoie — de obicei preferi să separi build-ul de deploy ca să nu reporneasci mereu VS.

---

## 6. Probleme frecvente

| Problemă | Soluție |
|---------|---------|
| VS nu vede HoloLens-ul | Verifică că sunt pe aceeași rețea. `ping <IP-HoloLens>` din cmd. |
| „Deploy failed: 0x80073CF9" | Deinstalează versiunea veche a appului de pe HoloLens (Settings → Apps → EchoRealm → Uninstall). |
| „Unable to activate Windows Store app" | Arhitectura greșită (trebuie ARM64, nu x64/x86). |
| Build foarte lung prima oară | Normal. IL2CPP compilează toate dependențele la prima iterație. Iterațiile următoare sunt mult mai rapide (cache). |
| PIN pairing nu merge | Re-generează PIN pe HoloLens: Settings → For Developers → Pair. |
| Appul pornește dar ecran negru | Deschide Device Portal → Logging, caută excepții. De obicei e o capability uitată. |

---

## 7. Pentru testul de sync HoloLens + Editor

Pentru scenariul nostru (Test 1):
1. **PC:** Unity Editor rulează MainScene (Play) → e player 2.
2. **HoloLens:** app deployed → e player 1 (master, primul la join).
3. Ambele folosesc aceeași sesiune Photon (`SessionName = "EchoRealm"`).
4. Pentru QR anchor: Editor-ul simulează anchor la origin (vezi `QRAnchorManager.cs` — ramura `#if !WINDOWS_UWP`). HoloLens scanează QR real.
5. **Important:** pentru ca sync-ul poziției să fie consistent vizual între Editor și HoloLens, ambele trebuie să aibă **același SceneRoot** ca referință. În Editor, nu miști SceneRoot — cubul apare la spawnOffset.

---

## 8. Checklist rapid înainte de fiecare deploy

- [ ] Unity nu are erori în Console
- [ ] Build Settings → UWP, ARM64, D3D Project
- [ ] Capabilities bifate (internet, mic, spatial, etc.)
- [ ] HoloLens aprins și conectat la Wi-Fi corect
- [ ] VS: Release/ARM64/Remote Machine + IP corect
- [ ] Ollama rulează pe PC pe portul 11500 (dacă vrei să testezi AI)
- [ ] Firewall-ul PC-ului permite conexiuni inbound pe portul 11500 (pentru HoloLens → Ollama)
