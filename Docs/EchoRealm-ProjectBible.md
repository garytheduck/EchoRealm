# EchoRealm — Project Bible
## Disertatie: Film Interactiv Adaptiv in Realitate Mixta cu AI pe Microsoft HoloLens 2

**Autor:** Samuel Dascalu
**Coordonator stiintific:** Prof. Univ. Dr. Ing. Radu-Daniel Vatavu
**Data inceput:** Aprilie 2026
**Baza:** Lucrare de licenta — "Dezvoltarea unei aplicatii de tip film interactiv in realitatea mixta folosind Microsoft HoloLens 2"

---

## 1. Viziune generala

EchoRealm este o experienta de film interactiv in realitate mixta (MR) pentru Microsoft HoloLens 2, in care mai multi utilizatori partajeaza aceeasi scena holografica si o pot transforma prin voce, gesturi si privire. Inteligenta artificiala (un LLM local) interpreteaza comenzile vocale naturale ale utilizatorilor si adapteaza lumea, dialogurile si narativa in timp real.

**Titlul complet al disertatie (propunere):**
"EchoRealm: Film Interactiv Adaptiv in Realitate Mixta cu Narativa si Control Vocal Bazate pe Inteligenta Artificiala pe Microsoft HoloLens 2"

**Titlul in engleza (propunere):**
"EchoRealm: Adaptive Interactive Mixed Reality Film with AI-Driven Narrative and Voice Control on Microsoft HoloLens 2"

---

## 2. Ce e nou fata de licenta

| Licenta (2024) | Disertatie — EchoRealm (2026) |
|----------------|-------------------------------|
| Photon PUN 2 (probleme de sync) | Photon Fusion 2 (Shared Mode, sync automat) |
| Fara anchor spatial comun | QR Code anchoring (Microsoft.MixedReality.QR) |
| Filmul era identic de fiecare data | AI genereaza dialog unic, naratie adaptiva |
| Interactiune doar cu gesturi | Voce naturala transforma lumea |
| Un singur deznodamant | Multiple ramificatii narative, final unic |
| Fara interpretare de comportament | AI reactioneaza la pattern-urile utilizatorilor |
| Text fix pentru personaje | Personaje cu dialog generat de AI |

---

## 3. Stack tehnologic

### Hardware
- **Microsoft HoloLens 2** (minim 2 dispozitive)
- **PC** (32 GB+ RAM, Windows 10) — ruleaza Ollama si poate servi ca server

### Software — Development
| Component | Versiune | Scop |
|-----------|----------|------|
| Unity Editor | 2022.3.62f3 LTS | Motor de joc, scena 3D, animatii |
| MRTK3 (Mixed Reality Toolkit 3) | Latest | Gesturi, eye tracking, UI holografic |
| Photon Fusion 2 | Free tier (20 CCU) | Sincronizare multi-HoloLens |
| QR Code Tracking | Integrat in OpenXR 1.11.2 | Detectie QR code pt anchor spatial (nu mai e pachet separat) |
| Ollama | v0.20.0+ | Server LLM local |
| Llama 3.1 8B | 4.9 GB | Model AI pentru narativa si interpretare comenzi |
| Visual Studio 2026 Community | v18.4.3 | IDE, build UWP |
| Mixed Reality OpenXR Plugin | 1.11.2 (via scoped registry npm) | OpenXR + QR Code tracking |

### Software — Runtime pe HoloLens
| Component | Scop |
|-----------|------|
| OpenXR | Standard XR, comunicare cu HoloLens |
| MRTK3 Runtime | Input (maini, ochi, voce) |
| Photon Fusion 2 Runtime | Networking intre dispozitive |
| Windows Speech Recognition | Speech-to-text (built-in, gratuit) |

### Software — Runtime pe PC
| Component | Scop |
|-----------|------|
| Ollama (localhost:11434) | Server API pentru LLM |
| Llama 3.1 8B | Procesare comenzi vocale, generare naratie |

### Platforme de build
- Universal Windows Platform (UWP)
- Architecture: ARM 64-bit
- Build Type: D3D Project
- Target SDK: Latest Installed

### Cost total: 0 RON (totul e gratuit/open source)

---

## 4. Arhitectura sistemului

