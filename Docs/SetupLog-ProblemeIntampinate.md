# EchoRealm — Log Setup & Probleme Intampinate

**Scop:** Documentarea completa a procesului de instalare, problemele intampinate si solutiile gasite.
Util pentru: (1) reproducerea proiectului, (2) sectiunea de metodologie din disertatie, (3) referinta viitoare.

**Perioada:** Aprilie 2026

---

## 1. Contextul initial

### Ce exista la inceput
- PC cu Windows 10, 32 GB+ RAM, 85.6 GB spatiu liber
- Lucrare de licenta functionala (Unity + MRTK3 + Photon PUN 2)
- HoloLens 2 (2 dispozitive disponibile la universitate)
- Nicio instalare prealabila pentru disertatie (totul de la zero)

### Ce trebuia instalat
1. Unity Editor 2022.3 LTS (cu UWP Build Support)
2. Visual Studio 2026 Community (UWP workload + MSVC)
3. MRTK3 (Mixed Reality Toolkit 3) — 7 pachete
4. Mixed Reality OpenXR Plugin
5. Photon Fusion 2 SDK
6. Ollama + Llama 3.1 8B

---

## 2. Instalare Unity Editor

### Problema 1: Unity 6.4 oferit ca default
- **Descriere:** Unity Hub propunea Unity 6.4 ca versiune recomandata
- **Risc:** Unity 6 (2025+) nu este compatibil cu MRTK3, care necesita Unity 2022.3 LTS
- **Solutie:** Am instalat manual Unity 2022.3.62f3 LTS din tab-ul "Official Releases" din Unity Hub
- **Lectie:** MRTK3 nu a fost inca portat pentru Unity 6. Trebuie verificata compatibilitatea inainte de a alege versiunea Unity.

### Problema 2: Unity Editor 2022.3.6f1 — folder rezidual
- **Descriere:** Exista un folder de la o instalare anterioara (2022.3.6f1) care continea doar `modules.json`, nu un editor functional
- **Risc:** Confuzie — parea ca editorul e instalat cand de fapt nu era
- **Solutie:** Nu s-a putut sterge din cauza permisiunilor. Am instalat 2022.3.62f3 separat. Folderul vechi nu a interferat.

### Problema 3: UWP Build Support absent la instalarea initiala
- **Descriere:** Am instalat Unity Editor fara modulul "Universal Windows Platform Build Support", necesar pentru build pe HoloLens 2
- **Solutie:** Unity Hub → Installs → click pe iconita de gear (settings) la 2022.3.62f3 → Add Modules → bifat "Universal Windows Platform Build Support" → Install
- **Lectie:** La instalarea Unity pentru HoloLens, INTOTDEAUNA bifati UWP Build Support din start. Altfel trebuie adaugat ulterior.

---

## 3. Instalare Visual Studio

### Problema 4: Visual Studio 2022 nu mai este disponibil
- **Descriere:** In Visual Studio Installer, tab-ul "Available" arata doar Visual Studio 2026 Community (versiunea curenta). VS 2022 aparea doar ca "Out of Support".
- **Decizie:** Am folosit Visual Studio 2026 Community (v18.4.3) — este compatibil cu Unity 2022.3 LTS
- **Componente instalate:**
  - Workload: "Universal Windows Platform development"
  - Workload: "Game development with Unity" (optional dar util)
  - Component individual: "MSVC v143 - VS 2022 C++ x64/x86 build tools" (necesar pentru compilare UWP ARM64)
- **Lectie:** Visual Studio 2026 functioneaza perfect ca IDE pentru Unity 2022.3 LTS. Nu e nevoie de VS 2022 exact.

---

## 4. Instalare Ollama + Llama 3.1 8B

