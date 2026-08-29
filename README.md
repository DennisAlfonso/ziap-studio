# ZIAP Studio

ZIAP Studio è un editor desktop Windows per i progetti Zenkaiverse. La milestone
`0.1.9 — Project Integrations & Fusion Audio` introduce capability custom
rilevate dal registry plugin di RPG Maker. La prima integrazione è un editor
semantico e un browser con preview per `ZDP_FusionAudio`.

## Funzionalità attuali

- folder picker nativo Windows;
- risoluzione metadata con precedenza `.ziap → engine → package → cartella`;
- lettura di `data/System.json` e rilevamento automatico di RPG Maker MZ/MV;
- stati distinti `DETECTED` e `ZIAP PROJECT`;
- dialog di inizializzazione precompilato con generazione automatica dell'ID;
- creazione atomica di `.ziap/project.json`, senza sovrascrivere identità esistenti;
- provider RPG Maker MZ con Database, World, System, Assets e mappe nominate;
- registry delle Project Integrations separato dal provider RPG Maker;
- rilevamento di `ZDP_FusionAudio` attivo esclusivamente tramite `js/plugins.js`;
- documento Fusion Audio con ricerca, inspector, sorgenti file/System Sound,
  varianti, volume, pitch, pan, cooldown e anti-ripetizione;
