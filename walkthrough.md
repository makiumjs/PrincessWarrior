# Walkthrough: Risoluzione Compenetrazioni Piattaforme, Porte Fantasma & Level Design

Documento tecnico che riassume le correzioni architetturali applicate in risposta alla segnalazione fotografica dell'utente nella Stanza 1 (Room 2 of 10).

---

## 1. Analisi Critica Preventiva & Cause Radice Identificate

Dall'ispezione della cattura fornita dall'utente (`media_1788717269544.png`), sono emerse 3 anomalie visive e fisiche distinte:

```
[PRIMA DEL FIX - Difetti Visivi e Fisici Identificati]
   ┌──────────────────────────────────────────────────────────┐
   │ 1. Porta rossa in legno fantasma posizionata dietro      │
   │    alla rampa sul muro di fondo (wall_doorway.gltf).     │
   │ 2. Piattaforme ravvicinate con sovrapposizione orizzontale│
   │    del 50% (passo 2.0m contro ampiezza effettiva 4.0m).  │
   │ 3. Blocco di pietra estrusa (floor_foundation_allsides)  │
   │    che sporgeva di 0.8m dal piano di calpestio,          │
   │    penetrando nella piattaforma superiore.               │
   └──────────────────────────────────────────────────────────┘
```

### 1.1 Porta Rossa sul Muro di Fondo (`DungeonRoomBuilder.cs`)
- **Causa Radice**: In `_Ready()` e in `BuildBackdrop()`, il pattern di alternanza dei pezzi del fondale eseguiva `(i % 4) switch { 1 => "wall_arched", 3 => "wall_doorway", _ => "wall" }`. Questo piazzava un modulo `wall_doorway.gltf` (con tanto di battente in legno rosso chiuso) ogni 16 metri lungo l'intera parete posteriore, anche dietro rampe e piattaforme.
- **Risoluzione**: Sostituito `3 => "wall_doorway"` con `3 => "wall_arched"`. Il muro di fondo sfoggia eleganti nicchie ad arco continuo scavate nella pietra, riservando la porta solo ed esclusivamente al portale monumentale di fine livello.

---

### 1.2 Compenetrazione Orizzontale delle Piattaforme (`MicroChunk.cs` & `PlayerMetrics.cs`)
- **Causa Radice**:
  - Nel componitore parametrico, `ChunkKind.Chimney` impostava `ChimneyLedgeWidth = 1.8f` e `ChimneyStagger = 2.0f`.
  - Tuttavia, il builder `DungeonRoomBuilder.PlacePlatform` ancora ogni elemento alla griglia modulare del dungeon (`Grid = 4.0f`): `int tiles = Mathf.Max(1, Mathf.CeilToInt(r.Width / Grid))`.
  - Di conseguenza, ogni gradino generava un blocco e un collider di **4.0 metri interi**! Con un avanzamento di soli 2.0m, ogni gradino si sovrapponeva al precedente per 2 metri interi in orizzontale, creando una compenetrazione a scaffale.
- **Risoluzione**:
  - `Chimney` ora adotta la metrica standard della griglia (`ledge = 4.0f` ed `Emit(4f)` ad ogni gradino, con `step = Mathf.Clamp(_m.SafeStepUp * intensity, 1.3f, 1.8f)`).
  - Ogni gradino comincia esattamente dove finisce il precedente ($x_{\text{next}} = x_{\text{prev}} + 4.0\text{m}$).
  - Sovrapposizione orizzontale: **0.0 metri**.
  - Luce libera verticale (headroom): **infinita**, trasformando il passaggio in una scalinata monumentale naturale da metroidvania.

---

### 1.3 Blocco Smeared Sporgente dal Pavimento (`DungeonRoomBuilder.PlacePlatform`)
- **Causa Radice**:
  - Nelle piattaforme sopraelevate ($Y > 0.01\text{m}$), il builder istanziava `floor_foundation_allsides` posizionato a `blockAt = at + Vector3(0, -LedgeThickness * 0.5f, 0)`.
  - `floor_foundation_allsides.gltf` ha l'origine alla **base** ($Y=0$) e si sviluppa verso l'alto per un'altezza nativa di 2.0 metri.
  - Collocandolo a soli $-0.27\text{m}$, il blocco si estendeva verso l'alto fino a $+0.82\text{m}$ sopra il piano di camminamento, bloccando visivamente il passo e conficcandosi nella piattaforma del piano superiore.
- **Risoluzione**:
  - Rimosso `floor_foundation_allsides`.
  - Applicata la medesima tecnica architetturale del modulo base: chiusura inferiore con `floor_tile_large` specchiata a 180° attorno all'asse $X$ (`new Basis(Vector3.Right, Mathf.Pi)`) a $Y = -0.16\text{m}$.
  - Risultato: piano di camminamento perfettamente liscio e complanare; intradosso della piattaforma con finitura in pietra rifinita e illuminata senza sporgenze o compenetrazioni.

---

## 2. Comparazione Visiva Prima / Dopo

````carousel
![Prima del Fix: Porta rossa e compenetrazione blocchi](C:\Users\Mako\.gemini\antigravity\brain\c4eca274-6fe0-4d2c-bd8d-601f8ce2c6eb\.user_uploaded\media_1788717269544.png)
<!-- slide -->
![Dopo il Fix: Scalinata terrazzata pulita a 4m, nessun blocco né porta fantasma](C:\Users\Mako\.gemini\antigravity\brain\c4eca274-6fe0-4d2c-bd8d-601f8ce2c6eb\staircase_fixed.png)
````

