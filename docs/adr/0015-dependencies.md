# 0015 Dependencies and versions

Status: accepted

- Runtime: none beyond the ASP.NET Core shared framework.
- Tests: `xunit.v3`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Testing.Extensions.CodeCoverage`, `Microsoft.Playwright`, `vitest`, `@vitest/coverage-v8`.
- NuGet versions are central in `Directory.Packages.props`; npm versions are exact with a lockfile.
- `global.json` pins `10.0.100` with `latestFeature`, so any 10.0 SDK works and the newest installed one is used. CI installs the latest `10.0.x`.
- Dependabot opens weekly grouped updates for NuGet, npm and GitHub Actions. The SDK is left out because bumping `global.json` would stop older 10.0 SDKs, such as distribution packages, from building.
