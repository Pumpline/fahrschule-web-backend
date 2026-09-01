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
  Grundsatz: **nur echte Änderungen** werden protokolliert – ein Schreibvorgang,
  der nichts ändert (z. B. identische Stammdaten erneut speichern, oder einen
  Fortschritts-Punkt auf seinen aktuellen Stand setzen), erzeugt **keinen**
  Eintrag. Lesende Zugriffe bleiben protokolliert (z. B. „Stammdaten angesehen").
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
- **Datensparsamkeit**: nur Vertragsdaten (Name, Journalnummer, Geburtsdatum,
  Kontakt, Adresse, Notiz) – keine besonderen Kategorien.
- **Journalnummer** (`Student.JournalNumber`, max. 30 Zeichen, optional): die
  eigene Aktennummer der Fahrschule („Schülerverzeichnis-Nr."), die die digitale
  Akte mit dem Papier-Journal verbindet. Sie steht auf dem Ausbildungsnachweis
  und ist – anders als Geburtsdatum/Kontakt – **nicht** hinter dem 👁 versteckt:
  sie kommt wie der Name direkt mit der Akte (`StudentAkteDto.JournalNumber`) und
  steht auch in der Liste. Beim Speichern prüft `StudentService`, dass die Nummer
  **nur einmal** vergeben ist (Vergleich ohne Groß-/Kleinschreibung und
  Leerzeichen, `StudentRules.NormalizeJournalNumber`); Nummern von zur Löschung
  vorgemerkten Schülern bleiben blockiert, solange diese wiederherstellbar sind.
- **Vorbesitz** (`StudentPriorLicenseClass` + `Student.PriorLicenseNote`): welche
  Fahrerlaubnis der Schüler **schon hat**. Eigene Verknüpfungstabelle statt eines
  Schalters an `StudentLicenseClass` – ein Vorbesitz ist keine Ausbildung, er hat
  weder Phase noch Fortschritt. Gesetzt wird der **ganze Block auf einmal**:
  `PUT /api/students/{id}/vorbesitz` mit `SetStudentPriorLicenseRequest`
  (vollständige Klassenliste, Freitext, feste Doppelstundenzahl + Begründung).
  Ein eigener Endpunkt, weil die Eingabe im **Ausbildungsfortschritt** sitzt –
  dort, wo sie etwas bewirkt – und dieser Tab die Stammdaten gar nicht kennt.
  Umgekehrt fasst das Stammdaten-Update den Vorbesitz nicht an, kann ihn also
  auch nicht versehentlich löschen. Eine Klasse, die gerade ausgebildet
  wird, kann nicht gleichzeitig Vorbesitz sein (Tippfehler-Schutz). Der Freitext
  fängt Fälle außerhalb der eigenen Klassenliste ab (z. B. ausländischer
  Führerschein) und zählt genauso als Vorbesitz. Steht auf dem Ausbildungsnachweis
  in der Zeile „Vorbesitz Klasse(n)".
- `StudentService`: Liste mit **Suche + Klassen-/Phasen-Filter + Paging**,
  CRUD, Klasse hinzufügen/entfernen, Phase setzen. Die Suche greift auf Vorname,
  Nachname **und Journalnummer** (so kommt man vom Papierordner zur Akte). Beim
  Hinzufügen einer Klasse prüft `StudentRules.CheckMinimumAge` das Mindestalter
  gegen die Klasse (Ausbildung darf bis zu 1 Jahr vor dem Mindestalter beginnen).
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

### Grundstoff-Soll bei Vorbesitz (§ 4 Abs. 3 FahrschAusbO)

> „Der Umfang des allgemeinen Teils (Grundstoff) beträgt mindestens **zwölf**
> Doppelstunden (90 Minuten); [...] Besitzt der Fahrschüler bereits eine
> Fahrerlaubnis, so beträgt der Umfang mindestens **sechs** Doppelstunden."

Zwei Dinge aus dem Wortlaut prägen das Modell:

- Die Bedingung ist ein **Ja/Nein am Schüler** – es kommt weder darauf an, *welche*
  Klasse er besitzt, noch welche er beantragt. Deshalb genügt ein abgeleiteter
  Schalter (`PriorLicenseClasses` nicht leer **oder** Freitext gefüllt); eine
  Regeltabelle „Vorbesitz X + beantragt Y" wäre Überbau.
- Der **Zusatzstoff** (§ 4 Abs. 4 + Anlage 2.8) wird **nicht** reduziert. Er bleibt
  die Pflichtzahl je Klasse (`LicenseClass.RequiredTheoryDoubleLessons`, B = 2).

Umsetzung:

- Die beiden Zahlen sind **Einstellungen**, nicht Klassen-Felder – der Grundstoff
  ist klassenunabhängig: `Theory.BasicDoubleLessons` (12) und
  `Theory.BasicDoubleLessonsWithPriorLicense` (6), im Adminpanel änderbar
  (Projektregel 3). Beim Speichern wird geprüft, dass der Vorbesitz-Wert nicht
  größer ist als der Ersterwerb-Wert.
- `StudentProgressRules.RequiredBasicTheoryLessons` entscheidet (reine Funktion,
  getestet): **Übersteuerung** des Fahrlehrers > abgeleitet aus Vorbesitz >
  Ersterwerb – und nie mehr, als der Plan an Themen hergibt (sonst wäre 100 %
  unerreichbar).
- Die **Themenliste bleibt vollständig** stehen; erfüllt ist der Grundstoff, sobald
  *irgendwelche* `n` Themen erledigt sind (`IsBasicTheory` = Theorie-Abschnitt,
  keine Klassenbindung, kein Zähler). `ProgressSectionDto` liefert dafür
  `RequiredDoneCount`/`DoneCount`, die Oberfläche zeigt „4 von 6". Mehr als das
  Soll abzuhaken schiebt die Klasse nicht über 100 % (beide Seiten des Bruchs
  gedeckelt).
- **Übersteuerung pro Schüler** (`RequiredBasicTheoryLessonsOverride` + Begründung)
  für das, was § 4 offenlässt: eine Mofa-Prüfbescheinigung ist keine Fahrerlaubnis,
  und zu ausländischen Fahrerlaubnissen sagt die Vorschrift nichts. Diese Fälle
  entscheidet der Fahrlehrer – bewusst **nicht** der Code.
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
- **Praxis & Zusatzstoff aus Klassen-Pflichtzahlen** (KONZEPT 3.3, wie Mockup):
  Sonderfahrten (Überland/Autobahn/Nacht) und die Zusatzstoff-Doppelstunden sind
  **keine** Theorie-Katalogpunkte, sondern **Pflichtzahlen pro Führerscheinklasse**
  (`LicenseClass.RequiredSpecialDrives*` + `RequiredTheoryDoubleLessons`, im
  Adminpanel-Klasseneditor pflegbar; gesetzliche B-Startwerte 5/4/3/2 per Seed +
  Migration). `EnsureSnapshotAsync` **generiert** daraus pro Klasse die
  Praxis-/Zusatzstoff-Zähler (deterministischer synthetischer Schlüssel je
  Klasse+Slot, damit Re-Reads nicht duplizieren), dazu eine Grundfahraufgaben-
  Checkbox und **freiwillige** Zähler („über die Pflicht hinaus"). Eine Klasse
  bekommt diese Zeilen erst, wenn sie die Pflicht hat (>0). Mandatory-Zähler
  folgen der Klassen-Einstellung (Admin-Änderung aktualisiert das Soll).
- **„Freiwillig" (Soll 0)**: `RequiredCount` kennt jetzt drei Fälle –
  `null` = Häkchen, `0` = freiwilliger Zähler (zählt, aber **nicht** zur Pflicht),
  `>0` = Pflicht-Zähler mit Soll (`StudentProgressRules.IsCountable/IsVoluntary/
  IsRequired`). Die Prozent-/Done-Zählung nutzt nur Pflicht-Punkte.
- **Anzeige pro Klasse**: der Service liefert den Fortschritt je Klasse, nach
  Abschnitten gruppiert, inklusive Prozent (`StudentProgressRules` – reine,
  getestete Logik). „Grundstoff" (gilt für alle Klassen) wird **immer** als
  eigene geteilte Karte geführt, auch bei nur einer Klasse. Endpunkte unter
  `/api/students/{id}/progress`.
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

### „Zählt als" – eine Stunde kann einen Punkt mehrfach zählen

Werden **zwei Autobahnfahrten am Stück** gefahren, ist das eine Stunde, muss aber zweimal
zählen. Deshalb ist aus dem Ja/Nein-Schalter `LessonItem.CountsTowardRequirement` die Zahl
**`CountedSessions`** geworden (Migration „MehrfachZaehlendeStunden"):

- `0` = nur geübt – die Stunde ist erfasst und zeigt den Punkt, der Zähler bewegt sich nicht,
- `1` = eine volle Fahrt (Normalfall; nicht genannte zählbare Punkte zählen genau einmal),
- `2`+ = mehrere am Stück. Obergrenze `StudentProgressRules.MaxCountedSessionsPerLesson` (20)
  – ein Vertipper-Schutz, keine Vorschrift.
- Bei **einfachen** Punkten (Theoriethemen) steht dort immer `0`: die sind „erledigt", nicht
  gezählt.

Wichtig ist die **Doppelbuchführung**: jede gezählte Stunde ist weiterhin eine eigene Zeile
(`StudentProgressEntry`) – die Zahl am `LessonItem` und die Anzahl dieser Zeilen sagen immer
dasselbe. So bleibt jede gezählte Stunde einzeln entfernbar, und die Zähler rechnen unverändert
über die Zeilen. `CreateAsync` legt entsprechend viele an, `UpdateAsync` gleicht die Zahl an
(fehlende ergänzen, überzählige – die jüngsten zuerst – entfernen).

Zwei Stellen mussten mitgezogen werden:

- `StudentProgressService.RemoveEntryAsync` löschte die Stunde weich, sobald sie nur diesen
  einen Punkt hatte. Bei einer doppelt zählenden Stunde wäre damit die **zweite** gezählte
  Stunde verwaist. Jetzt wird erst geprüft, ob danach noch etwas an der Stunde hängt; sonst
  wird nur die Zahl um eins gesenkt.
- `LessonDto.CoveredTitles` hängt die Zahl an, sobald sie über 1 liegt: „Autobahnfahrt (2×)".
  Das steht so in der Stundenliste **und** auf dem Ausbildungsnachweis – eine Zeile darf nicht
  stillschweigend für zwei gefahrene Fahrten stehen.

Im Request heißt das Feld `CountedSessions: [{ ItemId, Count }]` (statt der früheren Liste
`PartialPracticeItemIds`).

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
- **Berichtigen und Löschen** (wie bei den Stunden):
  - `Update` ändert **Datum, Ergebnis, Notiz**. Art, Klasse und „Vorprüfung" bleiben
    fest – dafür löschen und neu eintragen. Geprüft wird gegen die Sperre der
    *übrigen* Versuche (die bearbeitete Prüfung zählt dabei nicht mit).
  - `Delete` ist ein **Soft-Delete** (Projektregel 7): `IsDeleted` + Zeitpunkt +
    Benutzer, Migration „PruefungSoftDelete", dazu ein Query-Filter – damit
    verschwindet die Prüfung überall (Liste, Versuchszählung, Sperren, PDF),
    bleibt aber bis zum Fristende wiederherstellbar.
  - **Beide** verweigern den Schritt, wenn danach eine echte Praxisprüfung ohne
    bestandene Theorieprüfung dastünde (verständliche Meldung, KONZEPT 3.4).
  - Versuchsnummern und Sperren sind **abgeleitet** – sie stimmen nach jeder
    Änderung von selbst. Der **Stand** wird dabei nie zurückgesetzt
    (`RaisePhase` hebt nur an); das sagt der Löschdialog auch.
  - Audit: „Prüfung geändert" (vorher/nachher) bzw. „Prüfung gelöscht".
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

### Termin → Stunde („geplant" vs. „durchgeführt", KONZEPT 3.5)

Ein geplanter Praxis-/Theorie-Termin mit Schüler lässt sich **als durchgeführte
Stunde** übernehmen – ohne die Stunde getrennt neu einzutippen:

- `CalendarEvent` hat ein optionales **`LessonId`** (Migration „TerminStunde-
  Verknuepfung"; FK auf `Lesson`, `OnDelete: SetNull`). Gesetzt = „durchgeführt".
- `CreateLessonRequest` nimmt optional eine **`CalendarEventId`** entgegen.
  `LessonService.CreateAsync` setzt nach dem Anlegen `CalendarEvent.LessonId`,
  **wenn** der Termin zum selben Schüler gehört und noch nicht verknüpft ist.
  So bleibt die **eine** Eingabestelle für Stunden erhalten, nur eben vom Termin
  aus vorbefüllt.
- Frontend: Im Tages-Termin steht bei passenden Terminen „📝 Als Stunde eintragen"
  → wechselt in die Schüler-Akte (Tab Fortschritt) und öffnet den Stunden-Dialog
  **vorbefüllt** (Art, Datum, Dauer aus dem Termin; Klasse + behandelte Punkte
  wählt der Fahrlehrer). Nach dem Speichern zeigt der Termin „✓ durchgeführt".
  Verdrahtung über Query-Parameter (`?termin=&typ=&datum=&dauer=`), die die Akte
  als `lessonPrefill` an `StudentProgressPanel` weitergibt.
- Noch offen (später): Push-Erinnerung.

## Ausbildungsnachweis als PDF (KONZEPT 3.3/7) – Schritt 7

- **`TrainingRecordPdfService`** erzeugt den druckbaren Ausbildungsnachweis mit
  **QuestPDF** (Community-Lizenz – für Kleinbetriebe kostenlos, einmalig im
  statischen Konstruktor gesetzt). Es ruft Schüler-, **Stunden**- und
  **Prüfungs**-Service ab, sodass das PDF genau die eingetragenen Theorie- und
  Praxisstunden sowie die Prüfungen auflistet; das Layout liegt in
  `TrainingRecordDocument`.
- **Inhaltsgleich zum amtlichen Vordruck** (§ 31 Abs. 1 FahrlG / § 6 Abs. 2
  FahrSchAusbO), aber **kein Nachbau** des urheberrechtlich geschützten Formulars –
  eigenes, sauberes Layout mit denselben rechtlich geforderten Feldern/Spalten.
- Inhalt: Kopf mit Fahrschul-Stammdaten + Schülerblock (Familienname, Vorname,
  Anschrift, Geburtsdatum, beantragte Klassen, **Schülerverzeichnis-Nr.
  (Journalnummer)**; leere Linie für Vorbesitz). Dann **Theoretischer Unterricht**
  getrennt nach Grundunterricht (Grundstoff) und klassenspezifischem Unterricht
  (Zusatzstoff), **Praktische Ausbildung** (Datum / Art u. Inhalt / Beginn / Min.),
  Summenzeile, **Prüfungen** und Unterschriftsfelder.
- **Abschnitt „Prüfungen"** (Datum / Art / Klasse / Versuch / Ergebnis): zeigt die
  **echten** Theorie- und Praxisprüfungen chronologisch, mit Versuchsnummer und
  Ergebnis (bestanden / nicht bestanden / geplant). **Vorprüfungen** stehen bewusst
  **nicht** darin – sie sind interne Proben ohne Prüfungsversuch und gehören nicht
  auf den amtlichen Nachweis.
- Felder ohne Wert (z. B. noch keine Journalnummer) bleiben als **leere Linie** zum
  Ausfüllen von Hand stehen; ein leerer Prüfungs-Abschnitt zeigt „—".
- Die **FL-Spalte** (Fahrlehrer-Nummer) zeigt die in den Einstellungen gepflegte
  Nummer (Standard „01"), die Legende nennt dazu den Namen des Fahrlehrers. Die
  „Art u. Inhalt"-Spalte zeigt den eingetragenen Text **vollständig** (kein
  automatisches Kürzel-Raten).
- Endpunkt `GET /api/students/{id}/ausbildungsnachweis` → liefert die PDF-Datei.
  Frontend: Button „🖨 Ausbildungsnachweis (PDF)" im Stammdaten-Tab lädt sie über
  HttpClient (damit der Auth-Interceptor das Token erneuern kann).
- Der Kopf zeigt die **Fahrschul-Stammdaten** (Name/Anschrift/Erlaubnisnummer)
  aus den Einstellungen, sobald gepflegt (`SettingsService`, String-Settings
  `School.*`).

> Hinweis: Der frühere **Ausbildungsvertrag als PDF** wurde entfernt
> (Inhaber-Entscheidung) – der Vertrag wird manuell ausgefüllt.

## DSGVO im Adminpanel (KONZEPT 3.7) – Schritt 8

Die DSGVO-Funktionen liegen **admin-only** im Adminpanel
(`[Authorize(Roles = Roles.Admin)]` am `AdminController`, Route `api/admin`).
**Ausnahme** (Inhaber-Entscheidung): das Änderungsprotokoll ist auf eine eigene
Seite gewandert und für **alle Rollen** lesbar (siehe nächster Abschnitt).

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
  löschen").

### Änderungsprotokoll (eigene Seite, alle Rollen)

Der Inhaber wollte das **Änderungsprotokoll** breiter zugänglich: Es liegt jetzt
nicht mehr in der Admin-Karte, sondern auf einer **eigenen Seite** (`/protokoll`),
lesbar für **Admin, Fahrlehrer und Verwaltung**.

- Eigener **`AuditController`** (`api/audit`,
  `[Authorize(Roles = "Admin,Fahrlehrer,Verwaltung")]`, **read-only** – es gibt
  keinen Schreib-Endpunkt; Einträge entstehen nur über den `AuditWriter`).
- Nutzt denselben `AuditQueryService`: filterbar (Freitext über Nutzer/Aktion/
  EntityType/EntityId via `ILike`) und paginiert, neueste zuerst.
- **Kategorien + Rollen-Sichtbarkeit** (least privilege): Jeder Eintrag bekommt
  beim Schreiben eine **Kategorie** (`AuditLog.Category`, zentral aus Aktion/
  EntityType abgeleitet in `AuditCategory.For`, sechs Bereiche von „Anmeldung &
  Sicherheit" bis „Einrichtung"). Der `AuditQueryService` filtert auf die für die
  Rolle erlaubten Kategorien (Admin sieht alles; `AuditVisibilityService` hält die
  Zuordnung pro Rolle in `Setting`-Zeilen `Audit.Visible.{Rolle}`, der Admin pflegt
  sie über die Karte „Protokoll-Sichtbarkeit"). So sieht z. B. das Büro keine
  Passwort-Änderungen. Migration `AuditKategorien` füllt Altbestände per `CASE` nach.
- **Aktueller Initiator-Name + Schüler-Link**: Der `AuditQueryService` löst den
  Namen des Initiators zur Lesezeit aus `Users` auf (eine spätere Umbenennung wird
  reflektiert; Fallback auf den gespeicherten Namen). Bei schülerbezogenen Einträgen
  (führende Guid der `EntityId`) liefert er Schüler-Id + aktuellen Namen mit, das
  Frontend zeigt einen Link zur Akte.
- Frontend: Seite `protokoll` (`AuditLogPage`), Menüpunkt „Protokoll" für alle.
  Kategorie-Chips (nur die erlaubten), Spalte „Bereich", Schüler als Link.
  Hinweis Beschäftigtendatenschutz: das Protokoll zeigt, wer was geändert hat –
  bewusste Entscheidung des Inhabers, allen Mitarbeitern Lesezugriff zu geben.

- Noch offen (später): Legal-Texte (Impressum/Datenschutz) – für ein internes
  Tool nicht erforderlich (interne Entscheidung 13.06.).

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

## Theorie-Anwesenheit: Schnell-Abhaken (KONZEPT Stufe 2)

Eine Theorie-Doppelstunde haben **viele** Schüler gleichzeitig. Statt jeden
Schüler einzeln zu öffnen, wählt das Büro **Datum + Thema + die anwesenden
Schüler** und hakt das Thema bei allen **in einem Schritt** im Theorie-Fortschritt
ab. **Bewusst kein gespeicherter Anwesenheits-Listenspeicher** (Inhaber-Entscheidung
14.06.): Es ist nur eine Arbeitserleichterung – der **dauerhafte** Eintrag ist der
**Ausbildungsfortschritt** (das abgehakte Thema), nicht eine Anwesenheitsliste.
Es gibt darum **keine eigenen Tabellen**.

- **`TheoryAttendanceService`**: `GetTopicsAsync` (aktuelle, einfache Theorie-Themen
  aus dem Katalog) und `TickAsync(Datum, Thema, Schüler-IDs)`. Letzteres stellt je
  Schüler den Snapshot sicher (vorhandener `StudentProgressService`) und setzt den
  passenden `StudentProgressItem` auf erledigt (am gewählten Datum). Rückgabe:
  wie viele **neu abgehakt** / **schon erledigt** / **ohne dieses Thema** (nicht im
  Plan). Jede Änderung wird auditiert (DSGVO, „Theorie abgehakt").
- **Versehentlich abgehakt?** Rücknahme über den normalen Weg in der Schüler-Akte
  (Tab „Ausbildungsfortschritt", Austragen mit Bestätigung) – kein extra Speicher nötig.
- Endpunkte `/api/theory-attendance/topics` und `/api/theory-attendance/tick`.
  Frontend: Seite `theorie-anwesenheit` (Datum, Thema, Schüler-Mehrfachauswahl mit
  Suche, „Als anwesend abhaken" + Ergebnis-Meldung), Menüpunkt unter „Fahrlehrer".

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

## Geld: Zahlungen & Quittungen (KONZEPT 3.6)

Zwei Ebenen, weil das Recht sie unterschiedlich behandelt:

1. **`PaymentItem` (Zahlungsposten)** – die Arbeitsdaten. Entweder das Geld für **eine
   Praxisstunde** (`LessonId` gesetzt) oder ein **frei eingetragener Posten** („Grundbetrag“,
   „Prüfungsgebühr“). Solange der Posten auf **keiner** Quittung steht, ist er änderbar und
   löschbar (Soft-Delete, protokolliert).
2. **`Receipt` (Quittung)** + **`ReceiptItem`** – das Dokument. Beim Ausstellen werden alle
   offenen Posten genommen, bekommen eine **fortlaufende Nummer** (`Jahr-0001`, Eindeutigkeit
   über einen Unique-Index auf Jahr+Nummer) und werden als **eingefrorene Kopie** in die
   Quittung geschrieben. Danach ändert sich daran nichts mehr (GoBD).

**Warum die Kopie?** Eine ausgedruckte Quittung muss Jahre später noch genauso aussehen. Würde
das PDF die heutigen Posten lesen, würde eine spätere Berichtigung ein bereits ausgehändigtes
Dokument rückwirkend verändern.

- **Umsatzsteuer je Position**: Eingegeben wird der **Bruttobetrag** (das, was der Schüler
  bezahlt hat), `PaymentRules.SplitGross` rechnet Netto und USt heraus – je Posten gerundet,
  damit gedruckte Zeilen und Summen zusammenpassen. Der Satz (19/7/0 %) hängt am Posten,
  der Vorschlagswert steht im Adminpanel.
- **Geld ist `decimal`** (`numeric(10,2)`), nie Fließkomma (Projektregel 7).
- **Storno statt Änderung**: `CancelReceiptAsync` schreibt eine zweite Quittung mit eigener
  Nummer und **negierten Beträgen**, verlinkt beide und gibt die Posten wieder frei. Das
  Original bleibt bestehen – gelöscht wird eine Quittung nie.
- **Sperren**: Ein Posten auf einer Quittung lässt sich nicht mehr ändern/löschen; ebenso wenig
  der bezahlte Betrag der zugehörigen Stunde (die Stunde selbst darf weiter korrigiert werden,
  nur der Betrag ist fest). Auch das Löschen einer Stunde wird abgelehnt, solange ihr Geld auf
  einer Quittung steht – jeweils mit einer Meldung, die sagt, was zuerst zu tun ist.
- **Aufbewahrung**: `StudentRetentionRules.DueDateWithReceipts` nimmt die **spätere** der beiden
  Fristen – Ausbildungsdaten (§ 31 FahrlG, Standard 5 Jahre) und Quittungen (§ 147 AO,
  Standard 10 Jahre). Ein Schüler mit Quittung wird also nicht nach 5 Jahren gelöscht.
- **Audit**: „Zahlung eingetragen/geändert/gelöscht“, „Quittung ausgestellt“, „Quittung storniert“.
- **PDF**: `ReceiptPdfService` druckt die eingefrorene Kopie – Kopf mit Fahrschule und
  Steuernummer, Nummer und Datum, Positionen mit Netto/USt-Satz/USt/Brutto, Summenblock,
  Aufteilung nach Steuersätzen (wenn mehrere vorkommen), Unterschriftszeilen.
- Endpunkte: `GET/POST /api/students/{id}/payments`, `PUT/DELETE …/payments/{itemId}`,
  `POST …/receipts`, `POST …/receipts/{id}/cancel`, `GET …/receipts/{id}/pdf`.
  **Kein** PUT und **kein** DELETE für Quittungen – das ist Absicht, nicht vergessen.
- Einstellungen dazu (Adminpanel, Regel 3): Vorschlags-Steuersatz, Aufbewahrungsfrist für
  Quittungen, Steuernummer/USt-IdNr.

### Nachgebessert nach dem ersten Praxistest (30.08.2026)

- **Alles-oder-nichts beim Ausstellen und Stornieren.** Vorher liefen zwei `SaveChanges`
  hintereinander: erst die Quittung, dann die Verknüpfung der Posten. Ging etwas dazwischen
  schief, stand die Quittung schon in der Datenbank, während die Antwort ein Fehler war – die
  Oberfläche zeigte dann den alten Stand, und erst ein Neuladen brachte die Wahrheit ans Licht.
  Jetzt werden Quittung, ihre Zeilen und die Verknüpfung in **einem** `SaveChanges` gespeichert
  (ein `SaveChanges` ist eine Datenbank-Transaktion) – dasselbe beim Storno.
- **Nummernvergabe:** Der Wiederholungs-Block hat vorher **jede** `DbUpdateException` als
  „Nummer schon vergeben" gedeutet, dreimal identisch wiederholt und am Ende eine harmlos
  klingende Meldung geworfen – die echte Ursache stand nirgends. Jetzt wird geprüft, ob die
  Nummer wirklich vergeben ist; alles andere wird protokolliert und weitergereicht. Beim
  Wiederholen wird zusätzlich der **ganze** Objektgraph (Quittung *und* Zeilen) gelöst, sonst
  hängen die Zeilen als „Added" im ChangeTracker und werden nicht neu verknüpft.
- **Audit-Kategorie „Geld & Quittungen"** (`money`): „Zahlung"/„Quittung" fielen vorher in den
  Auffang-Zweig `_ => Security` und wären damit nur für den Admin sichtbar gewesen. Für die
  Rolle Verwaltung ist die neue Kategorie voreingestellt.
- **Storno bei sehr langer Bezeichnung:** Die Storno-Zeile setzt „Storno: " vor die
  ursprüngliche Bezeichnung. Bei einer Bezeichnung, die die Spalte (200 Zeichen) schon ausfüllt,
  wäre die Zeile zu lang geworden und der ganze Storno an der Datenbank gescheitert. Wird jetzt
  gekürzt (Test dazu vorhanden).
- **PDF-Schrift deterministisch** (`PdfDefaults`): `UseEnvironmentFonts = false` und die Familie
  ausdrücklich auf Lato (liegt dem QuestPDF-Paket bei). Vorher hing es von der Maschine ab,
  welche Schnitte gezogen werden. Gilt für **beide** Dokumente. Dazu: größere Schrift und
  seitliches Zellen-Padding auf der Quittung (die Spalten klebten aneinander).
