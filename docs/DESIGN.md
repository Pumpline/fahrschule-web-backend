# Design-Konzept

> Ziel: eine Oberfläche, die **ältere Bediener** (z. B. Großeltern) sofort verstehen.
> Ruhig, kontrastreich, große Schrift, große Flächen, einfache deutsche Wörter.
> **Klickbarer Entwurf:** [`design-mockup.html`](../design-mockup.html) – im Browser öffnen.

## Leitgedanken
- **Klarheit vor Schönheit.** Lieber wenige große Elemente als viele kleine.
- **Hoher Kontrast.** Fast schwarzer Text auf Weiß; kräftige, ruhige Hauptfarbe.
- **Große Klickflächen.** Buttons und Listenzeilen sind bewusst groß (gut auch am Tablet/Touch).
- **Wenig auf einmal.** Startseite = große Kacheln „Was möchten Sie tun?", keine verschachtelten Menüs.
- **Immer am gleichen Ort.** Navigation links, Kopfzeile oben – ändert sich nie.
- **Verständliche Sprache.** „Stunde eintragen", „Neuer Schüler" – keine Fachbegriffe/Abkürzungen.
- **Sicher führen.** Löschen/Ändern mit klarer Rückfrage; freundliche Fehlermeldungen.

## Farbe & Verläufe
Statt flacher Einzelfarben **weiche Verläufe** für mehr Tiefe (modern): die Kopfzeile geht von
fast Schwarz über Dunkelblau zu Blau, Buttons haben einen sanften Blau-Verlauf, der Seitenhintergrund
einen dezenten Schimmer. Alle Werte zentral in `:root` (hell) bzw. `[data-theme="dark"]` (dunkel).

| Zweck | Hell | Dunkel |
|---|---|---|
| Hauptfarbe | `#2a6fd6` | `#5b9bff` |
| Kopf-Verlauf | `#0e1b2e → #163a66 → #1e5fae` | `#05080d → #0c1a2e → #15396a` |
| Erledigt / positiv | `#1f7a33` | `#7fd49a` |
| Offen / Hinweis | `#a4560a` | `#f0b765` |
| Text | `#16202c` | `#e7ecf3` |
| Karten / Flächen | `#ffffff` | `#161d29` |
| Seitenhintergrund | helles Grau-Blau | nahezu Schwarz |

Grün/Bernstein zeigen auf einen Blick „fertig" bzw. „noch offen". Andere Grundtöne (z. B. ein
grüneres oder kühleres Blau) sind in Sekunden austauschbar.

## Responsiv (Handy · Tablet · Laptop · Computer)
Pflicht: die Oberfläche funktioniert auf allen Geräten.
- **Handy/Tablet (≤ 900px)**: Seitenleiste klappt ein, ☰-Button oben öffnet sie (mit abdunkelndem Hintergrund);
  nach Auswahl schließt sie automatisch.
- **Schmale Geräte (≤ 680px)**: Kopfzeile kompakt (nur Avatar), Schrift-Schieberegler ausgeblendet,
  Kalenderzellen kleiner.
- **Sehr schmal (≤ 600px)**: breite Tabellen sind seitlich scrollbar.
- Inhalte/Kacheln/Karten brechen automatisch um.

## Hell / Dunkel (Theme)
- **Dunkelmodus** vorhanden; Umschalter oben rechts (🌙 / ☀️).
- **Am Handy folgt das Theme zuerst dem System** (`prefers-color-scheme`).
- Manuelle Wahl möglich und wird **lokal gespeichert** (`localStorage`) – bleibt beim nächsten Mal erhalten.

## Schrift & Größe
- Serifenlose, gut lesbare Systemschrift; Grundgröße **19px** (größer als üblich).
- **Schriftgröße als Schieberegler in 5 festen Stufen** (oben rechts, schnappt zwischen den Werten
  16/18/20/23/27 px): links = kleiner, rechts = größer. Wird **lokal gespeichert**.
- Großzügige Zeilenhöhe und Abstände.

