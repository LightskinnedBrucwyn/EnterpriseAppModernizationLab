# BatHouseholdHub Private Data Boundary

Date: 2026-08-01

This boundary is the rulebook for turning BatHouseholdHub into a household finance command center without leaking private financial data into the public GitHub repository, logs, prompts, screenshots, or pull requests.

## Visibility and Runtime Boundary

Repository visibility: public.

Runtime deployment: private, LAN/Tailscale only.

The repository must be treated as public even when the app itself is designed for private household use. BatHouseholdHub source control can contain implementation plans, synthetic examples, and documentation; BatServer is the private boundary for real household finance data, uploaded files, imports, approvals, backups, and audit logs.

## Belongs in Public GitHub

The public GitHub repository may contain:

- Application source code.
- Documentation.
- Synthetic examples and schema sketches.
- Empty folders represented by placeholder files when needed.
- Sanitized test fixtures with fake names, fake accounts, fake merchants, fake institutions, fake dates, and fake amounts.
- Configuration templates that show variable names without values.
- Docker, build, and deployment instructions that do not include private host credentials, private network details beyond intentional LAN/Tailscale guidance, or secrets.
- Migration code and test cases using synthetic data.
- Repository hygiene scripts that scan staged or tracked files for prohibited finance files and likely secrets.

Synthetic examples must be obvious fakes. Good examples: `Example Checking`, `Demo Credit Union`, `Coffee Shop 123`, `1111`, `$42.00`, `2026-01-15`.

## Belongs Only on Private BatServer

The following must stay only on BatServer or other approved private backup storage:

- `household.json` and any replacement database.
- `App_Data`, `data`, uploads, data-protection keys, corrupt JSON backups, and local restore files.
- Rocket Money CSV exports.
- Bank, card, loan, payroll, credit-report, and collection-account files.
- Statements, paystubs, tax forms, receipts, PDFs, Excel workbooks, screenshots, and credit-report captures.
- Real account names, balances, limits, due dates, creditors, collectors, pay dates, paycheck amounts, transaction histories, statement balances, and score snapshots.
- AI prompts or responses that include private financial facts.
- Export archives and deploy tarballs that include runtime data.

These file types must not be committed when they contain real data: `.csv`, `.pdf`, `.xlsx`, `.xls`, `.db`, `.sqlite`, `.sqlite3`, `.zip`, `.tar`, `.gz`, screenshots, bank statements, credit reports, paystubs, and document uploads.

## Required Sanitization Rules

Before any data leaves BatServer or enters a branch, issue, PR, prompt, log excerpt, screenshot, or sample fixture:

- Replace real people with `Person A`, `Person B`, or fake household names.
- Replace institutions with fake names such as `Demo Bank`.
- Replace account numbers with fake last-four values like `1111`.
- Replace balances, limits, payments, paychecks, debts, and scores with synthetic values.
- Replace exact transaction dates with synthetic dates unless the date is part of public product behavior.
- Replace merchant names when they reveal sensitive behavior.
- Remove addresses, phone numbers, email addresses, employer identifiers, creditor account numbers, collector reference numbers, and confirmation numbers.
- Remove document metadata from PDFs/images before sharing.
- Avoid screenshots of production pages unless all visible financial values and names are redacted.
- Never paste raw CSV rows, statement lines, paystub tables, or credit-report entries into GitHub.

Allowed synthetic CSV fixture style:

```csv
Date,Name,Category,Amount,Account Name,Account Number,Institution Name
2026-01-15,Example Utility,Bills & Utilities,42.00,Example Checking,1111,Demo Bank
2026-01-20,Example Paycheck,Income,-1200.00,Example Checking,1111,Demo Bank
```

See `docs/finance/synthetic-fixtures.md` for fixture naming and example rules.

## Secret Handling

Secrets must be supplied through BatServer environment variables, local secret stores, or future secret-management tooling. They must not be committed to GitHub or written into docs with real values.

Current and future secrets include:

- `ANTHROPIC_API_KEY`
- future `OPENAI_API_KEY`
- bank aggregator credentials or tokens
- SMTP/email credentials
- Tailscale auth keys
- reverse-proxy credentials
- database passwords
- backup encryption keys

Required practices:

- Keep only placeholder names in `compose.yaml`, `.env.example`, or docs.
- Keep real `.env` files untracked.
- Run `scripts/check-repository-hygiene.sh --staged` before committing finance-related work.
- Rotate a secret immediately if it is pasted into GitHub, logs, chat, or a PR.
- Do not include secrets in AI prompts.
- Log only whether a provider is configured, never the value.

## Backup Expectations

BatServer backups must protect private finance data with the same care as account statements.

Minimum expectations:

- Back up the host-side BatHouseholdHub data directory, not the container filesystem.
- Encrypt backups before copying them off BatServer.
- Keep at least one local backup and one off-device backup.
- Test restore on a non-production path before trusting the backup process.
- Include `household.json` or the future database, uploads, and data-protection keys together so encrypted app artifacts remain usable.
- Keep backup retention short enough to limit exposure but long enough to recover from accidental deletion or corrupt writes.
- Do not commit backup archives or restore snapshots.

Future implementation should add a documented `backup`, `restore-dry-run`, and `restore` process with explicit paths and redaction safeguards.

## Audit Logging Expectations

Autonomous finance workflows require audit trails before they are allowed to recommend or draft actions.

Audit logs should capture:

- Who or what initiated the action: user, import, local agent, OpenAI-backed agent, background job.
- What changed: entity type, entity id, action, previous summary, new summary.
- When it happened.
- Whether approval was required.
- Whether approval was granted or denied.
- Import batch ids for CSV-driven changes.
- Model/provider name for AI-generated briefs or drafts.
- Redaction status for any data sent outside BatServer.

Audit logs should not capture:

- Full account numbers.
- Raw bank CSV rows.
- Full paystub or statement text.
- Complete AI prompts containing private data.
- Secrets or tokens.

Audit storage should be append-oriented and tamper-evident enough for household review. A practical first step is an append-only JSONL or SQLite audit table on BatServer, with synthetic unit tests in GitHub.

## Approval Boundary

The system may calculate, summarize, draft, and recommend without approval.

The system must require explicit approval before:

- Sending emails, letters, or creditor communications.
- Marking a bill paid based only on imported or agent-inferred data.
- Deleting financial records or uploaded documents.
- Modifying balances, limits, due dates, or debt priorities from imported data.
- Sending private financial context to a cloud AI provider.
- Creating exports that contain private data.

Approval records belong on BatServer, not in GitHub.
