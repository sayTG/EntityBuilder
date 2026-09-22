# EntityBuilder — codebase notes

_Last updated: 2026-09-22_

Grounded facts about this project so we don't have to re-derive them.

## Query builder — WHERE condition value kinds

`Models/QueryBuilderRequest.cs` → `WhereCondition.ValueKind` (string, default `"Literal"`) selects how the value is emitted into the generated SQL.

| ValueKind | Behaviour                                                                                   |
| --------- | ------------------------------------------------------------------------------------------- |
| `Literal` | `Value` is parameterised as `@pN` (default).                                                |
| `Now`     | No parameter added; the DB's current date+time function is inlined into the WHERE clause.   |
| `Today`   | No parameter added; the DB's current date-only function is inlined.                         |

Dialect-specific inlined expressions (`Data/SqlServerQueryExecutionService.cs` + `Data/SqliteQueryExecutionService.cs`, WHERE-build blocks):

| Kind    | SQL Server               | SQLite                          |
| ------- | ------------------------ | ------------------------------- |
| `Now`   | `GETDATE()`              | `datetime('now','localtime')`   |
| `Today` | `CAST(GETDATE() AS DATE)`| `date('now','localtime')`       |

`Now`/`Today` are deliberately inlined as SQL functions (not resolved server-side in C#) so **scheduled reports pick fresh time on every run** — the scheduled report stores the composed SQL and the worker re-executes it later (`Controllers/EntityBuilderController.cs::ScheduleReport` → `Services/ReportScheduleWorker.cs`). A C#-side `DateTime.Now` would freeze at schedule time.

UI (`wwwroot/js/entity-builder.js`, `wireValueKindToggles` helper): each WHERE row has two mutually-exclusive `.eb-btn-when` toggles (`data-mode="Now"` and `data-mode="Today"`) beside the value input. Whichever is pressed disables the input and sends `valueKind: "Now" | "Today"` to the payload. Both toggles are hidden for `IS NULL`, `IS NOT NULL`, and `IN` operators. The helper is shared by the main-page WHERE row (`addWhereBtn`) and the schedule-modal WHERE row (`addScheduleWhereRow`) — do not duplicate its logic.

## Scheduled reports — lifecycle & endpoints

Storage: Redis hash `scheduled-reports:{userEmail}`, one field per report id, JSON body (`Services/ReportScheduleService.cs`).

Worker: `Services/ReportScheduleWorker.cs` polls every 60s, calls `GetDueReportsAsync` (status=Queued, NextRun ≤ UtcNow), sends via `ReportEmailService`. Success on Once → `Sent`; success on recurring → `NextRun` recomputed. Failure → `Failed`.

Shared next-run computation: `Utilities/ScheduleNextRunCalculator.Compute(report, nowUtc)` — used by initial create (`EntityBuilderController.ScheduleReport`), edit (`ReportScheduleService.EditScheduledReportAsync`), and recurring worker (`ReportScheduleWorker.ProcessDueReportsAsync`). Do not duplicate the switch-on-Frequency logic elsewhere.

Actions available on a report and which endpoints/services handle them:

| Action | Statuses allowed | Controller action | Service method |
| ------ | ---------------- | ----------------- | -------------- |
| Cancel | Queued, Failed   | `DELETE /EntityBuilder/CancelScheduledReport/{id}` | `CancelScheduledReportAsync` |
| Rerun  | Sent, Failed     | `POST /EntityBuilder/RerunScheduledReport/{id}` | `RerunScheduledReportAsync` (sets Status=Queued, NextRun=UtcNow) |
| Edit   | Queued, Failed   | `POST /EntityBuilder/UpdateScheduledReport/{id}` with `EditScheduledReportRequest` body | `EditScheduledReportAsync` |
| Fetch  | Any              | `GET /EntityBuilder/GetScheduledReport/{id}` | `GetScheduledReportAsync` |

`EditScheduledReportRequest` covers `Subject`, `RecipientEmail`, `QueryDefinition` (a `QueryBuilderRequest`), and the schedule fields. `CreatedBy` / `Id` / `CreatedAt` stay put. When `QueryDefinition` is supplied the controller calls `IQueryExecutionService.BuildQueryAsync(request)` to regenerate the SQL + `@p` parameters, and `EditScheduledReportAsync(id, email, patch, rebuiltSql, rebuiltParameters)` writes them onto the report. Editing always resets `Status=Queued` and recomputes `NextRun`.

UI: `Views/EntityBuilder/Index.cshtml` has `#scheduleQueryEditor` inside the schedule modal (hidden by default, revealed in edit mode). It shows a read-only summary of table/joins/columns/group-by/order-by plus a compact WHERE editor using the same dropdown UX as the main builder (column, operator, value, NOW toggle, connector). Columns for the WHERE column dropdown come from `state.columns` — `entity-builder.js::ensureColumnsForDefinition` fetches them for the query's tables via the existing `fetchColumns(schema, table)` helper.

`entity-builder.js::executeQuery` stores each successful request as `lastQueryDefinition`; the create-schedule flow sends it alongside the SQL body so the server can prefer it.

## Structured query definition — the safe path for scheduled reports

`ScheduledReport.QueryDefinition` (a `QueryBuilderRequest`) is the source of truth for a report's query. SQL is **always regenerated server-side** from it on create + edit via `IQueryExecutionService.BuildQueryAsync(request)` (SQL Server: `GETDATE()`, SQLite: `datetime('now','localtime')`; both use `@p` parameters and bracketed identifiers). Client-supplied SQL is never trusted for scheduled reports.

`BuildQueryAsync` is implemented by delegating to `ExecuteStructuredQueryAsync(request, buildOnly: true)` — the shared private overload runs validation and assembles the SELECT / JOIN / WHERE / GROUP BY / ORDER BY clauses, then either paginates+executes (UI grid) or returns the unpaginated SQL (report path). Do NOT duplicate the build logic elsewhere.

Legacy fallback: for old clients that still post raw `Sql` (no `QueryDefinition`), `EntityBuilderController.ScheduleReport` still runs `SqlSafetyGuard.EnsureSafeSelect` and accepts it. Existing Redis rows with `QueryDefinition == null` cannot be edited via the UI — the edit modal shows a "cancel and re-schedule" message and disables the WHERE editor's Add button.

## Client-supplied SQL — safety guard

Any controller action that still ingests raw SQL from the client MUST call `Utilities/SqlSafetyGuard.EnsureSafeSelect(sql)` and return 400 on failure. Currently applied on:

- `EntityBuilderController.SendReportEmail`
- `EntityBuilderController.ScheduleReport` (legacy path only — when `QueryDefinition` is null)

Guard rejects: empty, statement-stacking (`;` between statements), SQL comments (`--`, `/* */`), anything not starting with `SELECT`/`WITH`, and any occurrence of `INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|INTO|xp_*|sp_*` on a word boundary. `ExecuteQuery` does **not** need the guard — structured builder path. `UpdateScheduledReport` does **not** need the guard either — it only accepts `QueryDefinition` (no SQL field on the request).

Defence-in-depth: the query builder already parameterises WHERE values. The guard exists for the send-report path where SQL travels as a string; the structured-definition path is the primary defence for scheduled reports.

UI: `wwwroot/js/entity-builder.js` row-render adds Edit / Rerun / Cancel buttons based on status; the schedule modal is reused in "edit mode" via `editingReportId` + `setScheduleModalMode('edit')`.

## Open threads to verify next time

- Whether the messaging service (`ReportEmailService`) treats the inlined `GETDATE()` / `datetime('now','localtime')` the same as parameterised SQL (i.e. it should — no Dapper template placeholders are involved for these).
- Edit-mode form always shows the recipient in the custom-email field (checkbox unchecked); we don't know server-side whether the address originally matched the logged-in email, so we can't reliably restore the "send to my login email" toggle. Fine, but worth confirming with the user.