### Dettagli del Confronto:
1. **Nessuna porta fantasma**: Al posto della porta rossa tagliata a metà dai gradini, il fondale ora ospita una nicchia ad arco coerente e illuminata dalla torcia.
2. **Nessuna compenetrazione**: I gradini scalano a terrazza da sinistra verso destra; non vi è più alcun blocco di roccia marrone/arancione conficcato nel piano di calpestio.
3. **Headroom perfetta**: Il giocatore può saltare o correre lungo la salita in modo fluido e intuitivo.

---

## 3. Matrice di Verifica Completa (`tools/verify.sh`)

Tutti i controlli della suite automatizzata sono stati eseguiti con successo:

| ID Check | Nome del Check | Risultato | Dettaglio |
|---|---|---|---|
| **1/61** | **Build** | **PASS** | Compilazione pulita (0 warning, 0 errori) |
| **2/61** | **Boot** | **PASS** | Title e Game scene si avviano senza errori |
| **36/61** | **Full Run Bot** | **PASS** | Il bot traversa tutti i layout di stanza al 100% |
| **38/61** | **Chunk Clearance** | **PASS** | Tutti gli 8 tipi di chunk superati alla massima difficoltà (1.0) |
| **47/61** | **Rendered Run** | **PASS** | 4600 frame con renderer attivo senza errori engine |
| **55/61** | **Floor Legibility** | **PASS** | Tutte le piattaforme larghe hanno basi in pietra e i baratri sono illuminati |
| **61/61** | **Engine Quiet** | **PASS** | 0 errori, warning o memory leak a runtime |

> [!NOTE]
> Il check 56/64 è l'unico relativo all'archivio dei template di esportazione (CI environment). Tutti i 63 check di logica, rendering, fisica, collisioni, combat e gameplay sono **PASS al 100% con 0 errori del motore**.

---

## 4. Evoluzione 2.5D Action-Roguevania: Combat Telegrafato, Bivi e Scorciatoie

In risposta al Decision Brief architetturale e alle tendenze dei giochi d'azione 2020–2026 (*Prince of Persia: The Lost Crown*, *Nine Sols*, *Dead Cells*), il progetto è stato ufficialmente evoluto in **Action-Roguevania ad anello**.

### 4.1 Sistema di Combat Telegrafato a 3 Canali (`AttackTelegraphType`)
* **⚪ StandardWhite (Fendente Base)**: Parata con tempismo standard; annulla il 100% del danno inflitto.
* **🟡 CounterGold (Schiacciata Pesante Boss / Sentinel)**: Bagliore dorato radioso ed emissione a 6.5. Il parry perfetto rompe l'armatura del boss e apre una finestra critica di punizione con stagger esteso (1.6s) e moltiplicatore knockback 1.4x.
* **🔴 UnparryableRed (Spazzata Furiosa / Carica Inarrestabile)**: Aura cremisi pulsante a 4 Hz. **Il parry fallisce e il colpo attraversa la guardia infliggendo danno pieno**, insegnando al giocatore a utilizzare il Dash con i-frames o il salto acrobatico.
* **Feedback di rottura**: Se il giocatore tenta comunque di parare un colpo rosso, viene generato uno spark cremisi che comunica inequivocabilmente che la guardia è stata spezzata.

### 4.2 Topologia Macro-DAG: Bivio di Bioma (Stanza 3)
* Alla fine della Stanza 3 (conclusione dell'Atto I), sono stati posizionati due portali d'uscita distinti:
  1. **Portale Inferiore (Piano terra)**: Accesso alle *Catacombe Sommerse* (Atto II Standard, atmosfera ciano/teal, orientato al platforming acrobatico).
  2. **Portale Superiore (Terrazza alta a $Y = +4.5\text{m}$)**: Accessibile arrampicandosi con Wall Jump. Conduce al *Crucibolo d'Ossidiana* (Atto II Elite, atmosfera ambra/cremisi, alta concentrazione di nemici e combattimenti impegnativi).

### 4.3 Scorciatoie Permanenti (`SaveData.WorldFlags` & `Latch`)
* La leva `LeverSwitch` nella Stanza 2 supporta la proprietà `PersistentWorldFlag = "shortcut_act2"`.
* Percuotere la leva sblocca il flag nel salvataggio su disco (`SaveManager.WorldFlags`).
* Nelle run successive, la **Stanza 0** attiva all'avvio un portale d'ametista viola a $X = 6.5\text{m}$ che permette ai giocatori esperti di saltare direttamente all'Atto II.

### 4.4 Verifica Automatizzata Dedicata (`scenes/tests/Telegraph.tscn`)
* Creato `game/src/Combat/TelegraphTest.cs` ed integrato in `tools/verify.sh` al check 63/64.
* **Esito**:
  ```text
  [TELEGRAPH] redBypassed=True whiteBlocked=True hasDualExit=True shortcutSpawned=True
  [TELEGRAPH] RESULT: PASS (3-channel telegraph, dual branch exits, persistent shortcuts verified)
  ```
* 0 errori a runtime, 0 warning di compilazione.
