# Piano di Implementazione: Evoluzione in 2.5D Action-Roguevania

Questo documento formalizza le istruzioni tecniche e architetturali per evolvere il progetto da prototipo lineare a **2.5D Action-Roguevania ad anello**, integrando i pattern di *Prince of Persia: The Lost Crown*, *Nine Sols* e *Dead Cells*.

---

## 🏛️ Decisioni Architetturali Fondative

1. **Rifiuto del Backtracking a Stanze Rigenerate**: Nessun tentativo di far ricordare lo stato interno a stanze procedurali effimere. Le stanze rimangono leggere e rigenerabili al volo tramite `DungeonRoomBuilder.RebuildAs(index)`.
2. **Conservazione dello Spatial Metric Contract**: Tutte e 4 le abilità di movimento (Double Jump, Dash, Wall Jump, Charge Attack) rimangono native e sbloccate fin dal frame 1.
3. **Progressione Macro-DAG**: La progressione si articola su bivi di bioma e scorciatoie permanenti (`SaveData.WorldFlags`) aperte colpendo leve fisiche (`Latch`).
4. **Combat Telegrafato a 3 Canali**: Sistema semiotico chiaro (Bianco / Oro / Rosso) per valorizzare il parry da 140ms e rendere i duelli leggibili a 60 FPS.

---

## 🛠️ Modifiche Proposte per Sottosistema

### 1. Contratti e Governance

#### [MODIFY] [PROMPT.md](file:///c:/Users/Mako/Desktop/Gioco/PROMPT.md)
* Aggiornare la definizione del target: da "Metroidvania puro con backtracking" a "2.5D Action-Roguevania ad anello con 4 abilità native, generazione procedurale a chunk metrici, combattimento telegrafato e scorciatoie permanenti".

#### [MODIFY] [ARCHITECTURE.md](file:///c:/Users/Mako/Desktop/Gioco/ARCHITECTURE.md)
* Inserire la specifica di `AttackTelegraphType` (White / Gold / Red) nel sottosistema `Combat` e `AI`.
* Inserire il contratto del doppio portale di uscita (`BranchingExit`) nel sottosistema `World`.
* Formalizzare il dizionario `WorldFlags` nel sottosistema `Save`.

---

### 2. Sottosistema Combat & AI: Telegrafo a 3 Canali

#### [NEW] [AttackTelegraphType.cs](file:///c:/Users/Mako/Desktop/Gioco/game/src/Core/AttackTelegraphType.cs)
```csharp
namespace LostCrownlike.Core;

public enum AttackTelegraphType
{
    StandardWhite = 0,   // Attacco ordinario: parabile con parry standard (azzera danno, stagger minimo)
    CounterGold = 1,     // Attacco potente/chiave boss: perfetto parry obbligatorio (spezza armatura, 3x stagger)
    UnparryableRed = 2   // Attacco pesante/spazzata: imparabile (il parry fallisce; obbligo di Dash con i-frame o salto)
}
```

#### [MODIFY] [EnemyController.cs](file:///c:/Users/Mako/Desktop/Gioco/game/src/AI/EnemyController.cs)
* Aggiungere la proprietà `[Export] public AttackTelegraphType CurrentTelegraph { get; protected set; }`.
* Nel metodo `ApplyAttackDamage(Node3D target)`:
  * Inoltrare il tipo di telegrafo in `DamageInfo.TelegraphType`.
* Nel gestore `OnParried(bool perfect, Vector3 at)`:
  * Se `CurrentTelegraph == AttackTelegraphType.UnparryableRed`: ignorare il parry, infliggere danno pieno al player.
  * Se `CurrentTelegraph == AttackTelegraphType.CounterGold`: applicare stagger maggiorato (`StaggerDuration * 1.5f`) e camera punch.

#### [MODIFY] [Warden.cs](file:///c:/Users/Mako/Desktop/Gioco/game/src/AI/Warden.cs) & [Sentinel.cs](file:///c:/Users/Mako/Desktop/Gioco/game/src/AI/Sentinel.cs)
* Alternare il repertorio di colpi:
  * Attacco 1 (Fendente base): `StandardWhite` (windup 0.55s).
  * Attacco 2 (Schiacciata dall'alto): `CounterGold` (windup 0.85s, parry perfetto spezza l'armatura).
  * Attacco 3 (Spazzata rotante / Furia infuocata): `UnparryableRed` (windup 0.70s, richiede dash-through).

