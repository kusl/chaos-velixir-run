# 0014 Test strategy

Status: accepted

- .NET: xUnit v3 (package `xunit.v3` 4.x) on Microsoft Testing Platform v2, enabled for `dotnet test` in `global.json`. Unit tests cover services and helpers; integration tests use `WebApplicationFactory` for pages, headers, cookies, the hub guard and compression.
- JavaScript: Vitest in Node tests the pure exports of each module. DOM, IndexedDB, canvas and WebSocket behaviour is covered by Playwright, so no DOM library is installed.
- End to end: Playwright .NET in `mcr.microsoft.com/playwright/dotnet:v<version>-noble`, which ships the .NET 10 SDK and browsers. The version comes from `Directory.Packages.props` only. The app is published and run inside the same container on localhost, in Production mode with relaxed limits. It is started from the source copy, not its publish folder, like velixir starts it. The app log is printed when tests fail and kept in `artifacts/e2e/app.log`.
- End-to-end tests run serially (`[assembly: Parallelization(Mode = ParallelMode.None)]`) because they share one server's board, canvas and slot machine. `CollectionBehavior.DisableTestParallelization` is obsolete in xUnit v3 4.x.
- Coverage is reported (Cobertura for .NET and JavaScript) with no threshold.
- Test projects use the default analyzer set; the app uses the recommended set. Warnings are errors everywhere.