## Bausteine (im Mockup zu sehen)
- **Kopfzeile**: nur **Logo/Name** („Fahrschule Muster"), Schrift-Schieberegler, Hell/Dunkel und ein
  **Benutzer-Menü** (aufklappbar: Persönliche Einstellungen, Benachrichtigungen, Passwort ändern, Abmelden).
  *Kein* Verwaltung/Fahrlehrer-Umschalter – die beiden Bereiche stehen nur **links** (beide Rollen
  dürfen auf beides zugreifen).
- **Seitenleiste**: nach Bereichen gruppiert. Verwaltung (Start, Schüler) · Fahrlehrer (Kalender) ·
  Einrichtung (Adminpanel). **„Stunde eintragen" ist kein Menüpunkt** – man öffnet den Schüler und
  trägt die Stunde dort in der Akte ein. *(Rechnungen sind vorerst zurückgestellt.)*
- **Kacheln**: große Einstiegspunkte auf der Startseite.
- **Listen/Tabellen**: große Zeilen, farbige Status-Marken (Theorie/Praxis/Fertig, offen/bezahlt).
- **Fortschritts-Liste**: Häkchen + Zähler (z. B. „Autobahnfahrt 2 / 4").
- **Kalender**: volle **Monatsansicht** (7-Spalten-Raster) mit Tages-Labels (was an welchem Tag ist),
  Monats-Navigation; Klick auf einen Tag zeigt darunter die Termine als große Karten.
- **Formulare**: große Felder, deutliche Beschriftungen, ein großer Bestätigen-Button.

## Gezeigte Bildschirme (Endstand des Entwurfs)
0. **Anmeldung** (Vollbild, erscheint zuerst) – ein Login für alle; Konto mit **temporärem Passwort**
   (z. B. helga@…) wird nach dem Anmelden **gezwungen, ein eigenes Passwort zu setzen** (eigene
   Vollbild-Maske). „Passwort vergessen → Inhaber". Abmelden über das Benutzer-Menü führt hierher zurück.
1. **Start** – aufgeräumtes Dashboard: **„Bald fällig"** und **„Letzte Änderungen"**. Keine Aktions-Kacheln.
2. **Schüler** – paginierte Liste (12/Seite, feste Höhe) mit **Filter-Karte**: Suche, Klasse- und
   Stand-Chips (Mehrfach, „Alle"-Chip je Gruppe), Frist-Chips (<7/<14/<30 Tage/eigene Tageszahl);
   Spalten: Name, Klasse, Stand, **Fortschritt in % mit Balken** (Datensparsamkeit – keine Details),
   Frist, Öffnen.
3. **Schüler-Akte** – Tabs: **Stammdaten** (Name sichtbar, Rest geblurrt mit 👁 je Feld; Unterlagen je
   Klasse mit „vorgelegt/läuft ab am", **Ablaufdatum-Pflicht** erzwingbar, Auto-Aufklappen bei baldigem
   Ablauf) · **Ausbildungsfortschritt** (Klassen-Chips + „Klasse hinzufügen"-Dialog mit Anrechnung;
   geteilter Grundstoff je Thema mit Klassen-Tags/Versionen; je Klasse Theorie/Praxis-Abschnitte;
   **＋/−-Zähler mit eigener Datums-/Notiz-Zeile pro Stunde**, unbegrenzte Zusatzstunden-Zähler je
   Klasse/Bereich; Häkchen-Punkte mit Austrag-Bestätigung) · **Prüfungen** (Versuchszählung,
   **Vorprüfungen nur als Vermerk**, Eintrag-Dialog, **Wiederholungs-Sperre** mit automatischer
   Verkürzung aus den Übungsstunden des Fortschritts). Button „Stunde eintragen" in der Akte.
4. **Kalender** – Monatsansicht mit Navigation (‹ › / Heute / Monats-Picker), Termine je Tag,
   Termin-Dialog (anlegen/bearbeiten/löschen, Datum+Von/Bis-Picker, Art inkl. „Sonstiges",
   Schüler-Suchfeld mit Vorschlägen, Notiz).
5. **Stunde eintragen** – aus der Akte: Theorie/Praxis, Klasse (oder Grundstoff), Datum, Dauer, abhaken.
6. **Adminpanel** – komplett bedienbar: Führerscheinklassen, Theorie-Themen und Unterlagen-Katalog
   (je mit Anlegen/Bearbeiten/**Löschen mit Folgen-Hinweis**, Klassen-Häkchen, Ablauf-Pflicht-Option),
   Erinnerungs-Vorläufe (wirken live), **Prüfungs-Sperre-Werte**, Benutzer (3 Rollen, Push-Spalte,
   **temporäres Passwort beim Anlegen + Reset**), **DSGVO im Panel** (Export-Download, Löschung mit
   Fristenprüfung, **„Zur Löschung vorgemerkt" mit Wiederherstellen**), Audit-Log (paginiert, feste
   Höhe, Filter, **👁-Detail mit Vorher/Nachher**).
7. **Benutzer-Menü** (oben rechts) – Persönliche Einstellungen, Benachrichtigungen (Geräte-Schalter,
   Ruhezeiten, iPhone-Hinweis), Passwort ändern, Abmelden.

## Was bewusst (noch) fehlt
Das ist ein **Entwurf** für Layout/Farben/Bedienung – kein fertiges Programm (Daten werden nicht
gespeichert). Feinheiten wie eigenes Icon-Set, Logo, exakte Abstände und Druck-/PDF-Layouts kommen
später. Rechnungen sind vorerst zurückgestellt.