#### [MODIFY] [EnemyVisual.cs](file:///c:/Users/Mako/Desktop/Gioco/game/src/AI/EnemyVisual.cs)
* Aggiornare `SetGlow` per differenziare cromaticamente il flash:
  * `StandardWhite`: Bianco/Azzurro freddo (`Color(0.9f, 0.95f, 1.0f)`).
  * `CounterGold`: Oro splendente (`Color(1.0f, 0.85f, 0.2f)`, energia emissiva 6.0).
  * `UnparryableRed`: Rosso cremisi pulsante (`Color(1.0f, 0.15f, 0.15f)`, energia emissiva 5.0).

#### [MODIFY] [PlayerController.cs](file:///c:/Users/Mako/Desktop/Gioco/game/src/PlayerCamera/PlayerController.cs)
* In `TakeDamage`: verificare se il player era in finestra attiva di parry. Se l'attacco è `UnparryableRed`, il parry viene invalidato e viene emesso un segnale di "ParryBypassed".

---

### 3. Sottosistema World: Bivio di Bioma (Stanza 3)

#### [MODIFY] [RoomExitTrigger.cs](file:///c:/Users/Mako/Desktop/Gioco/game/src/World/RoomExitTrigger.cs)
* Aggiungere la proprietà `[Export] public string DestinationAct { get; set; } = "standard";`.
* In `OnBodyEntered`: includere la destinazione nell'evento `LevelTransitionRequested`.

#### [MODIFY] [DungeonRoomBuilder.cs](file:///c:/Users/Mako/Desktop/Gioco/game/src/World/DungeonRoomBuilder.cs)
* Nella Stanza 3 (transizione Atto I -> Atto II):
  * Generare un'architettura di fine livello a doppia altezza:
    * **Portale Inferiore (Livello pavimento)**: Conduce a *Catacombe Sommerse* (Atto II-A, blu/verde acqua, platforming).
    * **Portale Superiore (Terrazza alta, $Y = 4.5\text{m}$)**: Raggiungibile con Wall Jump. Conduce a *Crucibolo d'Ossidiana* (Atto II-B, cremisi/oro, alta intensità di combattimento ed élite).

---

### 4. Sottosistema Save: Scorciatoie Permanenti (`SaveData.WorldFlags`)

#### [MODIFY] [SaveData.cs](file:///c:/Users/Mako/Desktop/Gioco/game/src/Save/SaveData.cs)
* Aggiungere `[Export] public Godot.Collections.Dictionary<string, bool> WorldFlags { get; set; } = new();`.

#### [MODIFY] [SaveManager.cs](file:///c:/Users/Mako/Desktop/Gioco/game/src/Save/SaveManager.cs)
* Aggiungere metodi pubblici helper `SetFlag(string flag, bool value)` e `GetFlag(string flag)`.
* Alla percussione di una leva (`LeverSwitch`), se la leva ha `PersistentId`, salvare il flag.
* Nella Stanza 0: se `shortcut_act2` è true, istanziare un portale rapido per saltare direttamente all'Atto II.

---

## 🧪 Piano di Verifica e Collaudo

### 1. Test Automatizzati (Estensione di `tools/verify.sh`)
* **Check 64**: Test unitario/headless su `AttackTelegraphType`: verificare che un attacco `UnparryableRed` ignori la parata e infligga danno.
* **Check 65**: Test generatore Stanza 3: verificare la presenza di 2 nodi `RoomExitTrigger` validi e raggiungibili tramite `PlayerMetrics`.
* **Check 66**: Test persistenza `WorldFlags`: percussione leva -> salvataggio -> ricaricamento -> flag preservato su disco.

### 2. Verifica Manuale con Gamepad / Tastiera
* Esecuzione di una run completa:
  1. Affrontare il Sentinel e il Warden leggendo i lampi visivi (Bianco, Oro, Rosso).
  2. Arrivare alla Stanza 3 e scegliere tra Portale Inferiore e Portale Superiore.
  3. Verificare il feedback aptico e visivo del parry su colpi oro vs schivata su colpi rossi.
