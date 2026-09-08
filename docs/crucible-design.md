# The Crucible — Design del bioma alternativo

**Modalità:** `@director` (Game Director & Systems Designer)
**Stato:** specifica di design, pronta per `@programmer` e `@techart`. Nessun codice C# in questo documento.
**Data:** 2026-09-07

---

## 0. Il problema che questo bioma risolve

Oggi il fork della Stanza 3 esiste solo come **cartello**. In `DungeonRoomBuilder.SpawnExit()` i due portali sono:

| Portale | Posizione | `DestinationActName` | Colore | `NextRoomIndex` |
|---|---|---|---|---|
| Terra | fine dell'ultimo chunk | `"Catacombs"` | `(0.20, 0.85, 0.80)` cyan | 4 |
| Terrazza (+4.5 m) | `terraceX + 6.0` | `"Crucible"` | `(1.00, 0.35, 0.15)` ambra-rosso | **4** |

Entrambi chiamano `BeginRebuild(4)`. **Il contenuto generato è identico.** Il ramo ha un nome, un colore e una salita, e nient'altro.

C'è poi un secondo problema, già diagnosticato nei commenti di `Warden.cs`:

> *"Every other type in the roster is beaten by movement... All four are answered with the same verb — get there and swing. That makes the parry decorative: it is never the cheapest option, so a player never has to learn it."*

La parata è la mossa-firma del gioco ed è obbligatoria solo due volte in dodici minuti (Sentinel alla Stanza 5, Warden alla Stanza 9).

**Tesi di design del Crucible:** il ramo alternativo non è "le Catacombe più difficili". È il ramo dove **la parata smette di essere opzionale e diventa l'economia della stanza**. Le Catacombe chiedono *dove sei*; il Crucible chiede *quando premi*.

