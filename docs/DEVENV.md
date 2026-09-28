# Development environment

Three runtimes, three dependency sets. A Python virtualenv does not cover the
.NET or Node sides, so all three are managed separately.

## 1. Postgres 18 on port 5433

Port 5432 is already occupied on this machine by an unrelated ephemeral
Postgres cluster running out of `/tmp/nbpg`, so JobSuites uses the Homebrew
cluster on **5433** instead of killing someone else's process.

```bash
brew services start postgresql@18
psql -h localhost -p 5433 -d postgres
```

Databases: `jobsuites` (dev) and `jobsuites_test` (reserved for the test
project), both owned by the `jobsuites` role.

Docker is not used: this is an Intel Mac with no Apple Virtualization support,
and the installed `Docker.app` bundle is malformed. Colima needs qemu, which is
not installed.

## 2. Local secrets

`src/JobSuites.Api/appsettings.Development.json` holds a real DB password and
JWT signing key and is **gitignored**. Create it once:

```bash
cd src/JobSuites.Api
cp appsettings.Development.json.example appsettings.Development.json
# then fill in:
#   Password=<openssl rand -base64 24>
#   Key=<openssl rand -base64 48>
```

The API refuses to start if `Jwt:Key` is shorter than 32 bytes, so a missing or
weak key fails immediately rather than silently signing tokens with a guessable
secret.

## 3. Python (ingest)

```bash
cd ingest
python3 -m venv .venv
.venv/bin/pip install --only-binary=:all: -r requirements.txt
```

Use `--only-binary=:all:` on Python 3.14. Several of these packages have no
3.14 wheels and will otherwise hang trying to build from source.

## 4. .NET (API)

```bash
dotnet restore
dotnet test
dotnet ef database update --project src/JobSuites.Api
```

`dotnet-ef` is pinned in `dotnet-tools.json`, so use `dotnet tool restore`
rather than a global install.

## 5. Node (web)

```bash
cd web
npm install
```

## Running everything

```bash
./scripts/dev.sh
```

Web on <http://localhost:5173>, API + Swagger on <http://localhost:5236/swagger>.
Vite proxies `/api` to the API, so the browser sees a single origin and no CORS
configuration is needed in development.

## Tests

```bash
dotnet test              # 15 integration tests, real API, throwaway database
cd web && npm test       # 12 component tests
```

The API tests create a uniquely named database per run and drop it afterwards.
The test role needs `CREATEDB` for this:

```sql
ALTER ROLE jobsuites CREATEDB;
```

Rate limiting is disabled during tests via `RateLimiting:Enabled=false`, since
every in-process test client shares one IP and would otherwise trip the limit and
fail for the wrong reason. Limiting is verified against a running server.