```
┌─────────────────────┐     ┌─────────────────────┐
│   HoloLens 2 (#1)   │     │   HoloLens 2 (#2)   │
│                      │     │                      │
│  - MRTK3 (gesturi,   │     │  - MRTK3 (gesturi,   │
│    eye tracking)     │     │    eye tracking)     │
│  - Windows Speech    │     │  - Windows Speech    │
│    Recognition       │     │    Recognition       │
│  - Photon Fusion 2   │     │  - Photon Fusion 2   │
│    (client)          │     │    (client)          │
│  - QR Anchor         │     │  - QR Anchor         │
│    (scan la start)   │     │    (scan la start)   │
└──────────┬───────────┘     └──────────┬───────────┘
           │          Photon Cloud            │
           └──────────────┬──────────────────┘
                          │
                          ▼
              ┌───────────────────────┐
              │   Unity Master Client │
              │   (unul din HoloLens  │
              │    sau PC separat)    │
              │                       │
              │  Trimite la Ollama:   │
              │  - comenzi vocale     │
              │  - actiuni utilizatori│
              │  - context scena      │
              └───────────┬───────────┘
                          │ HTTP POST
                          ▼
              ┌───────────────────────┐
              │   PC — Ollama Server  │
              │   localhost:11434     │
              │                       │
              │   Llama 3.1 8B        │
              │                       │
              │   Returneaza:         │
              │   - comenzi pt Unity  │
              │   - dialog generat    │
              │   - naratie adaptiva  │
              │   - consecinte logice │
              └───────────────────────┘
```

### Flux de date detaliat

1. **Start sesiune:**
   - Ambele HoloLens-uri scaneaza acelasi QR code fizic → stabilesc origine comuna
   - Photon Fusion 2 conecteaza dispozitivele in aceeasi Room
   - Scena apare in acelasi loc fizic pe ambele dispozitive

2. **In timpul filmului:**
   - Utilizatorul vorbeste → Windows Speech Recognition → text
   - Textul + contextul scenei → HTTP POST la Ollama
   - Ollama raspunde cu JSON: comenzi, dialog, consecinte
   - Unity Master Client executa comenzile
   - Photon Fusion 2 sincronizeaza pe toate HoloLens-urile

3. **La finalul filmului:**
   - Ollama primeste rezumatul complet (ce comenzi, ce alegeri, cat timp, cooperare)
   - Genereaza monolog final unic al Oracolului
   - Fiecare sesiune are un final diferit

---

## 5. Povestea — EchoRealm

### Premise

Astronautul din filmul de licenta a trecut prin portal, dar nu a ajuns acasa. A ajuns in **EchoRealm** — o dimensiune instabila unde realitatea raspunde la vocea si actiunile celor prezenti. Dobby a sarit dupa el prin portal.

### Personaje

| Personaj | Rol | Reprezentare |
|----------|-----|-------------|
| **Dobby** | Protagonist, ghid | Model 3D animat (din licenta, reutilizat) |
| **Astronautul** | Co-protagonist | Model 3D animat (din licenta, reutilizat) |
| **Oracolul** | Narator, ghid misterios | Sfera luminoasa cu particule, pulseaza cand vorbeste |
| **Paianjenul** | Personaj secundar optional | Model 3D animat (din licenta, reutilizat) |

### Structura pe acte

#### Actul 1 — Trezirea (30-45 secunde)
- Astronautul si Dobby se trezesc intr-un spatiu gol, cenusiu
- Oracolul (sfera luminoasa) apare si le explica:
  - "EchoRealm asculta. Vocea voastra transforma aceasta lume."
  - "Gasiti Ecoul Originar ca sa va intoarceti acasa."
- **Tehnic:** Photon sync confirmat, QR anchor activ, toti vad aceeasi scena

#### Actul 2 — Lumea raspunde (2-3 minute)
- Utilizatorii descopera ca pot **vorbi** si lumea se transforma
- Oracolul ii incurajeaza: "Spuneti ce vreti sa vedeti. EchoRealm asculta."
- Exemple de comenzi si efecte:
  - "Make it rain" → particule de ploaie
  - "I want a forest" → copaci apar cu animatie de crestere
  - "Light the way" → globuri luminoase apar pe o carare
  - "Dobby, dance" → Dobby danseaza
  - "Make it night" → skybox trece la noapte, apar stele
  - "Earthquake" → camera shake, particule de praf
  - "Release butterflies" → fluturi animati zboara prin scena
  - "Set it on fire" → foc + consecinta: Dobby se sperie, drumul se blocheaza
