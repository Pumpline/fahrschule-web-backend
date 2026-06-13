# Backend-Überblick (ASP.NET Core Web API)

> Für wen ist dieses Dokument? Für den Inhaber, der aus der Unity-Welt kommt
> und beim Mitlesen lernen möchte. Es erklärt, **was** im Backend liegt,
> **warum** es so aufgebaut ist und **wie** die Teile zusammenspielen.
> Begriffsbrücken Unity → Web: siehe [glossar.md](glossar.md).

## Das große Bild

Das Backend ist eine reine **Web-API**: Es nimmt HTTP-Anfragen entgegen
(JSON rein) und antwortet mit Daten (JSON raus). Es rendert keine Seiten –
das macht allein das Angular-Frontend. Verbunden sind beide nur über HTTP.

```
Anfrage vom Browser
   │
   ▼
Middleware-Pipeline (Program.cs)          ← Fehlerfang, Sicherheits-Header,
   │                                        Anmeldung prüfen, Berechtigung prüfen
   ▼
Controller (Fahrschule.Api)               ← dünn: entgegennehmen, Service rufen, antworten
   │
   ▼
Service (Fahrschule.Application)          ← die eigentliche Fachlogik
   │
   ▼
DbContext (Fahrschule.Infrastructure)     ← EF Core übersetzt Objekte ↔ SQL
   │
   ▼
PostgreSQL
```

## Die Projekte (Schichten) und ihre Aufgaben

| Projekt | Aufgabe | Darf benutzen |
|---|---|---|
| `Fahrschule.Domain` | Fachklassen (Entitäten) + Regeln wie `ISoftDeletable` – reines C#, keine Frameworks | nichts |
| `Fahrschule.Contracts` | DTOs: die Datenformate, die über die API gehen | nichts |
| `Fahrschule.Infrastructure` | Datenbankzugang: DbContext, Migrationen, Identity-Benutzer | Domain |
| `Fahrschule.Application` | Services = Fachlogik (z. B. `AuthService`) | Domain, Contracts, Infrastructure |
| `Fahrschule.Api` | Controller (dünn!), Middleware, Konfiguration, Start | Application, Contracts, Infrastructure |
| `Fahrschule.Tests` | xUnit-Tests für die wichtige Fachlogik | alle |

**Warum Schichten?** Damit jede Datei genau eine Sorte Verantwortung hat.
Fachlogik steht nie im Controller (testbar!), HTTP-Details (Cookies, Statuscodes)
stehen nie im Service. Wer etwas sucht, weiß sofort, in welchem Projekt es liegt.

**Warum DTOs statt Entitäten über die API?** Die Datenbankstruktur darf sich
ändern, ohne dass das Frontend bricht – und die API zeigt nur, was die
Oberfläche wirklich braucht (Datensparsamkeit, Projektregel 1).

## Anmeldung – wie der Login wirklich funktioniert

