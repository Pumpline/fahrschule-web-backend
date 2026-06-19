# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# Backend (ASP.NET Core Web API) - multi-stage build.
# Everything is built INSIDE Docker, so the server only needs Docker + the
# source (no .NET SDK on the host).
# ---------------------------------------------------------------------------

# --- Build stage: the .NET SDK compiles and publishes the API ---------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project files first and restore - this layer is cached as long
# as the dependencies don't change (faster rebuilds).
COPY Fahrschule.Api/Fahrschule.Api.csproj Fahrschule.Api/
COPY Fahrschule.Application/Fahrschule.Application.csproj Fahrschule.Application/
COPY Fahrschule.Infrastructure/Fahrschule.Infrastructure.csproj Fahrschule.Infrastructure/
COPY Fahrschule.Contracts/Fahrschule.Contracts.csproj Fahrschule.Contracts/
COPY Fahrschule.Domain/Fahrschule.Domain.csproj Fahrschule.Domain/
RUN dotnet restore Fahrschule.Api/Fahrschule.Api.csproj

# Now the rest of the source and publish a self-contained-free Release build.
COPY . .
RUN dotnet publish Fahrschule.Api/Fahrschule.Api.csproj -c Release -o /app --no-restore

# --- Runtime stage: only the ASP.NET runtime + the published app ------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# QuestPDF (PDF generation via SkiaSharp) needs fontconfig on Linux, otherwise
# the Ausbildungsnachweis/-vertrag PDFs fail at runtime. tzdata provides the
# IANA time zones (e.g. "Europe/Berlin") the appointment-reminder job needs to
# turn German wall-clock times into UTC correctly.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 tzdata \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Listen on a fixed internal port; the container is only reached from the web
# container / reverse proxy, never published directly to the internet.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Fahrschule.Api.dll"]