- **AI interpreteaza** orice formulare naturala si mapeaza la comenzile disponibile
- **AI decide consecinte** logice (focul sperie personajele, ploaia face sa creasca plante)

#### Actul 3 — Provocarea cooperativa (1-2 minute)
- Apare un obstacol (piatra uriasa, poarta inchisa, pod rupt)
- Rezolvarea necesita **cooperarea** celor doi utilizatori:
  - Unul priveste un obiect (eye tracking) + celalalt il muta (gest)
  - Sau unul da comanda vocala + celalalt executa gestul
- Daca nu reusesc, **AI-ul genereaza hint-uri adaptate** la ce au incercat
- Daca reusesc rapid, **AI-ul complica** (adauga un al doilea obstacol)

#### Actul 4 — Ecoul Originar (30-45 secunde)
- Dupa rezolvarea provocarii, apare portalul final
- Oracolul rosteste un **monolog final unic** generat de AI:
  - Reflecta comenzile date, alegerile facute, timpul petrecut
  - Ton adaptat: misterios, vesel, melancolic — in functie de sesiune
- Astronautul trece prin portal, Dobby face cu mana
- Ecranul se estompeaza

### Durata totala estimata: 4-6 minute per sesiune

---

## 6. Rolul AI-ului (Ollama + Llama 3.1 8B)

### Ce face AI-ul (valoare reala, nu if/else)

#### A. Interpretare voce naturala → comenzi Unity
- Utilizatorul poate spune orice in limbaj natural
- AI-ul intelege intentia si mapeaza la comenzile Unity disponibile
- Exemplu: "fa sa ploua", "I want rain", "pune apa din cer" → toate = comanda "rain"
- Un if/else NU poate face asta — sunt prea multe formulari posibile

#### B. Consecinte narative logice
- AI-ul nu doar activeaza efecte, ci decide si ce se intampla dupa
- "Set everything on fire" → foc + Dobby se sperie + drumul se blocheaza
- "Plant flowers" → flori cresc + fluturi apar + Dobby e fericit
- Consecintele sunt contextuale, nu predefinite

#### C. Dialog generat unic
- Personajele (Dobby, Oracol) vorbesc diferit de fiecare data
- Raspunsurile reflecta ce s-a intamplat in sesiune
- Imposibil de pre-scris pentru toate combinatiile

#### D. Hint-uri adaptate
- Daca utilizatorii se blocheaza, AI-ul genereaza indicii bazate pe CE au incercat
- Nu un hint generic, ci specific: "Ati incercat sa mutati piatra, dar nu ati privit simbolul de pe ea"

#### E. Final unic per sesiune
- Monologul final al Oracolului e generat de AI
- Reflecta: comenzile date, cooperarea, timpul, alegerile
- Fiecare grup de utilizatori primeste un final diferit

### Ce NU face AI-ul
- Nu genereaza obiecte 3D sau animatii noi (sunt pre-facute in Unity)
- Nu ruleaza pe HoloLens (ruleaza pe PC via Ollama)
- Nu face procesare continua — doar 5-10 apeluri pe sesiune, fiecare 2-5 secunde

### Format comunicare Unity ↔ Ollama

**Request (Unity → Ollama):**
```json
{
  "model": "llama3.1:8b",
  "prompt": "You are the AI director of EchoRealm, a mixed reality experience. Available commands: [rain, night, day, fire, wind, earthquake, open_path, close_path, dobby_dance, dobby_wave, dobby_scared, astronaut_jump, spawn_butterflies, spawn_fireflies, lightning, grow_tree, grow_flowers, shrink_scene, glow_objects, fog]. Current scene state: night, forest visible, no rain. User said: 'I want to see fireflies and make Dobby dance'. Return ONLY valid JSON.",
  "format": "json",
  "stream": false
}
```

**Response (Ollama → Unity):**
```json
{
  "commands": ["spawn_fireflies", "dobby_dance"],
  "consequence": "Dobby starts dancing among the fireflies, creating a magical atmosphere",
  "dobby_dialogue": "Oh, these lights are beautiful! Watch me dance with them!",
  "mood": "joyful"
}
```

---

## 7. Sincronizare multi-HoloLens

### Problema din licenta
- Photon PUN 2 sincroniza starea retelei, dar fiecare HoloLens avea propria origine spatiala
- Rezultat: utilizatorii vedeau scena in pozitii fizice diferite in camera
- Azure Spatial Anchors si HoloLens Sharing Service — ambele deprecate de Microsoft

