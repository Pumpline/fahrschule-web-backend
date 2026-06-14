# Deployment (erste Produktivversion auf srv1)

Diese Anleitung beschreibt das Ausrollen der Fahrschulverwaltung als **eigener,
getrennter Docker-Compose-Stack** auf dem bestehenden Server `srv1` – ohne
bestehende Dienste anzufassen, vollständig **reversibel**.

> Architektur: zwei Container (`fahrschule-api` = ASP.NET-Core-API,
> `fahrschule-web` = Nginx mit gebautem Angular + `/api`-Proxy). Nur `web`
> bindet einen Port – **nur auf localhost** (`127.0.0.1:8090`). Das **Host-Nginx**
> terminiert TLS und leitet `fahrschule.pumpline.systems` an diesen Port weiter.
> Die Datenbank ist eine **neue DB + eigener Benutzer im bestehenden
> `postgres-bots`-Container** (Muster der getrennten DBs/User).

## 0. Voraussetzungen
- Docker + Compose-Plugin auf dem Host (vorhanden).
- Host-Nginx (vorhanden).
- `postgres-bots`-Container läuft (pgvector/pg17).
- DNS: `fahrschule.pumpline.systems` zeigt auf die öffentliche Web-IP (Cloudflare;
  Proxy optional). Anlegen, sobald TLS steht.

## 1. Verzeichnis + Quellcode
```bash
sudo mkdir -p /opt/fahrschule && sudo chown "$USER" /opt/fahrschule
cd /opt/fahrschule
git clone <repo-url-backend>  backend
git clone <repo-url-frontend> frontend
cp backend/deploy/docker-compose.yml ./docker-compose.yml
cp backend/deploy/.env.example ./.env
```
Hinweis: Die Repos sind privat – Clone mit Deploy-Key/Token. Alternativ den
Quellbaum von einem anderen Rechner per `rsync` nach `backend/` bzw. `frontend/`
übertragen (ohne `bin/obj`, `node_modules`, `appsettings.Local.json`).

## 2. Datenbank in postgres-bots anlegen
Neue DB + eigener Anwendungsbenutzer (least privilege), getrennt von den übrigen
DBs:
```bash
docker exec -it postgres-bots psql -U <admin-user> -v ON_ERROR_STOP=1 <<'SQL'
CREATE ROLE fahrschule_app LOGIN PASSWORD 'STARK-ZUFAELLIG';
CREATE DATABASE fahrschule OWNER fahrschule_app;
SQL
```
Den DB-Zugang dann in `.env` als `DB_CONNECTION` eintragen.

**Wie erreicht die API den Postgres-Container?** Zwei saubere Optionen:
- **A (host-published Port):** Wenn `postgres-bots` einen Host-Port veröffentlicht
  (z. B. `127.0.0.1:5432`), nutzt die API `Host=host.docker.internal;Port=5432`
  (in `docker-compose.yml` ist `extra_hosts: host-gateway` dafür gesetzt).
- **B (gemeinsames Docker-Netz):** Sonst das Netz von `postgres-bots` ermitteln
  (`docker inspect -f '{{json .NetworkSettings.Networks}}' postgres-bots`), in
  `docker-compose.yml` unten als externes Netz `dbnet` eintragen, beim `api`-Dienst
  `dbnet` ergänzen und `extra_hosts` entfernen; dann `Host=postgres-bots;Port=5432`.

## 3. .env ausfüllen
`.env` (siehe `backend/deploy/.env.example`) – **niemals committen**:
```bash
JWT_SECRET="$(openssl rand -base64 48)"   # ≥32 Zeichen
# DB_CONNECTION, ADMIN_USERNAME, ADMIN_PASSWORD (temporär!), ADMIN_DISPLAYNAME, WEB_PORT
chmod 600 .env
```

## 4. Bauen + starten
```bash
cd /opt/fahrschule
docker compose build
docker compose up -d
docker compose ps
docker compose logs -f api    # EF-Migrationen + Seed-Admin laufen beim Start
curl -fsS http://127.0.0.1:8090/api/health   # {"status":"ok"}
```

## 5. Host-Nginx + TLS
```bash
sudo cp backend/deploy/nginx-fahrschule.conf /etc/nginx/sites-available/fahrschule.pumpline.systems
sudo ln -s /etc/nginx/sites-available/fahrschule.pumpline.systems /etc/nginx/sites-enabled/
# TLS: Let's Encrypt ...
sudo certbot --nginx -d fahrschule.pumpline.systems
# ... oder Cloudflare-Origin-Zertifikat: ssl_certificate-Pfade in der vHost-Datei anpassen.
sudo nginx -t && sudo systemctl reload nginx
```
DNS-Record in Cloudflare anlegen/prüfen (A/AAAA auf die Web-IP; Proxy optional).

## 6. Erststart / Anmeldung
- `https://fahrschule.pumpline.systems` öffnen.
- Mit `ADMIN_USERNAME` + temporärem `ADMIN_PASSWORD` anmelden → **erzwungener
  Passwortwechsel** beim ersten Login.
- Kurz prüfen: Anmeldung (Cookie `Secure`), Adminpanel, ein Schüler anlegen,
  Protokoll-Seite, Abmelden.

## 7. Updates einspielen
```bash
cd /opt/fahrschule/backend  && git pull
cd /opt/fahrschule/frontend && git pull
cd /opt/fahrschule && docker compose build && docker compose up -d
```
EF-Migrationen laufen beim Start automatisch.

## 8. Entfernen / Rollback (vollständig reversibel)
```bash
cd /opt/fahrschule && docker compose down            # Container weg
sudo rm /etc/nginx/sites-enabled/fahrschule.pumpline.systems && sudo systemctl reload nginx
docker exec -it postgres-bots psql -U <admin-user> -c "DROP DATABASE fahrschule;" \
  -c "DROP ROLE fahrschule_app;"                      # nur wenn Daten verworfen werden sollen
sudo rm -rf /opt/fahrschule
```
Bestehende Dienste (Pterodactyl, n8n, andere DBs) bleiben unberührt.

## 9. Backup
Vor größeren Änderungen Provider-Snapshot + `pg_dump` der `fahrschule`-DB:
```bash
docker exec postgres-bots pg_dump -U <admin-user> -d fahrschule -Fc > fahrschule-$(date +%F).dump
```

## 10. DSGVO-Hinweise (kurz)
- Zugriff nur nach Login, HTTPS Pflicht (erfüllt), httpOnly-Secure-Cookies.
- Sobald **echte** Schülerdaten verarbeitet werden: AV-Vertrag mit Hoster und
  ggf. Cloudflare (organisatorisch), Backups verschlüsselt/aufbewahrungskonform.
- Secrets ausschließlich in `.env`/Umgebung – nie ins Repo.
