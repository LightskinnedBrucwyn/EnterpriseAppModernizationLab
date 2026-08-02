# BatHouseholdHub Finance Architecture Review

Date: 2026-08-01

Scope: `projects/BatHouseholdHub` only. This review is documentation and planning only; it does not implement new production behavior or introduce real household financial records.

## Current Architecture

BatHouseholdHub is a Blazor Server household dashboard targeting .NET 8. It is intended for private runtime deployment on BatServer over LAN/Tailscale only, while this GitHub repository is public and must contain code, documentation, and synthetic examples only. The application is organized as a single web project:

- UI: Razor components in `projects/BatHouseholdHub/Components`.
- Domain models: `projects/BatHouseholdHub/Models/HouseholdData.cs`.
- Application services: `projects/BatHouseholdHub/Services`.
- Runtime persistence: JSON and uploaded files under `App_Data`.
- Deployment: Docker container defined by `Dockerfile` and `compose.yaml`, with `./data:/app/App_Data` mounted on BatServer.

The app registers one singleton `HouseholdStore`, scoped finance summary services, and one hosted recurring-transaction service in `Program.cs`. There is no database, identity provider, authorization layer, background job scheduler, message queue, or formal repository/data-access abstraction.

The finance pages currently live in:

- `Components/Pages/Home.razor`: recent activity, paycheck logging, savings goal, upload inbox, and next-bill snapshot.
- `Components/Pages/Finances.razor`: transaction entry, Rocket Money CSV import, filtering, review hints, recurring monthly transaction display.
- `Components/Pages/Bills.razor`: available funds, bills, cashflow windows, income events, bill statuses, manual reconciliation, and debt-payment list.
- `Components/Pages/Calendar.razor`: monthly bill calendar and category totals.

## Existing Capabilities

Current finance functionality includes:

- Manual transaction creation, editing, deletion, undo-last-delete, and simple monthly recurring transaction creation.
- Paycheck logging from the home page as an income transaction.
- Rocket Money CSV import with duplicate detection through a SHA-256 source key.
- Transfer-like category exclusion during import for internal transfers, card payments, savings transfers, and investments.
- Imported transaction matching against known or user-created bills by normalized name and approximate amount.
- Needs-review marking when an imported bill-like transaction cannot be confidently matched.
- Manual review resolution and manual transaction-to-bill linking.
- Bill creation and editing with amount, due day, frequency enum, category, priority, owner, status, autopay, notes, and optional delayed-income linkage.
- Manual statuses for bills: upcoming, pending, paid, reserved, delayed, needs review, and skipped.
- Cashflow summaries for until-next-paycheck, month-end, next 30 days, and custom date.
- Available-funds tracking across generic household member, shared, and buffer buckets.
- Income event tracking with estimated, pending, and received statuses.
- Simple debt-account model exists, though the current UI primarily treats debt as a bill category.
- One savings goal displayed on the home page.
- Uploaded-file storage and retrieval through the household inbox.
- Data-protection keys persisted to `App_Data/keys`.
- BatServer Docker deployment with persistent data mounted from `./data`.

## Data Persistence Behavior

`HouseholdStore` creates `App_Data`, `App_Data/uploads`, and `App_Data/household.json` under the app content root. In Docker, `compose.yaml` maps host `./data` to `/app/App_Data`, so household data survives container rebuilds.

Important behaviors:

- If `household.json` is missing, `HouseholdStore` seeds starter non-financial recipes, then ensures known bill, income-source, and shopping-site records.
- If `household.json` is corrupt, the app copies it to a timestamped `.corrupt-*.json` backup and starts from a fresh seed.
- Saves rewrite the whole JSON file.
- `SaveAsync` uses a semaphore, but some mutating methods modify `Data` before acquiring the save lock.
- The singleton store keeps all data in memory for the process lifetime.
- Uploads are stored as opaque files named by GUID, while file metadata is kept in `household.json`.
- Recurring transactions are processed on startup and every six hours by `RecurringTransactionService`.
- There is no versioned schema migration system.
- There is no transactional boundary across JSON writes and upload file writes.

