# Inventria

![CI](https://github.com/lynx7843/Inventria/actions/workflows/ci.yml/badge.svg)

A warehouse management system: a SvelteKit frontend, an ASP.NET Core 9 API, and a Microsoft SQL Server database.

## Features

* Products with SKUs, categories, units, barcodes, and reorder levels
* Stock tracked per warehouse bin, with lot and expiry support
* Receive, pick, and relocate stock; transfers, returns, and count sheets
* Purchase orders, suppliers, and batched pick lists
* Printable bin labels and pick sheets
* Reports and an audit log
* Admin and Employee roles
* Optional two-factor sign-in with an authenticator app

## Tech stack

SvelteKit and TypeScript, ASP.NET Core 9 and C#, Entity Framework Core, SQL Server 2022. Tests use xUnit, Vitest, and Playwright.

## Quick start (Docker Compose)

Requires Docker with Compose v2, or Podman with `podman-compose`.

```bash
git clone https://github.com/lynx7843/Inventria.git
cd Inventria
docker compose up --build
```

Open <http://localhost:5173> and sign in with `admin` / `password`. Stop with `Ctrl+C`; `docker compose down -v` also wipes the data.

The compose file uses dev-only secrets. To change them, copy `.env.example` to `.env` and edit it.

| Piece | Address |
| :--- | :--- |
| Frontend | <http://localhost:5173> |
| API | <http://localhost:5240> |
| SQL Server | port `1433` |

## Running without Docker

Requires Node.js 22+, the .NET 9 SDK, SQL Server, and `dotnet-ef` (`dotnet tool install --global dotnet-ef`).

```bash
# Configure the API once (secrets stay on your machine, not in git)
cd backend
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=WarehouseDb;User Id=sa;Password=<password>;TrustServerCertificate=True;"
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"

# Create the tables (also after pulling new migrations)
dotnet ef database update

# Terminal 1: API
dotnet run

# Terminal 2: frontend
cd frontend
npm install
npm run dev
```

The API does not apply migrations itself. Without them no accounts are seeded and sign-in fails.

## Accounts

A new database gets two accounts on the API's first start:

| Username | Password | Role |
| :--- | :--- | :--- |
| `admin` | `password` | Admin |
| `employee` | `password` | Employee |

Set `Seed:AdminPassword` and `Seed:EmployeePassword` (`SEED_ADMIN_PASSWORD` and `SEED_EMPLOYEE_PASSWORD` in `.env`) to choose other passwords, and change the defaults before anyone else can reach the system. Everyone can manage inventory; only Admins manage users. Ten wrong passwords lock an account for 15 minutes.

## Optional settings

* **Two-factor sign-in:** set `Auth:TotpEncryptionKey` (`TOTP_ENCRYPTION_KEY` in `.env`) to a random key, for example `openssl rand -base64 48`. Without it, accounts cannot enable two-factor. Changing it later forces enrolled accounts onto their recovery codes.
* **Timezone:** set `Warehouse__TimeZone` (`WAREHOUSE_TIMEZONE` in `.env`), for example `Australia/Sydney`, to choose where the warehouse day starts. The default is UTC. Stored timestamps are always UTC.

## Testing

```bash
dotnet test                        # backend, in memory on SQLite, from the repo root
cd frontend
npm test                           # frontend unit tests
npm run check                      # type check
npm run lint                       # prettier and eslint
```

Two opt-in suites need real infrastructure and are skipped otherwise:

```bash
# SQL Server only behaviour: RowVersion concurrency and duplicate-key handling.
# Uses a throwaway database that is dropped afterwards.
INVENTRIA_TEST_SQLSERVER='Server=localhost,1433;User Id=sa;Password=<password>;TrustServerCertificate=True;' \
  dotnet test backend.SqlServerTests

# End-to-end smoke test (log in, create an item, receive stock, check the count).
# Needs the full stack running; first time run `npx playwright install chromium`.
cd frontend
npm run test:e2e
```

CI runs the default suites on every push to `main` and `features`, and on pull requests to `main`.

## Troubleshooting

| Symptom | Fix |
| :--- | :--- |
| `Skipping admin seed: cannot connect to the database` | SQL Server is not running, or the connection string is wrong (`dotnet user-secrets list`). |
| `Skipping admin seed: ... migration(s) have not been applied` | Run `dotnet ef database update` in `backend/`, then restart the API. |
| Sign-in cannot connect | The API is not running on port 5240. |
| `admin` / `password` is rejected | A seed password was set, or it was changed on the Users screen. |
| Signed in, but every page returns to login | Cookie settings: `CookieSameSite=None` requires `CookieSecure=true` over HTTPS (see `appsettings.json`). |

The API terminal shows failed requests. In the browser, every 4xx response carries a `message` field, which is what the screen displays.

## Project structure

```
backend/               ASP.NET Core API (Controllers, Models, Migrations)
backend.Tests/         xUnit suite, in memory on SQLite
backend.SqlServerTests/  Opt-in tests against a real SQL Server
frontend/              SvelteKit app (src/lib, src/routes)
frontend/e2e/          Playwright smoke test
docker-compose.yml     One-command local environment
```