- preview diretta degli asset audio supportati da Windows e apertura della cartella SE;
- capability `Fusion Boss Battle` rilevata dai plugin Combat, Encounter e Arena attivi;
- workspace Boss Battle con inventario di `FusionCombat`, `FusionEncounters`, `FusionArenas` e `FusionPuzzles`;
- browser degli encounter con selezione sincronizzata di fase e sequenza;
- grafo interattivo delle fasi con transizioni e condizioni;
- timeline visuale di azioni, attese, sincronizzazioni, sequenze annidate e loop;
- stima dei frame minimi che distingue i tempi esatti da quelli successivi a condizioni dinamiche;
- Arena Preview con il Tilemap reale di RPG Maker MZ, selezione arena/mappa e asset del progetto;
- navigazione interattiva dell'arena con pan, zoom, griglia, regioni, anchor e ruoli;
- AttackGeometry collegata alla timeline con layer separati per telegraph, hitbox e traiettorie;
- probe interattivo per ispezionare gli attacchi il cui bersaglio viene acquisito soltanto a runtime;
- scheduler a 60 FPS per warning, partenze ripetute, collider mobili e raffiche multi-target;
- import dei trace reali del playtest con confronto temporale e geometrico Previsto/Runtime;
- overlay runtime di movimento boss/player, telegraph, collider, proiettili e impatti;
- Arena Preview separabile in una finestra ridimensionabile sincronizzata con il documento;
- validazione incrociata di boss, enemy, scaling profile, encounter, fasi, arene, puzzle e completion profile;
- verifica delle mappe e delle quantità di `<FusionAnchor:...>` richieste dalle arene;
- diagnostica Boss Battle integrata nel Pre-Flight con navigazione al workspace;
- salvataggio atomico di `data/fusion/audio.json` con protezione dalle modifiche esterne;
- diagnostica Fusion Audio nel Pre-Flight per schema, valori, slot e asset mancanti;
- albero minimale `Assets → Audio` con cartelle BGM/BGS/ME/SE e relativi file;
- provider generico con vista Files limitata e protetta da directory troppo grandi;
- descriptor indipendenti dalla UI per le risorse (`rpgmaker://database/...`);
- identità stabile della risorsa separata dal nome localizzato mostrato nella UI;
- Document Resolver e provider dedicato ai database RPG Maker;
- tab documenti con Project Overview sempre disponibile;
- rail Progetti collassabile automaticamente quando si apre un workspace;
- Project Explorer e coppia database/Inspector ridimensionabili;
- Project Overview responsive, con disposizione a due colonne sulle finestre ampie;
- CommandBar documento separata dalla navigazione delle tab;
- sezioni Inspector espandibili per ridurre lo scroll durante l'editing;
- dialog Localization larga e form di inizializzazione più leggibile;
- ripristino di posizione, dimensione, stato finestra e larghezze dei pannelli;
- documento `Remote Localization` con ricerca, filtro, tabella completa e azioni per file;
- riepilogo Localization compatto nella Overview con contatori e problemi recenti;
- CommandBar contestuale per Overview, Localization e database;
- indicatore accent nel rail per Overview e Remote Localization;
- nomenclatura remota esplicita: Allineato, Differente, Solo locale e Solo remoto;
- filtri per categoria nella surface di confronto JSON;
- definizioni dichiarative di colonne, campi e sezioni degli inspector;
- editor dichiarativo per Armi; Attori, Nemici e fallback generico restano read-only;
- quick-create `+ Nuova arma` che raccoglie identità, classificazione, riferimenti RPG Maker e progressione, quindi apre il Weapon Editor;
- schema autorevole per spada, stocco, pugnale, doppia lama, ascia e arma da fuoco;
- Weapon Editor a schede (`Panoramica`, `Combattimento`, profilo famiglia, `Progressione`, `Perk`, `Recupero`, `Avanzate`);
- picker semantici e ricercabili per tipo arma, elemento e abilità d'attacco, con ID tecnico secondario;
- origine visibile dei valori ABS: ereditati dalla skill oppure sovrascritti esplicitamente dall'arma;
- editor semantico per `perk`, `itemRare`, `lvReq`, `maxLevel` e `fhd:no_itemicon`;
- picker perk posizionali per colonna I/II/III, alimentati da `ZDP_WeaponPerks.js`;
- collezione modificabile dei parametri `cp[n]`, con `cp[1]` risolto come Maestria Hex;
- picker lore da `locales/it/books.json`, con ID, chiavi canoniche e alias legacy preservati;
- mini-editor del `Disassemble Pool` con risorsa raw, nome localizzato, quantità e probabilità;
- sorgente note sempre disponibile in sola lettura con contatori riconosciuti/non gestiti;
- parser generico dei notetag con AST e span originali, riutilizzabile sugli altri database;
- patch interne alle note che non eliminano testo, blocchi o notetag sconosciuti;
- validazioni pre-save per colonne perk, rarità, livelli, parametri, lore e disassemblaggio;
- `NumberBox` per prezzo, Icon ID e parametri delle armi;
- `ComboBox` semantiche per tipo, slot e animazione, salvando sempre l'ID raw;
- Nome e Descrizione delle armi modificabili direttamente; i valori raw di localizzazione restano preservati finché non vengono intenzionalmente sostituiti;
- risoluzione di riferimenti database (`Classe`, `Animazione`) e System enum;
- modello separato `RawValue / ResolvedValue / DisplayValue`;
- `Kind`, `Target` e stato `Resolved / MissingTarget` per i valori risolti;
- navigazione dai riferimenti database al documento e record di destinazione;
- `LocalizationService` con provider specifico per `locales/{locale}/{namespace}.json`;
- risoluzione dei path Fusion `{db[...]}` e `{main[...]}` in italiano;
- metadati di origine delle localizzazioni: namespace, path, locale e file sorgente;
- indicatore `Gestito da ZIAP Console` e comando `Apri in Console` nell'inspector;
- deep link generici Console con progetto, area, lingua, file e focus;
- apertura nel browser predefinito, senza token o credenziali negli URL;
- login myZenkai nel browser con OAuth Authorization Code, PKCE, MFA e consenso;
- callback loopback contenente soltanto un authorization code monouso;
- scambio del custom token tramite Firebase Authentication REST;
- refresh automatico degli ID token e refresh token protetto da Credential Manager;
- Account panel con stato connesso, nickname e disconnessione locale;
- Bearer Firebase prioritario per le ZIAP API, senza sessioni anonime;
- manifest Localization remoto ottenuto tramite una ZIAP API read-only;
- confronto locale/remoto per locale, percorso, versione e checksum;
- stati `Allineato`, `Differente`, `Solo locale` e `Solo remoto`;
- compatibilità con i checksum FNV-1a legacy della Console e SHA-256;
- verifica automatica all'apertura del progetto e refresh manuale dalla Overview;
- download autenticato della sola versione `current` richiesta, senza signed URL;
- diff JSON per chiave con stati `Modificato`, `Solo locale` e `Solo remoto`;
- confronto remoto interamente in memoria, senza scritture nel workspace;
- comandi espliciti `Scarica` e `Aggiorna dal pubblicato` con conferma e diff;
- nuova verifica di versione, checksum, JSON e modifiche locali prima della scrittura;
- nessun download o sovrascrittura automatica dei file locali;
- diagnostica non bloccante per riferimenti e localizzazioni mancanti;
- risoluzione dei riferimenti asset in percorsi fisici sicuri dentro il progetto;
- anteprime ritagliate per IconSet, face sheet e character sheet RPG Maker;
- anteprime complete per battler front-view e side-view;
- diagnostica non bloccante per asset mancanti, invalidi o non visualizzabili;
- `DocumentEditSession` con snapshot originale e working state basato su `JsonNode`;
- dirty state, command stack undo/redo e ChangeSet semantico per proprietà;
- snapshot della sorgente con hash, dimensione e data dell'ultima modifica;
- validazione pre-save: gli errori bloccano, gli avvisi restano non bloccanti;
- rilevamento delle modifiche esterne prima di ogni scrittura;
- writer JSON atomico con file `.ziap-tmp`, flush e verifica prima della sostituzione;
- patch testuali conservative: soltanto i token intenzionalmente modificati cambiano;
- infrastruttura Save, Save All e prompt per documenti sporchi in chiusura;
- normalizzazione visuale dei campi testo, mantenendo le note intenzionalmente raw;
- ricerca per ID e nome nei database aperti;
- visualizzazione di nome, package, versione, tipo, publisher e percorso;
- apertura della cartella in Esplora file;
- recent projects persistiti in `%LOCALAPPDATA%\Zenkaiverse\ZIAP Studio`.
- motore Pre-Flight generico con provider e regole separati dalla UI;
- profilo iniziale Weapons per rarità, perk, livelli, parametri custom, lore e disassemblaggio;
- analisi automatica all'apertura del progetto e dopo ogni salvataggio, oltre al comando manuale;
- pagina Pre-Flight con ricerca, filtri per severità, ignorati ed eccezioni obsolete;
- navigazione diretta dal problema al record arma con apertura della sezione `Avanzate`;
- contatori nella Overview, marker nella lista Armi e diagnostica contestuale nell'Inspector;
- suppression identificate da regola, database e record, salvate solo nel metadata `.ziap`;
- ripristino delle eccezioni e pulizia manuale delle sole suppression obsolete.