The current persistence model is workable for a local household prototype, but the next finance phases need stricter durability, auditability, and privacy controls.

## Existing Bills, Transactions, Recurring Payments, and Cashflow Logic

Bills are represented by `Bill` in `HouseholdData.cs`. They support category, frequency, priority, owner, notes, autopay, last-paid date, manual status, money type, and an optional linked income event. `EffectiveStatus` treats a bill as paid when `LastPaidDate` is in the current month.

Transactions are represented by the current `Transaction` class. They store description, category, owner, absolute amount, income flag, recurring rule id, account display text, institution, import source, source key, money type, matched bill id, and review flag.

Recurring payments are represented by `RecurringTransaction`. The app posts them once per month when active and when `DayOfMonth <= today.Day`. The implementation currently assumes monthly cadence even though the bill model has richer frequencies.

Cashflow is calculated by `CashflowService.BuildSummary`. It:

- Chooses a selected date from the selected cashflow window.
- Finds the next expected paycheck from `IncomeEvents`.
- Computes next due dates for active bills.
- Excludes reserved, skipped, blocked delayed, and pending bills from regular upcoming due totals.
- Treats delayed bills as payable when their linked income event has been received.
- Sums pending payments, reserved money, upcoming bills, required debt payments, expected income, and remaining funds.
- Surfaces needs-review transactions for the bills page.

## Technical Debt

- Finance models are all in one large `HouseholdData.cs` file with mixed household, food, shopping, upload, LLM, bill, transaction, income, debt, and savings concerns.
- JSON persistence and domain behavior are tightly coupled in `HouseholdStore`.
- Startup seed/ensure logic currently embeds household-specific known bill and income names in source code.
- There is no migration/version system for `household.json`.
- There is no formal validation layer for imported or manually entered financial data.
- `Transaction.IsIncome`, `MoneyType`, category strings, and signed import amounts create overlapping representations of money flow.
- Frequency support is uneven: bills expose weekly, biweekly, quarterly, and yearly values, but bill calendar and recurring transaction posting are effectively monthly.
- Current recurring transaction logic posts only when the app is running and checking after the due day.
- The Rocket Money importer handles one expected CSV shape and has no import-batch record, dry-run mode, column mapping UI, or per-row audit trail.
- Cashflow calculations are useful but not independently tested.
- There is no test project in the repository.
- There is no authorization, user identity, household role, or approval workflow.
- Upload handling does not validate allowed file classes beyond browser-provided metadata.
- Uploaded files, data-protection keys, and finance JSON are all under one mounted data folder with no documented retention policy.

## Security Risks

High-priority risks before autonomous finance workflows:

- No authentication or authorization is present. Any party with network access to the app can view and mutate household finance data.
- `/uploads/{id}` serves files by GUID without authorization checks, signed URLs, expiration, content-disposition policy, or malware scanning.
- `household.json` can contain transactions, income events, bill names, account display labels, institutions, uploaded-file metadata, and local LLM settings in one plaintext file.
- Known bill and income-source seed data in code can accidentally expose household-specific financial context through GitHub.
- `compose.yaml` accepts `ANTHROPIC_API_KEY` from the environment. That is better than committing a key, but secret-source expectations are not documented in the app.
- The app has no CSRF-specific approval model for high-risk finance actions beyond standard Blazor antiforgery middleware.
- There is no audit log of edits, imports, deletions, bill status changes, file reads, or future AI-agent recommendations.
- No explicit backup encryption, restore testing, or data-retention process is defined.
- There is no PII/financial-data redaction layer for logs or AI prompts.
- Product lookup services can make outbound network/API calls; future finance agents need stricter allowlists and prompt/data minimization.

## Missing Finance Entities

The following proposed entities should be designed before implementation. Names and fields below are synthetic schema sketches only.

### FinancialAccount

