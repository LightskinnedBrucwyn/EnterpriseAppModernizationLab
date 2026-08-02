# Synthetic Fixture Guidance

Date: 2026-08-01

BatHouseholdHub fixtures, examples, tests, screenshots, and documentation must be synthetic-only because the GitHub repository is public.

## Allowed

- Fake people: `Person A`, `Person B`, `Example Household`.
- Fake institutions: `Demo Bank`, `Example Credit Union`, `Sample Payroll`.
- Fake accounts: `Example Checking`, `Example Credit Card`, account last four `1111`.
- Fake merchants: `Example Utility`, `Coffee Shop 123`, `Demo Marketplace`.
- Fake values: small round or obviously sample amounts such as `$42.00`, `$75.00`, `$600.00`.
- Fake dates: deterministic sample dates such as `2026-01-15`.
- Placeholder URLs and hosts: `https://example.com`, `<tailnet-hostname>`, `<batserver-app-root>`.

## Prohibited

- Real household member names, employers, landlords, creditors, collectors, account names, account numbers, balances, limits, pay dates, paycheck amounts, scores, and transaction histories.
- Rocket Money exports, bank CSV files, statements, paystubs, credit reports, PDFs, Excel workbooks, screenshots, database files, backups, and runtime JSON.
- Private LAN hostnames, private IP addresses, usernames, domains, credentials, tokens, API keys, or data-protection keys.
- AI prompts or responses that include private finance facts.
- Runtime JSON, migration backups, restore snapshots, or backup excerpts, even when the file was created automatically.

## Fixture Pattern

Use tiny files or inline examples that prove behavior without resembling production records:

```csv
Date,Name,Category,Amount,Account Name,Account Number,Institution Name
2026-01-15,Example Utility,Bills & Utilities,42.00,Example Checking,1111,Demo Bank
2026-01-20,Example Paycheck,Income,-1200.00,Example Checking,1111,Demo Bank
```

Do not commit generated imports or outputs. If a test needs a fixture, keep it short, hand-authored, and visibly fake.
