# Glossar: Unity/Spieleentwicklung → Web

Übersetzungshilfe für den Einstieg in die Web-Entwicklung, wenn man aus der
Unity-/Spieleentwicklung kommt. Die Vergleiche sind bewusst vereinfacht – sie sollen ein
**Gefühl** geben, kein exaktes 1:1.

## Grundidee: Wo läuft was?

In Unity läuft das ganze Spiel auf **einem** Gerät. Im Web ist die Anwendung **zweigeteilt**:

| | Frontend (Angular) | Backend (ASP.NET Core) |
|---|---|---|
| Läuft | im Browser des Nutzers | auf dem Server |
| Vergleichbar mit | dem „Client" eines Multiplayer-Spiels | dem „Game Server" |
| Macht | Anzeige, Bedienung, Eingaben | Fachlogik, Datenbank, Rechte |
| Vertraut man ihm? | **Nein** (manipulierbar wie ein Spiel-Client) | **Ja** (prüft alles selbst nach) |

Wie beim Multiplayer gilt: **Der Server hat immer recht.** Das Frontend darf z. B. einen Button
ausblenden, aber die echte Rechteprüfung passiert im Backend – sonst wäre es „client-side anticheat".

## Begriffe von A bis Z

| Web-Begriff | Unity-Analogie | Erklärung |
|---|---|---|
| **Component** (Angular) | **Prefab** mit Skript | Wiederverwendbarer UI-Baustein: eigene Vorlage (HTML), eigenes Aussehen (CSS), eigenes Verhalten (TypeScript). Wird wie ein Prefab beliebig oft „instanziiert". |
| **Service** (Angular/ASP.NET) | Manager-Singleton (z. B. `GameManager`) | Bündelt Logik/Zustand an einer Stelle, von überall nutzbar – aber sauber per DI bereitgestellt statt `static`. |
| **Dependency Injection (DI)** | statt `FindObjectOfType`/Singleton | Man *erzeugt* sich seine Abhängigkeiten nicht selbst, sondern *bekommt* sie geliefert (im Konstruktor). Macht Code testbar und entkoppelt. |
| **HTTP-Request/Response** | RPC/Netzwerk-Message an den Server | Das Frontend schickt eine Anfrage („GET /api/schueler"), das Backend antwortet mit Daten. Jede Anfrage ist in sich abgeschlossen. |
| **REST-API** | die „Befehlsliste" des Game Servers | Vereinbarte Adressen (Endpunkte) + Verben: `GET` (lesen), `POST` (anlegen), `PUT` (ändern), `DELETE` (löschen). |
| **JSON** | serialisierte Daten (wie `JsonUtility`) | Textformat, in dem Frontend und Backend Daten austauschen. |
| **DTO** (Data Transfer Object) | serialisierbares `struct`/Datenklasse | Ein reines Datenpaket für den Transport über die API – getrennt von der internen Datenbank-Struktur. |
| **Controller** (ASP.NET) | Eingangstür / Message-Handler | Nimmt eine HTTP-Anfrage entgegen, reicht sie an einen Service weiter, gibt JSON zurück. Bewusst „dünn" – keine Fachlogik. |
| **Entity / EF Core** | Savegame-Datenklassen + automatisches Speichersystem | Entities sind die Datenklassen (Schüler, Termin …); EF Core liest/schreibt sie automatisch in die PostgreSQL-Datenbank (ORM). |
| **Migration** (EF Core) | versioniertes Savegame-Schema-Upgrade | Beschreibt Datenbank-Änderungen als Skript, damit jede Umgebung denselben Stand hat. |
| **Routing** (Angular) | Szenenwechsel (`SceneManager.LoadScene`) | Wechsel zwischen „Seiten" der App – nur dass keine Szene geladen wird, sondern Components ein-/ausgetauscht werden. URL = Szenenname. |
| **Lazy Loading** | Szene/Asset-Bundle erst bei Bedarf laden | Ein Feature-Bereich wird erst heruntergeladen, wenn man ihn öffnet → schnellerer Start. |
| **Signals / State** (Angular) | beobachtbare Felder (Events bei Änderung) | Zustand, bei dessen Änderung die UI automatisch neu rendert – ohne manuelles `UpdateUI()`. |
| **JWT / Token** | Session-Ticket nach dem Login | Beweisstück „ich bin eingeloggt", das bei jeder Anfrage mitgeschickt wird. Bei uns: im httpOnly-Cookie (für Skripte unlesbar). |
| **Service Worker / PWA** | „Installierter Client" für eine Web-App | Skript im Browser, das die App offline-fähig macht und Push-Nachrichten anzeigen kann; PWA = Website, die sich wie eine App installieren lässt. |
| **localStorage** | `PlayerPrefs` | Kleiner Schlüssel-Wert-Speicher im Browser, pro Gerät (bei uns: Theme, Schriftgröße). |
| **CSS-Variablen / Theme** | ScriptableObject mit Farbpalette | Zentrale Design-Werte (`--primary`, …), die alle Components nutzen – einmal ändern, überall wirksam. |
| **CORS** | „welche Clients darf mein Server akzeptieren" | Browser-Schutzregel: das Backend muss erlauben, dass unser Frontend (andere Adresse!) mit ihm sprechen darf. |
| **XSS / CSRF** | Cheat-/Injection-Schutz | Klassische Web-Angriffe: fremdes Skript in die Seite schmuggeln (XSS) bzw. fremde Aktionen im Namen des Nutzers (CSRF). Dagegen: Eingaben entschärfen, httpOnly-Cookies, Anti-CSRF-Token. |

## Typischer Ablauf (Beispiel: Stunde eintragen)

1. **Component** „Stunde eintragen" sammelt die Eingaben (Datum, Dauer, Häkchen).
2. Der zugehörige **API-Service** schickt sie als **JSON** per `POST /api/lessons` ans Backend.
3. Der **Controller** nimmt sie entgegen und reicht das **DTO** an den **Lesson-Service** weiter.
4. Der Service prüft die Fachregeln (z. B. „Praxisprüfung erst nach Theorie") und speichert
   per **EF Core** in PostgreSQL; das **Audit-Log** protokolliert die Änderung.
5. Die Antwort (JSON) kommt zurück; das **Signal** im Frontend aktualisiert die Anzeige.

→ In Unity-Worten: UI-Prefab → Netzwerk-Message → Server-Handler → Validierung → Save → Client-Update.
