# SSMS MCP Server

En lokal VSIX-utvidelse for SSMS 22 med Streamable HTTP direkte i SSMS-prosessen. Den gir MCP-klienter tilgang til aktiv SQL-kontekst, tabeller, visninger, avgrensede lesespørringer, bekreftede skrivekommandoer og databasekopiering.

HTTP-serveren kjører direkte i SSMS-prosessen på .NET Framework 4.8. MCP-bibliotekets versjon er tilpasset avhengighetene SSMS 22 allerede bruker. Det trengs ingen Bridge.exe.

## Bygg og installer

Krever Visual Studio 2026 med VSSDK, .NET 10 SDK og SSMS 22 på Windows. Prosjektet bruker SSMS-grensesnitt som Microsoft ikke støtter for tredjepartsutvidelser, og må derfor testes ved hver SSMS-oppgradering.

```powershell
./scripts/Build.ps1
```

Lagre arbeidet og lukk SSMS før installasjon. Kjør installasjonsskriptet for å installere VSIX-pakken og opprette en lokal tilgangsnøkkel:

```powershell
./scripts/Install.ps1
```

MCP-serveren og avhengighetene ligger i VSIX-pakken. Tilgangsnøkkelen lagres som brukermiljøvariabelen `SSMS_MCP_TOKEN`; del den ikke med andre.
Installasjonsskriptet avviser en VSIX-pakke som er eldre enn kildekoden; kjør `./scripts/Build.ps1` på nytt etter kodeendringer.

Start SSMS på nytt. Under **Tools → SSMS MCP Server status** vises prosess-ID, sesjonsmodus og MCP-adressen. Første SSMS-prosess bruker normalt `http://127.0.0.1:51741/mcp`. Flere samtidige SSMS-prosesser bruker neste ledige port. Endre klientens URL hvis adressen i statusvinduet er en annen.

Start MCP-klienten på nytt etter installasjon, slik at den leser `SSMS_MCP_TOKEN` fra brukermiljøet. I MetaMCP må SSMS-serveren kobles fra og til igjen, eller MetaMCP-backenden startes på nytt, før verktøylisten oppdateres.

For avinstallasjon: lagre arbeidet, lukk SSMS og kjør `./scripts/Uninstall.ps1`.

## MCP-klienter

Codex leser `~/.codex/config.toml` eller en prosjektlokal `.codex/config.toml`. Bruk adressen fra statusvinduet i SSMS:

```toml
[mcp_servers.ssms]
url = 'http://127.0.0.1:51741/mcp'
bearer_token_env_var = 'SSMS_MCP_TOKEN'
tool_timeout_sec = 180
```

Andre MCP-klienter må støtte Streamable HTTP og sende `Authorization: Bearer <verdien i SSMS_MCP_TOKEN>`. Serveren lytter bare på `127.0.0.1` og avviser ugyldig `Origin`.

Ved flere SSMS-prosesser velger du adressen fra statuskommandoen i riktig SSMS-vindu. Verktøyet `ssms_instances` viser prosess-ID og vindustittel for alle åpne SSMS-instanser.

## Godkjenning i MCP-klienten

Verktøy som kjører SQL forbereder først spørringen og viser server, database og nøyaktig SQL i MCP-klienten når klienten støtter MCP-godkjenning (elicitation). Kjøring skjer bare når denne forespørselen godkjennes. Hvis klienten ikke støtter dette, brukes klientens vanlige verktøytillatelse. Sett leserverktøyene til **Needs approval** i klienten og godkjenn hvert kall der. Ikke bruk **Always allow** for disse verktøyene dersom hvert kall skal godkjennes. SSMS viser fortsatt eventuell passorddialog ved separat SQL-pålogging.

Godkjenning via vanlig verktøytillatelse skjer før serveren kan hente den aktive serveren, databasen og SQL-teksten. Kontroller derfor riktig SSMS-instans og verktøyargumentene i klienten før du godkjenner. Serveren avviser kjøring hvis aktiv editor, tilkobling eller sesjon endres etter forberedelsen.

`ssms_run_delete`, `ssms_run_update` og `ssms_run_insert` krever alltid en egen bekreftelse i SSMS før SQL kjøres. Dialogen viser server, database og hele SQL-teksten. Avbryt, lukking av dialogen eller manglende SSMS-vindu stopper kommandoen. Bekreftelsen gjelder også når MCP-klienten ikke støtter MCP-godkjenning. `ssms_run_select` følger den eksisterende godkjenningsflyten.

## Sesjonstest

Inntil testen under er bestått, oppretter lesespørringene en separat SQL-sesjon. I det aktive SSMS-vinduet:

```sql
CREATE TABLE #ssms_mcp_probe (id int);
```