### Solutia in EchoRealm

#### Pasul 1: QR Code Anchoring
1. Se printeaza un cod QR pe hartie si se plaseaza in camera
2. Primul HoloLens (master) scaneaza QR-ul → stabileste originea lumii 3D
3. Al doilea HoloLens scaneaza acelasi QR → adopta aceeasi origine
4. Ambele dispozitive au acum un sistem de coordonate comun

**Pachet:** QR Code tracking este integrat direct in Mixed Reality OpenXR Plugin 1.11.2+ (nu mai necesita pachet separat; Mixed Reality Feature Tool a fost DISCONTINUAT de Microsoft in 2026)

#### Pasul 2: Photon Fusion 2 (Shared Mode)
- Inlocuieste PUN 2
- Shared Mode: toate dispozitivele partajeaza aceeasi stare
- Sincronizare automata a transformarilor (pozitie, rotatie, scala)
- Late joiner support: un dispozitiv care se conecteaza mai tarziu primeste starea curenta
- RPC-uri pentru comenzi AI (master client trimite la Ollama, rezultatul se sync pe toate)

#### Flux complet de conectare
```
1. Ambele HoloLens pornesc aplicatia EchoRealm
2. Ambele scaneaza QR code-ul fizic din camera
3. Photon Fusion 2 conecteaza ambele in Room "EchoRealm"
4. Master client (primul conectat) trimite "scena gata" via Photon
5. Filmul incepe simultan pe ambele dispozitive
6. Toate efectele, animatiile, dialogurile sunt sincronizate
```

---

## 8. Interactiuni utilizator

### Voce (principal — noutate EchoRealm)
- Microfon HoloLens 2 → Windows Speech Recognition → text
- Text trimis la Ollama → interpretare → comenzi Unity
- Orice limba (romana, engleza) — Ollama intelege ambele

### Gesturi (MRTK3 Hand Tracking)
- **Pinch & Drag:** muta obiecte in scena
- **Pinch & Scale:** mareste/micsoreaza scena
- **Pinch & Rotate:** roteste perspectiva
- **Air Tap:** selecteaza obiecte, activeaza butoane
- **Cooperare:** un utilizator tine un obiect, celalalt il muta

### Privire (MRTK3 Eye Tracking)
- Detecteaza spre ce se uita utilizatorul
- Folosit pentru:
  - Alegeri implicite (privesti mai mult spre o optiune = alegere)
  - Date pentru AI (ce a atras atentia utilizatorului)
  - Activare interactiuni (priveste + gest = actiune)

### ObjectManipulator (MRTK3)
- Intreg SceneRoot poate fi manipulat
- Utilizatorul poate vedea filmul din orice unghi
- Poate scala scena (miniatura pe masa sau full-size in camera)

---

## 9. Efecte si animatii pre-construite in Unity

### Lista de efecte disponibile (activate de AI prin comenzi)

| Comanda AI | Efect Unity | Tip |
|------------|-------------|-----|
| rain | Particle System — picaturi de ploaie | Particule |
| night | Skybox transition la noapte + stele | Shader/Material |
| day | Skybox transition la zi + soare | Shader/Material |
| fire | Particle System — flacari + lumina portocalie | Particule + Lumina |
| wind | Animatie arbori + particule de frunze | Animatie + Particule |
| earthquake | Camera shake + particule praf + obiecte se misca | Script + Particule |
| open_path | Animatie pietre se muta, cale se deschide | Animatie |
| close_path | Animatie pietre se inchid, cale blocata | Animatie |
| spawn_butterflies | Prefab fluturi animati instantiati | Prefab + Animatie |
| spawn_fireflies | Particule luminoase mici, miscare aleatorie | Particule |
| lightning | Flash de lumina + sunet tunet | Lumina + Audio |
| grow_tree | Animatie copac creste din pamant | Animatie de scala |
| grow_flowers | Animatie flori apar pe sol | Animatie |
| fog | Post-processing fog + particule | Shader + Particule |
| glow_objects | Material emission creste pe obiecte | Shader |
| shrink_scene | Scena se micsoreaza (perspectiva miniatura) | Animatie de scala |

### Animatii personaje

