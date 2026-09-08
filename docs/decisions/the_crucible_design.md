# The Crucible — Design del bioma alternativo

**Modalità:** `@director` (Game Director & Systems Designer)
**Stato:** specifica di design approvata dall'Architect, pronta per `@programmer` e `@techart`.
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

---

## 2. Meccanica centrale — Il Calore della Fornace

Un singolo valore `Heat ∈ [0, 100]`, di ambito stanza, azzerato-a-25 all'ingresso nel ramo e **trasportato dalla Stanza 4 alla Stanza 5**.

```
Heat(t + Δt) = clamp( Heat(t) + G·Δt − ΣD , 0 , 100 )
G = G_base(stanza) + 2.4 · N_emberwright_vivi
```

| Termine | Valore |
|---|---|
| `G_base` Stanza 4 | **+2.80 Calore/s** |
| `G_base` Stanza 5 | **+3.20 Calore/s** |
| `+2.4/s` per ogni Emberwright vivo | additivo, per istanza |

**Guadagni istantanei:**
- Danno subito: **+4**
- Detonazione Emberhusk: **+10**

**Drenaggi:**
- Parata perfetta (`ParryResult.Perfect`): **−12**
- Parata bloccata (`ParryResult.Blocked`): **−6**
- Nemico ucciso: **−8**
- Emberwright ucciso: **−23** totali (−8 base −15)
- Sfiatatoio scoria attivato (leva `Latch`): **−25** (una volta sola)

### 2.3 Soglie
- **Temprato (0–39)**: Pattern nativo, ×1.00 cooldown nemici.
- **Ravvivato (40–69)**: Ogni 2° attacco promosso di 1 canale telegraph, ×0.90 cooldown.
- **Incandescente (70–99)**: Ogni attacco promosso, ×0.80 cooldown, Suolo di scoria attivo (2s max a terra, poi 4 dmg/s).
- **Deflagrazione (100)**: 20 danni al player, Calore resettato a 60, tutti gli attacchi UnparryableRed per 2.5s, scoria attiva per 2.5s.

---

## 3. Nemici unici

1. **Emberwright (Fabbro di Brace)**: 60 HP, statico, +2.4 Calore/s in vita, telegrafo nativo sempre `CounterGold`, −23 Calore alla morte.
2. **Slagbound (Il Colato)**: 72 HP, guscio che riduce a 3 il danno e infligge 6 di ustione riflessa se colpito a guscio chiuso. Si apre SOLO con Parata Perfetta per 2.0s. Telegraph B/B/R.
3. **Emberhusk (Guscio di Brace)**: 14 HP, veloce, esplode 0.35s dopo la morte (raggio 3.0m, 22 danni, pozza di fuoco per 3.5s). Vulnerabile a knockback da parata Oro (1.4x).
4. **Forgemaster (Maestro di Fucina - Boss Stanza 5)**: 82 HP, variante Sentinel. A Calore < 70 parabile; a Calore >= 70 tutti gli attacchi diventano UnparryableRed e chip damage a 1.

---

## 4. Ricompense & WorldFlags
- **Sigillo di Brace**: Uccidere il Forgemaster sblocca parata cura e carica potenziata a 2.6x per il resto della run.
- `crucible_cleared`: Portale del Crucible disponibile anche a terra nella Stanza 3.
- `crucible_mastered`: Zero deflagrazioni = parata perfetta cura 4 HP fissi in ogni corsa futura.