## Architettura

```text
src/
├── ZiapStudio.Core/       progetti, documenti, editing, ChangeSet e AST notetag
├── ZiapStudio.Services/   resolver, asset, salvataggio e integrazioni
│   ├── Integrations/     registry plugin e provider delle capability di progetto
│   ├── Fusion/Audio/     catalogo, risoluzione asset e salvataggio FusionAudio
│   ├── Fusion/Bosses/    workspace e validazione dei contratti Boss Battle
│   ├── Fusion/Weapons/   cataloghi, semantica e patch dei notetag arma
│   ├── Authentication/  OAuth browser, Firebase token e sessione
│   └── Integration/
│       ├── Console/       target, deep-link builder e servizio di apertura
│       └── Remote/        manifest, diff semantico e sync pubblicato atomico
└── ZiapStudio/            WinUI 3 e integrazioni specifiche Windows
    └── Platform/Windows/  picker, shell e Windows Credential Manager
```

La logica di progetto è C# normale e non dipende da WinUI. L'applicazione è
intenzionalmente Windows-first; non contiene implementazioni preventive per altri
sistemi operativi.

Gli altri database continuano a essere consultabili senza possibilità di modifica.
Il metadata `.ziap/project.json` è pensato per essere versionato insieme al
repository; preferenze personali e recenti restano in `%LOCALAPPDATA%`.

## Schema di creazione armi