### Problema 5: Descarcarea Ollama prin curl a esuat
- **Descriere:** Am incercat sa descarcam OllamaSetup.exe prin `curl` din terminal. Fisierul a ajuns la 768MB dar procesul curl a ramas blocat.
- **Simptom:** Fisierul parea complet dar era locked de procesul curl. Dupa copiere pe Desktop, dimensiunea era doar 786KB (corupt).
- **Eroare la lansare:** "Another program is using this file" si "Setup files are corrupted"
- **Solutie:** Am renuntat la curl. Am instalat Ollama prin metoda oficiala PowerShell:
  ```powershell
  irm https://ollama.com/install.ps1 | iex
  ```
  Aceasta metoda a functionat fara probleme.
- **Lectie:** Pentru fisiere mari (>500MB), este mai fiabil sa folosesti metoda de instalare recomandata de dezvoltator decat `curl` direct.

### Problema 6: Ollama nu era in PATH dupa instalare
- **Descriere:** Dupa instalarea cu PowerShell, comanda `ollama` nu era recunoscuta in terminal
- **Cauza:** Ollama se instaleaza la `C:\Users\Samuel\AppData\Local\Programs\Ollama\ollama.exe` si nu se adauga automat in PATH
- **Solutie:** Ollama se lanseaza ca aplicatie GUI din Start Menu (apare in system tray). Dupa lansare, serverul ruleaza pe `localhost:11434` si poate fi accesat prin HTTP.
- **Verificare:**
  ```
  curl http://localhost:11434/
  # Raspuns: "Ollama is running"
  ```

### Problema 7: Start Menu arata OllamaSetup.exe in loc de Ollama
- **Descriere:** In Start Menu, shortcut-ul arata "OllamaSetup.exe" ceea ce era confuz
- **Solutie:** Click pe "Open" a lansat Ollama corect. Fereastra Ollama s-a deschis cu optiuni de model.
- **Descarcare model:** `ollama pull llama3.1:8b` (4.9 GB, ~10 minute)

---

## 5. Instalare MRTK3 — Cea mai mare provocare

### Problema 8: Mixed Reality Feature Tool DISCONTINUAT de Microsoft (!!!)

**Aceasta este cea mai importanta problema intampinata in tot procesul de setup.**

- **Descriere:** Microsoft Mixed Reality Feature Tool era instrumentul oficial pentru importul pachetelor MRTK3 si QR Code in Unity. La data de Aprilie 2026, tool-ul a fost complet scos de pe site-ul Microsoft.
- **Simptom:** Link-ul oficial `https://aka.ms/MRFeatureTool` returna **404 Not Found**
- **Impact:** Fara acest tool, nu exista o metoda directa de a instala MRTK3 in Unity, deoarece pachetele nu sunt publicate pe Unity Asset Store
- **Documentatie Microsoft:** Inca facea referire la Feature Tool, fara a mentiona ca a fost discontinuat

### Solutia gasita: Scoped Registry + GitHub Tarballs

Am descoperit ca pachetele MRTK3 sunt disponibile in doua moduri alternative:

#### A. Mixed Reality OpenXR Plugin — via Scoped Registry (npm feed Azure DevOps)

Am adaugat in `Packages/manifest.json`:
```json
{
  "scopedRegistries": [
    {
      "name": "Microsoft Mixed Reality",
      "url": "https://pkgs.dev.azure.com/aipmr/MixedReality-Unity-Packages/_packaging/Unity-packages/npm/registry/",
      "scopes": [
        "com.microsoft.mixedreality",
        "com.microsoft.spatialaudio"
      ]
    }
  ],
  "dependencies": {
    "com.microsoft.mixedreality.openxr": "1.11.2"
  }
}
```

Aceasta metoda functioneaza deoarece Microsoft inca mentine feed-ul npm pe Azure DevOps, chiar daca Feature Tool-ul a fost scos.

#### B. Pachete MRTK3 — via GitHub Release Tarballs (descarcate manual)

Pachetele MRTK3 nu sunt pe npm feed. Sunt disponibile doar ca `.tgz` pe GitHub Releases:

