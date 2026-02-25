const target =
  process.env["services__homesuitehome-webapi__https__0"] ??
  process.env["services__homesuitehome-webapi__http__0"] ??
  "https://localhost:7248";

module.exports = [
  {
    context: ["/api", "/openapi"],
    target,
    secure: false,
    changeOrigin: true,
  },
  {
    context: ["/hubs"],
    target,
    secure: false,
    changeOrigin: true,
    ws: true,
  },
];