Kernidee (siehe KONZEPT „Sicherheit & Anmeldung"): **zwei Tokens in
httpOnly-Cookies**.

1. **Login** (`POST /api/auth/login`): `AuthService` prüft E-Mail + Passwort
   über ASP.NET Core **Identity** (Passwörter liegen nur als Hash in der DB;
   nach 5 Fehlversuchen sperrt Identity das Konto 15 Minuten).
2. Bei Erfolg entstehen zwei Tokens:
   - **Zugriffstoken (JWT)**, 15 Minuten gültig – ein signiertes Datenpaket
     mit „Claims" (wer bin ich, welche Rollen habe ich). Der Server prüft bei
     jeder Anfrage nur die Signatur – kein Datenbankzugriff nötig.
   - **Refresh-Token**, 14 Tage gültig – nur sein **SHA-256-Hash** steht in
     der Datenbank (wie bei Passwörtern: ein DB-Leck verrät nichts).
3. Beide wandern in **httpOnly-Cookies**: JavaScript kann sie nicht lesen
   (Schutz vor XSS), `SameSite=Strict` verhindert, dass fremde Seiten sie
   mitschicken (Schutz vor CSRF).
4. Läuft das Zugriffstoken ab, ruft das Frontend `POST /api/auth/refresh`:
   das Refresh-Token wird **rotiert** (alt entwertet, neu ausgestellt) –
   ein gestohlenes, schon benutztes Token ist wertlos.
5. **Passwortwechsel** entwertet alle Refresh-Tokens → andere Geräte sind abgemeldet.

Besonderheiten aus dem Konzept:
- **Temporäres Passwort**: `ApplicationUser.MustChangePassword` steht als
  Claim im JWT; die `MustChangePasswordMiddleware` sperrt alles außer
  `/api/auth`, bis ein eigenes Passwort gesetzt ist. (Das Frontend leitet um –
  aber durchgesetzt wird die Regel im Backend, denn Browser-Prüfungen kann
  jeder umgehen.)
- **Erster Admin per Seed**: `DatabaseInitializer` legt beim allerersten
  Start Rollen + einen Admin aus der Konfiguration an – nur solange noch
  kein Benutzer existiert.
- **Sicher per Voreinstellung**: Die `FallbackPolicy` in Program.cs verlangt
  für JEDEN Endpunkt eine Anmeldung; Ausnahmen brauchen explizit `[AllowAnonymous]`.

## Datenbank & Migrationen

EF Core ist ein **ORM**: C#-Klassen ↔ Tabellen. Schemaänderungen laufen über
**Migrationen** – versionierte Schritte, die im Ordner
`Fahrschule.Infrastructure/Migrations` liegen und beim Start automatisch
angewendet werden (`Database:InitializeOnStartup`).

Neue Migration anlegen (im Ordner `backend/`):

```
dotnet ef migrations add NameDerAenderung --project Fahrschule.Infrastructure --startup-project Fahrschule.Api
```

Schon vorhandene Grundlagen für spätere Module:
- **`AuditLog`** – append-only-Protokoll „wer hat wann was geändert"
  (DSGVO-Pflicht); `IAuditWriter` schreibt Einträge, z. B. bei Passwortänderung.
- **`Setting`** – Schlüssel/Wert-Einstellungen, die das Adminpanel später
  pflegt (Projektregel 3: kein Hardcoding fachlicher Werte).
- **`ISoftDeletable`** – Vertrag für „weiches Löschen": markieren statt
  entfernen; echtes Löschen übernimmt der **Aufbewahrungs-Job** nach Fristende
  (Projektregel 7, siehe eigener Abschnitt unten).

## Konfigurationsdaten: Führerscheinklassen (erster Adminpanel-Baustein)

`LicenseClass` ist die erste Umsetzung von Projektregel 3 („fachliche Inhalte
sind Daten"): Klassen, Mindestalter und Voraussetzungen werden im Adminpanel
gepflegt, nicht im Code. Das Muster wiederholt sich bei allen kommenden
Konfigurationsdaten:

- **Service** (`LicenseClassService`) macht die Facharbeit, der Controller bleibt dünn.
- **Audit-Log**: jede Änderung wird mit Vorher/Nachher-JSON protokolliert.
- **Soft-Delete**: Löschen markiert nur (`IsDeleted`); ein globaler
  Query-Filter blendet Gelöschtes überall aus. Der eindeutige Index auf das
  Kürzel gilt nur für nicht gelöschte Zeilen.
- **Optimistische Nebenläufigkeit**: PostgreSQLs `xmin`-Systemspalte dient als
  Versionsmarke. Das Frontend schickt sie beim Speichern mit; hat jemand
  zwischenzeitlich gespeichert, antwortet die API mit **409 Conflict** und
  einer verständlichen Meldung, statt Änderungen zu überschreiben.
- **Rollen**: Lesen dürfen alle Angemeldeten, Schreiben nur `Admin`.
- **Seed**: Beim ersten Start entstehen gängige Klassen als **Startwerte**
  (im Adminpanel änderbar – der Code legt nichts fest).

## Ausbildungsplan-Punkte: Versionierung (KONZEPT 3.3a)

`CurriculumItem` trägt die Theorie-Themen (später auch Grundfahraufgaben und
Sonderfahrten). Das Besondere ist die **Versionierung**: Jeder Punkt hat eine
feste Kennung (`ItemKey`), die über alle Versionen gleich bleibt. Ändert der
Admin den INHALT (Bezeichnung, Soll-Anzahl, Klassen-Zuordnung), legt der
Service automatisch eine **neue Zeile mit Version+1** an und markiert die alte
als abgelöst (`SupersededAtUtc`) – gelöscht wird nie. Schüler-Checklisten
(Schritt 4) verweisen später auf die Version, die zu ihrer Anmeldung galt:
Gesetzesänderungen wirken nie rückwirkend. Rein organisatorische Änderungen
(aktiv/Reihenfolge) ändern die Version dagegen NICHT – die Entscheidung trifft
`CurriculumRules.NeedsNewVersion` (per Unit-Test abgesichert).

Die Klassen-Zuordnung läuft über die M:N-Tabelle `CurriculumItemClass`;
**keine Zuordnung bedeutet "gilt für alle Klassen"** (typisch Grundstoff).

## Unterlagen-Katalog je Klasse (KONZEPT 3.1)

`DocumentCatalogItem` steuert, welche Nachweise (Sehtest, Erste-Hilfe, Antrag …)
in der Schüler-Akte erscheinen – je nach gewählten Klassen (M:N über
`DocumentCatalogItemClass`; **keine Zuordnung = gilt für alle Klassen**).
Besonderheit: das Flag `ExpiryDateRequired` erzwingt später ein Ablaufdatum,
bevor eine Unterlage als „liegt vor" abgehakt werden darf. **Datensparsamkeit
(DSGVO)**: gespeichert wird nur der Status + Daten, nie die Dokumente selbst.
Gleiches Muster wie die übrigen Konfigurationsdaten (Audit, Soft-Delete,
xmin-Konfliktschutz, Admin-only beim Schreiben).

## Benutzerverwaltung (KONZEPT 3.7a)

`UserService` (über ASP.NET Core Identity) verwaltet die Konten im Adminpanel:
anlegen mit **generiertem temporärem Passwort** (`TemporaryPasswordGenerator`,
policy-konform und gut vorlesbar), Rolle/Name ändern, Passwort zurücksetzen,
löschen. Neue Benutzer und Resets setzen `MustChangePassword` → beim ersten
Anmelden wird ein eigenes Passwort erzwungen (kein E-Mail-Versand nötig).

Schutzregeln: man kann das eigene Konto nicht löschen, und der **letzte
verbleibende Admin** kann weder gelöscht noch herabgestuft werden. Alles
admin-only; jede Änderung wird auditiert (nie Passwörter). Der Reset entfernt
das alte Passwort und setzt direkt ein neues (`RemovePasswordAsync` +
`AddPasswordAsync`) – bewusst ohne E-Mail-Token-Provider.

## Betriebs-Einstellungen (Settings)

`SettingsService` liest/schreibt die betrieblichen Werte (Erinnerungs-Vorläufe,
Prüfungs-Sperre) aus der generischen `Setting`-Tabelle. Jeder Wert hat einen
**Default** und einen **erlaubten Bereich** (Validierung + Seed). Ergänzt um
eine fachliche Regel: die verkürzte Sperre darf nie länger als die normale
sein. Lesen dürfen alle Angemeldeten, Schreiben nur der Admin; Änderungen
werden auditiert.

## Schülerverwaltung (KONZEPT 3.1) – das erste große Fachmodul

- **Student** (Stammdaten, Soft-Delete, xmin) + **StudentLicenseClass**:
  die Phase liegt **pro Klasse** (Theory → TheoryExam → Practice →
  PracticeExam → Completed), nicht pro Schüler.
- **Datensparsamkeit**: nur Vertragsdaten (Name, Geburtsdatum, Kontakt,
  Adresse, Notiz) – keine besonderen Kategorien.
- `StudentService`: Liste mit **Suche + Klassen-/Phasen-Filter + Paging**,
  CRUD, Klasse hinzufügen/entfernen, Phase setzen. Beim Hinzufügen einer
  Klasse prüft `StudentRules.CheckMinimumAge` das Mindestalter gegen die
  Klasse (Ausbildung darf bis zu 1 Jahr vor dem Mindestalter beginnen).
- **Fortschritt %**: vorerst aus der Phase abgeleitet (`StudentRules`),
  bis die echte Stunden-/Prüfungserfassung kommt (Schritt 4).
- Gleiches Muster wie überall: Audit-Log, Soft-Delete, xmin-Konfliktschutz.
- **Unterlagen-Checkliste pro Schüler** (`DocumentChecklistItem` +
  `StudentDocumentService`): die nötigen Unterlagen werden aus dem Katalog
  **abgeleitet** (gilt-für-alle oder Schnittmenge mit den Klassen des Schülers);
  gespeichert wird nur der Status (liegt vor / vorgelegt am / läuft ab am) –
  nie Dokumente. „Ablaufdatum Pflicht" wird erzwungen; „bald fällig" nutzt die
  konfigurierbare Vorlaufzeit aus den Einstellungen.

## Ausbildungsfortschritt (KONZEPT 3.3 / 3.3a) – Schritt 4a

- **Persönliche Checkliste als Snapshot**: `StudentProgressItem` ist eine
  **Kopie** der Ausbildungsplan-Punkte, die zum Zeitpunkt der Anmeldung galten
  (Titel, Abschnitt, Soll-Anzahl und die kopierte Version). Spätere Änderungen
  am Master-Plan wirken so **nicht rückwirkend** – wichtig für den späteren
  Ausbildungsnachweis. Geteilte „Grundstoff"-Punkte werden **einmal** geführt
  und für alle passenden Klassen gewertet (`StudentProgressItemClass`; leere
  Klassenliste = gilt für alle Klassen des Schülers).
- **Snapshot „bei Bedarf"**: `StudentProgressService.EnsureSnapshotAsync` legt
  beim Laden fehlende Punkte an und ergänzt Klassen-Zuordnungen, wenn später
  eine Klasse dazukommt – nie wird etwas entfernt (eine entfernte Klasse darf
  erfassten Fortschritt nicht zerstören). So werden auch Altschüler nachgezogen.
- **Abhaken & Zählen**: einfache Punkte werden direkt erledigt gesetzt
  (mit „Erledigt am" + Notiz); zählbare Punkte (z. B. Sonderfahrten) haben
  einen **Zähler** – jede Stunde ist eine eigene Zeile mit Datum + Notiz
  (`StudentProgressEntry`), und beim Erreichen des Solls gilt der Punkt
  automatisch als erledigt. Das Austragen eines erledigten Punkts wird
  protokolliert (Bestätigung in der Oberfläche).
- **Anzeige pro Klasse**: der Service liefert den Fortschritt je Klasse, nach
  Abschnitten gruppiert, inklusive Prozent (`StudentProgressRules` – reine,
  getestete Logik). Endpunkte unter `/api/students/{id}/progress`.
## Stunde eintragen (KONZEPT 3.3) – Schritt 4b

- **`Lesson`** (+ `LessonType` Theorie/Praxis, + `LessonItem`): eine eingetragene
  Unterrichtseinheit – Typ, Klasse (oder `null` = „Grundstoff", zählt für alle),
  Datum, Dauer, optionale Notiz und die **behandelten Punkte**. Migration
  „Unterrichtsstunden".
- **`LessonService.CreateAsync`**: legt die Stunde an und **wirkt auf den
  Fortschritt** – einfache Punkte werden auf das Stundendatum abgehakt, zählbare
  bekommen je einen Eintrag (`StudentProgressEntry`); die Punkte werden über
  `LessonItem` mit der Stunde verknüpft (für den späteren Ausbildungsnachweis).
  Validierung: gültiger Typ, Dauer > 0, gewählte Klasse gehört zum Schüler,
  behandelte Punkte gehören zum Schüler. Audit „Stunde eingetragen".
- Das ist die **einzige** Eingabestelle für Stunden (KONZEPT 3.3). Endpunkte
  unter `/api/students/{id}/lessons`. Frontend: Modal im Tab „Ausbildungsfortschritt".

## Anrechnung beim Klasse-Hinzufügen (KONZEPT 3.3a) – Schritt 4c

- **`StudentProgressService.GetCreditPreviewAsync`**: vergleicht den heutigen Plan
  der neuen Klasse mit dem schon Erledigten und teilt in drei Töpfe: **angerechnet**
  (geteilter Punkt erledigt, Version unverändert), **bitte prüfen** (erledigt, aber
  der Plan-Punkt hat seither eine neue Version) und **neu** (klassenspezifisch oder
  noch offen). Endpunkt `GET /api/students/{id}/progress/credit-preview/{classId}`.
- **Modell-Hinweis**: Die Anrechnung passiert **automatisch** – geteilte Punkte
  (leere Klassenzuordnung) sind **eine** Snapshot-Zeile, die für alle Klassen des
  Schülers zählt; ist sie erledigt, gilt sie sofort auch für die neue Klasse. Die
  Vorschau macht das nur **transparent** (Mockup-Dialog) und kennzeichnet geänderte
  Inhalte. Frontend: zweistufiger „Klasse hinzufügen"-Dialog (wählen → Vorschau in
  box gut/warn/neu → bestätigen).

## Prüfungen & Wiederholungs-Sperre (KONZEPT 3.4) – Schritt 5

- **`Exam`** (Kind Theorie/Praxis, `IsPreliminary`, Datum, Result geplant/bestanden/
  nicht bestanden): Migration „Pruefungen". Echte Prüfungen zählen als Versuche,
  Vorprüfungen werden nur **vermerkt** (kein Versuch, keine Sperre).
- **Abgeleitet, nicht gespeichert** (bleibt konsistent bei Änderungen):
  - **Versuchsnummer** = laufende Nummer der echten Prüfungen je Art+Klasse.
  - **Wiederholungs-Sperre** (`ExamRules`): aus der letzten nicht bestandenen,
    noch nicht durch eine bestandene aufgelösten Prüfung; Sperr-Ende = Datum +
    normale Wochen, **verkürzt** sobald genug **Übungsstunden** (gleiche Art+Klasse,
    nach dem Fehlversuch) im Ausbildungsfortschritt stehen. Werte aus den Settings
    (`ExamLockNormalWeeks/ShortenedWeeks/PracticeLessonsForShortening`). So sind
    Stunden nur an EINER Stelle eingetragen und die Sperre rechnet selbst.
- **`ExamService`**: GetForStudent (Prüfungen + Sperr-Infos), Create mit Regeln:
  Praxisprüfung erst nach bestandener Theorieprüfung; eine echte Wiederholung darf
  nicht vor dem Sperr-Ende geplant werden; Audit „Prüfung eingetragen".
- Endpunkte `/api/students/{id}/exams`. Frontend: dritter Tab „Prüfungen" mit
  Tabelle, „Prüfung eintragen"-Modal und Sperr-Karte(n).
- Noch offen (später): Prüfungstermine bei TÜV/DEKRA (`ExamBooking`), volle
  Zulassungs-Verwaltung.

## Terminkalender (KONZEPT 3.5) – Schritt 6

- **`CalendarEvent`** (Datum, Von/Bis-Zeit, Kind Praxis/Theorie/Prüfung/Sonstiges,
  optionaler Schüler, Notiz, `Reminded` für späteren Push): Migration „Kalender".
  Ein Fahrlehrer → globaler Kalender; ein späteres `InstructorUserId` kann Termine
  einem Fahrlehrer zuordnen.
- **`CalendarService`**: Monatsabruf, Anlegen/Ändern/Löschen mit **Doppelbuchungs-
  Prüfung** (`CalendarRules.Overlaps` – Überlappung nur, wenn beide eine Endzeit
  haben), Validierung (Endzeit > Startzeit, eigene Bezeichnung bei „Sonstiges",
  Schüler existiert), Audit. Endpunkte `/api/calendar?year=&month=`.
- Frontend: Seite `kalender` (`CalendarPage`) mit Monatsgitter, Tages-Terminliste
  und Termin-Dialog (anlegen/bearbeiten/löschen, optionaler Schüler).
- Noch offen (später): Termin → Unterrichtsnachweis verknüpfen, Push-Erinnerung.

## Ausbildungsnachweis als PDF (KONZEPT 3.3/7) – Schritt 7

- **`TrainingRecordPdfService`** erzeugt den druckbaren Ausbildungsnachweis mit
  **QuestPDF** (Community-Lizenz – für Kleinbetriebe kostenlos, einmalig im
  statischen Konstruktor gesetzt). Es ruft die vorhandenen Services
  (Schüler/Fortschritt/Prüfungen) ab, sodass das PDF dieselben Zahlen zeigt wie
  der Bildschirm; das Layout liegt in `TrainingRecordDocument`.
- Inhalt: Kopf (Name, Geburtsdatum, Erstell-Datum), je Klasse der Stand und die
  Plan-Punkte (erledigt/offen, zählbare mit x/n), dann die Prüfungen.
- Endpunkt `GET /api/students/{id}/ausbildungsnachweis` → liefert die PDF-Datei.
  Frontend: Button „🖨 Ausbildungsnachweis (PDF)" im Stammdaten-Tab lädt sie über
  HttpClient (damit der Auth-Interceptor das Token erneuern kann).
- Der Kopf zeigt die **Fahrschul-Stammdaten** (Name/Anschrift/Erlaubnisnummer)
  aus den Einstellungen, sobald gepflegt (`SettingsService`, String-Settings
  `School.*`). Noch offen (später): Vertrag/Quittung als weitere Vorlagen.

## DSGVO im Adminpanel (KONZEPT 3.7) – Schritt 8

Kein separates DSGVO-Center – alles liegt im Adminpanel (eine Karte), **admin-only**
(`[Authorize(Roles = Roles.Admin)]` am `AdminController`, Route `api/admin`).

- **Audit-Log-Ansicht** (`AuditQueryService`): filterbar (Freitext über Nutzer/
  Aktion/EntityType/EntityId via `ILike`) und paginiert, neueste zuerst. Das Log
  bleibt append-only (nur `AuditWriter` schreibt).
- **„Zur Löschung vorgemerkt" + Wiederherstellen** (`StudentService.GetDeletedAsync`
  / `RestoreAsync`): zeigt die soft-gelöschten (ausgeblendeten) Schüler
  (`IgnoreQueryFilters`) und macht das rückgängig (protokolliert). Das echte
  Entfernen übernimmt der Aufbewahrungs-Job nach Ablauf der gesetzlichen Frist –
  unabhängig vom Vormerken (Projektregel 7, eigener Abschnitt).
- **Datenexport** (`StudentExportService`, Art. 15/20 DSGVO): sammelt alle Daten
  eines Schülers (Stammdaten, Fortschritt, Unterlagen, Prüfungen, Stunden, Termine)
  in **eine JSON-Datei** und protokolliert den Export. Endpunkt
  `GET /api/admin/students/{id}/export`.
- Frontend: `DsgvoManagement`-Karte im Adminpanel (Schüler wählen → Export/Löschen,
  Vorgemerkt-Liste, Aufbewahrungs-Übersicht mit Lösch-Datum + „fällige jetzt
  löschen", Audit-Log mit Suche).
- Noch offen (später): Legal-Texte (Impressum/Datenschutz).

## Aufbewahrungs-Job: endgültiges Löschen nach Fristende (KONZEPT 3.7 / § 31 FahrlG)

**Rechtsgrundlage**: § 31 Abs. 3 Fahrlehrergesetz – die Ausbildungs-Aufzeichnungen
sind **nach Ablauf des Jahres, in dem der Unterricht abgeschlossen wurde, fünf
Jahre** aufzubewahren und danach **unverzüglich zu löschen**. Über-Aufbewahrung
ist also genauso ein DSGVO-Verstoß wie zu frühes Löschen. (Rechnungen/steuerlich
relevante Daten = 10 Jahre nach AO/HGB – betrifft die noch nicht gebaute
Rechnungsfunktion, nicht die reinen Ausbildungsdaten.)

- **Die Frist ist eine Einstellung in JAHREN, kein fester Wert** (Regel 3): das
  Setting `Retention.StudentYears` (Standard 5, Bereich 1–30) wird im Adminpanel
  unter „Einstellungen" gepflegt – falls sich das Gesetz ändert, ohne neue
  Programmierung.
- **`StudentRetentionRules`** (reine, getestete Logik): bestimmt das
  **Ausbildungsende** als spätestes Datum aus letzter Stunde, letzter Prüfung und
  Anmeldedatum (das deckt auch **Abbrecher** ab, die nie „abgeschlossen" sind) und
  daraus das **Lösch-Datum** = 1. Januar des Jahres (Ausbildungsende-Jahr + Frist + 1).
- **`RetentionService`** (`Fahrschule.Application/Retention`) ist die einzige
  Stelle, die personenbezogene Daten endgültig entfernt:
  - `GetStatusAsync` listet die Schüler, deren Lösch-Datum erreicht ist, mit
    Ausbildungsende + Lösch-Datum.
  - `RunAsync` prüft **alle** Schüler (Löschung richtet sich nach dem gesetzlichen
    Datum, nicht nach dem Vormerken) und entfernt die fälligen **endgültig samt
    abhängiger Daten**. Termine (`CalendarEvent`) zeigen mit
    `DeleteBehavior.Restrict` auf den Schüler – die Datenbank würde das Löschen
    sonst verweigern –, deshalb werden sie zuerst entfernt; der Rest
    (Anmeldungen, Fortschritt, Stunden, Prüfungen) ist als **Cascade**
    konfiguriert und verschwindet automatisch mit dem Schüler. Wichtig: die
    Aktivitäts-Abfragen nutzen `IgnoreQueryFilters`, sonst würden bei einem
    ausgeblendeten Schüler die Stunden „verschwinden" und das Lösch-Datum fiele
    fälschlich auf das Anmeldedatum zurück.
  - Jede endgültige Löschung wird **auditiert** (Aktion „Endgültig gelöscht",
    Benutzer „System (Aufbewahrung)" beim automatischen Lauf) – aber sparsam:
    nur Name + Ausbildungsende + Frist als Nachweis, dass die Frist gewahrt
    wurde, ohne die gerade gelöschten Daten erneut zu speichern.
- **Automatik via `RetentionBackgroundService`** (`Fahrschule.Api/BackgroundJobs`):
  ein **`BackgroundService` / `IHostedService`** – ein Dauerläufer, den der
  ASP.NET-Host beim Start hochfährt (web-typisch; am ehesten mit einer
  Unity-`Update`-Schleife vergleichbar, nur einmal **pro Tag** statt pro Frame).
  Er läuft außerhalb jeder HTTP-Anfrage und hat darum keinen eigenen
  (request-gebundenen) `DbContext`; pro Lauf öffnet er über die
  `IServiceScopeFactory` einen frischen **DI-Scope**, um sich Service + DbContext
  zu leihen. Ein Fehler im Lauf wird protokolliert und legt nie die ganze App
  lahm – der nächste Tageslauf versucht es erneut.
- **Manuell auslösbar**: Im Adminpanel (DSGVO-Karte) erscheint bei fälligen
  Schülern ein Button „… fällige jetzt endgültig löschen" (mit Bestätigung) –
  praktisch zum Prüfen und für den Fall, dass nicht bis zum nächsten
  Tageslauf gewartet werden soll. Endpunkte: `GET /api/admin/retention`,
  `POST /api/admin/retention/run` (beide admin-only).

## Start-Dashboard (KONZEPT 3.1/3a)

- **`DashboardService`** füllt die Startseite, indem es vorhandene Daten bündelt
  (nur lesen): **„Bald fällig"** = Unterlagen mit Ablaufdatum innerhalb des
  Erinnerungsfensters (`DocumentExpiryReminderDays` aus den Settings) oder schon
  überfällig, je Schüler/Unterlage; **„Letzte Änderungen"** = die neuesten
  Audit-Einträge (über `AuditQueryService`).
- Endpunkt `GET /api/dashboard` (alle angemeldeten Rollen). Frontend: `start-page`
  zeigt beide Listen; eine „Bald fällig"-Zeile öffnet die Schüler-Akte.

## Fehlerbehandlung – eine Stelle für alles

Services werfen aussagekräftige Ausnahmen (`AppValidationException`,
`AuthenticationFailedException`, …) mit **deutschen, lösungsorientierten
Meldungen**. Die `ExceptionHandlingMiddleware` übersetzt sie in einheitliche
JSON-Antworten (ProblemDetails, HTTP 400/401/403/404). Unerwartete Fehler
landen mit allen Details im Server-Log – der Benutzer sieht nur eine
freundliche, neutrale Meldung.

## Konfiguration & Geheimnisse

- `appsettings.json` = Struktur + ungefährliche Standardwerte.
- `appsettings.Development.json` = Werte NUR für localhost (Dev-Datenbank,
  Dev-JWT-Schlüssel, Seed-Admin). Bewusst eingecheckt, damit der Einstieg
  leicht ist – Program.cs **verweigert den Start in Produktion**, wenn der
  Entwicklungs-Schlüssel noch gesetzt ist.
- Produktion: alles Geheime über **Umgebungsvariablen**
  (z. B. `Jwt__SecretKey`, `ConnectionStrings__Default`). Nie ins Repository.

## Tests

`Fahrschule.Tests` (xUnit) sichert die sicherheitskritische Logik:
Token-Erzeugung (richtige Claims/Laufzeit), Refresh-Token-Hashing,
Gültigkeitsregeln. Ausführen:

```
cd backend
dotnet test
```
