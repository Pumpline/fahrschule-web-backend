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
  entfernen; echtes Löschen übernimmt später der Aufbewahrungs-Job (Projektregel 7).

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
- Noch offen (spätere Teilschritte): vollwertiger „Stunde eintragen"-Datensatz
  mit Typ/Dauer (4b) und die **Anrechnung** beim Klasse-Hinzufügen (4c).

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
