# Stage 1: Build Angular frontend
FROM node:22-alpine AS frontend-build
WORKDIR /app/frontend
COPY HomeSuiteHome.WebApp/package.json HomeSuiteHome.WebApp/package-lock.json ./
RUN npm ci
COPY HomeSuiteHome.WebApp/ ./
RUN npm run build

# Stage 2: Build .NET backend
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS backend-build
WORKDIR /src
COPY HomeSuiteHome.WebApi/HomeSuiteHome.WebApi.csproj HomeSuiteHome.WebApi/
COPY HomeSuiteHome.ServiceDefaults/HomeSuiteHome.ServiceDefaults.csproj HomeSuiteHome.ServiceDefaults/
RUN dotnet restore HomeSuiteHome.WebApi/HomeSuiteHome.WebApi.csproj
COPY HomeSuiteHome.WebApi/ HomeSuiteHome.WebApi/
COPY HomeSuiteHome.ServiceDefaults/ HomeSuiteHome.ServiceDefaults/
RUN dotnet publish HomeSuiteHome.WebApi/HomeSuiteHome.WebApi.csproj -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN addgroup -S appgroup && adduser -S appuser -G appgroup

COPY --from=backend-build /app/publish .
COPY --from=frontend-build /app/frontend/dist/HomeSuiteHome/browser ./wwwroot/

RUN mkdir -p /app/media && chown -R appuser:appgroup /app/media

USER appuser

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV FileStorage__MediaPath=/app/media

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "HomeSuiteHome.WebApi.dll"]
