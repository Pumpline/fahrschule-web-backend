-- Legt einmalig den Datenbank-Benutzer und die Datenbank für die LOKALE
-- ENTWICKLUNG an (passend zur Verbindungszeichenfolge in
-- backend/Fahrschule.Api/appsettings.Development.json).
--
-- Ausführen (einmalig, fragt nach dem postgres-Passwort):
--   psql -U postgres -h localhost -f scripts/dev-datenbank-anlegen.sql
--
-- Alternative ohne lokales PostgreSQL: docker compose up -d
-- (die Docker-Datenbank ist mit denselben Zugangsdaten vorkonfiguriert).

-- Benutzer nur anlegen, wenn er noch nicht existiert (Skript ist wiederholbar).
DO
$$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'fahrschule') THEN
        CREATE ROLE fahrschule LOGIN PASSWORD 'fahrschule-dev';
    END IF;
END
$$;

-- Datenbank anlegen, falls sie fehlt (CREATE DATABASE geht nicht im DO-Block,
-- deshalb der \gexec-Trick: die SELECT-Zeile erzeugt den Befehl nur bei Bedarf).
SELECT 'CREATE DATABASE fahrschule OWNER fahrschule'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'fahrschule')
\gexec