| Personaj | Animatii disponibile |
|----------|---------------------|
| Dobby | idle, walk, run, dance, wave, scared, point, sit, celebrate |
| Astronaut | idle, walk, jump, look_around, wave, enter_portal |
| Oracol (sfera) | pulse_calm, pulse_excited, pulse_mysterious, pulse_warning |
| Paianjen | idle, crawl, point_direction |

---

## 10. Structura proiect Unity

```
EchoRealm/
├── Assets/
│   ├── Scenes/
│   │   └── MainScene.unity
│   ├── Scripts/
│   │   ├── Networking/
│   │   │   ├── QRAnchorManager.cs        — detectie QR, setare origine
│   │   │   ├── FusionNetworkManager.cs   — conectare Photon Fusion 2
│   │   │   └── SharedSceneManager.cs     — sync stare scena
│   │   ├── AI/
│   │   │   ├── OllamaClient.cs           — HTTP client pentru Ollama API
│   │   │   ├── VoiceCommandProcessor.cs  — speech-to-text + trimitere la AI
│   │   │   ├── NarrativeManager.cs       — logica narativa, context management
│   │   │   └── CommandExecutor.cs        — executa comenzile primite de la AI
│   │   ├── Interaction/
│   │   │   ├── EyeTrackingManager.cs     — date eye tracking, alegeri implicite
│   │   │   ├── GestureManager.cs         — interactiuni gestuale
│   │   │   └── CooperationDetector.cs    — detecteaza cooperarea intre utilizatori
│   │   ├── Characters/
│   │   │   ├── DobbyController.cs        — logica personaj Dobby
│   │   │   ├── AstronautController.cs    — logica personaj astronaut
│   │   │   └── OracleController.cs       — logica Oracol (sfera)
│   │   ├── Effects/
│   │   │   ├── WeatherController.cs      — ploaie, noapte, zi, ceata
│   │   │   ├── EnvironmentController.cs  — copaci, flori, fluturi
│   │   │   └── CameraEffects.cs          — shake, tranzitii
│   │   └── Film/
│   │       ├── FilmDirector.cs           — orchestreaza actele filmului
│   │       ├── ActManager.cs             — logica per act
│   │       └── SessionLogger.cs          — logheaza actiuni pt AI context
│   ├── Prefabs/
│   │   ├── Characters/
│   │   ├── Effects/
│   │   ├── Environment/
│   │   └── UI/
│   ├── Animations/
│   │   ├── Dobby/
│   │   ├── Astronaut/
│   │   └── Oracle/
│   ├── Materials/
│   ├── Audio/
│   │   ├── Music/
│   │   ├── SFX/
│   │   └── Voice/
│   ├── Textures/
│   └── Shaders/
├── Packages/                              — MRTK3, Photon Fusion 2, etc.
├── ProjectSettings/
└── Build/                                 — output UWP
```

---

## 11. Evaluare si user study (propunere)

### Design experimental
- **Grup A (control):** Film cu interactiuni predefinite, text fix, fara AI
- **Grup B (experimental):** Film cu AI — voce transforma lumea, dialog unic
- **Participanti:** 10-20 persoane (in perechi, cate 2 pe HoloLens)

### Metrici masurate
| Metrica | Instrument | Ce masoara |
|---------|-----------|------------|
| Prezenta (presence) | IPQ Questionnaire | Cat de "acolo" s-a simtit utilizatorul |
| Usability | SUS (System Usability Scale) | Cat de usor e de folosit |
| Imersiune | Custom questionnaire | Cat de captivanta a fost experienta |
| Engagement | Session logs (nr. comenzi, timp interactiune) | Cat de activ a fost utilizatorul |
| Cooperare | CooperationDetector logs | Cat de mult au colaborat |
| Unicitate perceputa | Post-interview | Au simtit ca experienta e unica? |

### Ipoteze de testat
- H1: Grupul B (cu AI) raporteaza imersiune mai mare decat Grupul A
- H2: Grupul B da mai multe comenzi vocale si interactioneaza mai mult
- H3: Grupul B percepe experienta ca "unica" semnificativ mai des

---

## 12. Planificare si pasi urmatori

