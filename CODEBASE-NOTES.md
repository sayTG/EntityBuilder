# EntityBuilder — codebase notes

_Last updated: 2026-09-22_

Grounded facts about this project so we don't have to re-derive them.

## Query builder — WHERE condition value kinds

`Models/QueryBuilderRequest.cs` → `WhereCondition.ValueKind` (string, default `"Literal"`) selects how the value is emitted into the generated SQL.

| ValueKind | Behaviour                                                                                   |
| --------- | ------------------------------------------------------------------------------------------- |
| `Literal` | `Value` is parameterised as `@pN` (default).                                                |
| `Now`     | No parameter added; the DB's current-datetime function is inlined into the WHERE clause.    |

Dialect-specific `Now` expressions:

- SQL Server (`Data/SqlServerQueryExecutionService.cs`, WHERE-build block near line 240): `GETDATE()`.
- SQLite (`Data/SqliteQueryExecutionService.cs`, WHERE-build block near line 232): `datetime('now','localtime')`.

`Now` is deliberately inlined as a SQL function (not resolved server-side in C#) so **scheduled reports pick fresh time on every run** — the scheduled report stores the composed SQL and the messaging service re-executes it later (`Controllers/EntityBuilderController.cs::ScheduleReport` → `Services/ReportScheduleWorker.cs`). A C#-side `DateTime.Now` would freeze at schedule time.

UI (`wwwroot/js/entity-builder.js`, `addWhereBtn` handler + `executeQuery` payload builder): each WHERE row has an `.eb-btn-now` toggle beside `.where-value`. Toggling it disables the input and adds `valueKind: "Now"` to the payload. The toggle is hidden for `IS NULL`, `IS NOT NULL`, and `IN` operators (Now doesn't apply there); backend also treats those branches as literal-only.

## Open threads to verify next time

- Whether the messaging service (`ReportEmailService`) treats the inlined `GETDATE()` / `datetime('now','localtime')` the same as parameterised SQL (i.e. it should — no Dapper template placeholders are involved for these).
