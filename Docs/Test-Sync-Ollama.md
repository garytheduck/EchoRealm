# Plan de Test — Faza 0: Infrastructură (Sync + Ollama)

Validăm cele două piloane critice ale proiectului **înainte** să investim timp în efecte, animații și personaje. Dacă oricare din testele astea pică, restul nu contează.

---

## Test 1: Sincronizare Multi-Dispozitiv (HoloLens + Editor)

### Obiectiv
Un cub spawnat pe un dispozitiv apare în aceeași poziție (fizică) pe celălalt, iar mișcările sunt sincronizate real-time.

### Ce validăm
- ✅ Photon Fusion 2 Shared Mode stabilește sesiune
- ✅ Master detectat corect (primul care intră)
- ✅ Spawn de NetworkObject e vizibil pe ambele clients
- ✅ NetworkTransform sincronizează poziția/rotația
- ✅ Schimbarea de state authority la grab funcționează
- ✅ QR anchor aliniază spațiul (doar pe HoloLens — Editor simulează)

### Setup în Unity (o singură dată)

#### A. Creează prefab-ul cubului
1. În scenă: **GameObject → 3D Object → Cube**. Scale = `(0.15, 0.15, 0.15)` (15 cm).
2. Redenumește: `NetworkedTestCube`.
3. Adaugă componente:
   - **NetworkObject** (Fusion) — `Add Component → Network Object`
   - **NetworkTransform** (Fusion) — `Add Component → Network Transform`
   - **BoxCollider** (există deja de la Cube)
   - **ObjectManipulator** (MRTK) — `Add Component → Object Manipulator`
   - **NetworkedTestCube.cs** (scriptul nostru)
4. În Inspector la **NetworkedTestCube**:
   - `Cube Renderer` → drag Mesh Renderer-ul din același GameObject
5. În **ObjectManipulator** → evenimente:
   - `OnManipulationStarted` → drag GameObject-ul → alege `NetworkedTestCube.OnGrabStart`
   - `OnManipulationEnded` → drag GameObject-ul → alege `NetworkedTestCube.OnGrabEnd`
6. Drag cubul în `Assets/EchoRealm/Prefabs/` → devine **prefab**.
7. Șterge-l din scenă (rămâne doar prefab-ul).

#### B. Creează Spawner + UI în scenă
1. **GameObject → Create Empty** → `TestCubeSpawner`.
2. Atașează `TestCubeSpawner.cs`.
3. În Inspector:
   - `Cube Prefab` → drag prefab-ul `NetworkedTestCube`
   - `Anchor Transform` → drag `SceneRoot` (din ierarhie)
   - `Spawn Offset` = `(0, 0.3, 0.5)` (30cm sus, 50cm în față de QR)
   - `Spawn On Session Join` = **false** (preferăm trigger manual)

4. **GameObject → UI → Canvas** → setează **Render Mode: World Space**.
5. Canvas → RectTransform: Width=400, Height=250, Scale=(0.001, 0.001, 0.001) — astfel canvas-ul e 40x25 cm.
6. Poziționează canvas-ul lângă SceneRoot (ex: la `(0, 1.5, 1)`).
7. Adaugă în Canvas:
   - **TextMeshPro Text** (status) — sus
   - **Button** → text „Spawn Cube"
   - **Button** → text „Randomize"
   - **Button** → text „Despawn"
8. Atașează `SyncTestUI.cs` pe Canvas și leagă referințele în Inspector.

