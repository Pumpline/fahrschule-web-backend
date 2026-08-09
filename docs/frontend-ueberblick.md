# Frontend-Überblick (Angular)

> Erklärt Aufbau und Konzepte des Angular-Frontends – mit Brücken zur
> Unity-Welt ([glossar.md](glossar.md)). Das Aussehen folgt verbindlich dem
> Design-Entwurf ([design-mockup.html](../design-mockup.html), erklärt in
> [DESIGN.md](DESIGN.md)).

## Ordnerstruktur (Konvention für alles Weitere)

```
frontend/src/app/
├─ core/          Dienste, die es genau EINMAL gibt (Singletons)
│  ├─ auth/          AuthService, HTTP-Interceptor, Routen-Wächter (Guards)
│  ├─ settings/      DisplaySettingsService (Hell/Dunkel, Schriftgröße)
│  ├─ api/           ein typisierter API-Service je Backend-Controller
│  └─ models/        TypeScript-Interfaces (Datenformen der API)
├─ layout/        Der Rahmen: Kopfzeile + Seitenleiste (Shell)
├─ features/      Je Fachbereich ein Ordner – wächst mit jedem Modul
│  ├─ auth/          Anmelden, Passwort festlegen/ändern
│  ├─ admin/         Adminpanel (Klassen, Theorie-Themen …)
│  └─ start/         Startseite (Dashboard)
└─ shared/        Wiederverwendbare Bausteine (Passwort-Feld, Platzhalter …)
```

> **Sprach-Standard** (seit 12.06.2026, siehe CLAUDE.md): Code, Dateinamen und
> Kommentare sind **englisch**; deutsch bleiben Oberflächen-Texte, Browser-URLs
> (/anmelden …) und die CSS-Klassennamen (sie spiegeln das verbindliche
> design-mockup.html eins zu eins).

Regeln aus CLAUDE.md, hier umgesetzt:
- **Standalone Components**: Jede Komponente importiert selbst, was sie
  braucht – keine NgModule-Sammeldateien.
- **Lazy Loading**: Jede Seite wird per `loadComponent` erst geladen, wenn
  sie aufgerufen wird (im Build sichtbar als eigene „Lazy chunk files").
- **Signals** für Zustand (statt Variablen + manuellem Aktualisieren).
- Später gilt: **ein API-Service pro Fachbereich**, passend zu genau einem
  Backend-Controller (heute: `AuthService` ↔ `AuthController`).

## Die wichtigsten Konzepte an unserem Code

**Component** (≈ Prefab mit eigenem Verhalten + Vorlage): z. B. `LoginPage` –
eine TypeScript-Klasse (`login-page.ts`) mit HTML-Vorlage (`login-page.html`).
Was im Template steht, verbindet Angular automatisch mit den Feldern der Klasse.

**Signal** (≈ reaktiver Wertbehälter): `benutzer = signal<…|null>(null)`.
Liest ein Template `benutzer()`, aktualisiert sich die Anzeige von selbst,
sobald jemand `benutzer.set(…)` ruft. `computed(…)` leitet Werte ab,
`effect(…)` führt Nebenwirkungen aus (z. B. Theme ans Dokument schreiben).

**Dependency Injection**: `inject(AuthService)` liefert die eine, zentrale
Instanz – niemand erzeugt Dienste mit `new` (wie ein Manager-Singleton,
nur vom Framework verwaltet und dadurch testbar).

**Router + Guards**: `app.routes.ts` bildet Adressen auf Seiten ab.
Guards (`auth.guards.ts`) prüfen vorher: angemeldet? → sonst `/anmelden`;
temporäres Passwort? → erzwungen `/passwort-festlegen`. Wichtig: Guards sind
Bedienkomfort – die echte Zugriffskontrolle macht immer das Backend.

**HTTP-Interceptor** (`auth.interceptor.ts`): läuft bei jeder API-Anfrage.
Er schickt die Cookies mit und erneuert bei einer 401-Antwort **einmal
automatisch** das Token und wiederholt die Anfrage – der Benutzer merkt vom
Ablauf des 15-Minuten-Tokens nichts.

**Tokens im Frontend?** Gibt es nicht. Die JWTs liegen in httpOnly-Cookies,
die JavaScript nicht lesen kann (XSS-Schutz). Das Frontend kennt nur den
Benutzer (Name, Rollen) aus den API-Antworten.

## Anmelde-Ablauf aus Frontend-Sicht

1. App-Start → Guard ruft `ensureSessionChecked()`: fragt `GET /api/auth/me`,
   ob noch eine Sitzung (Cookie) besteht – erst dann wird eine Seite gezeigt.
2. Nicht angemeldet → Vollbild **/anmelden**. Nach Erfolg: normale Benutzer
   zur **/start**, Benutzer mit temporärem Passwort erzwungen zu
   **/passwort-festlegen** (das temporäre Passwort merkt sich der AuthService
   nur im Arbeitsspeicher, damit man es nicht doppelt eintippt).
3. **Abmelden** im Benutzer-Menü → `POST /api/auth/logout` (Server entwertet
   das Refresh-Token, löscht die Cookies) → zurück zu /anmelden.

## Design-Umsetzung

- Alle Farben/Verläufe liegen als **CSS-Variablen** in `src/styles.scss` –
  übernommen aus dem Mockup. Dunkelmodus = Attribut `data-theme="dark"` am
  `<html>`, mehr nicht.
- **Schriftgröße**: 5 feste Stufen (16/18/20/23/27 px) über `--basis`;
  der Regler in der Kopfzeile schreibt die Stufe, fast alles ist in `em`
  und wächst mit. Theme + Stufe werden im `localStorage` gemerkt; ein
  Mini-Skript in `index.html` wendet sie schon VOR dem App-Start an
  (kein falsches Aufblitzen).
- **Responsiv**: unter 900 px klappt die Seitenleiste ein (☰ + abdunkelnder
  Hintergrund), unter 680 px wird die Kopfzeile kompakt – wie im Mockup.

## Adminpanel: Führerscheinklassen (erster Konfigurations-Baustein)

`features/admin/` zeigt das Muster für alle kommenden Adminpanel-Bereiche:

- **AdminPage** als EINE Seite mit gestapelten Karten je Bereich (wie im
  Mockup – keine Reiter); nur die Rolle `Admin` sieht den Menüpunkt und kommt
  durch den `adminGuard` – verbindlich prüft zusätzlich das Backend.
- **LicenseClassManagement**: Karte mit Tabelle, pro Zeile nur ein schlanker
  „Bearbeiten"-Knopf. Im Dialog: alle Felder, ein Aktiv-Häkchen und der
  Löschen-Knopf; **Löschen immer mit Bestätigungs-Dialog und Folgen-Hinweis**
  (Projektregel 2).
- **API-Service** `LicenseClassesApi` (core/api/) – ein Service pro
  Backend-Controller. Beim Speichern wandert die **Versionsmarke** mit:
  meldet die API 409 (jemand anderes hat gespeichert), zeigt die Oberfläche
  die Meldung und lädt den echten Stand neu.
- Nach jedem Speichern wird die Liste **vom Server neu geladen** – so zeigt
  die Tabelle nie einen erfundenen Zwischenstand.

## Schülerverwaltung (features/students/)

Das erste große Fachmodul mit eigener Liste und Detailseite:

- **StudentsListPage**: Suche über **Name oder Journalnummer**, Klassen-Filter
  (ein Chip) und Stand-Filter (mehrere Phase-Chips), Spalte „Journalnr.",
  Fortschrittsbalken je Zeile, Paging, „Neuer Schüler"-Dialog (mit optionaler
  Journalnummer). Datensparsamkeit: die Liste zeigt nur den aggregierten
  Fortschritt.
