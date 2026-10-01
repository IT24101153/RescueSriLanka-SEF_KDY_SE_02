# Backend test-host logging fix

## Result

- Release build: passed, 0 warnings, 0 errors.
- Full Release backend suite: 442 passed, 0 failed, 0 skipped.
- Focused Component D Release suite: 271 passed, 0 failed, 0 skipped, including Help Request coordination tests.
- git diff --check: passed.
- No production files changed in this task. Existing Component D Help Request behavior and all other preexisting uncommitted work were preserved.
- No assertions weakened, tests skipped/ignored, exception swallowing, continue-on-error, authentication changes, production service replacements, database/migration changes, commits, pushes or branch switches.

## Exact original failures

All names below have namespace RescueSriLanka.Api.Tests.

| Test | Factory and failure point |
| --- | --- |
| ComponentBAuthorizationIntegrationTests.PlannerTrigger_RequiresAuthentication | ComponentBAuthorizationIntegrationTests.ApiFactory; test constructor calls CreateClient |
| ComponentBAuthorizationIntegrationTests.PlannerTrigger_RequiresCoordinatorRole | Same factory and constructor |
| ComponentBAuthorizationIntegrationTests.CoordinatorReachesControllerAndGetsObjectiveValidation | Same factory and constructor |
| ReportWithPhotoTests.AnUnacceptableFileIsRefusedBeforeAnyReportExists | ReportWithPhotoTests.Factory; PostAsync calls CreateClient |
| ReportWithPhotoTests.AStorageFailureKeepsTheReportAndSaysSo | Same factory and PostAsync |
| ReportWithPhotoTests.ThePhotoIsStoredBeforeTheAgentIsAskedToLook | Same factory and PostAsync |

All six fail before their HTTP assertions. Their common exception chain is:

1. System.AggregateException: An error occurred while writing to logger(s). (Cannot open log for source '.NET Runtime'. You may not have write access.)
2. System.InvalidOperationException: Cannot open log for source '.NET Runtime'. You may not have write access.
3. System.ComponentModel.Win32Exception: Access is denied.

The captured stacks include Logger.ThrowLoggingError, HostingLoggerExtensions.HostedServiceStartupFaulted, Host.StartAsync, Program line 443, DeferredHostBuilder.DeferredHost.StartAsync, WebApplicationFactory.CreateClient and the factory entry points above. Inner frames include EventLogInternal.OpenForWrite, InternalWriteEvent, EventLogLogger.Log and WindowsEventLog.WriteEntry. Full per-test messages and stacks are retained in [before-test-host-fix.trx](../backend/RescueSriLanka.Api.Tests/TestResults/before-test-host-fix.trx).

## Why Event Log is used, and what triggers the failure