#### C. Fusion Project Settings (verificare)
**Tools → Fusion → Settings** (sau `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`):
- App Id Fusion: introdu ID-ul tău de la dashboard Photon (gratuit la https://dashboard.photonengine.com/)
- Region: `eu` (sau auto)

### Procedura de Test

#### Pasul 1: Test doar în Editor (single client)
1. Run Play în Editor.
2. Verifică Console:
   - `[FusionNetwork] Starting Fusion session 'EchoRealm'...`
   - `[FusionNetwork] Session started! Master: True`
3. UI status afișează: `Photon: connected [MASTER]`, `Players: 1`
4. Click „Spawn Cube" → cubul apare roșu deasupra SceneRoot.
5. Click „Randomize" → cubul sare la poziții random în jurul anchorului.

✅ **Pass:** cubul spawnează, e interactiv. Oprește Play.

#### Pasul 2: Deploy pe HoloLens
Urmează ghidul `Deploy-HoloLens.md`. Rezultat: ai appul pe HoloLens.

#### Pasul 3: Test 2-client sync (HoloLens = player 1, Editor = player 2)
1. **HoloLens:** pornește appul. Scanează QR. UI arată: `Photon: connected [MASTER]`, `Players: 1`.
2. **PC:** Unity Editor → Play. După 2-3 secunde ambele ar trebui să arate `Players: 2`.
3. **HoloLens (master):** apasă „Spawn Cube" (air-tap pe buton).
4. **Editor:** cubul roșu apare în același loc relativ la SceneRoot.
5. **HoloLens:** apucă cubul cu mâna, mișcă-l.
6. **Editor:** vezi cubul mișcându-se în timp real. ✅ **Sync funcționează!**
7. **HoloLens:** apasă „Randomize" → ambele văd cubul sărind.
8. **HoloLens:** apasă „Despawn" → cubul dispare pe ambele.

### Criterii de succes
- Latența sub 200ms la mișcare (vizual fluent)
- Nu apar 2 cuburi (duplicare de spawn)
- Dacă închizi Editor-ul, `Players: 1` se actualizează pe HoloLens

### Dacă pică
| Simptom | Cauză probabilă |
|---------|-----------------|
| `Players: 1` pe ambele, nu se găsesc | App Id Photon lipsă sau regiuni diferite |
| Cubul apare pe HoloLens dar nu în Editor | Prefab-ul nu e înregistrat ca NetworkObject (lipsește componenta) |
| Cubul se mișcă pe master dar nu pe client | Lipsește NetworkTransform pe prefab |
| „Failed to start session" | Internet blocat sau firewall |

---

## Test 2: Ollama End-to-End (doar Editor)

### Obiectiv
Un text prompt introdus în Editor → ajunge la Ollama → primește JSON valid → CommandExecutor îl interpretează corect.

### Pre-check (pe PC, înainte de test)
```powershell
# Verifică Ollama rulează pe 11500
Invoke-RestMethod -Uri "http://127.0.0.1:11500/api/tags"
```
Răspunsul trebuie să listeze `llama3.2:3b`. Dacă nu:
```powershell
$env:OLLAMA_HOST="127.0.0.1:11500"
ollama serve
# (în alt terminal)
ollama pull llama3.2:3b
```

### Setup în Unity
În MainScene, pe GameObject-ul `AISystem` (sau creează-l):
1. Atașează `OllamaClient.cs` (verifică `serverUrl = http://127.0.0.1:11500`, `model = llama3.2:3b`)
2. Atașează `DebugVoiceInput.cs` (editor-only)

Opțional (pentru test full pipeline):
3. `VoiceCommandProcessor.cs`
4. `CommandExecutor.cs` + referințe către `WeatherController`, `SkyboxController` etc.

### Procedura

#### Test 2A: Raw Ollama call (cel mai simplu)
1. Play în Editor.
2. Selectează `AISystem` în Hierarchy.
3. În Inspector la `DebugVoiceInput`:
   - `Test Prompt` = `"Fă să plouă și să se întunece brusc"`
4. Click-dreapta pe component → **Send Debug Command**.
5. Console ar trebui să arate:
   ```
   [OllamaClient] Sending request to http://127.0.0.1:11500/api/generate
   [OllamaClient] Response received (duration: 15-35s):
   {
     "commands": ["rain", "night"],
     "consequence": "Cerul se întunecă brusc și picăturile încep să cadă...",
     "dobby_dialogue": "Nu-mi place ploaia, stăpâne!",
     "mood": "scared"
   }
   ```

✅ **Pass:** JSON valid primit, toate câmpurile completate (nu null).

#### Test 2B: Pipeline complet (cu CommandExecutor)
Cerință: scripturile `WeatherController`, `SkyboxController`, `CommandExecutor` atașate în scenă.

1. Play în Editor.
2. Trimite comanda debug (ca la 2A).
3. Verifică:
   - `[CommandExecutor] Executing: rain` → `[WeatherController] StartRain()` → particle system rain activ în scenă
   - `[CommandExecutor] Executing: night` → `[SkyboxController] TransitionToNight()` → skybox se întunecă treptat
   - `[DobbyController] ShowDialogue("Nu-mi place ploaia...")` → text apare în scenă (dacă ai DobbyController activ)

✅ **Pass:** efectele se activează conform JSON-ului.

### Prompturi de test recomandate
| Prompt (RO) | Comenzi așteptate |
|-------------|-------------------|
| „Fă să plouă" | `["rain"]` |
| „Vreau foc și fum" | `["fire", "fog"]` sau `["fire"]` |
| „Noapte cu fulgere" | `["night", "lightning"]` |
| „Dobby, dansează!" | `["dobby_dance"]` |
| „Să apară fluturi coloraţi" | `["spawn_butterflies"]` |
| „Cutremur!" | `["earthquake"]` |

### Benchmark așteptat (Llama 3.2 3B pe CPU)
- Primul call (cold start): 30–60s (încarcă modelul în RAM)
- Call-uri ulterioare: 15–35s
- Dacă ai GPU NVIDIA cu CUDA: 3–8s

---

## Checklist — Faza 0 completă

- [ ] Test 1 Pas 1 (Editor single) pass
- [ ] HoloLens deploy reușit
- [ ] Test 1 Pas 3 (sync cu 2 clients) pass
- [ ] Test 2A (raw Ollama) pass
- [ ] Test 2B (pipeline complet) pass
- [ ] Latență sync < 200ms
- [ ] Latență Ollama < 35s pe prompturi normale

Când toate bifate → **poți trece la conținut (efecte, personaje, animații).**