**Repository:** https://github.com/MixedRealityToolkit/MixedRealityToolkit-Unity

**ATENTIE — Formatul tag-urilor GitHub:**
- Tag-ul NU este `v3.3.0`, ci `core-v3.3.0` (prefixat cu numele pachetului)
- Am incercat initial cu `v3.3.0` → toate download-urile au returnat "Not Found" (fisiere de 9 bytes)
- Am descoperit formatul corect prin GitHub API: `https://api.github.com/repos/MixedRealityToolkit/MixedRealityToolkit-Unity/releases`

**Pachete descarcate (7 fisiere .tgz):**

| Pachet | Versiune | Dimensiune | Tag GitHub |
|--------|----------|------------|------------|
| org.mixedrealitytoolkit.core | 3.3.0 | ~150 KB | core-v3.3.0 |
| org.mixedrealitytoolkit.input | 3.3.0 | ~200 KB | core-v3.3.0 |
| org.mixedrealitytoolkit.spatialmanipulation | 3.4.0 | ~100 KB | core-v3.3.0 |
| org.mixedrealitytoolkit.standardassets | 3.2.1 | ~2 MB | core-v3.3.0 |
| org.mixedrealitytoolkit.uxcomponents | 3.4.0 | ~100 KB | core-v3.3.0 |
| org.mixedrealitytoolkit.uxcore | 3.3.0 | ~150 KB | core-v3.3.0 |
| com.microsoft.mrtk.graphicstools.unity | 0.8.1 | ~16 MB | v0.8.1 (repo separat) |

**Procedura:**
1. Descarcate fisierele `.tgz` de pe GitHub Releases
2. Plasate in `EchoRealm/Packages/MRTK3/`
3. Referentiate in `manifest.json` cu path relativ:
   ```json
   "org.mixedrealitytoolkit.core": "file:MRTK3/org.mixedrealitytoolkit.core-3.3.0.tgz"
   ```
4. Unity Package Manager le-a importat automat la redeschiderea proiectului

### Problema 9: Graphics Tools v0.8.8 nu exista
- **Descriere:** Am incercat sa descarcam versiunea 0.8.8 a Graphics Tools, dar nu exista pe GitHub
- **Solutie:** Versiunea cea mai recenta era v0.8.1 (16 MB). Am folosit aceasta versiune.
- **Repository separat:** https://github.com/microsoft/MixedRealityToolkit-GraphicsTools-Unity

### Problema 10: QR Code Tracking — pachet separat nu mai e necesar
- **Descriere:** In versiunile mai vechi, QR Code tracking necesita un pachet separat (`Microsoft.MixedReality.QR`)
- **Descoperire:** Incepand cu Mixed Reality OpenXR Plugin versiunea 1.11.x, QR Code tracking este **integrat direct** in plugin
- **Avantaj:** Nu mai trebuie instalat nimic suplimentar pentru QR codes
- **Referinta:** Namespace `Microsoft.MixedReality.OpenXR` contine deja clasele necesare

---

## 6. Configurare OpenXR pentru HoloLens 2

### Pasi urmati (fara probleme):
1. File → Build Settings → Switch Platform to **Universal Windows Platform**
   - Architecture: **ARM 64-bit**
   - Build Type: **D3D Project**
   - Target SDK: **Latest Installed**
2. Edit → Project Settings → XR Plug-in Management → Tab UWP:
   - Bifat **OpenXR**
   - Bifat **Microsoft HoloLens feature group**
3. OpenXR Settings (sub XR Plug-in Management):
   - Interaction Profiles: **Microsoft Hand Interaction**, **Eye Gaze Interaction**
   - Features: **Hand Tracking**, **Mixed Reality Features**
   - Depth Submission Mode: **Depth 16 Bit**

