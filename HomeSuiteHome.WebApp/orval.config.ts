import { defineConfig } from "orval";

export default defineConfig({
  api: {
    input: "../HomeSuiteHome.WebApi/obj/HomeSuiteHome.WebApi.json",
    output: {
      target: "./src/api/generated/endpoints.ts",
      schemas: "./src/api/generated/model",
      client: "react-query",
      mode: "tags-split",
      override: {
        mutator: {
          path: "./src/api/client.ts",
          name: "customFetch",
        },
        query: {
          useQuery: true,
        },
      },
    },
  },
});
