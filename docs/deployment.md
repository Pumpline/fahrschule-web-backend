# Deployment (Produktivversion auf srv1)

Diese Anleitung beschreibt das Ausrollen der Fahrschulverwaltung als **eigener,
getrennter Docker-Compose-Stack** auf dem bestehenden Server `srv1` – ohne
bestehende Dienste anzufassen, vollständig **reversibel**.

> **Der Stack liegt tatsächlich unter `/home/admin/fahrschule`** (Nutzer `admin`,
> kein `sudo` nötig) – nicht unter `/opt/fahrschule`, wie hier ursprünglich
> stand. Und die beiden Quellordner sind **keine Git-Checkouts**: der Quellbaum
> wurde hinkopiert. Updates laufen deshalb über Abschnitt 7 (Archiv übertragen
> und Ordner tauschen), **nicht** über `git pull`. Korrigiert am 09.08.2026,
> nachdem `git pull` auf dem Server ins Leere lief.

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

So sieht es auf srv1 aus (gewachsener Stand, Nutzer `admin`):
```
/home/admin/fahrschule/
  backend/              Quellbaum des Backends (KEIN Git-Checkout)
  frontend/             Quellbaum des Frontends (KEIN Git-Checkout)
  docker-compose.yml    Kopie von backend/deploy/docker-compose.yml
  .env                  Secrets, chmod 600 – niemals committen
  backups/              pg_dump-Dateien
  fahrschule.vhost      Kopie der Nginx-vHost-Datei
  backend.old*, frontend.old*   frühere Stände (Rollback von Hand)
```

Neu aufsetzen:
```bash
mkdir -p ~/fahrschule && cd ~/fahrschule
# Quellen übertragen – siehe Abschnitt 7 (git archive + scp + entpacken)
cp backend/deploy/docker-compose.yml ./docker-compose.yml
cp backend/deploy/.env.example ./.env
```
Die Repos sind privat. Deshalb liegt hier **kein** Clone, sondern der übertragene
Quellbaum – ohne `bin/obj`, `node_modules`, `appsettings.Local.json`. Wer künftig
lieber `git pull` möchte, braucht auf dem Server einen Deploy-Key oder Token;
dann würde Abschnitt 7 durch `git pull` ersetzt.

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

Weil auf dem Server kein Git liegt, wird der Quellbaum übertragen und der Ordner
**getauscht**. `git archive` nimmt genau den committeten Stand – nichts
Ungespeichertes, keine `bin/obj`- oder `node_modules`-Reste.

**Auf dem Entwicklungsrechner** (in den jeweiligen Repos, Stand vorher pushen):
```bash
git archive --format=tar.gz --prefix=backend/  -o backend.tar.gz  HEAD
git archive --format=tar.gz --prefix=frontend/ -o frontend.tar.gz HEAD
scp backend.tar.gz frontend.tar.gz admin@srv1:~/fahrschule/stage/
```

**Auf srv1** – erst sichern, dann tauschen, dann bauen:
```bash
cd ~/fahrschule
docker exec -u postgres postgres-bots pg_dump -d fahrschule > backups/fahrschule-$(date +%Y%m%d-%H%M%S).sql
cd stage && tar -xzf backend.tar.gz && tar -xzf frontend.tar.gz && cd ..
diff -rq backend stage/backend    # Kontrolle: nur die erwarteten Änderungen?
mv backend backend.old-$(date +%Y%m%d)   && mv stage/backend backend
mv frontend frontend.old-$(date +%Y%m%d) && mv stage/frontend frontend
docker compose build && docker compose up -d
docker compose logs --tail=40 api
curl -fsS http://127.0.0.1:8090/api/health
```
Der `diff` lohnt sich: er zeigt auch, ob im laufenden Ordner etwas liegt, das
**nicht** aus dem Repo stammt und beim Tausch verloren ginge.

EF-Migrationen laufen beim Start automatisch. Prüfen lässt sich das mit:
```bash
docker exec -u postgres postgres-bots psql -d fahrschule -At -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 3'
```

**Rollback**: die `.old-<Datum>`-Ordner zurücktauschen und erneut bauen. Eine
bereits angewandte Migration darf dabei stehen bleiben, solange sie nur etwas
hinzugefügt hat (z. B. eine nullbare Spalte) – der ältere Code kennt sie nicht
und stört sich nicht an ihr. Nur wenn Daten zurück sollen, den Dump einspielen.

## 8. Entfernen (vollständig reversibel)
```bash
cd ~/fahrschule && docker compose down                # Container weg
sudo rm /etc/nginx/sites-enabled/fahrschule.pumpline.systems && sudo systemctl reload nginx
docker exec -it postgres-bots psql -U <admin-user> -c "DROP DATABASE fahrschule;" \
  -c "DROP ROLE fahrschule_app;"                      # nur wenn Daten verworfen werden sollen
rm -rf ~/fahrschule
```
Bestehende Dienste (Pterodactyl, n8n, andere DBs) bleiben unberührt.
Für den Rollback eines **Updates** siehe Abschnitt 7 – dafür reicht das
Zurücktauschen der `.old-<Datum>`-Ordner.

## 9. Backup
Vor größeren Änderungen Provider-Snapshot + `pg_dump` der `fahrschule`-DB. Über
den `postgres`-Systemnutzer im Container braucht es kein Passwort:
```bash
docker exec -u postgres postgres-bots pg_dump -d fahrschule > ~/fahrschule/backups/fahrschule-$(date +%Y%m%d-%H%M%S).sql
```
Das läuft **zusätzlich** zur regelmäßigen Server-Sicherung
(`~/backup-retention.sh`) – die bleibt davon unberührt.

## 10. DSGVO-Hinweise (kurz)
- Zugriff nur nach Login, HTTPS Pflicht (erfüllt), httpOnly-Secure-Cookies.
- Sobald **echte** Schülerdaten verarbeitet werden: AV-Vertrag mit Hoster und
  ggf. Cloudflare (organisatorisch), Backups verschlüsselt/aufbewahrungskonform.
- Secrets ausschließlich in `.env`/Umgebung – nie ins Repo.
