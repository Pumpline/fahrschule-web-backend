# Fahrschulverwaltung – Konzept

> Interne Verwaltungssoftware für eine Familien-Fahrschule.
> Backend: ASP.NET Core Web API · Frontend: Angular · DSGVO-konform (Deutschland/EU)

## 1. Entscheidungen (Stand: 2026-06-10)

| Thema | Entscheidung |
|---|---|
| Schülerzugang | **Nur interne Verwaltung** (kein Schülerportal). Datenmodell aber so bauen, dass ein Portal später ergänzbar bleibt. |
| Login-Methode | **E-Mail + Passwort** (eigenständig, keine US-Datenübermittlung). Kein Google-OAuth. |
| Hosting | **Noch offen** → Konzept hosting-neutral; EU/Deutschland-Hosting empfohlen. |
| MVP-Umfang | Schüler + Unterricht + Prüfungen · Terminkalender · DSGVO im Adminpanel |
| Rollen | `Admin` · `Fahrlehrer` (mit Termin-Push) · `Verwaltung` (wie Fahrlehrer, ohne Push). |
| Rechnungen | **Vorerst zurückgestellt** (entschieden 2026-06-10). Konzept in 3.6 bleibt für später erhalten. |
| Bedienung | **Sehr einfach** – die Bediener sind älter. Große Schrift/Buttons, wenige Schritte, einfaches Deutsch. |
| Bereiche | Klare Trennung **Verwaltung** (Büro/Inhaber) vs. **Fahrlehrer** (Stundeneintrag). |
| Editierbarkeit | Ausbildungsplan, Klassen, Sonderfahrten, Preise, Fristen sind **als Daten pflegbar** (kein Hardcoding). |
| Admin-Panel | **Zentrales Ziel**: fast alles per Konfiguration steuerbar, kaum Code-Änderungen nötig. Siehe 1b. |
| Plattform | **PWA** – ein Codebestand für Computer (Browser) **und** installierbare App auf iPhone/Android. **Kein App Store.** |
| Installation | **„Installieren"-Button nach dem Login**: Android = direkte Installation, iPhone = bebilderte Anleitung. |
| Rechnungen | **Rechtssicher** (UStG §14, fortlaufende Nummern, GoBD-unveränderbar, Storno statt Edit, USt/Kleinunternehmer-Schalter). |
| Löschen | **Soft-Delete**: erst als gelöscht markieren, echtes Entfernen nur durch Aufbewahrungs-Job nach Fristende. Bis dahin **wiederherstellbar**. |
| Anmeldung-Sicherheit | JWT im **httpOnly-Cookie + Refresh-Token**, Konto-Sperre, Admin-Passwort-Reset, erster Admin per Seed. |
| Betrieb | Automatische **verschlüsselte Backups + getesteter Restore**; **Docker** für portables Deployment. |

> Verbindliche Arbeitsregeln stehen in [CLAUDE.md](CLAUDE.md) (DSGVO, Einfachheit, Editierbarkeit, Rollen).

## 1a. Die zwei Bereiche

**Verwaltung (Inhaber/Büro)** – das „echte Verwaltungszeug", das der Fahrlehrer meist nicht selbst macht:
- Schüler-Stammdaten anlegen/pflegen
- Ausbildungsnachweis (gesetzliches Dokument) erstellen/drucken
- Quittungen & Rechnungen *(zurückgestellt, siehe 3.6)*
- DSGVO-Aufgaben (Export, Löschung, Audit)
- Einstellungen inkl. **Ausbildungsplan-Pflege**

**Fahrlehrer-Bereich** – schnell und unkompliziert während/nach der Stunde:
- Stunde eintragen: Theorie oder Praxis, Datum, Dauer
- Abhaken, **was gemacht wurde** (z. B. Rückwärts einparken, Gefahrbremsung, Überlandfahrt)
- Sehen, **was bei einem Schüler noch offen ist**
- Eigene Termine

## 1b. Admin-Panel — das zentrale Ziel

Ein mächtiges Admin-Panel, in dem der Inhaber **fast alles per Konfiguration** steuern kann,
ohne dass dafür neuer Code geschrieben werden muss. Leitidee: **Fachliche Inhalte sind Daten, keine Programmierung.**

Im Admin-Panel einstellbar:
- **Führerscheinklassen**: anlegen, umbenennen, deaktivieren (B, BE, A, A1, A2, AM, B96, L, T …),
  inkl. **Mindestalter & Voraussetzungen** je Klasse (z. B. B = 18, BF17 = 17, AM = 15/16) → für Eingabe-Prüfung.
- **Ausbildungspläne je Klasse**: Pflicht-Theoriestunden (Doppelstunden Grund-/Zusatzstoff),
  Sonderfahrten-Soll (Überland/Autobahn/Nacht), Grundfahraufgaben (Einparken, Gefahrbremsung …).
  Jeder Punkt anlegbar/änderbar/abschaltbar → Gesetzesänderungen ohne Programmierung.
- **Theorie-Themenkatalog**: Lektionen/Themen pflegen.
- **Unterlagen-Katalog je Klasse**: welche Nachweise welche Klasse benötigt (steuert die Unterlagen-Liste im Schüler);
  je Unterlage einstellbar, ob ein **Ablaufdatum Pflicht** ist (Abhaken erst mit Datum möglich).