`+ Nuova arma` crea un record completo nel primo slot vuoto predisposto da RPG Maker.
La finestra iniziale resta deliberatamente breve: identità, famiglia/sottotipo,
impugnatura, tipo arma, elemento, abilità d'attacco, rarità e livello richiesto.
Subito dopo la creazione ZIAP seleziona il record e apre il Weapon Editor contestuale.
Lo schema genera inoltre categorie Fusion, progressione, perk, pool di
disassemblaggio e valori iniziali specifici della famiglia.

Il `Weapon Editor` permette di rifinire gli stessi valori dopo la creazione.
Il profilo comune comprende `attackDamageRate`, `attackFlatDamage`,
`attackDefenseRate`, `attackInterval`, `attackRange`, `attackRadius`,
`projectileSpeed` e `projectileColliderRadius`; questi valori non vengono duplicati
automaticamente: l'editor mostra la skill d'origine e li salva sull'arma soltanto
quando viene attivato `Sovrascrivi cadenza e balistica`. Le firearm aggiungono
`magazineSize`, `reloadDuration`, `firearmAccuracy`, `firearmStability`,
`firearmHandling` e gli override di mira. Il Pre-Flight usa lo stesso parser dello
schema e impedisce il salvataggio di combinazioni incomplete o fuori intervallo.

ZIAP non inserisce o rinumera record nell'array: se non esistono slot liberi, chiede
di aumentare prima il massimo nel database RPG Maker. Le patch restano conservative
anche per l'array `traits`, quindi campi e notetag sconosciuti non vengono riserializzati.

## Requisiti

- Windows 10 2004 (build 19041) o successivo;
- .NET SDK 8;
- Windows SDK 10.0.26100;
- Visual Studio 2022 con i tool di build per applicazioni Windows, oppure `dotnet`.

## Build e test

```powershell
dotnet restore .\ZiapStudio.sln
dotnet test .\ZiapStudio.sln -p:Platform=x64
dotnet build .\src\ZiapStudio\ZiapStudio.csproj -p:Platform=x64
```

Per avviare il profilo packaged:

```powershell
dotnet run --project .\src\ZiapStudio\ZiapStudio.csproj -p:Platform=x64
```

## Integrazione con ZIAP Console

La base URL predefinita è sempre `https://ziap.zenkaiverse.net`, anche nelle build Debug.
Per lavorare contro una Console locale è necessario abilitarla esplicitamente:

```powershell
$env:ZIAP_CONSOLE_BASE_URL = "http://localhost:4200"
dotnet run --project .\src\ZiapStudio\ZiapStudio.csproj -p:Platform=x64
```

Per tornare al valore di produzione nella stessa sessione PowerShell:

```powershell
Remove-Item Env:ZIAP_CONSOLE_BASE_URL
```

Studio invia soltanto coordinate di navigazione (`source`, `language`, `file`, `focus`).
Autenticazione, permessi, modifica, staging e publish restano responsabilità della Console.

## Remote Workspace Awareness

Studio interroga per impostazione predefinita le Cloud Functions europee
`getLocalizationPublishedManifest` e `getLocalizationPublishedFile`. La prima espone
soltanto metadata; la seconda restituisce esclusivamente il JSON `current` richiesto
dopo aver verificato progetto, locale, percorso e `versionId`. Nessun percorso Storage,
bucket, collection o signed URL viene esposto al client.

Normalmente il client usa il Firebase ID token della sessione myZenkai. Il token di
sviluppo resta disponibile soltanto nelle build Debug come fallback esplicito:

```powershell
$env:ZIAP_REMOTE_API_TOKEN = "<token configurato nel backend>"
dotnet run --project .\src\ZiapStudio\ZiapStudio.csproj -p:Platform=x64
```

Il token non viene salvato nel progetto né inserito negli URL. In Release non viene
letto. Per un emulatore o un endpoint alternativo:

```powershell
$env:ZIAP_REMOTE_LOCALIZATION_MANIFEST_URL = "http://127.0.0.1:5001/myzenkai-c58ee/europe-west1/getLocalizationPublishedManifest"
$env:ZIAP_REMOTE_LOCALIZATION_FILE_URL = "http://127.0.0.1:5001/myzenkai-c58ee/europe-west1/getLocalizationPublishedFile"
```

Configurazione e deploy del backend da `D:\Zenkaiverse\Firebase`:

