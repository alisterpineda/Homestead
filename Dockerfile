# Stage 1: Build Angular frontend
FROM node:25-alpine AS frontend-build
WORKDIR /app/frontend
COPY Homestead.WebApp/package.json Homestead.WebApp/package-lock.json ./
RUN npm ci
COPY Homestead.WebApp/ ./
RUN npm run build

# Stage 2: Build .NET backend
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS backend-build
WORKDIR /src
COPY Homestead.WebApi/Homestead.WebApi.csproj Homestead.WebApi/
COPY Homestead.ServiceDefaults/Homestead.ServiceDefaults.csproj Homestead.ServiceDefaults/
RUN dotnet restore Homestead.WebApi/Homestead.WebApi.csproj
COPY Homestead.WebApi/ Homestead.WebApi/
COPY Homestead.ServiceDefaults/ Homestead.ServiceDefaults/
RUN dotnet publish Homestead.WebApi/Homestead.WebApi.csproj -c Release -o /app/publish -p:OpenApiGenerateDocuments=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN addgroup -S appgroup && adduser -S appuser -G appgroup

RUN apk add --no-cache tesseract-ocr tesseract-ocr-data-eng

COPY --from=backend-build /app/publish .
COPY --from=frontend-build /app/frontend/dist/Homestead/browser ./wwwroot/

RUN mkdir -p /app/media && chown -R appuser:appgroup /app/media

USER appuser

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
ENV FileStorage__MediaPath=/app/media
ENV Ocr__Tesseract__TessDataPath=/usr/share/tessdata

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Homestead.WebApi.dll"]