- **Preise & Leistungen**: Leistungskatalog mit Beträgen (Grundbetrag, Fahrstunde, Sonderfahrt, Gebühren …).
- **Dokumentvorlagen**: Ausbildungsvertrag, Ausbildungsnachweis, Quittung, Rechnung – Texte/Logo/Kopf bearbeitbar.
- **Fahrschul-Stammdaten**: Name, Anschrift, Erlaubnisnummer, Bankverbindung, Logo.
- **Benutzer & Rechte**: Fahrlehrer anlegen, Rollen/Berechtigungen vergeben.
- **Auswahllisten/Stammwerte**: z. B. Zahlungsarten, Prüforganisationen (TÜV/DEKRA), Anreden.
- **DSGVO**: Aufbewahrungsfristen, Impressum & Datenschutztexte, Lösch-/Exportfunktionen.
- **Audit-Log-Ansicht**: filterbar nach Nutzer/Zeitraum/Datensatz – „wer hat was wann geändert".
- **Erinnerungs-/E-Mail-Texte**: Vorlagen für Benachrichtigungen (falls E-Mail aktiviert).
- **Feature-Schalter**: einzelne Module an-/ausschalten (z. B. Rechnungen aus, falls extern gemacht).

Damit das funktioniert, wird das System von Grund auf **konfigurationsgetrieben** gebaut:
neue Klassen, Stunden, Aufgaben, Preise und Texte entstehen durch Eintragen im Panel, nicht durch Code.

## 2. Architektur

```
[Angular SPA]  ──HTTPS/JWT──>  [ASP.NET Core Web API]  ──>  [PostgreSQL]
   Browser                         Controller + Services        verschlüsselt
```

- **Backend**: ASP.NET Core (.NET 8/9), EF Core, PostgreSQL, ASP.NET Core Identity (E-Mail/Passwort), JWT für die SPA.
- **Frontend**: Angular (Standalone Components, Angular Router, HttpClient-Interceptor für Token).
- **Rollen**: `Admin` (Inhaber – alles inkl. Adminpanel), `Fahrlehrer` (erhält Termin-Push),
  `Verwaltung` (sieht/bedient alles wie der Fahrlehrer, aber **ohne Push-Benachrichtigungen**).
  (`Schüler` reserviert für später.)
- **Trennung**: API liefert nur JSON; keine serverseitigen Razor-Views (anders als Altversion).

### Sicherheit & Anmeldung
- **Token-Handling**: JWT/Sitzungstoken im **httpOnly-Cookie** (nicht in `localStorage` → schützt vor
  Schad-Skripten/XSS) plus **Refresh-Token** und automatischem **Sitzungsablauf**.
