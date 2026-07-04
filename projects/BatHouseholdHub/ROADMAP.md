# Household Hub — Roadmap

Where the app is headed and what's already landed. The goal of this pass: take a
good-looking-but-passive app and make it **live, interactive, accurate, and private**.

Status key: ✅ done & deployed · 🔜 next · 💡 idea / later

---

## ✅ Phase 1 — One source of truth for schedule math
Due-date logic was copy-pasted across four files and had drifted. Consolidated into
`Services/BillScheduleService.cs`.
- Weekly/biweekly bills now count every occurrence (SNAP Finance hits ~2×/month everywhere).
- Unpaid past-due bills show a red **Overdue** badge and stay in the cashflow total instead of
  silently sliding to next month.
- A paid biweekly bill no longer reads "Paid" for the whole month.
- Dropped `CashflowWindow.Next30Days` (it was "This month" with extra steps).
- Calendar totals reconcile with the pills on the grid.

## ✅ Phase 2 — Live money data (Plaid balances + smarter bills)
- Every sync pulls real **account balances** (`AccountsBalanceGetAsync`), which feed Available
  Funds automatically; manual fields become cash-only.
- Per-account owner assignment + "count in funds" toggle (off for credit/loans).
- **Sync now** button on `/banking`; readable account names on transactions.
- Bills **learn their real amount** from matched transactions and flag price changes
  ("▲ went up $12").

## ✅ Phase 3 — Interactive calendar
- Click a day → detail panel with everything due that day as working bill rows
  (mark paid / status / delete), income expected, and a prefilled quick-add.
- Income shown as green pills; hover/selected states; non-monthly bills rendered.

## ✅ Phase 4 — Groceries reliability + smarts
- Fixed the checkbox glitch (missing `@key` on a re-sorting list).
- Unified section-guessing across all add paths; new **Meat** section.
- "Usually buy" one-tap re-add chips; "Clear checked" post-shopping cleanup.

## ✅ Phase 5 — Profiles & privacy
- Per-person **PIN** (PBKDF2 hash + salt in `household.json`); unlock is per browser session.
- While locked, a person's transaction **details** are hidden from the other's view (history,
  search, filters, review queue) and shown only as an aggregate so household totals stay honest.
- Banking management controls hidden for a locked person's items.

## ✅ Hardening — HTTPS-only access
- App port bound to host loopback; only the HTTPS tailnet URL
  (`https://letsgetrichbabe.<tailnet>.ts.net`) is reachable. The plain `http://batserver:5188`
  endpoint is dead.

## 🔜 Phase 6 — Production Plaid (link the real bank)
Checkpoint, not code. Blocked on:
1. Set profile PINs first (built — just needs each person to set theirs).
2. Verify the Plaid Link URL each time (legit surfaces: `cdn.plaid.com`, `secure.plaid.com`,
   other `*.plaid.com`, or the bank's genuine OAuth domain).
3. On batserver: `PLAID_SECRET`=production secret, `PLAID_ENV=production`, redeploy.

---

## 💡 Later ideas
- "What groceries actually cost" line from bank transactions (category Groceries, last 30 days).
- Meal-plan → grocery-list spend estimate.
- Overdue push-notification escalation.
