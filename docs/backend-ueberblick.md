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
