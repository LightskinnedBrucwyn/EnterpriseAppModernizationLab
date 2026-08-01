# BatHouseholdHub Finance Automation Roadmap

Date: 2026-08-01

Goal: prepare BatHouseholdHub to become a secure, mostly autonomous household finance command center hosted on BatServer. This roadmap is intentionally phased so security and data boundaries come before automation.

## Phase 0. Security and Repository Hygiene

Objective: make the current app safe enough to keep private finance data on BatServer while development continues in GitHub.

Work:

- Confirm `.gitignore` covers all runtime finance data, uploads, database files, exports, screenshots, statements, paystubs, and deploy archives.
- Remove or quarantine household-specific seed facts from source code in favor of private BatServer setup data.
- Add authentication and authorization before exposing finance pages beyond a trusted local session.
- Protect `/uploads/{id}` behind authorization and consider short-lived download links.
- Document the BatServer data directory, backup scope, restore process, and secret-source expectations.
- Add security headers/reverse-proxy guidance for HTTPS on the tailnet.
- Add a private-data redaction checklist for PRs and AI prompts.
- Add a first audit log design for imports, edits, deletions, generated briefs, and approval actions.

Exit criteria:

- No real financial records are required in source code.
- Private runtime data is clearly excluded from GitHub.
- Uploads and finance pages have an authentication plan before more automation lands.
- Backup and restore expectations are documented.

## Phase 1. Data Model and Storage

Objective: move from a prototype household JSON blob toward explicit finance entities and versioned persistence.

Work:

- Split finance entities out of `HouseholdData.cs` into focused model files.
- Add proposed entities: `FinancialAccount`, `CreditCardAccount`, `LoanAccount`, `RecurringBill`, `Transaction`, `CreditScoreSnapshot`, `CreditReportItem`, `CollectionAccount`, `Paycheck`, `FinancialGoal`, `ImportBatch`, and `FinancialBrief`.
- Add schema versioning and migration helpers for existing `household.json`.
- Decide whether the first durable store remains JSON or moves to local SQLite on BatServer.
- Add repository/service boundaries so UI components do not mutate the in-memory data graph directly.
- Add validation for money amounts, dates, due days, account display labels, and import metadata.
- Add synthetic fixtures and unit tests for migrations, validation, and cashflow calculations.

Exit criteria:

- Finance entities have stable ids and clear ownership.
- Existing household data can be migrated without manual JSON edits.
- Tests cover core cashflow, recurring schedule, import, and migration rules using fake data.

## Phase 2. Rocket Money CSV Importer

Objective: make imports repeatable, reviewable, and reversible.

Work:

- Create `ImportBatch` records for every CSV upload.
- Store original file hash and sanitized metadata, not raw CSV content in GitHub.
- Add dry-run import preview before applying changes.
- Validate required columns and produce row-level errors.
- Map imported accounts to `FinancialAccount`.
- Improve duplicate detection across date, account, amount, normalized merchant, and source hash.
- Separate expenses, income/refunds, transfers, card payments, and savings moves with explicit rules.
- Add reconciliation review states: new, auto-matched, needs category, possible duplicate, ignored transfer, linked to bill.
- Add rollback for a single import batch.

Exit criteria:

- A Rocket Money CSV can be imported, reviewed, applied, audited, and rolled back.
- No raw CSV file is committed or required for tests.
- Synthetic CSV fixtures cover happy path, malformed rows, duplicates, transfers, and bill matches.

## Phase 3. Credit Utilization and Debt Priority Engine

Objective: turn account and debt data into explainable payoff guidance.

Work:

- Model credit limits, current balances, statement balances, due dates, minimum payments, and APRs.
- Calculate per-card and aggregate utilization.
- Flag cards above target utilization thresholds.
- Compare debt payoff strategies: minimums, avalanche, snowball, utilization-first, and hardship-preserving.
- Include collection accounts and validation/settlement status without treating them like normal revolving debt.
- Produce recommendations with assumptions and confidence.
- Keep all recommendations advisory until approved by the household.

Exit criteria:

- The app can answer "what should we pay first and why?" from normalized data.
- Recommendations are deterministic, tested, and explainable.
- No creditor action is taken automatically.

## Phase 4. Payday Briefing

Objective: generate a practical plan whenever a paycheck lands or is expected.

Work:

- Promote current income events/paycheck logging into `Paycheck`.
- Link upcoming bills, delayed bills, reserved funds, buffer needs, debt priorities, and goals to a payday window.
- Generate `FinancialBrief` records for payday, weekly, and month-end reviews.
- Include approval-required actions separately from suggested actions.
- Add local-only brief generation first, then cloud AI summaries only through a sanitized context builder.
- Add audit records for generated briefs and accepted/rejected recommendations.

Exit criteria:

- On payday, the app can show what to pay, what to reserve, what to delay, and what remains.
- Briefs are saved with enough metadata to understand what data snapshot they came from.
- Cloud AI receives only minimized, redacted context when explicitly enabled.

## Phase 5. Dashboard Redesign

Objective: redesign the finance UI around repeated household operations instead of separate prototype panels.

Work:

- Create a command-center dashboard with bills due, next paycheck, cash available, debt priority, utilization, import status, and approvals needed.
- Add focused views for accounts, cards, loans, collections, goals, imports, and briefs.
- Make review queues first-class: unmatched transactions, missing account data, unusual spending, bill changes, pending approvals.
- Make privacy state visible: local-only data, cloud-assisted summaries, last backup, last import, audit log.
- Keep current pages working during migration or redirect them gradually to redesigned finance views.

Exit criteria:

- The first finance screen answers "what needs attention today?"
- Users can inspect and approve recommendations without digging through raw transaction lists.
- Mobile layout supports quick household checks from the tailnet.

## Phase 6. Local/OpenAI Hybrid Agent Integration

Objective: add agent assistance without giving agents unchecked access to private financial data.

Work:

- Define an agent boundary service that exposes read-only summaries by default.
- Add a redaction/minimization layer for cloud model calls.
- Prefer local BatServer/Ollama analysis for private raw data.
- Use OpenAI only for tasks that benefit from stronger reasoning or writing after explicit configuration and approval.
- Log provider, model, prompt purpose, redaction status, and output id.
- Add approval gates before any suggested mutation becomes a stored change.
- Add tests for prompt-context builders to ensure sensitive fields are excluded.

Exit criteria:

- Agents can generate briefs and recommendations from controlled context.
- Users can see what data was used and whether it left BatServer.
- Agents cannot send communications, modify records, or export data without approval.

## Phase 7. Approval-Gated Letters and Creditor Workflows

Objective: help draft and track financial communications while keeping humans in control.

Work:

- Add workflows for goodwill letters, validation letters, settlement notes, payment-plan requests, and dispute drafts.
- Link drafts to `CreditReportItem`, `CollectionAccount`, or `LoanAccount` records.
- Store generated drafts as private BatServer documents.
- Require explicit approval before export, email, print, or send.
- Track communication history, deadlines, follow-up dates, and outcomes.
- Add templates using synthetic examples in GitHub and private generated letters on BatServer.

Exit criteria:

- The app can draft creditor communications from private records.
- Every draft has an approval state and audit trail.
- No letter or creditor workflow can be sent automatically.

## Cross-Phase Principles

- GitHub holds code, docs, and synthetic examples only.
- BatServer holds private data, uploads, imports, backups, approvals, and audit logs.
- Automation recommends before it acts.
- Every import and agent-generated recommendation is reviewable.
- Every destructive or external action requires approval.
- Tests use fake data only.