Kall MCP-verktøyet `ssms_probe_session`. Et svar med `verified: true` viser at utvidelsen kan lese temp-tabellen og `@@SPID` fra den samme sesjonen. Deretter brukes samme SPID for denne tilkoblingen så lenge den er åpen. Bytt av tilkobling krever en ny test. Rydd opp i vinduet med `DROP TABLE #ssms_mcp_probe;` når testen er ferdig. Hvis testen feiler, fortsetter lesekallene i separat sesjon.

Med separat SQL-pålogging spør SSMS om passord når en tilkobling må opprettes. Passordet skrives ikke til disk eller sendes til MCP-prosessen. Windows- og Entra-pålogging bruker den lokale identiteten eller interaktiv innlogging.

## Verktøy og grenser

| Verktøy | Innhold |
| --- | --- |
| `ssms_context` | Aktiv server, database, editorinnhold, markert tekst og sesjonsmodus. |
| `ssms_instances` | Åpne SSMS-instanser og prosess-ID-er. |
| `ssms_list_objects` | Tabeller og visninger. |
| `ssms_describe_object` | Kolonner i valgt tabell eller visning. |
| `ssms_preview_rows` | Inntil 100 rader fra valgt tabell eller visning. |
| `ssms_run_select` | Én SELECT-setning. |
| `ssms_run_delete` | Én DELETE-setning. Må bekreftes i SSMS. |
| `ssms_run_update` | Én UPDATE-setning. Må bekreftes i SSMS. |
| `ssms_run_insert` | Én INSERT-setning. Må bekreftes i SSMS. |
| `ssms_probe_session` | Test av samme SPID og temp-tabell. |
| `ssms_copy_database` | Kopierer aktiv brukerdatabase til et nytt navn på samme SQL Server-instans. |
| `ssms_set_database_offline` | Setter en registrert testdatabase offline fra en egen tilkobling til `master`; valgfri `rollbackImmediate`. |
| `ssms_set_database_online` | Setter testdatabasen online fra `master` og kontrollerer tilstanden. |
| `ssms_kill_session` | Avslutter en valgt SPID etter kontroll av at sesjonen bruker testdatabasen. |
| `ssms_hold_lock` | Holder en eksklusiv tabellås i en vedvarende MCP-styrt SQL-transaksjon i 1–300 sekunder. |
| `ssms_release_lock` | Frigir låsen ved å rulle tilbake transaksjonen. |
| `ssms_reset_database_copy` | Gjenoppretter kopien fra opprinnelig backup eller sletter den. |
| `ssms_inspect_recovery` | Viser recovery-modell, backuphistorikk og kjente avhengigheter for databasen i aktiv SQL-fane. |
| `ssms_set_recovery_simple` | Endrer aktiv brukerdatabase fra `FULL` til `SIMPLE` etter bekreftelse i SSMS. |

Hvis et SQL-verktøy kalles uten en aktiv spørringsfane, åpner utvidelsen en ny fane og bruker SSMS-tilkoblingen som velges der. Velg riktig server og database i Object Explorer, eller fullfør SSMS sin tilkoblingsdialog når den vises. MCP-spørringen skrives ikke i fanen. `ssms_context` leser fortsatt bare en aktiv SQL-fane, og `ssms_probe_session` krever en eksisterende levende SQL-sesjon.

Når klienten støtter MCP-godkjenning, kreves klientbekreftelse for lesekall og databasekopiering. Andre klienter må settes til **Needs approval** for at disse kallene skal godkjennes; serveren kan ikke kontrollere denne klientinnstillingen. Skriveverktøyene viser i stedet alltid bekreftelsesdialogen i SSMS. Lesesvar begrenses til 100 rader eller 1 MiB, og SQL-kjøring har 10 sekunders tidsavbrudd. Skriveverktøyene tillater bare én setning av riktig type og avviser blant annet nøstede skrivekommandoer, `SELECT INTO`, `OUTPUT INTO` og sekvensuttrykk. De returnerer antall berørte rader (`affectedRows`); SQL Server kan returnere `-1` hvis antallet ikke er tilgjengelig. SQL-syntakskontrollen er ikke en sikkerhetsgrense; rettighetene til databasebrukeren avgjør hva tilkoblingen faktisk kan lese og endre.

### Databasekopi

`ssms_copy_database` tar `targetDatabase`, `backupDirectory` og `dataDirectory`. Katalogene er **absolutte baner på SQL Server-verten**, og SQL Server-tjenestekontoen må kunne skrive der. Verktøyet tar en `COPY_ONLY`-fullbackup av databasen i den aktive SQL-fanen, og gjenoppretter den under det nye navnet med nye data- og loggfiler. En unik backupfil blir liggende i `backupDirectory`, også dersom gjenopprettingen feiler. Måldatabasen må ikke finnes fra før. Verktøyet avviser systemdatabaser og kilder med FILESTREAM eller andre spesialfiltyper. Det kopierer ikke serverobjekter som pålogginger og SQL Agent-jobber.