```powershell
firebase functions:secrets:set ZIAP_STUDIO_READ_TOKEN
firebase deploy --only functions:oauthAuthorize,functions:oauthToken,functions:resolveExternalAuthRequest,functions:getExternalConsentRequest,functions:approveExternalAuthRequest,functions:getLocalizationPublishedManifest,functions:getLocalizationPublishedFile
```

Il backend registra `ziap_studio_desktop_v1` come client first-party con callback
loopback e PKCE obbligatorio. Nell'URL torna soltanto il codice monouso; custom token,
ID token e refresh token viaggiano nel body HTTPS. Non vengono create sessioni anonime
e non è incorporato alcun service account in Studio.

## Metadata ZIAP

Un progetto può dichiarare `.ziap/project.json`:

```json
{
  "schemaVersion": 1,
  "id": "fusion-hexella-dive",
  "name": "Fusion: Hexella Dive",
  "version": "0.8.0",
  "type": "rpgmaker-mz",
  "publisher": "Zenkaiverse"
}
```

I valori ZIAP hanno precedenza sui metadata dell'engine. `package.json` conserva
la propria identità separata: il suo campo `name` non sostituisce il nome del
progetto. I campi ancora mancanti ricevono un fallback sicuro dal filesystem.

## Project Integrations e Fusion Audio

Studio non analizza il sorgente di `ZDP_FusionAudio.js`. Legge soltanto il
registry dichiarativo `js/plugins.js`; quando trova il plugin attivo,
`FusionAudioIntegrationProvider` pubblica il documento
`fusionaudio://catalog/` nel nodo `System → Plugin`.

Il contenuto modificabile vive in `data/fusion/audio.json`. Le sorgenti
`systemSound` risolvono gli slot tramite `data/System.json`, mentre le sorgenti
`files` puntano a nomi relativi sotto `audio/se` senza estensione obbligatoria.
Il selector delle varianti indicizza ricorsivamente `audio/se/**`, riunisce le
copie `.ogg`/`.m4a` dello stesso asset e permette di cercare per nome o cartella.
Nel popup si usano `↑`/`↓` per scorrere, `Invio` per assegnare e `Spazio` per
ascoltare l'elemento evidenziato; ogni risultato espone anche il pulsante play.
Questi pulsanti riproducono l'asset grezzo. Il comando `Riproduci evento` della
toolbar usa invece il catalogo modificato in memoria e simula il runtime:
selezione varianti, anti-ripetizione, volume master/categoria/evento, pitch,
pan, cooldown e risoluzione degli slot `System.json`.
Il runtime FHD conserva un catalogo interno di fallback e applica il JSON come
override, lasciando alle chiamate `FusionAudio.register()` la precedenza finale.

## Fusion Boss Battle

Quando almeno uno dei plugin core `ZDP_FusionCombat`, `ZDP_FusionEncounter` o
`ZDP_FusionArena` è attivo, Studio pubblica `Fusion Boss Battle` sotto
`System → Plugin`. Il workspace legge i quattro database Fusion senza eseguire o
analizzare il sorgente JavaScript dei plugin e controlla i riferimenti fra i relativi
record. Per ogni arena verifica inoltre l'esistenza delle mappe dichiarate e confronta
gli anchor richiesti con i notetag presenti negli eventi della mappa.

La surface resta intenzionalmente read-only, ma proietta già gli encounter in un grafo
interattivo delle fasi e in una timeline per sequenza. Le attese deterministiche
contribuiscono al tempo minimo; dopo `waitUntil`, chiamate a sequenze annidate o loop,
gli step successivi sono marcati con `≥` perché il frame assoluto dipende dal runtime.
Questa distinzione evita che l'editor presenti come esatto un timing che non può
conoscere staticamente.

Il workspace separa ora il lavoro in quattro modalità focalizzate: `Panoramica` mostra
soltanto struttura e transizioni dell'encounter, `Sequenza` concentra timeline e step
selezionato, `Runtime` isola gli scostamenti osservati durante il playtest, mentre
`Arena` rende espliciti fase, sequenza, trace, arena e mappa nello stesso contesto.
L'elenco tecnico completo degli step è chiuso per impostazione
predefinita e il comando `Focus` può nascondere il selettore degli encounter per
ampliare la superficie attiva. Nel grafo le condizioni non sono più etichette sempre
visibili: le connessioni della fase selezionata vengono evidenziate e il dettaglio
resta disponibile nel riepilogo contestuale e tramite tooltip.

