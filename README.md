# Fahrschulverwaltung – Backend

ASP.NET Core Web API (.NET 10) für die interne Fahrschulverwaltung.
Teil eines **Zwei-Repo-Projekts**: dieses Backend stellt nur die JSON-API bereit,
die Oberfläche liegt im separaten **Frontend-Repo** (Angular).

> Verbindliche Projektregeln: [CLAUDE.md](CLAUDE.md) · Fachkonzept: [KONZEPT.md](KONZEPT.md)
> Ausführliche Erklärung: [docs/backend-ueberblick.md](docs/backend-ueberblick.md)

## Schnellstart

1. **Datenbank** (PostgreSQL): entweder lokal vorhanden oder per
   `docker compose up -d` (siehe [docker-compose.yml](docker-compose.yml)).
   Erststart-Skript: [scripts/dev-datenbank-anlegen.sql](scripts/dev-datenbank-anlegen.sql).
   Die echten lokalen Zugangsdaten gehören in `Fahrschule.Api/appsettings.Local.json`
   (gitignoriert) – Vorlage sind die Werte in `appsettings.Development.json`.
2. **Starten**:
   ```
   dotnet run --project Fahrschule.Api --launch-profile http
   ```
   Beim ersten Start legt das Backend Tabellen (EF-Migration), Rollen und den
   ersten Admin an. API läuft auf `http://localhost:5080` (Probe: `/api/health`).
3. **Tests**: `dotnet test`

## Aufbau (Schichten)

```
Fahrschule.Api            Controller (dünn), Middleware, Start
Fahrschule.Application    Services = Fachlogik (z. B. AuthService)
Fahrschule.Infrastructure EF Core DbContext, Migrationen, Identity
Fahrschule.Contracts      DTOs (Datenformate der API)
Fahrschule.Domain         Entitäten/Regeln (reines C#)
Fahrschule.Tests          xUnit-Tests
```

## Anmeldung (Kurzfassung)

E-Mail + Passwort (ASP.NET Core Identity), **JWT + Refresh-Token in
httpOnly-Cookies** (XSS-/CSRF-Schutz), Konto-Sperre nach Fehlversuchen,
erzwungene Passwort-Änderung bei temporärem Passwort, erster Admin per Seed.
Details: [docs/backend-ueberblick.md](docs/backend-ueberblick.md).
