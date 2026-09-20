#!/bin/sh
# Applies pending migrations, then starts the API. Only in this entrypoint -
# Program.cs deliberately does not apply migrations itself (see SeedFirstAdmin),
# so the manual `dotnet ef database update` step from the README still works
# unchanged outside Docker; this is just that same step automated for compose.
set -e

./efbundle --connection "$ConnectionStrings__DefaultConnection"

exec dotnet inventria.dll