Una semantic layer evita che il designer debba conoscere a memoria gli ID del runtime.
Fasi e sequenze possono dichiarare facoltativamente `displayName`, `summary`,
`playerGoal` e `designerIntent`; se questi metadati mancano, Studio continua comunque
a spiegare automaticamente ogni step. Il catalogo opzionale
`data/FusionActionCatalog.json` associa agli action ID un nome leggibile, una categoria,
un'icona e un template descrittivo, mantenendo sempre visibili ID e argomenti tecnici
come informazione secondaria. La timeline evidenzia anche le dipendenze essenziali fra
step, ad esempio una posizione prodotta da `combat.captureTarget` e consumata dai cast
successivi.

L'overview compatta della timeline rappresenta le attese come segmenti, le finestre
di attacco come barre e gli step salienti con marker iconici. L'hover espone dettagli,
target, warning e ripetizioni; il click seleziona e porta in vista lo step, mentre
click-and-drag, frecce, Page Up/Down, Home ed End spostano il cursore temporale. Lo
stesso frame viene condiviso con lo scheduler di Arena Preview, anche quando la preview
si trova nella finestra separata.

La scheda `Arena Preview` ricostruisce la scena dai dati originali `MapXXX.json`,
`MapInfos.json` e `Tilesets.json`. Il rendering usa le copie di `pixi.js` e del
`Tilemap` presenti nel progetto RPG Maker, quindi autotile, livelli e flag del
tileset seguono lo stesso codice dell'engine. Il renderer gira in una WebView2
isolata e read-only: non esegue i plugin del gioco, non espone oggetti host, blocca
rete, finestre e permessi, e può leggere soltanto il runtime MZ e le immagini di
tileset/parallasse necessarie. Gli overlay dell'editor restano separati dal tilemap,
così griglia, regioni, `<FusionAnchor>` e `<FusionRole>` possono essere attivati e
ispezionati senza alterare la scena.

La preview può essere spostata in una finestra secondaria, dove il canvas occupa
l'intera area client disponibile. Arena e mappa restano sincronizzate con il documento;
la tab mostra chiaramente che la preview è esterna e permette di richiamarla o riportarla
nel workspace. Chiudere la finestra, il documento, il progetto o Studio aggiorna e libera
la superficie collegata senza lasciare renderer WebView2 orfani.

Selezionando un'azione di attacco nella timeline, `AttackGeometry` sovrappone tre
layer indipendenti: telegraph previsto, collisione primaria del runtime e
traiettoria. Le geometrie sono ricavate dai notetag ABS delle skill e dai parametri
di `FHD_EnemyAttackTelegraph.js`; le hitbox aggiuntive sono visualizzate
separatamente. Per target acquisiti a runtime, come `captured:impact`, Studio usa un
probe spostabile con doppio clic sulla mappa e lo dichiara come tale, invece di
presentare una coordinata statica come reale.

La modalità `Simula` aggiunge uno scheduler a 60 FPS basato sul tempo minimo della
sequenza. Distingue durata visiva del telegraph, ritardo effettivo di esecuzione,
`repeat`, `repeatOnUse` e `repeatDelay`; per i proiettili mostra i collider circolari
in movimento lungo la traiettoria invece di trattare l'intero corridoio come una
hitbox. Le skill direzionali usano le stesse otto direzioni discrete dell'adapter
Alpha ABS, mentre `combat.castVolley` espande tutti i target e rispetta l'indice
degli anchor omonimi. Dopo `roleMovementComplete`, l'origine del cast viene inoltre
proiettata sulla destinazione raggiunta dal ruolo.