Represents a cash, checking, savings, prepaid, or generic external account.

Synthetic shape:

```csharp
public sealed class FinancialAccount
{
    public Guid Id { get; init; }
    public string DisplayName { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string AccountType { get; set; } = "Checking";
    public string Owner { get; set; } = "Shared";
    public string LastFour { get; set; } = "";
    public decimal? CurrentBalance { get; set; }
    public DateTime? BalanceAsOf { get; set; }
    public bool IsActive { get; set; } = true;
}
```

### CreditCardAccount

Extends account tracking for revolving credit, utilization, statement, and payoff planning.

Fields likely needed: `FinancialAccountId`, `CreditLimit`, `CurrentBalance`, `StatementBalance`, `MinimumPayment`, `Apr`, `StatementCloseDay`, `PaymentDueDay`, `AutopayEnabled`, `LastReportedBalance`.

### LoanAccount

Tracks installment loans and secured debts.

Fields likely needed: `FinancialAccountId`, `OriginalPrincipal`, `CurrentPrincipal`, `InterestRate`, `PaymentAmount`, `PaymentDueDay`, `TermMonths`, `StartDate`, `MaturityDate`, `CollateralDescription`, `ServicerName`.

### RecurringBill

Replaces or wraps the current `Bill` for richer schedule and autopay behavior.

Fields likely needed: `Name`, `Vendor`, `Category`, `AmountMode`, `ExpectedAmount`, `MinimumAmount`, `ScheduleRule`, `NextDueDate`, `GracePeriodDays`, `Priority`, `Autopay`, `LinkedAccountId`, `LinkedDebtAccountId`, `Status`, `Owner`, `Notes`.

### Transaction

The current `Transaction` should evolve into an import-safe ledger item.

Fields likely needed: `PostedDate`, `AuthorizedDate`, `Description`, `MerchantName`, `CategoryId`, `Amount`, `Direction`, `AccountId`, `InstitutionName`, `Source`, `SourceKey`, `ImportBatchId`, `MatchedBillId`, `ReviewStatus`, `Tags`, `Notes`.

### CreditScoreSnapshot

Tracks score snapshots from credit-monitoring sources without storing complete reports in GitHub.

Fields likely needed: `Source`, `Owner`, `Score`, `ScoreModel`, `CapturedAt`, `ReportDate`, `Factors`, `SyntheticOrManual`.

### CreditReportItem

Tracks report-level tradelines or public-record style items for review workflows.

Fields likely needed: `Owner`, `Bureau`, `CreditorName`, `AccountType`, `MaskedAccountNumber`, `OpenedDate`, `ReportedBalance`, `ReportedStatus`, `LastReportedAt`, `DisputeStatus`, `Notes`.

### CollectionAccount

Represents collection items separately from ordinary loans or cards.

Fields likely needed: `CollectorName`, `OriginalCreditor`, `Owner`, `ClaimedBalance`, `SettlementOffer`, `ValidationDeadline`, `StatuteOfLimitationsDate`, `Status`, `CommunicationLogIds`, `DocumentIds`.

### Paycheck

Promotes current paycheck/income-event behavior into a first-class payroll model.

Fields likely needed: `Owner`, `Employer`, `GrossAmount`, `NetAmount`, `PayDate`, `PayPeriodStart`, `PayPeriodEnd`, `DeductionsSummary`, `ImportBatchId`, `Status`.

### FinancialGoal

Generalizes the current `SavingsGoal`.

Fields likely needed: `Name`, `GoalType`, `TargetAmount`, `CurrentAmount`, `TargetDate`, `Priority`, `FundingAccountId`, `MonthlyContributionTarget`, `Status`.

### ImportBatch

Records every CSV/file import and reconciliation pass.

Fields likely needed: `Source`, `OriginalFileName`, `ContentHash`, `ImportedAt`, `ImportedBy`, `RowCount`, `ImportedCount`, `SkippedCount`, `InvalidCount`, `SanitizationStatus`, `ReviewStatus`.

