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

RUN apk add --no-cache tesseract-ocr fontconfig \
    tesseract-ocr-data-eng tesseract-ocr-data-fra tesseract-ocr-data-deu \
    tesseract-ocr-data-spa tesseract-ocr-data-ita tesseract-ocr-data-por \
    tesseract-ocr-data-nld tesseract-ocr-data-rus tesseract-ocr-data-chi_sim \
    tesseract-ocr-data-jpn tesseract-ocr-data-kor tesseract-ocr-data-ara \
    tesseract-ocr-data-hin tesseract-ocr-data-pol tesseract-ocr-data-tur \
    tesseract-ocr-data-vie tesseract-ocr-data-tha tesseract-ocr-data-swe \
    tesseract-ocr-data-dan tesseract-ocr-data-nor

COPY --from=backend-build /app/publish .
COPY --from=frontend-build /app/frontend/dist/Homestead/browser ./wwwroot/

RUN mkdir -p /media /cache && chown -R appuser:appgroup /media /cache

USER appuser

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
ENV FileStorage__MediaPath=/media
ENV FileStorage__CachePath=/cache
ENV Ocr__Tesseract__TessDataPath=/usr/share/tessdata

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Homestead.WebApi.dll"]