La barra inferiore di Arena Preview rappresenta anche la struttura della sequenza:
le attese sono segmenti neutri, le finestre di telegraph/esecuzione sono rosse e
cue, catture, cast, loop e sincronizzazioni hanno marker dedicati. Gli eventi sullo
stesso frame vengono raggruppati con un contatore; passando il puntatore si vedono
dettagli, target e frame d'impatto, mentre il clic seleziona lo step corrispondente
anche nella timeline di Studio. Lo slider, la riproduzione e i marker condividono
sempre lo stesso cursore con la finestra principale e con la preview separata.

### Runtime Trace & Fidelity

Con `ZDP_FusionRuntimeTrace` attivo, i playtest delle Fusion Arena producono file
versionati in `.ziap/runtime-traces`. Studio li filtra per encounter e sincronizza
automaticamente arena e mappa con la registrazione scelta; il pulsante di refresh importa
subito un trace appena concluso senza riaprire il documento. Per le sequenze ripetute
viene mostrata l'esecuzione più recente del file selezionato. Il selettore indica anche
il profilo di cattura `Full`, `Balanced`, `Compact` o `Custom`: i profili ridotti
mantengono integri gli eventi di combattimento e riducono soltanto campioni continui di
posizione e formattazione del JSON.

La barra inferiore separa `PREVISTO` e `RUNTIME`. La seconda corsia mostra step reali,
telegraph, esecuzioni, attivazioni dei collider e impatti; hover e click espongono e
raggiungono il frame effettivo. Le raffiche confrontano ogni cast usando il proprio
`castIndex`, quindi `repeatOnUse` e `repeatDelay` non generano falsi scostamenti. Gli
stati `Aligned`, `Drift`, `Divergent` e `Missing` sintetizzano delta di frame, centro e
raggio.

La scheda `Runtime` presenta questi stati come una review orientata ai problemi: per
impostazione predefinita elenca soltanto drift, divergenze e cast mancanti, lasciando gli
attacchi allineati dietro un filtro opzionale. Il dettaglio espone frame previsti e reali,
telegraph, collider, geometria e identificativi di cast/run. Selezionare un confronto
sincronizza lo step e il cursore temporale; i comandi contestuali aprono direttamente lo
stesso punto nella timeline o nell'Arena Preview.

In modalità temporale il layer `Runtime` sovrappone all'arena le posizioni interpolate
di boss e giocatore, le relative scie, i telegraph realmente mostrati, collider,
proiettili e target colpiti. Si tratta di dati osservati dal runtime, non di una seconda
simulazione; possono quindi essere confrontati visivamente con Telegraph, Hitbox e
Traiettoria previsti usando lo stesso scrubber.

Per l'abilità 95 (`Impatto stordente`) telegraph e collider primario coincidono:
raggio 1 tile, centro verticale a `-0.5` tile e warning di 60 frame. L'anteprima
evidenzia però anche il punto grezzo acquisito e il relativo offset, utile per
distinguere lo scarto visivo dello sprite dalla geometria effettiva. La collisione
finale può comunque dipendere dalla hurtbox del giocatore e dallo stato dinamico
del runtime. Le sequenze che attraversano condizioni dinamiche mantengono quindi
il prefisso `≥`: lo scheduler rappresenta il tempo minimo, non spaccia una stima
statica per un frame assoluto.

## Project Pre-Flight

La `0.1.8` analizza `data/Weapons.json` senza modificarlo. Il profilo considera
soltanto i record non null con `wtypeId != 0` e riusa lo stesso provider semantico
dell'editor avanzato, così parsing, cataloghi e diagnostica non possono divergere.
L'analisi parte all'apertura del progetto, dopo un salvataggio e tramite `Analizza ora`.

Il comando `Ignora` registra una suppression strutturale in `.ziap/project.json`:

```json
{
  "preflight": {
    "suppressions": [
      {
        "ruleId": "weapon.rarity.missing",
        "scope": "Weapons",
        "recordId": 12,
        "ignoredAt": "2026-08-15T14:30:00+02:00",
        "reason": "Eccezione intenzionale per il prototipo"
      }
    ]
  }
}
```

L'identità è `ruleId + scope + recordId`: rinominare un'arma non perde quindi
l'eccezione. Le suppression che non corrispondono più a un problema restano visibili
come obsolete e vengono rimosse soltanto con un comando manuale.