### FinancialBrief

Stores generated daily/payday/weekly briefs without exposing raw prompts or sensitive supporting data.

Fields likely needed: `BriefType`, `GeneratedAt`, `PeriodStart`, `PeriodEnd`, `SummaryMarkdown`, `RecommendedActions`, `RiskFlags`, `ApprovalRequiredActions`, `DataSnapshotHash`, `ModelProvider`.

## Recommended Phased Architecture

Phase 0 should focus on security and public-repository hygiene before adding autonomy. Remove household-specific seed facts from source code, document private-data handling, add authentication for BatServer access, and ensure `.gitignore` covers all private runtime data.

Phase 1 should introduce finance-focused model boundaries while preserving existing JSON data. Add schema versioning, migration helpers, import-batch records, and dedicated services for accounts, transactions, recurring bills, paychecks, goals, and audit events. If JSON remains temporarily, separate public app configuration from private finance data.

Phase 2 should formalize Rocket Money imports with dry-run preview, import-batch persistence, column validation, duplicate review, account matching, transfer handling, and reversible reconciliation.

Phase 3 should build credit utilization and debt priority calculations from normalized credit-card, loan, collection, and recurring-bill entities. These engines should return recommendations, never direct actions.

Phase 4 should add payday and cashflow briefs based on paychecks, upcoming bills, account balances, goals, and pending review items.

Phase 5 should redesign the dashboard around operational finance workflows: bills due, approvals needed, next payday, utilization alerts, debt priority, import health, and private document inbox.

Phase 6 should add local/OpenAI hybrid agent integration behind strict data minimization, audit logging, approval gates, and a provider abstraction.

Phase 7 should support approval-gated letters and creditor workflows. Generated letters should be drafts only until explicitly approved by a household user.

## Exact Files Likely to Change

Documentation and hygiene:

- `docs/finance/finance-architecture-review.md`
- `docs/finance/private-data-boundary.md`
- `docs/finance/automation-roadmap.md`
- `.gitignore`
- `projects/BatHouseholdHub/DEPLOY.md`
- `projects/README.md`

Models and persistence:

- `projects/BatHouseholdHub/Models/HouseholdData.cs`
- `projects/BatHouseholdHub/Services/HouseholdStore.cs`
- new `projects/BatHouseholdHub/Models/Finance/*.cs`
- new `projects/BatHouseholdHub/Services/Finance/*.cs`
- new `projects/BatHouseholdHub/Services/Persistence/*.cs`
- possible future `projects/BatHouseholdHub/Data` folder if moving to SQLite or another local database

Finance services:

- `projects/BatHouseholdHub/Services/CashflowService.cs`
- `projects/BatHouseholdHub/Services/BillCalendarService.cs`
- `projects/BatHouseholdHub/Services/RecurringTransactionService.cs`
- new importer/reconciliation services for Rocket Money and future sources
- new audit, sanitization, brief-generation, and approval workflow services

UI:

- `projects/BatHouseholdHub/Components/Pages/Home.razor`
- `projects/BatHouseholdHub/Components/Pages/Finances.razor`
- `projects/BatHouseholdHub/Components/Pages/Bills.razor`
- `projects/BatHouseholdHub/Components/Pages/Calendar.razor`
- `projects/BatHouseholdHub/Components/Layout/NavMenu.razor`
- `projects/BatHouseholdHub/wwwroot/app.css`

Deployment and security:

- `projects/BatHouseholdHub/Program.cs`
- `projects/BatHouseholdHub/compose.yaml`
- `projects/BatHouseholdHub/Dockerfile`
- new reverse-proxy/auth documentation or compose overlay for BatServer
- future backup/restore scripts outside Git-tracked private data

Testing:

- new `projects/BatHouseholdHub.Tests/BatHouseholdHub.Tests.csproj`
- tests for import parsing, duplicate detection, bill matching, cashflow windows, recurring schedules, schema migrations, and redaction helpers