### Faza 1 — Setup (Aprilie 2026) [COMPLET ~90%]
- [x] Instalare Unity 2022.3.62f3 LTS + UWP Build Support
- [x] Instalare Visual Studio 2026 Community (v18.4.3) + UWP workload
- [x] Instalare Ollama v0.20.0+ + pull Llama 3.1 8B (4.9 GB)
- [x] Git repo (https://github.com/garytheduck/EchoRealm) + .gitignore
- [x] Documentare proiect (Project Bible + Setup Log)
- [x] Creare proiect Unity "EchoRealm" (3D Built-In Render Pipeline)
- [x] Configurare UWP (ARM64, D3D) + OpenXR (HoloLens feature group, Hand Tracking, Eye Gaze)
- [x] Import MRTK3 (7 pachete via GitHub tarballs — Feature Tool discontinuat!)
- [x] Import Mixed Reality OpenXR Plugin 1.11.2 (via scoped registry Azure DevOps)
- [x] Import Photon Fusion 2 (Shared Mode, App ID configurat, 20 CCU free)
- [x] QR Code tracking — integrat in OpenXR 1.11.2 (NU mai e pachet separat)
- [x] Structura foldere Assets creata (Scripts, Prefabs, Animations, Materials, etc.)
- [x] 2 commit-uri pe GitHub (setup initial + Photon Fusion 2)
- [ ] Configurare Player Settings capabilities (InternetClient, Microphone, SpatialPerception, WebCam, GazeInput)
- [ ] Creare MainScene cu MRTK XR Rig
- [ ] Testare build UWP pe HoloLens

### Faza 2 — Networking (Mai 2026)
- [ ] Implementare QRAnchorManager.cs
- [ ] Implementare FusionNetworkManager.cs
- [ ] Testare sync pe 2 HoloLens-uri
- [ ] Verificare ca scena apare in acelasi loc fizic

### Faza 3 — Scena si animatii (Mai-Iunie 2026)
- [ ] Construire scena EchoRealm in Unity
- [ ] Import/creare assets (Dobby, Astronaut, Oracol, mediu)
- [ ] Creare toate animatiile si efectele din tabelul de comenzi
- [ ] Configurare Animator Controllers
- [ ] Testare efecte individual

### Faza 4 — Integrare AI (Iunie-Iulie 2026)
- [ ] Implementare OllamaClient.cs (HTTP communication)
- [ ] Implementare VoiceCommandProcessor.cs (speech-to-text)
- [ ] Implementare CommandExecutor.cs (mapare comenzi → efecte Unity)
- [ ] Implementare NarrativeManager.cs (context tracking, naratie)
- [ ] Prompt engineering pentru Llama 3.1 (optimizare raspunsuri)
- [ ] Testare end-to-end: voce → AI → efect vizual

### Faza 5 — Film complet (Iulie-August 2026)
- [ ] Implementare FlowDirector si ActManager
- [ ] Integrare toate componentele (networking + AI + efecte + personaje)
- [ ] Testare pe 2 HoloLens-uri — film complet
- [ ] Iteratie si polish

### Faza 6 — Evaluare (Septembrie 2026)
- [ ] Pregatire user study (chestionare, protocol)
- [ ] Recrutare participanti (10-20 persoane)
- [ ] Rulare sesiuni de testare
- [ ] Colectare si analiza date
- [ ] Scriere rezultate

### Faza 7 — Scriere disertatie (Octombrie-Noiembrie 2026)
- [ ] Scriere capitole disertatie
- [ ] Revizuire cu Prof. Vatavu
- [ ] Pregatire prezentare

---

## 13. Documente interne

- **[SetupLog-ProblemeIntampinate.md](SetupLog-ProblemeIntampinate.md)** — Log detaliat al procesului de instalare, toate problemele intampinate si solutiile gasite (inclusiv discontinuarea Mixed Reality Feature Tool)

---

## 14. Referinte cheie

- [MRTK3 Documentation](https://learn.microsoft.com/en-us/windows/mixed-reality/mrtk-unity/mrtk3-overview/)
- [Photon Fusion 2 Documentation](https://doc.photonengine.com/fusion/current/getting-started/fusion-intro)
- [Microsoft.MixedReality.QR](https://learn.microsoft.com/en-us/windows/mixed-reality/develop/advanced-concepts/qr-code-tracking-overview)
- [Ollama API Documentation](https://github.com/ollama/ollama/blob/main/docs/api.md)
- [Unity 2022.3 LTS](https://unity.com/releases/editor/qa/lts-releases)
- [OpenXR Standard](https://www.khronos.org/openxr/)

---

*Acest document este "Project Bible" — sursa de adevar pentru tot ce tine de EchoRealm. Se actualizeaza pe parcursul dezvoltarii.*
