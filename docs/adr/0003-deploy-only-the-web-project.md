# 0003 Deploy only the web project

Status: accepted

velixir builds with a Nixpacks-style plan: `dotnet restore` and `dotnet publish -c Release -o out` at the upload root, then starts `./out/<first csproj>`. Uploading the repository root would restore, build and publish the test projects too, including the Playwright driver, onto a 1 GB disk.

- The deploy workflow runs `velixir deploy --app <id> --source-dir ./src/Chaos.Web`, the documented monorepo option.
- `Chaos.Web.csproj` is self-sufficient: target framework, nullable, implicit usings and runtime settings are in the project, not only in `Directory.Build.props`. `global.json`, central package management and analyzers do not reach the velixir build, which is fine because the project has no package references.
- The start command runs from the upload root, so the content root is the source folder, not `out/`. The web root is set to `wwwroot` next to the assembly when it exists, so published assets are served. The content root is left alone because `WebApplicationFactory` sets it.
- The app binds to `PORT` when set, like zen.
- Deploys are not gated on CI, matching zen. A failing velixir build keeps the previous release; the `/health` probe stops a release that does not start.
