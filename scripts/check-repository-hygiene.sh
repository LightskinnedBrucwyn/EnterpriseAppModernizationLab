#!/usr/bin/env bash
set -euo pipefail

mode="${1:---staged}"

case "$mode" in
  --staged)
    files="$(git diff --cached --name-only --diff-filter=ACMR)"
    ;;
  --all)
    files="$(git ls-files)"
    ;;
  *)
    echo "Usage: $0 [--staged|--all]" >&2
    exit 2
    ;;
esac

if [ -z "$files" ]; then
  echo "Repository hygiene check: no files to scan."
  exit 0
fi

failed=0

fail() {
  failed=1
  printf 'Repository hygiene check failed: %s\n' "$1" >&2
}

is_env_example() {
  case "$1" in
    *.env.example|.env.example) return 0 ;;
    *) return 1 ;;
  esac
}

while IFS= read -r path; do
  [ -z "$path" ] && continue
  lower="$(printf '%s' "$path" | tr '[:upper:]' '[:lower:]')"

  case "$lower" in
    *.csv|*.tsv|*.ofx|*.qfx|*.qbo|*.pdf|*.xls|*.xlsx|*.xlsm|*.db|*.sqlite|*.sqlite3|*.bak|*.backup|*.dump|*.restore|*.zip|*.tar|*.tgz|*.gz)
      fail "$path is a prohibited finance/export/database/archive file type."
      ;;
  esac

  case "$lower" in
    *statement*|*paystub*|*credit-report*|*credit_report*|*rocket-money*|*rocket_money*|*bank-export*|*bank_export*|*private-brief*|*private_brief*)
      fail "$path looks like private household finance data."
      ;;
  esac

  case "$lower" in
    *screenshot*.png|*screenshot*.jpg|*screenshot*.jpeg|*screen-shot*.png|*screen-shot*.jpg|*statement*.png|*paystub*.png|*credit-report*.png)
      fail "$path looks like a private screenshot or capture."
      ;;
  esac

  case "$lower" in
    .env|*.env|*.env.*)
      if ! is_env_example "$lower"; then
        fail "$path is an environment file; commit .env.example only."
      fi
      ;;
    *.pem|*.key|*.pfx|*.p12|*id_rsa*|*id_ed25519*|*secret*)
      fail "$path looks like a credential or secret file."
      ;;
  esac

  if ! git cat-file -e ":$path" 2>/dev/null && [ "$mode" = "--staged" ]; then
    continue
  fi

  if [ "$mode" = "--staged" ]; then
    content_cmd=(git show ":$path")
  else
    content_cmd=(cat "$path")
  fi

  if "${content_cmd[@]}" 2>/dev/null | LC_ALL=C grep -E -- '-----BEGIN (RSA |OPENSSH |EC |DSA )?PRIVATE KEY-----|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9_-]{20,}|ghp_[A-Za-z0-9_]{30,}|xox[baprs]-[A-Za-z0-9-]{20,}' >/dev/null; then
    fail "$path contains a likely secret token or private key."
  fi

  # High-signal populated secret assignments in env, JSON, YAML, Razor, Markdown, or source comments.
  if "${content_cmd[@]}" 2>/dev/null | LC_ALL=C grep -Ei -- '(^|[^A-Za-z0-9_{])(ANTHROPIC_API_KEY|OPENAI_API_KEY|PLAID_SECRET|TAILSCALE_AUTHKEY|DATABASE_PASSWORD|BACKUP_ENCRYPTION_KEY)[[:space:]]*[:=][[:space:]]*["'\'']?[^[:space:]#<"'\''{$]+' >/dev/null; then
    fail "$path contains a likely populated secret environment variable."
  fi

  if "${content_cmd[@]}" 2>/dev/null | LC_ALL=C grep -Ei -- '(api[_-]?key|client[_-]?secret|password|passwd|access[_-]?token|refresh[_-]?token|auth[_-]?token|authkey|connectionstring|database_url)[[:space:]"'\'']*[:=][[:space:]]*["'\'']?[A-Za-z0-9_./+=:@-]{12,}' >/dev/null; then
    fail "$path contains a likely populated credential setting."
  fi

  if "${content_cmd[@]}" 2>/dev/null | LC_ALL=C grep -E -- 'https?://[^[:space:]/:@]+:[^[:space:]@]+@' >/dev/null; then
    fail "$path contains a URL with embedded credentials."
  fi

  if "${content_cmd[@]}" 2>/dev/null | LC_ALL=C grep -E -- '(^|[^0-9])(10\.[0-9]{1,3}\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[0-1])\.)' >/dev/null; then
    fail "$path contains a private LAN IP address."
  fi

  if "${content_cmd[@]}" 2>/dev/null | LC_ALL=C grep -E -- 'tskey-[A-Za-z0-9_-]{20,}|eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}' >/dev/null; then
    fail "$path contains a likely auth key or JWT."
  fi

  if "${content_cmd[@]}" 2>/dev/null | LC_ALL=C grep -E -- '[0-9]{3}-[0-9]{2}-[0-9]{4}|(^|[^0-9])([0-9][ -]?){13,19}([^0-9]|$)' >/dev/null; then
    fail "$path contains a likely SSN, card, or account-number-shaped value."
  fi
done <<EOF
$files
EOF

if [ "$failed" -ne 0 ]; then
  exit 1
fi

echo "Repository hygiene check passed."
