# Builds the RescueSriLanka API for Render (or any Docker host).
# Build context is this folder: backend/RescueSriLanka.Api

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY RescueSriLanka.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Render tells the app which port to listen on through $PORT.
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet RescueSriLanka.Api.dll"]