- **Konto-Sperre** nach mehreren Fehlversuchen (Brute-Force-Schutz, Identity-Bordmittel); starke Passwörter erzwingen.
- **Neue Benutzer per temporärem Passwort**: Beim Anlegen erzeugt das System ein **temporäres Passwort**
  (Admin teilt es mit); bei der **ersten Anmeldung** wird der Benutzer **gezwungen, ein eigenes Passwort
  zu setzen** (Flag „muss ändern").
- **Passwort-Reset ohne E-Mail-Zwang**: Admin kann das Passwort zurücksetzen – erzeugt wieder ein
  temporäres Passwort mit erneutem Änderungszwang.
- **Erster Admin per Seed**: beim allerersten Start wird ein Admin-Konto angelegt (Zugangsdaten aus Konfiguration),
  danach deaktiviert sich der Seed.
- Schutz vor üblichen Web-Lücken (CSRF bei Cookies, Eingabevalidierung, sichere HTTP-Header, HTTPS-Pflicht).

### Plattform: PWA (Web + installierbare App, kein App Store)
Ein einziger Angular-Codebestand dient gleichzeitig als Webanwendung (Computer/Browser) und als
installierbare App auf iPhone und Android (Progressive Web App). **Kein App Store** – Installation
direkt aus der App heraus.

- **Service Worker + Web-App-Manifest** (Icon, Name, Vollbild-Start) – Angular-PWA-Unterstützung.
- **„Installieren"-Button nach dem Login**, geräteabhängig:
  - **Android/Desktop-Chrome/Edge**: fängt das `beforeinstallprompt`-Ereignis ab und löst die
    Installation per Klick direkt aus.
  - **iPhone (Safari)**: programmatische Installation ist von Apple nicht erlaubt → Button öffnet
    eine **einfache, bebilderte Anleitung** („Teilen → Zum Home-Bildschirm").
  - Button nur anzeigen, wenn noch nicht installiert.
- **Offline**: Stufe „einfach" – Daten ansehen + Fahrstunden-Einträge zwischenspeichern und beim
  Online-Gehen hochladen. Server bleibt die maßgebliche Datenquelle (lokaler Speicher ist nur Cache).
- **Push-Nachrichten** (z. B. Termine) als optionale Erweiterung:
  - Android: problemlos.
  - iPhone: nur ab **iOS 16.4** **und** wenn die App zum Startbildschirm hinzugefügt wurde.
- **Store-App (Apple/Google) bewusst nicht** – bei späterem Bedarf via Capacitor aus demselben Code verpackbar.

### Push-Benachrichtigungen (Termin-Erinnerung)
- **Zweck**: Der **Fahrlehrer** wird vor seinem nächsten Termin erinnert. **Vorlaufzeit einstellbar,
  Standard 30 Minuten** (Adminpanel/persönliche Einstellungen → Prinzip „alles editierbar").
- **Nur die Rolle Fahrlehrer** erhält Push; die Rollen **Verwaltung und Admin nicht** (sie sehen den
  Kalender, brauchen aber keine Erinnerung – das ist der zentrale Unterschied der Rolle Verwaltung).
- Aktuell **ein Fahrlehrer** in der Fahrschule → er erhält Erinnerungen für alle Termine.
  Datenmodell so anlegen, dass eine spätere Zuordnung „Termin → bestimmter Fahrlehrer" leicht ergänzbar ist.
- **Technik (Web Push / VAPID)**: Beim Aktivieren abonniert das Gerät; Backend speichert die
  **Push-Subscription pro Nutzer/Gerät**. Ein **Hintergrund-Job** sendet 30 Min vorher an die Geräte
  des Fahrlehrers und markiert den Termin als „erinnert" (keine Doppel-Push). Der Service Worker (PWA)
  zeigt die Nachricht – auch bei geschlossener App.
- **Einwilligung nötig**: Der Fahrlehrer aktiviert Erinnerungen selbst, **pro Gerät**.
- **iPhone**: nur als installierte PWA und iOS ≥ 16.4.
- **DSGVO/Datensparsamkeit**: Push-Inhalt **nur Zeit + kurze Info** (z. B. „Nächster Termin in 30 Min: 14:00 Praxis"),
  **kein Schülername**. Versand läuft über Apple/Google/Mozilla-Push-Dienste → in der Datenschutzerklärung erwähnen.

### Strikte Trennung Frontend / Backend
Frontend und Backend sind **zwei eigenständige Projekte** in getrennten Ordnern. Sie kennen sich
nur über die HTTP-API (JSON). So kann man jedes Teil unabhängig verstehen, testen, austauschen.

```
fahrschuleweb_new/
├─ backend/              ASP.NET Core Web API (eigene Solution)
│  ├─ Api/               Controller – nehmen Anfragen entgegen, geben JSON zurück (dünn!)
│  ├─ Application/       Services – die eigentliche Fachlogik
│  ├─ Domain/           Entitäten/Modelle (Student, Lesson, …) + Enums
│  ├─ Infrastructure/   EF Core DbContext, Migrationen, externe Dienste
│  ├─ Contracts/        DTOs (Datenformate der API) + Mapping
│  └─ Tests/            Unit-/Integrationstests
├─ frontend/             Angular-App (eigene package.json)
│  └─ src/app/
│     ├─ core/          Singletons: API-Services, Auth, Guards, HTTP-Interceptor, Modelle
│     ├─ shared/        Wiederverwendbare UI-Bausteine (Buttons, Tabellen, Dialoge), Pipes
│     ├─ features/      je Fachbereich ein Ordner: students/, lessons/, exams/, admin/ …
│     └─ layout/        Rahmen: Navigation, Kopf-/Fußzeile
└─ docs/                 Ausführliche Dokumentation (siehe Abschnitt 7)
```

**Warum so?**
- **Backend** ist in Schichten getrennt (Controller → Service → Daten). Die Controller bleiben „dünn",
  die Logik liegt in Services – das macht Tests und Änderungen einfach.
- **DTOs** trennen die *interne* Datenbankstruktur von dem, was die API nach außen zeigt → man kann
  die Datenbank ändern, ohne die API (und damit das Frontend) zu brechen.
- **Frontend** ist nach Fachbereichen (`features/`) organisiert, jeweils **lazy-loaded** (wird erst
  geladen, wenn gebraucht) → schnell und übersichtlich.
- Klare Konvention: **ein API-Service pro Fachbereich** im Frontend, der genau zu einem Controller passt.

## 3. Module

### 3.1 Schülerverwaltung (MVP)
- **Schülerliste mit Suche, Filtern und Seiten**: Namenssuche plus **Mehrfach-Filter** nach **Klasse**
  und **Stand** (Theorie/Praxis/Fertig) über an-/abwählbare Chips (nichts gewählt = alle; „Fertige
  ausblenden" wird damit überflüssig – einfach Fertig nicht wählen) sowie nach **ablaufender Frist**
  (Zeitfenster <7 / <14 / <30 Tage oder eigene Tageszahl); eigene Frist-Spalte. Lange Listen sind paginiert.
- **Datensparsamkeit in der Übersicht**: Die Liste zeigt nur **aggregierten Fortschritt (%)**, keine
  Detailangaben („Sehtest fehlt" o. Ä.). Einzelheiten stehen erst in der Schüler-Akte (Zugriffskontrolle).
- **Stammdaten** (erster Tab der Akte): alle Felder sind da, aber aus Datenschutzgründen **verdeckt
  (verschwommen)**. Über ein **👁-Symbol rechts an jedem Feld** deckt man es einzeln auf – und kann es
  dann auch **bearbeiten**. **Leere Felder werden als „leer" markiert** → erinnert daran, die Angabe nachzufragen.
- **Unterlagen** (Status immer sichtbar): *vorhanden/fehlt* je Dokument (Sehtest, Erste-Hilfe, Passbild,
  Fahrerlaubnis-Antrag vom Amt …). **Die Liste richtet sich automatisch nach den gewählten Klassen** –
  gepflegt über einen **Unterlagen-Katalog je Klasse** im Adminpanel (Standard-Unterlagen gelten für alle,
  klassenspezifische kommen automatisch dazu; bei C/D z. B. Eignungsnachweise – nur als Status, kein Dokument). Die Details **„vorgelegt am" / „läuft ab am"** sind
  standardmäßig **eingeklappt** (Pfeil zum Aufklappen), klappen aber **automatisch auf und werden
  hervorgehoben, wenn das Dokument bald abläuft** (Schwelle **im Adminpanel einstellbar, Standard ~21 Tage**).
  **Erinnerung automatisch**: Sobald ein Ablaufdatum eingetragen ist, wird erinnert – kein extra
  Schalter (kein Datum = keine Erinnerung; ein Bedienschritt weniger).
  **Ablaufdatum-Pflicht**: Im Unterlagen-Katalog ist je Unterlage einstellbar, dass ein Ablaufdatum
  **Pflicht** ist (z. B. Fahrerlaubnis-Antrag) – die Unterlage lässt sich dann erst als „liegt vor"
  abhaken, wenn das Datum eingetragen ist. So kann beim Eintragen nichts vergessen werden.
  **Keine Dokumente/Dateien speichern** (Datensparsamkeit) – nur Status + Daten. Ablaufende Unterlagen
  erscheinen zusätzlich in „Bald fällig" (Verwaltung informiert dann den Schüler).
- Mehrere Führerscheinklassen pro Schüler (B, BE, A, A2, AM …), aktiv/inaktiv.
- **Status-Pipeline je Führerscheinklasse** (nicht pro Schüler): Theorie → Theorieprüfung → Praxis →
  Praxisprüfung → Abgeschlossen. So kann ein Schüler mehrere Klassen mit unterschiedlichem Fortschritt haben.
- Freitext-Notizen.

### 3.2 Ausbildungsplan (editierbar) — das „vorgeschriebene Blatt" (MVP)
- Pflegbarer Lehrplan, gegliedert in Abschnitte, z. B.:
  - Theorie-Pflichtthemen (Grundstoff + klassenspezifischer Zusatzstoff)
  - Grundfahraufgaben Praxis (z. B. Rückwärts einparken, Umkehren, Gefahrbremsung)
  - Sonderfahrten (Überland, Autobahn, Nacht) mit Soll-Anzahl
- **Geltungsbereich je Punkt (wichtig): kein monolithischer „Grundstoff".** Jedes *Thema/Punkt* hat eine
  eigene **Klassen-Zuordnung** (M:N) – also „für welche Klassen gilt dieses Thema":
  - Ein Thema, das für **mehrere** Klassen des Schülers gilt (z. B. die meisten Grundstoff-Themen für B & A1)
    → wird **einmal** gemacht und für alle zählenden Klassen angerechnet.
  - Ein Thema, das nur für **eine** Klasse gilt (z. B. LKW-Grundstoff nur C/CE, „Anhänger" nur B/BE)
    → gehört faktisch zu dieser Klasse.
  - **Zusatzstoff**/Praxis sind ohnehin klassenspezifisch.
  - Der „Grundstoff" eines Schülers ergibt sich somit **automatisch** aus den Themen, deren Klassen-Zuordnung
    seine angemeldeten Klassen schneidet.
- Jeder Punkt ist **anlegbar/umbenennbar/deaktivierbar** und einem Geltungsbereich zuordenbar → bei
  Gesetzesänderung anpassbar (z. B. „bei Klasse X kein Rückwärtseinparken mehr" einfach abschalten).
- Dient als Vorlage für den Fortschritt jedes Schülers (pro Klasse, geteilte Punkte nur einmal).

### 3.3 Unterricht & Fortschritt / Ausbildungsnachweis (MVP)
- **Stunde wird in der Schüler-Akte eingetragen** (nicht über einen globalen Menüpunkt): Schüler öffnen →
  „Stunde eintragen" → Theorie/Praxis, **für welche Klasse** (oder *Grundstoff = zählt für alle Klassen*),
  Datum, Dauer und die behandelten Ausbildungsplan-Punkte **abhaken**.
- **Fortschritt pro Führerscheinklasse** sichtbar: erledigt / noch offen, je Klasse getrennt; **geteilte
  Grundstoff-Punkte** erscheinen einmal und zählen für alle gewählten Klassen. Innerhalb jeder Klasse
  ist der Fortschritt in **Theorie und Praxis** gegliedert.
- **Zählbare Punkte** (Sonderfahrten je Art, Zusatzstoff-Doppelstunden) haben einen **＋/−-Zähler**
  (z. B. „Überlandfahrt 1 / 5") statt nur eines Hakens; **jede gezählte Stunde bekommt eine eigene
  Zeile mit Datum + optionaler Notiz** („1. Fahrt am …", „2. Fahrt am …"); − entfernt die letzte.
  Bei Erreichen des Solls wird der Punkt automatisch als erledigt markiert. Einmalige Punkte
  (Grundfahraufgaben, Themen) bleiben Häkchen.
- **Alle Stunden werden im Ausbildungsfortschritt eingetragen** – das ist die *einzige* Stelle dafür
  (kein paralleles Eintragen an anderen Stellen). Übungs-/Zusatzstunden (Praxis) und zusätzliche
  Theoriestunden werden **je Klasse und je Bereich (Theorie/Praxis)** mitgezählt (unbegrenzter Zähler
  „freiwillig", Datum + Notiz pro Stunde) – so ist erkennbar, *worin* eine Extra-Stunde gemacht wurde.
- **Beim ersten Abhaken eines Themas** klappt automatisch ein kleines Feld auf für **„Erledigt am" (Datum)**
  und eine **optionale Notiz** (z. B. „nachgeholt", „Sondertermin", „aus früherer Klasse angerechnet").
  Danach ist das Feld eingeklappt und über einen **kleinen Pfeil** jederzeit wieder ein-/ausklappbar.
- **Schutz vor versehentlichem Ändern**: Abhaken (erledigt setzen) ist einfach; ein **erledigtes wieder
  austragen** erfordert eine **Bestätigung** (und wird im Audit-Log protokolliert).
- Pro Schüler wählbar, **welche Klassen** er gerade macht (eine oder mehrere).
- **Ausbildungsnachweis** (Verwaltung): druckbares Dokument des Ausbildungsstands (pro Klasse).
- Auswertung: absolvierte Einheiten und Sonderfahrten pro Schüler und Klasse.

### 3.3a Planänderungen, Versionierung & Anrechnung
Weil der Ausbildungsplan **editierbar** ist und sich über Jahre ändert (Gesetzesänderungen),
darf eine bereits laufende/abgeschlossene Ausbildung nicht rückwirkend „kaputtgehen", und eine
neue Klasse muss den **heute gültigen** Stoff verlangen.

- **Versionierung**: Jeder Plan-Punkt hat eine **feste Kennung** + **Version/Gültig-ab**. Änderungen
  erzeugen eine neue Version; alte Versionen bleiben erhalten.
- **Snapshot beim Anmelden**: Beginnt ein Schüler eine Klasse, erhält er eine **eigene Kopie** des
  damaligen Plans als persönliche Checkliste. Spätere Master-Änderungen wirken **nicht rückwirkend**.
  (Auch rechtlich sauber: der Ausbildungsnachweis zeigt, was *zum Zeitpunkt* galt.)
- **Anrechnung beim Klasse-Hinzufügen**: Kommt z. B. B zu einem Schüler dazu, der früher A1 gemacht hat,
  vergleicht das System den heutigen B-Plan mit dem Erledigten:
  - *unveränderter geteilter Grundstoff* → **Anrechnung vorgeschlagen** (Haken vorbelegt),
  - *seit damals geänderter/neuer Stoff* → bleibt **offen**, wird hervorgehoben,
  - *klassenspezifisches B* → ohnehin neu.
- **Fahrlehrer bestätigt**: Was wirklich angerechnet wird (Aktualität/Gültigkeit), entscheidet der
  Fahrlehrer per Haken – das System schlägt nur sinnvoll vor.

### 3.4 Prüfungen & Zulassungen (MVP)
- Prüfungen: Theorie/Praxis, Datum, bestanden/durchgefallen; **Versuche werden gezählt**
  (1. Versuch, 2. Versuch, …).
- **Vorprüfungen** (interne Probe: Theorie-Fragen am Laptop, Praxis-Testfahrt – oft Voraussetzung,
  bevor die Fahrschule zur echten Prüfung zulässt) werden **nur vermerkt**: kein Versuchszähler,
  **keine Sperre** bei Nichtbestehen, keine Auswirkung auf die echte Prüfung.
- Zulassung (Fahrlehrer-Entscheidung) als Voraussetzung – Minuten-Logik bewusst nicht automatisch.
- Praxisprüfung erst eintragbar, wenn Theorie bestanden.
- **Wiederholungs-Sperre nach Fehlversuch**: Jede nicht bestandene Prüfung erzeugt automatisch einen
  **eigenen Sperr-Block** („frühestens wieder prüfen am …", Standard **2 Wochen**). Die Sperre kann
  durch **zusätzliche Übungsstunden** (Standard **2**) auf die **verkürzte Sperre** (Standard **1 Woche**)
  reduziert werden. Diese Stunden werden **nicht separat** erfasst, sondern wie alle anderen im
  **Ausbildungsfortschritt** eingetragen; das System zählt die passenden Stunden automatisch und rechnet
  das Sperr-Ende um (nur **eine** Eingabestelle für Stunden).
  Gilt für Theorie wie Praxis und **bei jedem Fehlversuch erneut**. Alle drei Werte sind im
  **Adminpanel einstellbar** (kein Hardcoding). Beim Planen eines neuen Prüfungstermins wird gegen
  die laufende Sperre geprüft (verständliche Warnung).

### 3.5 Terminkalender (MVP)
- Kalenderansicht pro Fahrlehrer (Tag/Woche).
- Fahrstunden & Theorietermine planen, Doppelbuchung verhindern (Konfliktprüfung).
- Verknüpfung Termin → Unterrichtsnachweis (geplant vs. durchgeführt).

### 3.6 Rechnungen / Zahlungen (zurückgestellt – Konzept für später)
- Leistungskatalog mit Preisen (Grundbetrag, Fahrstunde, Sonderfahrt, Prüfungsgebühr …).
- Rechnung pro Schüler, offene/bezahlte Beträge, einfache Quittung als PDF.
- **Rechtssichere Rechnungen** (deutsches Recht):
  - **Fortlaufende, lückenlose Rechnungsnummern** (Pflicht).
  - **Pflichtangaben nach §14 UStG**: Aussteller-Anschrift, Steuernummer/USt-IdNr., Datum,
    Leistung/Menge, Netto, Steuersatz/-betrag, Brutto.
  - **GoBD: nach Ausstellung unveränderbar** → keine Bearbeitung, nur **Storno + Neuausstellung**.
  - **USt- bzw. Kleinunternehmer-Schalter (§19 UStG)** umschaltbar. **Vorläufige Annahme: umsatzsteuerpflichtig**
    (mit Steuerberater bestätigen). System unterstützt beide Fälle.
- **DSGVO/Steuer**: Rechnungen unterliegen **10 Jahren** Aufbewahrung (AO §147) → nicht löschbar vor Fristende.

### 3.7 DSGVO im Adminpanel (MVP)
Kein separates „DSGVO-Center" – alle Datenschutz-Funktionen liegen **direkt im Adminpanel**:
- **Datenexport** pro Schüler (Auskunftsrecht Art. 15 / Portabilität Art. 20) als JSON/PDF.
- **Löschung** mit Fristenprüfung (steuerlich relevante Daten geschützt). Gelöschte Schüler stehen in
  einer Liste **„Zur Löschung vorgemerkt"** und können bis zum endgültigen Entfernen nach Fristende
  jederzeit **wiederhergestellt** werden. Export, Löschung und Wiederherstellung werden im Audit-Log protokolliert.
- **Audit-Log**: wer hat wann welche Daten geändert (Vorher/Nachher).
- **Aufbewahrungssteuerung**: konfigurierbare Fristen, automatischer Löschjob.
- **Legal**: Impressum & Datenschutzerklärung pflegbar.

#### 3.7a Anmeldung & persönliche Einstellungen
- **Login-Seite** (Vollbild): E-Mail + Passwort – **immer derselbe Anmeldeweg**. Ist das Passwort des
  Kontos als **temporär markiert**, wird der Nutzer direkt nach dem Anmelden zur **Passwort-festlegen-Seite**
  (Vollbild, erzwungen) geleitet; sonst geht es direkt in die App. Kein separater „Temp-Login".
- **Benutzer-Menü** (oben rechts) mit **Persönliche Einstellungen** (eigener Name/E-Mail, Passwort ändern),
  **Benachrichtigungen** (Termin-Erinnerungen pro Gerät an/aus, Ruhezeiten – nur Fahrlehrer; iPhone-Hinweis),
  **Passwort ändern**, **Abmelden**.

## 3.8 Einstellungen (übergreifend)
- Führerscheinklassen, **Ausbildungsplan-Vorlagen**, Preise/Leistungen, Aufbewahrungsfristen,
  Legal-Texte, Nutzerverwaltung (Fahrlehrer anlegen).

## 3a. Erweiterter Funktionskatalog (aus Praxis-Recherche)

Recherchiert anhand gängiger deutscher Fahrschul-Software (Fahrschulcockpit, Yolawo, FADATA u. a.)
und der gesetzlichen Ausbildungsstruktur. „Stufe": **MVP** = erster Stand, **2** = bald danach, **3** = später/optional.

### Verwaltungs-Alltag (Büro/Inhaber)
| Funktion | Nutzen | Stufe |
|---|---|---|
| Anmeldung & Ausbildungsvertrag erzeugen | Schüler aufnehmen, Vertrag drucken/PDF | MVP |
| Ausbildungsnachweis (Druck/PDF) | gesetzlich vorgeschriebener Nachweis | MVP |
| Quittungen & Rechnungen, offene Posten | Zahlungen erfassen, Übersicht offener Beträge | zurückgestellt |
| Antrags-/Unterlagen-Checkliste (nur Häkchen) | Status: Sehtest/Erste-Hilfe/Passbild vorhanden – **ohne Dokumente zu speichern** (Datensparsamkeit) | MVP |
| Prüfungstermine bei TÜV/DEKRA verwalten | Datum, Prüforganisation, Gebühr, Ergebnis | MVP |
| Theorie-Anwesenheitslisten | Nachweis, welcher Schüler welche Doppelstunde besucht hat | 2 |
| Erinnerungen/Wiedervorlage | „Prüfung fällig", „Antrag läuft ab", „Sehtest-Gültigkeit" | 2 |
| Fahrzeugverwaltung | Fahrzeuge + Termine (HU/Wartung/Versicherung), km-Stand | 2 |
| BF17 – Begleitpersonen verwalten | Eintragung/Verwaltung der Begleiter (ab 17) | 2 |
| Einfaches Mahnwesen | Erinnerung bei offenen Beträgen | 3 |
| Fahrlehrer-Arbeitszeit/Stundenübersicht | Grundlage für Lohn (nur falls gewünscht) | 3 |
| ASF/Aufbauseminare verwalten | Seminarteilnehmer/Termine | 3 |
| Statistik/Auswertungen | Bestehensquoten, Auslastung, Umsatz | 3 |
| Mehrere Standorte/Filialen | falls je nötig | 3 |

### Fahrlehrer-Alltag
| Funktion | Nutzen | Stufe |
|---|---|---|
| Schnell-Eintrag einer Fahrstunde | Datum, Dauer, Schüler – wenige Klicks (tablet-tauglich) | MVP |
| Ausbildungsstufe + Grundfahraufgaben abhaken | „was wurde geübt/gemacht" pro Stunde | MVP |
| Sonderfahrten-Zähler | z. B. Überland 3/5, Autobahn 2/4, Nacht 1/3 | MVP |
| Offen-/Erledigt-Übersicht pro Schüler | sofort sehen, was noch fehlt | MVP |
| Tagesplan / heutige Termine | was steht heute an | 2 |
| Kurz-Bewertung/Notiz pro Stunde | Lernstand festhalten | 2 |
| Mobile/Offline-Nutzung | Eintrag auch unterwegs (später) | 3 |

> Hinweis Datensparsamkeit (DSGVO): Sehtest, Erste-Hilfe-Bescheinigung und Passbild werden
> **nur als Status-Häkchen** geführt ("liegt vor: ja/nein"), die Dokumente selbst speichern wir nicht.

## 4. Datenmodell (Entwurf, basiert auf Altversion + Erweiterungen)

- **Student** (Schüler): Stammdaten, Status, n:m Führerscheinklassen.
- **LicenseClass / StudentLicenseClass**: Klassen-Katalog + Zuordnung.
- **LicenseClass**: enthält zusätzlich **Mindestalter & Voraussetzungen** (editierbar) für die Eingabe-Prüfung.
- **StudentLicenseClass**: die Klassen-Anmeldung eines Schülers; trägt den **Status/Phase pro Klasse**
  (statt einem Status pro Schüler). Ein Schüler kann mehrere haben.
- **CurriculumSection / CurriculumItem** *(neu, editierbar, versioniert)*: Ausbildungsplan –
  Abschnitt (z. B. „Sonderfahrten") + Punkt (z. B. „Überlandfahrt", Soll-Anzahl, aktiv/inaktiv, Reihenfolge).
  Jeder Punkt hat eine **feste Kennung (Key)** + **Version/Gültig-ab**.
  **Klassen-Zuordnung pro Punkt** über `CurriculumItemClass` (M:N) → bestimmt, für welche Klassen ein Thema gilt
  (kein einheitlicher „Grundstoff"-Block; z. B. LKW-Themen nur C/CE). Geteilt = für mehrere Klassen zugeordnet.
- **StudentProgress** *(neu, Snapshot)*: beim Anmelden einer Klasse wird der damalige Plan in die
  persönliche Checkliste kopiert (inkl. Titel/Version) → Stand (offen/geübt/abgeschlossen, **Erledigt-am-Datum**,
  **optionale Notiz**, Fahrlehrer).
  Geteilte Punkte werden nur **einmal** geführt und für alle zugeordneten Klassen gewertet. Spätere
  Master-Änderungen wirken nicht rückwirkend; beim Klasse-Hinzufügen schlägt das System **Anrechnung**
  für unveränderte geteilte Punkte vor (Fahrlehrer bestätigt).
- **Lesson** (Unterrichtseinheit): Typ, Start, Dauer, Fahrlehrer; verknüpft die in der Stunde abgehakten Punkte.
- **Exam** (Prüfung, mit Versuchszähler) + **ExamAdmission** (Zulassung).
- **ExamLock** *(neu)*: Sperr-Block je nicht bestandener Prüfung – Sperre-bis (berechnet aus den im
  Ausbildungsfortschritt erfassten Übungsstunden nach dem Fehlversuch), Soll-Stundenzahl, verkürzt ja/nein.
  Konfigurierbare Werte (normale/verkürzte Sperre, Stundenzahl) in den Einstellungen.
- **CalendarEvent** *(neu)*: Termin (Datum, Von/Bis, Art, Schüler optional, Notiz), ggf. → Lesson nach
  Durchführung; Feld **„erinnert"** (für Push, keine Doppelbenachrichtigung); optional später `InstructorUserId`.
- **PushSubscription** *(neu)*: Web-Push-Abo pro Nutzer/Gerät (Endpoint + Schlüssel) – nur mit Einwilligung.
- **Invoice / InvoiceItem / Payment** *(neu)*: Rechnung, Positionen, Zahlungen.
- **ServiceItem** *(neu)*: Leistungs-/Preiskatalog.
- **DocumentCatalogItem / DocumentCatalogItemClass** *(neu, editierbar)*: Katalog der Unterlagen mit
  Klassen-Zuordnung (welches Dokument für welche Klasse nötig) – steuert die Schüler-Unterlagen;
  Feld **AblaufdatumPflicht** (Abhaken nur mit eingetragenem Ablaufdatum).
- **DocumentChecklistItem** *(neu)*: Unterlagen-Status pro Schüler (aus dem Katalog je Klasse) –
  vorhanden ja/nein, **vorgelegt am**, **läuft ab am**; **keine Dateien**. Erinnerung **automatisch**,
  sobald ein Ablaufdatum gesetzt ist → „Bald fällig".
- **ExamBooking** *(neu)*: Prüfungstermin bei TÜV/DEKRA (Prüforganisation, Datum, Gebühr).
- **Vehicle** *(neu, Stufe 2)*: Fahrzeug + Termine (HU/Wartung/Versicherung), km-Stand.
- **TheoryAttendance** *(neu, Stufe 2)*: Anwesenheit je Theorie-Doppelstunde.
- **Reminder/Task** *(neu, Stufe 2)*: Wiedervorlagen/Erinnerungen.
- **Companion** *(neu, Stufe 2)*: BF17-Begleitpersonen.
- **DocumentTemplate** *(neu)*: bearbeitbare Vorlagen (Vertrag, Nachweis, Quittung, Rechnung).
- **ConfigList / ConfigValue** *(neu)*: pflegbare Auswahllisten (Zahlungsarten, Prüforganisationen, Anreden …).
- **AuditLog**, **Setting**: wie Altversion.
- **ApplicationUser** (Identity): Fahrlehrer/Admin, E-Mail+Passwort, Rollen/Rechte.

### Beispiel-Seed (gesetzliche Standardwerte, später im Panel änderbar)
- **Klasse B – Theorie**: 14 Doppelstunden (12 Grundstoff + 2 Zusatzstoff).
- **Klasse B – Sonderfahrten**: 5 Überland, 4 Autobahn, 3 Nacht (= 12).
- **Grundfahraufgaben**: z. B. Rückwärtsfahren, Umkehren, Einparken, Gefahrbremsung.
- **Ausbildungsstufen**: Grundstufe, Aufbaustufe, Leistungsstufe, Sonderfahrten, Reife-/Teststufe.

> Diese Werte sind nur Startwerte. Pro Klasse unterschiedlich und jederzeit im Admin-Panel anpassbar.

## 4a. Bedienbarkeit (die Bediener sind älter)

Leitprinzip: **so einfach wie möglich.** Konkret:
- Große Schrift, hohe Kontraste, große Buttons/Klickflächen.
- Klare Startseite mit wenigen großen Kacheln statt verschachtelter Menüs.
- Jede Aufgabe in möglichst wenigen Schritten; gleiche Dinge immer am gleichen Ort.
- Einfaches Deutsch, keine Fachbegriffe/Abkürzungen ohne Erklärung; Oberfläche komplett deutsch.
- Löschen/Ändern immer mit verständlicher Rückfrage; freundliche, lösungsorientierte Fehlermeldungen.
- Fahrlehrer-Bereich auf das Nötigste reduziert (Stunde eintragen, abhaken) – getrennt von der Verwaltung.
- **Schriftgröße im Programm umschaltbar** (A / A+ / A++) – jeder stellt sich die für sich passende Größe ein.

## 5. DSGVO – technische & organisatorische Maßnahmen (von Anfang an)

- Verschlüsselte Verbindung (HTTPS) und verschlüsselte DB-Backups.
- Rollenbasierter Zugriff (least privilege).
- Audit-Log für alle personenbezogenen Änderungen (append-only, selbst geschützt).
- Datensparsamkeit: **keine** besonderen Kategorien (Gesundheit/Sehtest/Erste-Hilfe) speichern.
- **Soft-Delete**: Löschen markiert Datensätze zunächst nur als gelöscht (unsichtbar); das *echte*
  Entfernen übernimmt der Aufbewahrungs-Job **nach Ablauf der gesetzlichen Frist** (schützt Pflichtdaten).
  Bis dahin kann der Admin eine Löschung **rückgängig machen** (Wiederherstellen, protokolliert).
- Aufbewahrungsfristen + automatische, fristengeprüfte Löschung.
- Export/Löschung als Self-Service im Admin (Betroffenenrechte).
- Begleitdokumente (außerhalb Code): Verarbeitungsverzeichnis (Art. 30), AV-Vertrag mit Hoster (Art. 28), TOM-Dokumentation.

### Backup & Wiederherstellung (Geschäftskontinuität)
- **Automatische, verschlüsselte Backups** (regelmäßig, z. B. täglich).
- **Zweiter Speicherort** (offsite) – nicht nur auf demselben Server.
- **Restore wird getestet** (ein Backup, das man nie zurückgespielt hat, ist kein Backup).
- Klare Aufbewahrung der Backups im Rahmen der Fristen.

## 6. Vorgeschlagene Reihenfolge der Umsetzung

1. **Projektgerüst**: API-Solution + Angular-App + PostgreSQL + Identity/JWT-Login.
2. **Konfigurations-Fundament + Admin-Panel-Basis**: Klassen, Ausbildungspläne, Auswahllisten,
   Stammdaten – alles als pflegbare Daten (Grundlage für „kaum Code-Änderungen").
3. **Schüler + Führerscheinklassen** (CRUD, Liste, Detail) + Unterlagen-Checkliste.
4. **Ausbildungsplan-Fortschritt + Unterricht (Fahrlehrer-Eintrag/Abhaken)**.
5. **Prüfungen + Zulassungen + Prüfungstermine (TÜV/DEKRA)**.
6. **Terminkalender**.
7. **PDF-Dokumente** (Ausbildungsnachweis, später Vertrag).
8. **DSGVO im Adminpanel** (Audit-Ansicht, Export, Löschung, Retention, Legal).
9. **Stufe 2**: Anwesenheitslisten, Erinnerungen, Fahrzeugverwaltung, BF17.
10. **Feinschliff**: Dashboard, Statistik, weitere Stufe-3-Features.
11. **Zurückgestellt**: Rechnungen/Zahlungen + Quittungen (Konzept in 3.6), wenn gewünscht.

## 7. Dokumentation, Wartbarkeit & Lernbegleitung

Der Inhaber des Projekts kommt aus der **Spieleentwicklung (Unity)**, nicht aus der Web-Welt,
und möchte beim Mitlesen **lernen**. Deshalb gelten hier besondere Ansprüche:

**Ausführliche, lehrreiche Dokumentation**
- `docs/`-Ordner mit verständlichen Erklärungen, nicht nur Stichpunkten.
- Jede Schicht/jeder Fachbereich bekommt ein kurzes „Was ist das, warum so, wie hängt es zusammen".
- **Kommentare im Code erklären das *Warum*** (web-typische Konzepte werden benannt), nicht nur das Was.
- README im Wurzelordner: Überblick + „Wie starte ich Backend und Frontend?" in einfachen Schritten.
- Bei neuen Bausteinen kurz erklären, wie sie sich ins Gesamtbild einfügen.

**Übersetzungshilfe Spieleentwicklung → Web** (`docs/glossar.md`)
- Brücke zu bekannten Unity-Konzepten, z. B.:
  - Angular-**Component** ≈ ein **Prefab** mit eigenem Verhalten und Vorlage.
  - **Dependency Injection** (ASP.NET/Angular) ≈ Dienste „injizieren" statt selbst zu erzeugen.
  - **Service** ≈ ein Manager-Singleton, der Logik/Zustand bündelt.
  - **HTTP-Request/Response** ≈ Nachricht ans „Server-Backend" und Antwort zurück.
  - **DTO** ≈ ein einfaches Datenpaket (wie ein serialisierbares struct) für den Transport.

**Moderne, wartbare Struktur (verbindlich)**
- Backend: Schichten Controller → Service → Daten; dünne Controller; DTOs an der API-Grenze;
  EF Core Migrationen; abhängigkeiten via Dependency Injection.
- Frontend: aktuelles Angular (Standalone Components, Signals für Zustand), Feature-Ordner,
  Lazy Loading, ein typisierter API-Service pro Fachbereich, zentraler HTTP-Interceptor fürs Token.
- Einheitlicher Stil (Formatierung/Linting), sprechende Namen, kleine überschaubare Dateien.
- Keine „Magie": lieber etwas mehr klarer Code als clevere Abkürzungen, die niemand mehr versteht.
- Tests für die wichtige Fachlogik (Backend-Services), damit Änderungen sicher bleiben.

**Technische Grundsätze (verbindlich)**
- **Geld** immer als `decimal` (in Cent gerechnet), nie Fließkomma; Währung €.
- **Deutsche Formate** überall: Datum `tt.mm.jjjj`, Komma als Dezimaltrennzeichen, Uhrzeit 24h.
- **Zentrales Fehler-Handling** im Backend (einheitliche, verständliche Fehlerantworten) + **strukturiertes Logging**.
- **Optimistische Nebenläufigkeit** (EF Core RowVersion) gegen versehentliches gegenseitiges Überschreiben.
- **Docker** für portables Deployment (hält die Hosting-Entscheidung offen).
- **PDF-Erzeugung** über eine feste Bibliothek (z. B. QuestPDF – Lizenz für kleine Firmen prüfen) für
  Rechnung, Quittung, Ausbildungsnachweis, Vertrag.

## 8. Offene Punkte / später zu klären

- Hosting-Entscheidung (EU-Cloud vs. lokal) → beeinflusst Backup/Deploy.
- Brauchen wir mehrere Fahrlehrer/Standorte oder genau eine Familie?
- E-Mail-Versand (Terminerinnerungen) gewünscht? → eigener SMTP/Anbieter, DSGVO-Hinweis.
- Druck-/PDF-Bedarf über Rechnungen hinaus (z. B. Ausbildungsnachweis-Ausdruck)?
