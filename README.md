# ZIAP Studio

ZIAP Studio è un editor desktop Windows per i progetti Zenkaiverse. La milestone
`0.1.6` rifinisce il workspace con un documento strumento Remote Localization,
una Overview sintetica, CommandBar contestuale e navigazione rail con selezione
esplicita. Published Localization Sync continua a confrontare e aggiornare i file
in modo esplicito e atomico.

## Funzionalità attuali

- folder picker nativo Windows;
- risoluzione metadata con precedenza `.ziap → engine → package → cartella`;
- lettura di `data/System.json` e rilevamento automatico di RPG Maker MZ/MV;
- stati distinti `DETECTED` e `ZIAP PROJECT`;
- dialog di inizializzazione precompilato con generazione automatica dell'ID;
- creazione atomica di `.ziap/project.json`, senza sovrascrivere identità esistenti;
- provider RPG Maker MZ con Database, World, System, Assets e mappe nominate;
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
- `NumberBox` per prezzo, Icon ID e parametri delle armi;
- `ComboBox` semantiche per tipo, slot e animazione, salvando sempre l'ID raw;
- Nome e Descrizione localizzati intenzionalmente protetti dalla modifica diretta;
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

## Architettura

```text
src/
├── ZiapStudio.Core/       progetti, documenti, editing e ChangeSet
├── ZiapStudio.Services/   resolver, asset, salvataggio e integrazioni
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

Nella `0.1.2` soltanto i campi strutturali sicuri di Armi sono editabili. Nome e Descrizione
restano read-only quando possono contenere chiavi di localizzazione; gli altri database
continuano a essere consultabili senza possibilità di modifica.
Il metadata `.ziap/project.json` è pensato per essere versionato insieme al
repository; preferenze personali e recenti restano in `%LOCALAPPDATA%`.

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