### Observatie importanta:
- **Ordinea conteaza:** Trebuie mai intai sa instalezi Mixed Reality OpenXR Plugin (via scoped registry in manifest.json), apoi sa repornesti Unity, si abia apoi sa configurezi OpenXR in Project Settings. Altfel, "Microsoft HoloLens" feature group nu apare ca optiune.

---

## 7. Instalare Photon Fusion 2

### Pasi urmati (fara probleme majore):
1. Cont creat pe https://www.photonengine.com/ (gratuit)
2. Dashboard → Create App → Type: Fusion → copiat App ID
3. Descarcat Photon Fusion 2 SDK (.unitypackage) de pe https://www.photonengine.com/fusion/download
4. In Unity: Assets → Import Package → Custom Package → selectat .unitypackage
5. Fusion Hub → tab Setup → lipit App ID → Apply

### Informatii utile:
- **Plan gratuit:** 20 CCU (Concurrent Users) — suficient pentru 2-3 HoloLens-uri
- **Mod folosit:** Shared Mode (nu Host/Server) — cel mai potrivit pentru MR colaborativ
- **351 fisiere** au fost adaugate in proiect dupa import

---

## 8. Git Repository

### Setup:
- Repository: https://github.com/garytheduck/EchoRealm (Private)
- Branch principal: `main`
- `.gitignore` configurat pentru Unity (Library/, Temp/, Build/, *.csproj, *.sln, etc.)
- Exclus din repo: `.claude/`, `.env`, `.docx` (teza de licenta), `Tools/`

### Commit-uri:
| # | Hash | Mesaj | Fisiere |
|---|------|-------|---------|
| 1 | 6183c3d | Initial EchoRealm project setup: Unity 2022.3 LTS + MRTK3 + OpenXR for HoloLens 2 | 117 fisiere |
| 2 | 5f1fd31 | Add Photon Fusion 2 SDK and update project settings | 351 fisiere |

---

## 9. Rezumat probleme si solutii

| # | Problema | Severitate | Solutie | Timp pierdut |
|---|---------|-----------|---------|-------------|
| 1 | Unity 6.4 recomandat (incompatibil MRTK3) | CRITICA | Instalat 2022.3.62f3 LTS manual | 5 min |
| 2 | Folder rezidual Unity 2022.3.6f1 | MINORA | Ignorat, nu a interferat | 10 min |
| 3 | UWP Build Support absent | MEDIE | Adaugat prin Add Modules | 15 min |
| 4 | VS 2022 indisponibil | MINORA | VS 2026 functioneaza identic | 5 min |
| 5 | Curl download Ollama corupt | MEDIE | PowerShell install script | 30 min |
| 6 | Ollama nu era in PATH | MINORA | Lansat din Start Menu (GUI app) | 10 min |
| 7 | Start Menu arata OllamaSetup | MINORA | Click Open a functionat | 2 min |
| 8 | **Mixed Reality Feature Tool DISCONTINUAT** | **CRITICA** | **Scoped registry + GitHub tarballs** | **2+ ore** |
| 9 | Tag-uri GitHub gresit formatate | MEDIE | Descoperit format `core-v3.3.0` prin API | 30 min |
| 10 | Graphics Tools v0.8.8 inexistent | MINORA | Folosit v0.8.1 (ultima versiune) | 10 min |
| 11 | QR package separat | INFORMATIVA | Nu mai e necesar (integrat in OpenXR 1.11.x) | 0 min |
| 12 | **Ollama port 11434 blocat de Hyper-V** | **MEDIE** | **Folosit port 11500** | **30 min** |
| 13 | Llama 3.1 8B prea lent pe CPU (44-76s) | MEDIE | Trecut pe Llama 3.2 3B (15-35s) | 20 min |

### Timp total estimat pierdut pe probleme: ~4.5 ore
### Timp total setup (cu probleme incluse): ~8 ore

---

## 10. Testare Ollama — Rezultate

### Problema 12: Portul default Ollama (11434) blocat de Windows Hyper-V