- **StudentDetailPage** (die „Akte"): Stammdaten mit **bedarfsgesteuerter
  Augen-Enthüllung pro Feld** (Datensparsamkeit, KONZEPT 3.1 / DSGVO): Die Akte
  wird **ohne** die sensiblen Werte geladen – das Backend meldet nur, *welche*
  Felder gefüllt sind. Ein gefülltes Feld bleibt hinter dem 👁 verdeckt; erst der
  Klick holt diesen **einen** Wert nach (dieser Zugriff wird im Audit-Log
  protokolliert: „Stammdaten angesehen") und macht das Feld bearbeitbar. Leere
  Felder werden direkt bearbeitbar angezeigt (kein Auge – es gibt nichts zu
  schützen). Adresse ist **ein** Freitextfeld (Straße/PLZ/Ort zusammengefasst).
  Gespeichert werden nur die aufgedeckten/leeren Felder (`editableFields`), damit
  ein verdeckter Wert nie versehentlich überschrieben wird. **Name und
  Journalnummer** stehen dagegen offen da (und die Journalnummer zusätzlich unter
  der Überschrift): Die Nummer ist eine interne Aktennummer, die das Büro ständig
  braucht und die ohnehin auf dem Ausbildungsnachweis steht. Darunter die
  Führerscheinklassen mit **Status pro Klasse** (Phasen-Auswahl), Klasse
  hinzufügen (mit Mindestalter-Prüfung) / entfernen, und Löschen (Soft-Delete).
- **Vorbesitz** liegt im **Ausbildungsfortschritt**, direkt unter „Welche Klassen
  macht der Schüler zurzeit?" – dort, wo er etwas bewirkt. Zwei Anläufe brauchte
  es bis dahin (beide Male an Projektregel 2 gescheitert), deshalb festgehalten:
  - **Gleiche Bedienung wie „＋ Klasse hinzufügen"**: eine Zeile Chips zeigt, was
    eingetragen ist, „＋ Führerschein eintragen" öffnet den Dialog. Vorher war es
    eine eigene Karte in den Stammdaten mit *allen* Klassen als Knopfreihe.
  - **Kein verstecktes Menü**: der Sonderfall steht im Dialog offen da.
  - **Der Sonderfall ist eine Auswahl, keine Rätselei**: zwei Radio-Knöpfe
    „Das Programm rechnet: 6 Doppelstunden" / „Ich lege es selbst fest".
    Vorher musste man wissen, dass ein *leeres* Zahlenfeld „automatisch" heißt.
  - Der Dialog rechnet **live** vor (`priorDraftLessonCount()`), bevor übernommen
    wird; die Zeile darunter sagt danach in Klartext, was gilt.
  - Klassen, die der Schüler gerade macht, tauchen zur Auswahl nicht auf.
  - Der Panel holt den Vorbesitz über die Akte (`loadPriorLicense()`), weil die
    Fortschritts-Antwort ihn nicht mitführt – er gehört zum Schüler, nicht zur Klasse.
- **StudentDocuments** (eingebettet in die Akte): die aus dem Katalog
  abgeleitete Unterlagen-Liste mit Häkchen „liegt vor", Vorgelegt-/Ablaufdatum,
  Hervorhebung bald ablaufender Unterlagen und Erzwingung der Ablaufdatum-Pflicht.
- Route mit Parameter: `/schueler/:id`; der Wert kommt über
  `withComponentInputBinding` in das `id`-Input. Wichtig: Laden erst in
  `ngOnInit`, nicht im Konstruktor (das required-Input ist dort noch nicht gesetzt).

## Entwicklungsserver & Proxy

`npm start` startet den Dev-Server auf `http://localhost:4200` und leitet
alle `/api`-Anfragen per **Proxy** (`proxy.conf.json`) ans Backend
(`http://localhost:5080`) weiter. Für den Browser kommt damit alles von einer
Adresse – die httpOnly-Cookies funktionieren ohne CORS-Sonderlocken.

⚠️ **Node-Version**: Angular 22 braucht Node ≥ 22. Da auf dem Rechner ein
älteres Node installiert ist, liegt ein passendes Node 22 projektlokal unter
`.tools/node` – Start am einfachsten über `scripts\start-frontend.cmd`.

## PWA: installierbare App (KONZEPT „Plattform: PWA")

Die App ist eine **Progressive Web App** – ein Codebestand, der im Browser läuft
**und** sich wie eine echte App auf Handy/Tablet/Desktop **installieren** lässt
(kein App Store). Web-Begriff *Service Worker*: ein kleines Skript, das der
Browser im Hintergrund hält; es legt die App-Dateien in einen Cache, sodass die
App schnell startet und auch offline öffnet.

- **Manifest** (`public/manifest.webmanifest`): Name, Farben, Icons, Vollbild-Start
  (`display: standalone`). Im `index.html` verlinkt (plus `theme-color` und die
  iPhone-spezifischen `apple-touch-icon`/`apple-mobile-web-app-*`-Angaben).
- **Icons** unter `public/icons` (weißes Auto auf Markenblau, normal + „maskable").
  Reproduzierbar über `scripts/generate-pwa-icons.mjs` (`npm i -D sharp` nötig) –
  so liegt kein Binär-Designtool im Repo; Icons jederzeit ersetzbar.
- **Service Worker**: `@angular/service-worker` mit `ngsw-config.json` (cacht die
  App-Hülle). In `app.config.ts` via `provideServiceWorker(..., { enabled:
  !isDevMode() })` registriert – **nur im Produktions-Build aktiv**, nicht beim
  `ng serve`. `/api`-Aufrufe sind vom Caching ausgenommen (immer frisch vom Server).
- **„App installieren"** liegt bewusst als **Eintrag im Benutzer-Menü** (oben
  rechts, neben „Passwort ändern") – kein aufdringlicher Aufruf, nur die
  Möglichkeit. Logik in `core/pwa/pwa-install.service.ts`, eingebunden in der
  `Shell`: Android/Desktop-Chromium installieren direkt über das
  `beforeinstallprompt`-Ereignis; auf dem **iPhone** (Safari verbietet das
  programmatisch) öffnet der Eintrag eine kurze, bebilderte Anleitung („Teilen →
  Zum Home-Bildschirm"). Der Eintrag erscheint nur, wenn eine Installation möglich
  ist, und verschwindet, sobald die App installiert ist.
- **Datensparsamkeit/DSGVO**: alles läuft lokal/auf demselben Server – keine
  fremden CDNs, keine US-Dienste. (Push-Benachrichtigungen sind ein **eigener**,
  noch offener Konzept-Punkt und hier bewusst nicht enthalten.)

## Was bewusst noch fehlt

- **Offline-Datenerfassung** (Stunden offline zwischenspeichern + später hochladen)
  und **Push-Benachrichtigungen** – eigene Konzept-Punkte für später.
- Die noch nicht gebauten Module folgen der Reihenfolge aus KONZEPT.md Abschnitt 6.