Program.cs uses WebApplication.CreateBuilder(args). There is no application-specific AddEventLog call or EventLog setting in the inspected appsettings files. The .NET host defaults add EventLogLoggerProvider on Windows and give it a default Warning threshold. This does not depend on running as a Windows service or using the Development environment. See [the .NET 10 host source, AddDefaultServices](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting/src/HostingHostBuilderExtensions.cs#L256-L288).

Both failing factories select Testing and disable migrations, but originally retain default logging providers. The working ComponentDApiFactory explicitly calls ConfigureLogging with ClearProviders. It also isolates databases/workers/providers for its own tests; those additional substitutions were not needed or copied for this fix.

A logging-only diagnostic run proves the six tests succeed when console logging replaces the default providers. Its retained console output exposes the initiating Data Protection diagnostics: DPAPI CryptographicException (Error occurred during a cryptographic operation) and UnauthorizedAccessException while trying to write the user-profile ASP.NET DataProtection-Keys directory. Under the restricted test account, these recoverable diagnostics cause the Event Log provider itself to throw. That logging exception then escapes host startup. It is therefore more precise than saying registration of the Event Log provider alone fails. The provider's attempted write is the fatal operation.

The test-only fix leaves those diagnostics visible through console logging. It does not change Data Protection, production logging, authentication, services or business behavior. No test depends on a specific logging provider.

## Exact files changed in this task

- backend/RescueSriLanka.Api.Tests/TestHostLogging.cs: shared UseTestLogging extension; ClearProviders then AddConsole.
- backend/RescueSriLanka.Api.Tests/ComponentBAuthorizationIntegrationTests.cs: one factory call to UseTestLogging.
- backend/RescueSriLanka.Api.Tests/ReportWithPhotoTests.cs: one factory call to UseTestLogging.
- docs/backend-test-host-logging-fix.md: this report.

## Validation and warnings

Requested commands were run:

~~~powershell
dotnet build RescueSriLanka.slnx --configuration Release
dotnet test RescueSriLanka.slnx --configuration Release --no-build
~~~

The test command included a TRX logger/results directory to retain evidence. A follow-up build with --no-restore confirmed final source after removal of unused imports. The focused run used the same Component D/service filter as the preceding implementation validation.

The initial sandboxed build failed to reach NuGet (NU1301, network socket permission denied). Retrying the requested build with network permission restored packages successfully and produced zero compiler warnings/errors. Test execution remained restricted. Tests used a deliberately non-working loopback PostgreSQL connection and Database:MigrateOnStartup=false; no shared database was accessed or mutated.

Non-failing console diagnostics remain: user-profile Data Protection cryptography/key-write errors, missing wwwroot, TestServer request-body-size feature warnings, and the photo test's intentional fake-storage failure warning. They are retained rather than suppressed. Git also emits normal LF-to-CRLF notices; there are no whitespace errors.

Evidence:

- [Original full run: 436 passed, 6 failed](../backend/RescueSriLanka.Api.Tests/TestResults/before-test-host-fix.trx)
- [Logging-only diagnostic: all six pass](../backend/RescueSriLanka.Api.Tests/TestResults/logging-only-diagnostic.trx)
- [Final full Release run: 442 passed](../backend/RescueSriLanka.Api.Tests/TestResults/after-test-host-fix.trx)
- [Final focused Release run: 271 passed](../backend/RescueSriLanka.Api.Tests/TestResults/component-d-after-test-host-fix.trx)

TRX files are local ignored test artifacts. This result supersedes the six-failure validation note in the earlier Help Request implementation report; its implementation remains untouched.

## git status --short

Includes all preserved work from earlier tasks; only the four files listed above changed in this task.

```text
 M backend/RescueSriLanka.Api.Tests/AssignmentServiceTests.cs
 M backend/RescueSriLanka.Api.Tests/ComponentBAuthorizationIntegrationTests.cs
 M backend/RescueSriLanka.Api.Tests/ComponentDSafetyAgentGoldenTests.cs
 M backend/RescueSriLanka.Api.Tests/ReportWithPhotoTests.cs
 M backend/RescueSriLanka.Api/Features/ComponentD/Agents/SafetyValidation/SafetyValidationTools.cs
 M backend/RescueSriLanka.Api/Features/ComponentD/Services/AssignmentService.cs
 M backend/RescueSriLanka.Api/Program.cs
 M frontend/src/components/componentD/DispatchLifecycle.test.tsx
 M frontend/src/components/componentD/RescueCoordinatorDashboard.tsx
 M frontend/src/components/componentD/api.ts
 M frontend/src/components/componentD/types.ts
 M frontend/src/shared/layout/ConsoleShell.test.tsx
 D mobile/android/.kotlin/sessions/kotlin-compiler-9685438044901224839.salive
 M mobile/android/gradle.properties
?? backend/RescueSriLanka.Api.Tests/ComponentDIncidentSelectionTests.cs
?? backend/RescueSriLanka.Api.Tests/HelpRequestCoordinationApiTests.cs
?? backend/RescueSriLanka.Api.Tests/HelpRequestCoordinationTests.cs
?? backend/RescueSriLanka.Api.Tests/TestHostLogging.cs
?? backend/RescueSriLanka.Api/Features/ComponentD/Controllers/RescueHelpRequestsController.cs
?? backend/RescueSriLanka.Api/Features/ComponentD/DTOs/HelpRequestCoordinationDtos.cs
?? backend/RescueSriLanka.Api/Features/ComponentD/Services/ActiveResponseWork.cs
?? backend/RescueSriLanka.Api/Features/ComponentD/Services/HelpRequestCandidateService.cs
?? backend/RescueSriLanka.Api/Features/ComponentD/Services/HelpRequestReadService.cs
?? backend/RescueSriLanka.Api/Features/ComponentD/Services/HelpRequestRecommendationService.cs
?? docs/backend-test-host-logging-fix.md
?? docs/component-d-help-request-coordination.md
?? docs/component-d-help-request-implementation-report.md
?? frontend/src/components/componentD/AssignmentIncident.test.tsx
?? frontend/src/components/componentD/AssignmentIncident.tsx
?? frontend/src/components/componentD/HelpRequestApi.test.ts
?? frontend/src/components/componentD/HelpRequestCoordination.test.tsx
?? frontend/src/components/componentD/HelpRequestCoordinationPanel.css
?? frontend/src/components/componentD/HelpRequestCoordinationPanel.tsx
?? frontend/src/components/componentD/HelpRequestResponseMap.tsx
?? frontend/src/components/componentD/IncidentApi.test.ts
?? frontend/src/components/componentD/incidentOptions.ts
```
