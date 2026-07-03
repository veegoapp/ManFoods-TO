db-push:
	bash scripts/db-update.sh

run:
	dotnet run --project MvcApp.csproj

build:
	dotnet build MvcApp.csproj
