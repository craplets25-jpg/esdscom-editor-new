# .NET 10.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that an .NET 10.0 SDK required for this upgrade is installed on the machine.
2. Ensure that the SDK version specified in any `global.json` files is compatible with .NET 10.0 (update `global.json` if present).
3. Upgrade projects listed in the PROJECTS_TO_UPGRADE section below by changing their `TargetFramework` from `net6.0` to `net10.0` and apply per-project fixes.

   PROJECTS_TO_UPGRADE

   - `Client/eSDSCom.Editor.Client.csproj`
   - `Server/eSDSCom.Editor.Server.csproj`
   - `Shared/eSDSCom.Editor.Shared.csproj`
   - `FixXml/FixXml.csproj`
   - `xsdNavigator/xsdNavigator.csproj`
   - `Tests/eSDSCom.AuthoringTool.Tests/eSDSCom.Editor.Tests.csproj`

4. Run unit tests and fix compilation/test failures.
5. Review and update NuGet package versions where required (security or compatibility updates).
6. Update CI workflows and README docs to require .NET 10 SDK and any new build steps.
7. Commit changes on branch `upgrade-to-NET10`, push branch, and open a pull request to `main` with upgrade notes and test results.

## Settings

### Target Framework

- Upgrade to target .NET version `NET 10.0 (Long Term Support)`.

### Excluded projects

No projects are excluded at this stage.

### Project upgrade details

#### `Client/eSDSCom.Editor.Client.csproj`

Project properties changes:
- Change `<TargetFramework>net6.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`.

NuGet packages changes:
- To be determined by package analysis. Run `dotnet list package --outdated` and `dotnet list package --vulnerable` during the analysis step.

Other changes:
- Blazor/wasm-specific adjustments may be required for updated SDK and package versions.

#### `Server/eSDSCom.Editor.Server.csproj`

Project properties changes:
- Change `<TargetFramework>net6.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`.

NuGet packages changes:
- To be determined by package analysis.

Other changes:
- Review any usage of obsolete APIs and update to supported alternatives if needed.

#### `Shared/eSDSCom.Editor.Shared.csproj`

Project properties changes:
- Change `<TargetFramework>net6.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`.

NuGet packages changes:
- To be determined.

#### `FixXml/FixXml.csproj`

Project properties changes:
- Change `<TargetFramework>net6.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`.

#### `xsdNavigator/xsdNavigator.csproj`

Project properties changes:
- Change `<TargetFramework>net6.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`.

#### `Tests/eSDSCom.AuthoringTool.Tests/eSDSCom.Editor.Tests.csproj`

Project properties changes:
- Change `<TargetFramework>net6.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`.


---

Summary: This initial plan upgrades all projects currently targeting `net6.0` to `net10.0`. Next step is to run analysis (dotnet CLI checks) to detect package updates and API-breaking changes. After your confirmation I'll proceed to:

- run `dotnet --info` and confirm .NET 10 SDK availability,
- run `dotnet list package` and `dotnet list package --vulnerable` across projects,
- open `./.github/upgrades/NET10-Upgrade-Plan.md` for any edits you want, and
- implement the project file changes and iterate until the solution builds and tests pass.

Please review the plan file and reply `continue` to proceed or tell me what you'd like to change.