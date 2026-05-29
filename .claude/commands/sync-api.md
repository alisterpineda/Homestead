---
description: Regenerate the Angular API client after a backend contract change
---

Sync the frontend API client with the backend OpenAPI spec. The spec is generated at build time
(no running backend needed). Run these steps and report the result of each:

1. Build the WebApi to regenerate the OpenAPI spec:
   `dotnet build Homestead.WebApi`  → emits `Homestead.WebApi/obj/Homestead.WebApi.json`
2. Regenerate the client (ng-openapi-gen → `src/app/api/generated/`):
   `cd Homestead.WebApp && npm run generate-api`
3. Report what changed:
   `git -C Homestead.WebApp diff --stat src/app/api/generated/`
4. Confirm consumers still build:
   `cd Homestead.WebApp && npm run build`
5. If new endpoints/models appeared, update the domain service wrappers in
   `Homestead.WebApp/src/app/api/services/` to expose them — components must use those wrappers,
   never the generated functions or `HttpClient` directly.