Klienten viser hele SQL-batchen før godkjenning når den støtter MCP-godkjenning. Sett `ssms_copy_database` til **Needs approval** i klienter uten slik støtte. Kopieringen kan ta betydelig lenger tid enn lesespørringer; øk `tool_timeout_sec` ved behov. Ved avbrudd kan en backupfil eller delvis opprettet måldatabase bli liggende, og disse må kontrolleres i SSMS før nytt forsøk.

### Feilinjeksjon mot databasekopier

De nye verktøyene krever at testdatabasen er opprettet med `ssms_copy_database` etter denne versjonen. Utvidelsen registrerer server, databasenavn, database-ID, opprettelsestidspunkt, backupfil og filbaner lokalt i `%LOCALAPPDATA%\SsmsMcpServer\database-copies.json`. En database som er slettet og opprettet på nytt under samme navn, avvises. Tidligere kopier uten registrering må opprettes på nytt. Backupfilen må beholdes for `ssms_reset_database_copy`.

`ssms_set_database_offline(database, rollbackImmediate)` og `ssms_set_database_online(database)` bruker alltid en egen tilkobling til `master`. Tilkoblingen i den aktive fanen kan derfor brytes når databasen tas offline, mens online-kallet fortsatt kan bruke serveropplysningene fra fanen. Begge kallene leser tilstanden fra `sys.databases` etterpå. `rollbackImmediate` er som standard `false`.

`ssms_kill_session(database, sessionId)` kontrollerer at SPID-en er en brukersesjon i testdatabasen, og returnerer også `programName` slik at programmet bak sesjonen kan identifiseres. SPID-en må finnes når kallet kjøres. SQL-rettigheter for å se sesjonen og kjøre `KILL` kreves.

`ssms_hold_lock(database, schema, table, maximumSeconds)` åpner en egen forbindelse til testdatabasen, starter en transaksjon og tar `TABLOCKX, HOLDLOCK` på en brukertabell. Svaret inneholder `lockId`, SPID og utløpstid. Låsen frigjøres automatisk etter høyst 300 sekunder, ved lukking av SSMS, eller med `ssms_release_lock(lockId)`. Bare én MCP-styrt lås kan være aktiv per SSMS-instans.

Lesespørringer som bruker radversjonering kan fortsatt lese mens tabellåsen holdes; bruk en konfliktende skriveoperasjon når testen må blokkere sikkert.

`ssms_reset_database_copy(database, delete, rollbackImmediate)` gjenoppretter kopien fra backupfilen og flytter filene til kopiens opprinnelige filbaner. Sett `delete` til `true` for å slette databasen og fjerne registreringen. `rollbackImmediate` er valgfri ved aktive forbindelser. Reset og sletting må ha nødvendige SQL Server-rettigheter. Dersom gjenopprettingen feiler etter overgang til `SINGLE_USER`, må databasens tilstand kontrolleres i SSMS.

Alle disse handlingene går gjennom den eksisterende MCP-godkjenningen med synlig SQL når klienten støtter den. Sett dem til **Needs approval** i klienter uten MCP-godkjenning. Ved SQL-pålogging ber SSMS om passord for den separate forbindelsen.

### Recovery-modell

Kjør `ssms_inspect_recovery` i SQL-fanen for databasen som skal vurderes. Svaret viser recovery-modell, tilstand, siste full-, differensial- og loggbackup fra `msdb`, antall loggbackuper siste 30 dager, samt registrert Always On, replikering og log shipping. `assessment` er en forsiktig vurdering: `keep_full` ved registrert avhengighet, `review_log_backups` ved nylige loggbackuper, `already_simple`, `manual_review` eller `decision_required`. Historikk i `msdb` beviser ikke at en backup er tilgjengelig eller kan gjenopprettes, og viser ikke nødvendigvis eksterne backupjobber.

Avklar med databaseeier hvor mye datatap som er akseptabelt, om gjenoppretting til et bestemt tidspunkt kreves, og hvordan fullbackuper tas og testes. `SIMPLE` tillater ikke loggbackup eller gjenoppretting til et bestemt tidspunkt mellom databackuper. Kjør `ssms_set_recovery_simple` bare når dette er akseptert og loggbackupjobber er håndtert. Verktøyet viser SQL og krever alltid bekreftelse i SSMS. Det avviser systemdatabaser, databaser som ikke er skrivbare og online i `FULL`, og registrert Always On, replikering eller log shipping. Etter endringen leses modellen tilbake fra `sys.databases`. Endringen bryter loggbackupkjeden; behold en fungerende plan for fullbackup.

## Verifisering

`dotnet test SsmsMcp.slnx` tester SELECT- og skrivevalidering samt MCP-protokollen over Streamable HTTP. Installasjon, autentisering, aktiv editor, samme SPID, bekreftelsesdialogen og klientoppsett må også testes i SSMS 22 på målmaskinen.

HTTP-feil logges i `%LOCALAPPDATA%\SsmsMcpServer\extension.log` for feilsøking.
