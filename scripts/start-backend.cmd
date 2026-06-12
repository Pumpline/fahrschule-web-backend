@echo off
rem Startet das Backend (ASP.NET Core Web API) auf http://localhost:5080.
rem Aufruf: ueber den Server-/Vorschau-Bereich oder direkt per Doppelklick.
rem
rem Das Profil "http" setzt ASPNETCORE_ENVIRONMENT=Development (laedt
rem appsettings.Development.json + appsettings.Local.json) und bindet auf Port 5080.
rem Beim ersten Start legt das Backend automatisch Tabellen, Rollen und den
rem ersten Admin an (siehe README, Schritt 2).
cd /d "%~dp0..\backend"
call dotnet run --project Fahrschule.Api --launch-profile http