- **Descriere:** Ollama nu poate porni pe portul default 11434
- **Cauza:** Windows Hyper-V rezerva range-ul de porturi 11385-11484 (`netsh interface ipv4 show excludedportrange protocol=tcp`)
- **Eroare:** `Error: listen tcp 127.0.0.1:11434: bind: An attempt was made to access a socket in a way forbidden by its access permissions.`
- **Solutie:** Pornire Ollama pe port 11500:
  ```powershell
  $env:OLLAMA_HOST="127.0.0.1:11500"
  C:\Users\Samuel\AppData\Local\Programs\Ollama\ollama.exe serve
  ```
- **Important:** Terminalul trebuie lasat deschis. OllamaClient.cs configurat cu `http://127.0.0.1:11500`

### Problema 13: Llama 3.1 8B prea lent pe CPU

- **Descriere:** Pe CPU (fara GPU dedicat), Llama 3.1 8B genereaza raspunsuri in 44-76 secunde — prea mult pentru o experienta interactiva
- **Cauza:** Modelul de 4.9 GB ruleaza exclusiv pe CPU (total_vram="0 B"); PC-ul are 35.9 GB RAM, 18 GB disponibil
- **Solutie:** Trecut pe **Llama 3.2 3B** (~2 GB), de ~3x mai rapid

### Rezultate benchmark Ollama

| Model | Dimensiune | Prima cerere (include loading) | Cereri ulterioare | Calitate JSON |
|-------|-----------|-------------------------------|-------------------|---------------|
| llama3.1:8b | 4.9 GB | 76s | 44s | Excelenta |
| **llama3.2:3b** | ~2 GB | **15s** | **35s** | **Foarte buna** |

### Exemplu de prompt si raspuns (llama3.2:3b)

**Prompt:**
```
User said: "set everything on fire and make it night"
Scene: day, no effects active
```

**Raspuns AI (35s):**
```json
{
  "commands": ["fire", "night"],
  "consequence": "The world around you erupts in flames as night falls. The sky is painted with hues of orange and red.",
  "dobby_dialogue": "Oh dear, oh dear! Fire everywhere! What shall we do?!",
  "mood": "scared"
}
```

**Observatii:**
- AI-ul alege **comenzile corecte** din lista disponibila
- Genereaza **dialog in-character** pentru Dobby
- Adauga **consecinte narative logice** (foc → cer rosu)
- Detecteaza **mood-ul corect** (scared — foc = frica)
- Promptul trebuie sa specifice explicit "ALL fields must have string values, not null" pentru modelul 3B

---

## 11. Recomandari pentru reproducerea proiectului

Daca cineva doreste sa reproduca acest setup in viitor:

1. **NU folosi Unity 6.x** pentru proiecte MRTK3 — foloseste Unity 2022.3 LTS
2. **NU cauta Mixed Reality Feature Tool** — a fost discontinuat de Microsoft
3. **Foloseste scoped registry** pentru Mixed Reality OpenXR Plugin (vezi sectiunea 5)
4. **Descarca MRTK3 de pe GitHub Releases** cu tag-ul `core-v3.3.0` (nu `v3.3.0`)
5. **Instaleaza Ollama** cu `irm https://ollama.com/install.ps1 | iex` (nu curl)
6. **QR Code tracking** este deja inclus in OpenXR Plugin 1.11.2+ (nu mai trebuie pachet separat)
7. **Visual Studio 2026** functioneaza perfect cu Unity 2022.3 (nu e nevoie de VS 2022)
8. **Bifati UWP Build Support** la instalarea Unity (altfel trebuie adaugat separat)
9. **Ollama port 11500** — daca portul default 11434 e blocat de Hyper-V, folositi alt port
10. **Llama 3.2 3B** recomandat peste 3.1 8B pe CPU — de 3x mai rapid, calitate suficienta

---

*Document creat: Aprilie 2026*
*Ultima actualizare: 6 Aprilie 2026*