| | Sunken Catacombs (terra) | The Crucible (terrazza) |
|---|---|---|
| Verbo dominante | attraversare | reggere |
| Pressione | spaziale (gap, altezza, timing dei chunk) | temporale (il Calore sale da solo) |
| Nemici per stanza | 7–9 | **4–5** |
| HP medio nemico | ~24 | **~49** |
| Danno medio per colpo | ~11 | **~15** (+22 per detonazione) |
| Chunk `Arena` per stanza | 1 | **4** |
| Checkpoint interni | 2–3 | **1** (solo l'ingresso) |
| Risposta al mashing | funziona | **punita meccanicamente** |

---

## 1. Struttura del ramo

**Estensione:** il Crucible sostituisce le **Stanze 4 e 5** (su `RunLength = 10`). I due rami riconvergono alla Stanza 6, che è identica su entrambi i percorsi.

Questa scelta rispetta due invarianti già nel codice e non ne rompe nessuna:

- `IsHalfwayRoom => RunLength > 3 && RoomIndex == RunLength / 2` → la Stanza 5 è già una stanza-boss sigillata su entrambi i rami. Il Crucible non introduce una struttura nuova: **sostituisce il Sentinel con una sua variante**.
- `IsSecondHalf(index, total)` → la Stanza 4 è prima metà, la 5 è seconda metà. Il Crucible usa un proprio set di layout e ignora questa distinzione, perché la sua progressione è di Calore, non di verbi.

| Stanza | Percorso Catacombe | Percorso Crucible |
|---|---|---|
| 3 | fork: portale a terra / portale in terrazza (+4.5 m) | — |
| 4 | `EarlyLayout(4)` | **`CrucibleLayout A` — "La Colata"** |
| 5 | `LateLayout(5)`, sigillata, **Sentinel** (55 HP) | **"La Fucina"**, sigillata, **Maestro di Fucina** (82 HP) |
| 6 | riconvergenza — identica | riconvergenza — identica |

### 1.1 Pedaggio d'ingresso

Il ramo si sceglie salendo 4.5 m su una parete arrampicabile: il giocatore **dimostra già il kit di movimento** per accedervi. Il pedaggio vero è un altro:

| Regola d'ingresso | Valore | Perché |
|---|---|---|
| Calore iniziale all'attraversamento del portale | **25** | non si entra "freddi": la pressione parte già accesa |
| Checkpoint nella Stanza 4 | **1 solo**, all'ingresso | morire nel Crucible costa l'intero ramo, non l'ultimo chunk |
| Checkpoint nella Stanza 5 | **0** (è una stanza-boss sigillata, come già la 5 delle Catacombe) | coerente con la struttura esistente |
| Cura passiva | **nessuna** | l'unica cura è la parata (§4) |

> **Nota di produzione.** `CheckpointTrigger` è ciò che rende possibile il loop morte/respawn nelle stanze generate (`SpawnCheckpoints` piazza uno shrine a ogni `EncounterPoint`). Ridurre a uno non è "togliere una feature": è la scommessa del ramo. Se in playtest risulta punitivo oltre il divertente, la leva da muovere è **due** checkpoint, non il ripristino del comportamento standard.

---

## 2. Meccanica centrale — Il Calore della Fornace

Un singolo valore `Heat ∈ [0, 100]`, di ambito stanza, azzerato-a-25 all'ingresso nel ramo e **trasportato dalla Stanza 4 alla Stanza 5**.

Il Calore sale da solo e scende solo se il giocatore combatte bene. Non è una barra di rabbia del nemico: è **lo stato della stanza**, e riusa i tre canali di telegraph che il gioco ha già.

### 2.1 Formula

```
Heat(t + Δt) = clamp( Heat(t) + G·Δt − ΣD , 0 , 100 )

G = G_base(stanza) + 2.4 · N_emberwright_vivi
```

| Termine | Valore |
|---|---|
| `G_base` Stanza 4 | **+2.80 Calore/s** |
| `G_base` Stanza 5 | **+3.20 Calore/s** |
| `+2.4/s` per ogni Emberwright vivo | additivo, per istanza |

**Guadagni istantanei (`+ΣD` negativi):**

| Evento | Δ Calore |
|---|---|
| Il giocatore subisce danno (qualsiasi fonte) | **+4** |
| Detonazione di un Emberhusk | **+10** |

**Drenaggi:**

| Evento | Δ Calore |
|---|---|
| Parata perfetta (`ParryResult.Perfect`) | **−12** |
| Parata bloccata (`ParryResult.Blocked`) | **−6** |
| Nemico ucciso (qualsiasi tipo) | **−8** |
| Emberwright ucciso | **−8 −15 = −23** totali |
| Sfiatatoio di scoria attivato (leva `Latch`) | **−25**, una volta per sfiatatoio |

> Lo sfiatatoio riusa `LatchGate` / `LeverSwitch` così com'è: una leva colpita con la spada. Nessun sistema nuovo, e dà al chunk `Latch` — oggi l'unico che chiede di **agire** sul livello — un secondo significato dentro il ramo di combattimento.

### 2.2 Verifica di sanità della curva

Una stanza è ~200 m; a `MoveSpeed = 8 m/s` sono ~25 s di corsa pura, e le arene sigillano finché non sono ripulite.

| Scenario | Calore netto | Tempo al Flashover da 25 |
|---|---|---|
| Il giocatore corre e non combatte (Stanza 4) | +2.80/s | **26.8 s** |
| Un Emberwright vivo, nessuna uccisione | +5.20/s | **14.4 s** |
| Combattimento normale (1 uccisione / 6 s, 1 parata / 4 s) | +2.80 − 1.33 − 3.00 = **−1.53/s** | mai |
| Combattimento sbagliato (0 parate, 1 colpo subito / 3 s) | +2.80 + 1.33 = **+4.13/s** | **18.1 s** |

Lo stato di riposo previsto è **Ravvivato (40–69)**: il Crucible è pensato per essere giocato con la barra a metà, non a zero. Chi la tiene a zero sta giocando meglio del previsto e va ricompensato con la fluidità, non con un buff.

### 2.3 Soglie e loro effetti

| Stato | Range | Promozione telegraph | Moltiplicatore `AttackCooldown` nemici | Suolo di scoria | Colore HUD |
|---|---|---|---|---|---|
| **Temprato** (Tempered) | 0–39 | nessuna: pattern nativo del tipo | ×1.00 | inattivo | `#6B4A32` ferro spento |
| **Ravvivato** (Kindled) | 40–69 | **ogni 2° attacco** promosso di un canale | ×0.90 | inattivo | `#E08A2C` ambra |
| **Incandescente** (Molten) | 70–99 | **ogni** attacco promosso di un canale | ×0.80 | **attivo** (§3) | `#FF5A26` arancio-rosso |
| **Deflagrazione** (Flashover) | 100 | vedi sotto | ×0.80 | attivo forzato 2.5 s | `#FFD24A` bianco-caldo, lampeggiante |

**Scala di promozione** (deterministica, mai casuale — il progetto ha già pagato tre volte il prezzo dei check resi flaky da un lancio di dado):

```
StandardWhite (0) → CounterGold (1) → UnparryableRed (2) → [cap: resta Red]
```

La promozione si applica in `SelectAttackTelegraph()`, **dopo** che il tipo ha scelto il proprio canale nativo. Il contatore `%2` di Ravvivato usa la sequenza d'attacco già presente in ogni tipo (`_attackSequence`, `_sentinelAttackSeq`), non un timer.

> **Perché questo è il cuore del design.** Il canale Rosso oggi esiste solo sul Warden infuriato. Qui, per la prima volta, un nemico normale può diventare non-parabile *perché il giocatore ha gestito male la stanza*. Il pericolo diventa una conseguenza delle proprie scelte, non una proprietà del mostro.

### 2.4 Deflagrazione (Flashover)

Al raggiungimento di 100:

| Effetto | Valore |
|---|---|
| Danno immediato al giocatore | **20** (rispetta `HurtInvulnerabilityDuration = 1.0 s`) |
| Calore dopo l'evento | **→ 60** (non 0: la punizione non regala una ripartenza pulita) |
| Finestra "tutto Rosso" | **2.5 s**, ogni attacco di ogni nemico è `UnparryableRed` |
| Suolo di scoria | forzato attivo per gli stessi 2.5 s, ignorando la soglia |
| Streak di parata | azzerato |

La Deflagrazione **non uccide da sola** un giocatore a piena vita: 20 danni su 100 HP. È un fallimento costoso e leggibile, non una morte improvvisa. Un giocatore che ne incassa cinque nel ramo è comunque morto, ed è giusto così.

---

## 3. Meccanica 2 — Il suolo di scoria

Attiva a **Calore ≥ 70**.

| Regola | Valore |
|---|---|
| Tempo di contatto a terra tollerato | **2.0 s** continui |
| Danno oltre la soglia | **4 danni/s**, tick ogni 0.5 s (2 danni per tick) |
| Reset del timer | qualsiasi frame **non** a terra |
| `WallSlide` conta come | **aria** (il timer si azzera) |
| `Dash` a terra conta come | terra (il dash non è una via d'uscita gratuita) |

**Perché esiste.** ARCHITECTURE.md dice esplicitamente che il problema del roster è che *"dash, double jump e wall jump sono decorazione durante un combattimento"*. Il suolo di scoria è la risposta diretta: nel Crucible il kit di movimento **è** parte del combattimento, e il wall jump — l'abilità più trascurata del gioco — diventa il modo migliore per resettare il timer restando in mischia.

**Requisito per `@techart`:** ogni arena del Crucible deve avere almeno **una parete arrampicabile per lato**, alta ≥ 3.0 m, entro 6.4 m (una lunghezza di dash) dal centro dell'arena. Senza questo, il suolo di scoria è una tassa e non una scelta. Vale come vincolo di layout tanto quanto il Spatial Metric Contract.

---

## 4. Meccanica 3 — L'economia della parata

Nel Crucible non ci sono cure passive. **La parata perfetta è l'unica fonte di vita.**

```
Cura = 5 + 3 · min(S, 4)          S = parate perfette consecutive senza subire danno
```

| Streak `S` | Cura per parata |
|---|---|
| 0 | 5 |
| 1 | 8 |
| 2 | 11 |
| 3 | 14 |
| 4+ | **17** (cap) |

| Regola | Valore |
|---|---|
| Cooldown della cura | **1.0 s** — uno per finestra di `HurtInvulnerabilityDuration` |
| Parata *bloccata* (non perfetta) | cura **0**, ma **non** azzera lo streak |
| Azzeramento streak | qualsiasi danno subito: nemico, scoria, detonazione, Deflagrazione |
| Cap | `MaxHealth = 100`, nessun sovra-guarigione |

**Verifica del bilanciamento.** Il danno medio dei nemici del Crucible è 17, e `HurtInvulnerabilityDuration = 1.0 s` limita l'incasso a un colpo/s per fonte. Il picco di cura a gioco perfetto è 17 HP/s. **I due valori si equivalgono di proposito:** un giocatore che para tutto sopravvive indefinitamente; un giocatore che para il 60% affoga lentamente. La parata non è un bonus, è il pavimento.

`ParryWindowMs = 380`, `PerfectParryMs = 220`, `ParryCooldownMs = 520`: nessuno di questi valori cambia nel Crucible. Il ramo non altera la finestra, altera **cosa vale colpirla**.

---

## 5. Nemici unici

Tre tipi nuovi più una variante del boss di metà corsa. Ognuno chiede una risposta che nessuno dei quattro tipi esistenti chiede, ognuno è agganciato al Calore da un lato diverso, e ognuno rispetta la regola non negoziabile del progetto: **si deve poter capire cosa fa prima che agisca**.

### 5.1 Fabbro di Brace — *Emberwright*

> *Alimenta la fornace. Uccidilo in fretta o la stanza cuoce te.*

Fabbro scheletrico inginocchiato a un'incudine. Non insegue, non pattuglia, non ti raggiunge mai. **Aggiunge +2.4 Calore/s finché è vivo.** È il primo nemico del gioco la cui minaccia non è il danno, ma il tempo.

| Campo `EnemyController` | Valore | Note |
|---|---|---|
| `MaxHealth` | **60** | due impegni: carica (55) + un leggero (10) = 65 |
| `PatrolSpeed` / `ChaseSpeed` | 0 / 0 | statico, come `CrossbowSentry` |
| `DetectionRadius` | 11.0 | |
| `LoseSightRadius` | 14.0 | |
| `AttackRange` | 3.2 | solo autodifesa a corto raggio |
| `AttackWindupTime` | 0.75 | tell lungo: è pensato per essere parato |
| `AttackRecoveryTime` | 0.40 | |
| `AttackCooldown` | 2.40 | |
| `AttackDamage` | 15 | |
| `StaggerDuration` | 0.50 | |
| `SteerDeadzone` | — | non si muove |
| Telegraph nativo | **sempre `CounterGold`** | l'unico tipo del gioco che non alterna |
| Δ Calore in vita | **+2.4/s** | additivo per istanza |
| Δ Calore alla morte | **−23** (−8 base −15 sollievo) | |

**Perché `CounterGold` fisso.** È un nemico che va raggiunto attraverso gli altri. La sua onda d'urto è l'ultimo ostacolo prima dell'incudine, e va **parata** per aprirsi il passaggio: la parata perfetta lo stordisce (`ParryStaggerRadius = 3.2`, che è esattamente il suo `AttackRange` — l'ostacolo e la soluzione hanno lo stesso raggio, di proposito) e concede la finestra per ucciderlo. Il moltiplicatore di knockback 1.4× del canale Oro qui non serve a spingerlo via: serve a non farsi spingere via.

**Regola di piazzamento (`@techart`):** mai più di **due** vivi contemporaneamente per stanza. Tre significano +7.2 Calore/s, che è una Deflagrazione ogni 10 secondi e nessuna decisione.

**Lettura visiva:** colonna di calore verticale sopra l'incudine, alta ~4 m, di intensità **proporzionale al Calore corrente della stanza**. È anche il secondo indicatore diegetico della barra: il giocatore deve poter leggere lo stato della stanza senza guardare l'HUD.

---

### 5.2 Il Colato — *Slagbound*

> *Non si martella. Si legge il terzo colpo.*

Elite in mischia coperto da un guscio di metallo fuso. Lento, inevitabile, e **punisce attivamente la risposta sbagliata**.

| Campo `EnemyController` | Valore | Note |
|---|---|---|
| `MaxHealth` | **72** | vedi derivazione sotto |
| `PatrolSpeed` | 1.20 | |
| `ChaseSpeed` | 3.40 | più lento del giocatore (8.0): si affronta, non si scappa |
| `DetectionRadius` | 10.0 | |
| `LoseSightRadius` | 16.0 | non molla |
| `AttackRange` | 2.00 | portata maggiore del grunt (1.4) |
| `AttackWindupTime` | 0.62 | > 0.38 s di `ParryWindowMs`: leggibile |
| `AttackRecoveryTime` | 0.40 | |
| `AttackCooldown` | 1.90 | |
| `AttackDamage` | 18 | |
| `StaggerDuration` | 0.90 | |
| `SteerDeadzone` | 0.30 | |

**Il guscio.** Riusa i due agganci che il progetto ha già aggiunto per il boss (`AbsorbDamage`, `StaggersOnHit`) — nessuna duplicazione del percorso di morte e knockback.

| Regola del guscio | Valore |
|---|---|
| Danno assorbito mentre è sigillato | ridotto a **3** (piatto, come `ChipDamage` del Warden) |
| **Ustione di ritorno** | colpirlo da sigillato infligge **6 danni al giocatore** |
| L'ustione rispetta `HurtInvulnerabilityDuration` | **sì** — mashare costa ~6 HP/s, non 40 |
| L'ustione applica stun / knockback | **no** — è una tassa, non un contrattacco |
| Apertura del guscio | **solo `ParryResult.Perfect`**, per **2.0 s** |
| Parata bloccata | knockback normale, guscio **non** si apre |
| Colpi da guscio aperto | danno pieno, stordisce normalmente |

**Pattern di telegraph (nativo):**

| Sequenza | 1 | 2 | 3 | ripete |
|---|---|---|---|---|
| Canale | `StandardWhite` | `StandardWhite` | **`UnparryableRed`** | → 1 |

Due colpi su tre sono parabili, il terzo va schivato con il dash (`DashIFrameDuration = 0.15 s`). Sotto **Incandescente** il pattern promosso diventa Oro / Oro / Rosso: la parata rende *di più* (knockback 1.4×) proprio quando la stanza è più pericolosa.

**Derivazione dei 72 HP.** Una catena di combo perfetta dentro la finestra da 2.0 s è: 10 + 12 + 14 + 10 + 12 + 14 = **72**. Una finestra impeccabile lo uccide esattamente. Una finestra sporca ne lascia un pezzo e obbliga a leggere un secondo ciclo di attacchi. Il numero è la regola.

**Lettura visiva:** venature di lava sul guscio che si spengono a guscio aperto. La differenza fra sigillato e aperto deve essere leggibile alla distanza della camera — è la stessa lezione della tinta blu-acciaio della `CrossbowSentry`.

---

### 5.3 Guscio di Brace — *Emberhusk*

> *Non conta se muore. Conta dove.*

Corridore fragile e veloce che **esplode 0.35 s dopo la morte**.

| Campo `EnemyController` | Valore | Note |
|---|---|---|
| `MaxHealth` | **14** | due leggeri (10+12) o un pesante (25) |
| `PatrolSpeed` | 2.60 | |
| `ChaseSpeed` | 6.20 | veloce, ma sotto gli 8.0 del giocatore |
| `DetectionRadius` | 9.0 | |
| `LoseSightRadius` | 15.0 | |
| `AttackRange` | 1.20 | |
| `AttackWindupTime` | 0.28 | il tell più corto del ramo: è una minaccia di prossimità |
| `AttackRecoveryTime` | 0.20 | |
| `AttackCooldown` | 1.10 | |
| `AttackDamage` | 9 | il danno diretto è secondario |
| `StaggerDuration` | 0.25 | |
| Telegraph nativo | **sempre `StandardWhite`** | semplice, perché è veloce |

**Detonazione:**

| Parametro | Valore |
|---|---|
| Innesco dopo la morte | **0.35 s** (miccia leggibile: lampeggio + sibilo) |
| Raggio del colpo | **3.0 m** |
| Danno | **22** |
| Pozza di fuoco residua | raggio **2.4 m**, durata **3.5 s** (= `CorpseLingerSeconds`) |
| Danno della pozza | **5 per tick da 0.5 s** |
| Δ Calore | **+10**, sempre, anche se ucciso lontano |

**Perché è il nemico più interessante dei tre.** Il moltiplicatore di knockback **1.4× della parata Oro** è oggi un numero in `EnemyController.OnParried()` senza un vero consumatore: nessuna situazione nel gioco rende la distanza di spinta una decisione. Qui sì.

| Modo di ucciderlo | Spinta | Esito |
|---|---|---|
| Attacco leggero (`LightKnockback = 3`) | corta | esplode addosso |
| Attacco pesante (`HeavyKnockback = 6`) | media | al limite del raggio da 3.0 m |
| Pesante caricato (6 × `1.8` = **10.8**) | lunga | sicuro, ma costa 450 ms di carica |
| Parata perfetta su Oro (5.5 × **1.4** = 7.7 m/s + 3 verticale) | lunga e **gratuita** | la risposta d'autore |

Il progetto si chiede esplicitamente *"è tutto ciò che è implementato anche raggiungibile?"*. Questo nemico rende raggiungibile un moltiplicatore che oggi non lo è.

**Regola di piazzamento (`@techart`):** mai singolo. Gli Emberhusk arrivano **in coppia o in terzina**, perché la decisione è "in che ordine e in che direzione", e con uno solo non c'è decisione.

---

### 5.4 Il Maestro di Fucina — *Forgemaster* (boss di ramo, Stanza 5)

Variante di `Sentinel`, che è a sua volta variante di `Warden`. Eredita la **grammatica** (armatura, chip, stordimento solo da parata perfetta, tell lungo) e cambia la **condizione di vittoria**.

| Campo | Valore | Confronto con Sentinel |
|---|---|---|
| `MaxHealth` | **82** | 55 |
| `AttackDamage` | **19** | 16 |
| `AttackRange` | **2.10** | 1.90 |
| `AttackWindupTime` | **0.68** | 0.70 |
| `AttackRecoveryTime` | **0.42** | 0.45 (eredit.) |
| `AttackCooldown` | **1.95** | 2.10 |
| `StaggerDuration` | **1.50** | 1.40 |
| `ChipDamage` | **2** | 2 |
| `EnrageAt` | **0** (non si infuria mai) | 0 |
| `ParryStaggerRadius` | **4.0** (eredit. dal Warden) | 4.0 |

**Il cancello di Calore — la regola che definisce il combattimento:**

| Calore | Pattern telegraph | `ChipDamage` | Conseguenza |
|---|---|---|---|
| **< 70** | alterna `StandardWhite` / `CounterGold` (come il Sentinel) | 2 | il boss **si può aprire** |
| **≥ 70** | **ogni attacco `UnparryableRed`** | **1** | il boss **non si può aprire**: nessuna parata, nessuna finestra, e il chip dimezzato |

**Il Calore è la sua barra della vita.** Un giocatore che entra Incandescente non può vincere: può solo subire, e il chip a 1 significa 82 colpi. Deve prima **raffreddare la stanza** — e la stanza gli fornisce esattamente gli strumenti per farlo.

**Sequenza dell'arena (Stanza 5):**

| Tempo | Evento |
|---|---|
| 0 s | la porta sigilla (`SealedUntilBossDies`, già il comportamento della stanza di metà corsa) |
| 0 s | due **sfiatatoi di scoria** (`Latch`) sui due lati dell'arena, −25 Calore ciascuno, **una volta sola** |
| ogni 12 s | spawn di **2 Emberhusk** dai bordi |
| continuo | `G_base = 3.20/s` |

**Bilancio dell'incontro, verificato:**

```
Ingresso            +3.20/s  (base)
2 husk / 12 s       +1.67/s  (+10 ciascuno alla detonazione, inevitabile)
                    ---------
Pressione totale    +4.87/s

Parate perfette     −12 ogni 1.95 s  =  −6.15/s
Uccisioni husk      −8  ogni 6 s     =  −1.33/s
                    ---------
Netto a gioco perfetto              =  −2.62/s
Netto a zero parate                 =  +3.53/s  →  Deflagrazione da 60 in ~11.3 s
```

Due sfiatatoi da −25 sono il margine d'errore: **50 punti di Calore, cioè circa 14 secondi di gioco sbagliato perdonati** (a +3.53/s), spendibili quando il giocatore vuole. È l'unica risorsa non rinnovabile dello scontro, e decidere quando bruciarla *è* il combattimento.

**Ricompensa:** l'uccisione azzera il Calore, apre la porta e concede il **Sigillo di Brace** (§7).

---

## 6. Curve e budget — riepilogo numerico

### 6.1 Blocco statistiche completo del ramo

| | Emberwright | Slagbound | Emberhusk | Forgemaster |
|---|---|---|---|---|
| `MaxHealth` | 60 | 72 | 14 | 82 |
| `AttackDamage` | 15 | 18 | 9 (+22 detonaz.) | 19 |
| `AttackRange` | 3.20 | 2.00 | 1.20 | 2.10 |
| `AttackWindupTime` | 0.75 | 0.62 | 0.28 | 0.68 |
| `AttackRecoveryTime` | 0.40 | 0.40 | 0.20 | 0.42 |
| `AttackCooldown` | 2.40 | 1.90 | 1.10 | 1.95 |
| `StaggerDuration` | 0.50 | 0.90 | 0.25 | 1.50 |
| `PatrolSpeed` | 0 | 1.20 | 2.60 | 1.60 |
| `ChaseSpeed` | 0 | 3.40 | 6.20 | 3.20 |
| `DetectionRadius` | 11.0 | 10.0 | 9.0 | 14.0 |
| `LoseSightRadius` | 14.0 | 16.0 | 15.0 | 20.0 |
| Canale nativo | Oro fisso | B/B/**R** | Bianco | B/O alternati |
| Δ Calore | +2.4/s | — | +10 alla morte | — |

### 6.2 Tempo di uccisione (danno giocatore invariato)

Riferimenti: leggero 10 (+2 per step di combo → 10/12/14), pesante 25, pesante caricato **55** (`ChargeDamageMultiplier = 2.2`).

| Nemico | Colpi necessari | Note |
|---|---|---|
| Emberhusk (14) | 2 leggeri, **o 1 pesante** | il pesante è preferibile per la spinta |
| Emberwright (60) | 1 caricato + 1 leggero (65) | oppure 2 combo complete (72) |
| Slagbound (72) | **1 finestra da 2.0 s perfetta** | 10+12+14+10+12+14 = 72 esatti |
| Forgemaster (82) | **2 finestre di stordimento** da 1.50 s | ~58 danni per finestra a combo pulita |
| *(riferimento)* Sentinel (55) | 1 finestra da 1.40 s | invariato sul ramo Catacombe |

### 6.3 Pressione di danno del ramo

Il check di soglia esistente misura **110 danni su quattro stanze** di esposizione da 4 s (~27.5 danni per stanza). Obiettivo per il Crucible con lo stesso protocollo (giocatore fermo, 4 s, nessuna reazione):

| Metrica | Catacombe (misurato) | Crucible (target di design) |
|---|---|---|
| Danno per stanza in 4 s di esposizione | ~27.5 | **42–52** |
| Nemici per stanza | 7–9 | 4–5 |
| Danno per colpo | ~11 | ~15 (Emberhusk: 9 al colpo, **22** alla detonazione) |

Il limite è **bilaterale**, come già per il check esistente: sotto 42 il ramo non è un ramo difficile, sopra 60 in esposizione passiva è una stanza d'apertura che uccide, e va rifiutata.

---

## 7. Ricompense e persistenza

### 7.1 Ricompensa immediata — Sigillo di Brace

Concesso all'uccisione del Maestro di Fucina, attivo **per il resto della corsa** (Stanze 6-9, boss finale incluso):

| Effetto | Valore |
|---|---|
| La cura da parata perfetta (§4) resta attiva **fuori** dal Crucible | formula identica, `5 + 3·min(S,4)` |
| Danno dell'attacco caricato | `ChargeDamageMultiplier` **2.2 → 2.6** (55 → **65**) |

**Verifica sul boss finale:** l'armatura del Warden è **piatta** (`AbsorbDamage => min(ChipDamage, amount)`, chip = 2). Il bonus di carica **non** accelera la fase corazzata — di proposito, perché `ChipDamage` è piatto *proprio* per impedire che il pesante caricato diventi la risposta all'armatura. Il Sigillo accelera solo le finestre di stordimento: da ~5 colpi a ~4 per finestra da 1.6 s. Ricompensa reale, grammatica del boss intatta.

### 7.2 Persistenza fra corse — nuovi `WorldFlags`

Segue esattamente il precedente di `shortcut_act2` (`SaveData.WorldFlags`, scritto da una leva, letto in `SpawnExit`).

| Flag | Condizione di sblocco | Effetto nelle corse successive |
|---|---|---|
| `crucible_cleared` | uccidere il Maestro di Fucina, anche sporcamente | il portale del Crucible nella Stanza 3 appare **anche a terra**, senza la salita di 4.5 m sulla terrazza |
| `crucible_mastered` | completare l'intero ramo (Stanze 4 **e** 5) con **zero Deflagrazioni** | la parata perfetta cura **4 HP fissi** in ogni corsa futura, su entrambi i rami, dalla prima stanza |

`crucible_mastered` è la vera meta-progressione da Roguevania: non un numero più grande, ma **una regola del gioco che cambia perché il giocatore ha dimostrato di saperla usare**.

> **Rischio dichiarato.** `crucible_mastered` altera il bilanciamento globale di tutte le corse successive, comprese quelle sul ramo Catacombe. 4 HP a parata è deliberatamente basso (un quarto del picco del Crucible). Se in playtest le Catacombe risultano banalizzate, la leva corretta è **abbassare a 3 HP o limitarlo all'Atto I**, non rimuovere il flag: un ramo opzionale e difficile che non lascia traccia permanente non è un Roguevania.

---

## 8. Layout dei chunk — per `@techart`

Vincoli non negoziabili, invariati: salto singolo **3.2 m**, doppio **5.6 m**, dash **6.4 m**, clearance verticale fra piattaforme sovrapposte **1.6 m**, ogni percorso elevato ha pilastri di sostegno, zero compenetrazioni.

**Nessun `ChunkKind` nuovo.** Il Crucible si compone interamente con i quattordici già esistenti. Cambia il **peso**, non il vocabolario.

### 8.1 `CrucibleLayout A` — Stanza 4, "La Colata"

Tre movimenti come tutti i layout esistenti, ~190 m, prevalentemente piatto: nel Crucible la stanza è il combattimento, non il percorso.

**Movimento 1 — l'apertura**

`Gauntlet(0.7)` → `Arena` → `Spikes` → `Gauntlet` → `Current` → `Drop`

**Movimento 2 — la pressione**

`Arena` → `Sweep` → `Gauntlet` → `Latch` *(sfiatatoio, −25 Calore)* → `Gap(0.9)` → `Arena`

**Movimento 3 — la corsa verso il portale**

`Gauntlet` → `Spikes` → `Chasm(0.85)` → `Arena` → `Gauntlet`

| Proprietà | Valore |
|---|---|
| Totale chunk | 17 |
| `Arena` | **4** (contro 1 dei layout normali) |
| `Difficulty` del composer | **0.85** fisso — il Crucible non usa la rampa `0.5 + index·0.07`; è già la scelta difficile |
| Elevazione netta | **≤ 6 m** — piatta di proposito |
| Checkpoint | **solo al primo `Gauntlet`** |

**Popolamento (deterministico, mai casuale):**

| Arena | Composizione |
|---|---|
| 1 | 1 Slagbound + 2 Emberhusk |
| 2 | 1 Emberwright + 2 Emberhusk |
| 3 | 1 Slagbound + 1 Emberwright |
| 4 | 1 Slagbound + 3 Emberhusk |

Totale: **4–5 corpi per arena, 3 Slagbound, 2 Emberwright, 7 Emberhusk** nella stanza. Il ciclo `(RoomIndex + placed) % types` del ramo principale **non si applica**: il Crucible ha una tabella di composizione esplicita, perché gli incontri sono progettati e non ruotati.

### 8.2 `CrucibleLayout B` — Stanza 5, "La Fucina"

Ricalca la forma di `BossLayout` — corta, piatta, larga — che è già la forma giusta per un'arena sigillata:

`Gauntlet(0.7)` → `Latch` *(sfiatatoio 1)* → `Gauntlet` → `Gauntlet` → `Latch` *(sfiatatoio 2)* → `Gauntlet`

| Requisito | Valore |
|---|---|
| Larghezza dell'arena centrale | ≥ 22 m (il Forgemaster ha `AttackRange` 2.1 e va aggirato) |
| Pareti arrampicabili | **una per lato**, altezza ≥ 3.0 m, entro 6.4 m dal centro |
| Sfiatatoi | 2, ai lati opposti — raggiungerli **costa terreno**, ed è il punto |
| Punti di spawn Emberhusk | 2, ai bordi, fuori dal raggio d'attacco del boss |

### 8.3 Identità visiva

| Elemento | Specifica |
|---|---|
| Palette base | ambra-rosso `(1.00, 0.35, 0.15)` — **lo stesso colore del portale**, così la promessa fatta alla Stanza 3 viene mantenuta |
| Luce ambientale | più calda e **più bassa** delle Cripte: la fonte è il pavimento, non le torce a parete |
| Reattività al Calore | l'emissione delle venature nel pavimento scala **linearmente con `Heat/100`** |
| Suolo di scoria attivo | le venature diventano bianco-caldo e pulsano a **2 Hz** |
| Colonne di calore | sopra ogni Emberwright vivo, altezza ~4 m, intensità ∝ Calore |

**Regola di leggibilità.** Lo stato della stanza deve essere leggibile **senza guardare l'HUD**: pavimento + colonne dicono la stessa cosa della barra. È lo stesso principio per cui il Warden corazzato non può somigliare al Warden aperto.

---

## 9. HUD e UX

| Elemento | Specifica |
|---|---|
| Barra del Calore | orizzontale, **sotto** la barra della vita, larghezza 60% di quella della vita |
| Visibilità | **solo dentro il Crucible**; assente su tutto il ramo Catacombe |
| Riempimento | gradiente sui quattro colori di stato (§2.3), transizione in 0.25 s |
| Tacche di soglia | segni fissi a **40** e **70** — il giocatore deve sapere *dove* sono i cambi di regola, non solo *che* esistono |
| Allarme pre-Deflagrazione | a **Calore ≥ 90**: pulsazione a 3 Hz + tono ascendente, con **≥ 2.1 s di preavviso** a `G` massimo |
| Contatore streak parata | 4 pip accanto alla barra della vita, si accendono a `S = 1..4`, si spengono tutti insieme al danno subito |
| Indicatore Emberwright | icona incudine + conteggio, visibile solo con almeno uno vivo |
| Deflagrazione | flash schermo pieno bianco-caldo, `CameraPunch` a **0.35** (contro lo 0.22 standard) |

**Vincolo architetturale:** l'HUD resta un puro consumatore di segnali. Non legge il valore del Calore da un nodo di gameplay — lo riceve dall'`EventBus`, esattamente come la barra del boss riceve `BossStateChanged`. Questa è la ragione per cui `BossStateChanged` esiste, e vale identica qui.

---

## 10. Contratto d'interfaccia per `@programmer`

Nessun codice qui — solo la superficie che il design richiede. Ogni voce va aggiunta ad `ARCHITECTURE.md` **prima** di essere implementata, come prescrive il documento stesso.

### 10.1 Nuovi segnali `EventBus`

| Segnale | Payload | Emesso da | Consumato da |
|---|---|---|---|
| `HeatChanged` | `float heat01, int state` | World | UI, AI, Audio, ProceduralArt |
| `Flashover` | `Vector3 atPosition` | World | UI, Audio, PlayerCamera |
| `ParryStreakChanged` | `int streak` | Combat | UI |
| `BranchEntered` | `string branchName` | World | UI, Audio, Save |

Payload appiattiti in tipi compatibili con `Variant`, come già imposto per `DamageInfo`. `state` attraversa come `int` e viene ricastato da ogni handler, esattamente come `AbilityFlags`.

### 10.2 Nuovi tunable (tutti `[Export]`, nessun valore hard-coded)

| Ambito | Campi |
|---|---|
| Calore | `HeatGainPerSecond`, `HeatPerPerfectParry`, `HeatPerBlockedParry`, `HeatPerKill`, `HeatPerDamageTaken`, `KindledThreshold`, `MoltenThreshold`, `FlashoverDamage`, `FlashoverResetTo`, `FlashoverRedSeconds` |
| Scoria | `SlagGraceSeconds`, `SlagDamagePerTick`, `SlagTickSeconds` |
| Parata | `ParryHealBase`, `ParryHealPerStreak`, `ParryHealStreakCap`, `ParryHealCooldownSeconds` |
| Slagbound | `ShellChipDamage`, `ShellBurnDamage`, `ShellOpenSeconds` |
| Emberhusk | `FuseSeconds`, `BlastRadius`, `BlastDamage`, `PoolRadius`, `PoolSeconds`, `PoolDamagePerTick` |
| Emberwright | `HeatPerSecondAlive`, `HeatReliefOnDeath` |

### 10.3 Punti di estensione da riusare (nessuno nuovo)

| Meccanica | Aggancio esistente |
|---|---|
| Guscio dello Slagbound | `AbsorbDamage(int)` + `StaggersOnHit(DamageInfo)` |
| Promozione dei telegraph | `SelectAttackTelegraph()` |
| Emberwright statico | `PatrolSpeed`/`ChaseSpeed` a 0 + `AttackRange` lungo (schema `CrossbowSentry`) |
| Forgemaster | eredita `Sentinel`, che eredita `Warden`; override di `Configure()` e `SelectAttackTelegraph()` |
| Sfiatatoi di scoria | `LatchGate` / `LeverSwitch`, invariati |
| Detonazione | `CorpseLingerSeconds` già scandisce 3.5 s — stessa durata della pozza, di proposito |
| Persistenza | `SaveData.WorldFlags` + `SaveManager.GetWorldFlag` |

**Regola vincolante:** ogni flag, layer o stato aggiunto deve avere un consumatore **nello stesso commit**. Il progetto ha già rimosso cinque sistemi dichiarati-e-mai-raggiunti (`WallJump` senza superfici, `ChargeAttack` senza meccanica, `PhysicsLayers.Hazard` vuoto, `PhysicsLayers.Pickup` inutilizzato, `WorldFlags` mai scritto). Il Calore non deve diventare il sesto.

---

## 11. Criteri di accettazione per `@qa`

Otto check headless proposti per `tools/verify.sh`. Ognuno deve essere verificato **fallire correttamente** prima di essere considerato verde, e ognuno deve reggere il mutation testing indicato.

| # | Check | Asserzione | Mutazione che lo deve far fallire |
|---|---|---|---|
| 1 | `CrucibleForkTest` | i due portali della Stanza 3 producono stanze **con contenuto diverso** (roster + conteggio arene) | rendere identici i due rami → rosso |
| 2 | `HeatCurveTest` | giocatore fermo, nessun combattimento: Deflagrazione entro 26.8 s ± 1.5 s in Stanza 4 | `HeatGainPerSecond = 0` → rosso |
| 3 | `HeatDrainTest` | parata perfetta scriptata: il Calore scende **esattamente di 12** | drenaggio a 0 → rosso |
| 4 | `TelegraphPromotionTest` | a Calore 75 un `BasicMelee` emette `UnparryableRed`; a Calore 10 emette il canale nativo | rimuovere la promozione → rosso |
| 5 | `SlagboundShellTest` | 10 colpi a guscio sigillato → ≤ 30 danni al nemico **e** danno di ritorno > 0; dopo parata perfetta un colpo infligge pieno | assorbimento identità → rosso; ustione a 0 → rosso |
| 6 | `EmberhuskBlastTest` | ucciso a ≤ 2 m il giocatore subisce la detonazione; ucciso con pesante caricato **no**. Pozza sparita a 3.5 s | miccia a 0 s → rosso; **controllo obbligatorio**: verificare che la pozza sia esistita prima di asserire che è sparita |
| 7 | `ForgemasterGateTest` | a Calore ≥ 70 nessuna parata apre il boss; sotto 70 la parata perfetta stordisce per 1.5 s | rimuovere il cancello → rosso |
| 8 | `CrucibleThreatTest` | esposizione passiva 4 s: danno attribuibile **fra 42 e 52** per stanza, **con run di controllo a `SpawnEnemies = false`** | tutti i tipi disarmati → attribuibile 0 → rosso |

**Trappole già pagate da questo progetto, da evitare per nome:**

- Il check 6 è la stessa classe di errore del bolt che spariva all'impatto: una pozza che scade da sola supera un test di cleanup **anche se non è mai esistita**. Va asserito che esisteva a metà durata.
- Il check 8 è la stessa classe del primo `ThreatBudgetTest`: senza run di controllo, "i danni ci sono" non distingue i nemici dai trappole.
- Il canale `stderr` vale qui come ovunque: qualsiasi errore engine stampato durante un test del Crucible fa fallire la suite (check 50).

---

## 12. Cosa questo documento non decide

Onestà su ciò che nessun numero su carta può stabilire, in coerenza con la sezione "What is genuinely open" di `STATUS.md`:

1. **Se 2.0 s di finestra sullo Slagbound siano generosi o crudeli.** I 72 HP sono derivati da una combo perfetta; se in pratica una combo perfetta sotto pressione non è ottenibile, il numero giusto è 60, non 72.
2. **Se il ritmo del Calore sia leggibile.** 2.80/s è calcolato su una stanza da 200 m, non provato su una stanza giocata. Il valore da guardare in playtest è **quante Deflagrazioni subisce un giocatore competente alla prima corsa**: il target è 1–2 nella Stanza 4, 0–1 nella Stanza 5.
3. **Se togliere i checkpoint sia una scommessa o una crudeltà.** È la decisione più aggressiva del documento ed è quella con la leva di ritorno più semplice (portarli a 2).
4. **Se `crucible_mastered` banalizzi le corse successive.** Dichiarato come rischio in §7.2, con la leva già identificata.
5. **Se quattro arene per stanza siano un combattimento o una routine.** Lo stesso dubbio che `STATUS.md` solleva già su "quattro-otto nemici a stanza": nessun check headless può rispondere.

Nessuno di questi cinque punti blocca l'implementazione. Tutti e cinque vanno misurati **giocando**, che resta il primo elemento della to-do list del progetto.
