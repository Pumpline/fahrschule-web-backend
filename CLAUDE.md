# Projektregeln – Fahrschulverwaltung

Interne Verwaltungssoftware für eine Familien-Fahrschule (Deutschland).
Backend: ASP.NET Core Web API · Frontend: Angular · Datenbank: PostgreSQL.
Konzept: siehe [KONZEPT.md](KONZEPT.md).

## Oberste Grundsätze (immer einhalten)

### 1. DSGVO ist nicht verhandelbar
- **Datensparsamkeit**: Nur erheben, was wirklich gebraucht wird. **Keine** besonderen
  Kategorien (Gesundheit, Sehtest, Erste-Hilfe-Nachweise) speichern.
- **Rechtsgrundlage** je Datenfeld bedenken: Vertrag (Ausbildung) bzw. gesetzliche Pflicht (Steuer/Nachweise).
- **Aufbewahrungsfristen**: Rechnungen/steuerlich relevante Daten **10 Jahre** (AO §147) –
  NIE vor Fristende automatisch löschen. Ausbildungsnachweise gemäß gesetzlicher Frist aufbewahren.
- **Betroffenenrechte** müssen technisch möglich sein: Auskunft/Export, Berichtigung, Löschung (mit Fristenprüfung).
- **Audit-Log**: Jede Änderung an personenbezogenen Daten protokollieren (wer/wann/vorher/nachher).
- **Sicherheit**: HTTPS, gehashte Passwörter (Identity-Standard), rollenbasierter Zugriff (least privilege),
  verschlüsselte Backups. Keine Klartext-Passwörter, keine Secrets im Code/Repo.
- **Keine US-Datenübermittlung** ohne ausdrückliche Freigabe (kein Google-Login, keine US-Tracker/-CDNs).
- **Push-Benachrichtigungen** nur mit Einwilligung (pro Gerät), Inhalt sparsam (kein Schülername); nur Fahrlehrer, nicht Verwaltung.
- Bei jeder neuen Funktion mit personenbezogenen Daten kurz prüfen: Rechtsgrundlage? Frist? Export-/Löschbarkeit? Audit?

### 2. Die Bediener sind älter – Einfachheit hat Vorrang
- Klare, große Schrift; gut lesbare Kontraste; große Klickflächen/Buttons.
- **Wenig Schritte** pro Aufgabe, keine versteckten Menüs, keine Fachbegriffe/Abkürzungen ohne Erklärung.
- Deutliche Beschriftungen in einfachem Deutsch (nicht englisch in der Oberfläche).
- Wichtige/destruktive Aktionen (Löschen) immer mit Bestätigungsdialog und klarer Folgenbeschreibung.
- Fehlermeldungen verständlich und lösungsorientiert ("Bitte Geburtsdatum eintragen"), nicht technisch.
- Konsistentes Layout: gleiche Dinge immer am gleichen Ort. Lieber ein paar große Seiten als viele kleine.
- Tastatur und Maus müssen reichen; keine Gesten/Doppelklick-Tricks voraussetzen.
- Im Zweifel: die einfachere Lösung wählen, auch wenn sie weniger "mächtig" ist.

### 3. Alles fachlich Veränderliche muss editierbar sein (kein Hardcoding)
- Ausbildungsplan/Pflichtthemen, Führerscheinklassen, Sonderfahrten, Grundfahraufgaben,
  Preise und Aufbewahrungsfristen werden als **Daten** gepflegt, nicht im Code festgeschrieben.
- Ziel: Bei Gesetzesänderungen (z. B. "Klasse X braucht kein Rückwärtseinparken mehr")
  passt der Inhaber das in den Einstellungen an – ohne neue Programmierung.

### 4. Zwei getrennte Bereiche / drei Rollen
- **Bereiche**: **Verwaltung** (Stammdaten, Ausbildungsnachweis, DSGVO, Einstellungen, Plan-Pflege)
  und **Fahrlehrer** (schneller Stundeneintrag, Abhaken des Fortschritts, Termine) – optisch und
  navigatorisch klar getrennt.
- **Rollen**: `Admin` (alles inkl. Adminpanel) · `Fahrlehrer` (erhält Termin-Push) ·
  `Verwaltung` (sieht/bedient alles wie der Fahrlehrer, aber **ohne Push-Benachrichtigungen**).
- **Kein separates DSGVO-Center**: Datenexport, Löschung (mit Fristenprüfung) und Audit-Log
  liegen im Adminpanel.

### 5. Strikte Trennung Frontend / Backend + moderne, wartbare Struktur
- Frontend (Angular) und Backend (ASP.NET Core) sind **getrennte Projekte**, verbunden nur über die JSON-API.
- Backend in Schichten: **Controller (dünn) → Service (Fachlogik) → Daten (EF Core)**; DTOs an der API-Grenze;
  Dependency Injection; EF Core Migrations. Keine Fachlogik in Controllern.
- Frontend modern: **Standalone Components, Signals** für Zustand, **Feature-Ordner**, **Lazy Loading**,
  ein typisierter **API-Service pro Fachbereich**, zentraler **HTTP-Interceptor** fürs Token.
- Sprechende Namen, kleine Dateien, einheitliche Formatierung/Linting. Keine „cleveren" Abkürzungen.
- Tests für wichtige Backend-Fachlogik.

### 6. Ausführliche, lehrreiche Dokumentation (der Inhaber lernt Web)
- Inhaber kommt aus Unity/Spieleentwicklung, **nicht** aus dem Web → erklären, nicht nur abliefern.
- Kommentare erklären das **Warum**; web-typische Konzepte beim Namen nennen und kurz einordnen.
- `docs/` pflegen: Überblick, Schicht-/Feature-Erklärungen, `glossar.md` (Unity→Web-Begriffe), Start-Anleitung.
- Bei jedem neuen Baustein kurz erklären, wie er ins Gesamtbild passt.

### 7. Recht, Finanzen & Sicherheit (verbindlich)
- **Soft-Delete überall**: Löschen markiert nur als gelöscht; echtes Entfernen ausschließlich durch den
  Aufbewahrungs-Job nach Fristende. Bis dahin **wiederherstellbar** (Admin, protokolliert).
  Niemals Pflichtdaten (z. B. Rechnungen) hart löschen.
- **Rechnungen rechtssicher**: fortlaufende lückenlose Nummern, §14-UStG-Pflichtangaben,
  **nach Ausstellung unveränderbar (GoBD)** → nur Storno + Neu, USt/Kleinunternehmer-§19-Schalter.
- **Geld** als `decimal`/Cent, nie Fließkomma.
- **Sichere Anmeldung**: Token im httpOnly-Cookie + Refresh-Token, Konto-Sperre, Admin-Passwort-Reset,
  erster Admin per Seed; CSRF-/XSS-Schutz, HTTPS-Pflicht, sichere HTTP-Header.
- **Backups**: automatisch, verschlüsselt, offsite, Restore getestet.
- **Optimistische Nebenläufigkeit** (RowVersion) gegen gegenseitiges Überschreiben.
- Status/Phase liegt **pro Führerscheinklasse** (StudentLicenseClass), nicht pro Schüler.

## Technische Konventionen
- Backend: reine Web-API (JSON), keine serverseitigen Razor-Views. EF Core Migrations.
- Frontend: Angular Standalone Components, deutschsprachige UI, HttpClient mit JWT-Interceptor.
- **Responsiv für alle Geräte** (Handy, Tablet, Laptop, Computer): einklappbare Navigation auf kleinen
  Bildschirmen, umbrechende Inhalte, scrollbare Tabellen.
- Sprache im Code: Englische Bezeichner sind okay; **Oberfläche/Texte für Nutzer auf Deutsch**.
- Konfiguration/Secrets über appsettings/Umgebungsvariablen, niemals committen.

## Arbeitsweise
- Vor größeren Änderungen kurz Rücksprache, wenn fachliche Annahmen nötig sind.
- Änderungen klein und nachvollziehbar halten; bestehenden Stil beibehalten.
- Bei Unsicherheit zu Gesetzeslage: kennzeichnen und nachfragen, nicht raten.
