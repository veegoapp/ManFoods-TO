#!/usr/bin/env bash
# =============================================
# Manfoods McDonald's — DB Update Script
# الاستخدام: bash scripts/db-update.sh
# =============================================
set -e

echo "⏳ جاري تطبيق التحديثات على قاعدة البيانات..."

if [ -n "$NEON_DATABASE_URL" ]; then
  psql "$NEON_DATABASE_URL" -f scripts/migrate.sql
elif [ -n "$DATABASE_URL" ]; then
  psql "$DATABASE_URL" -f scripts/migrate.sql
elif [ -n "$PGHOST" ] && [ -n "$PGUSER" ]; then
  PGPASSWORD="$PGPASSWORD" psql \
    -h "$PGHOST" \
    -p "${PGPORT:-5432}" \
    -U "$PGUSER" \
    -d "${PGDATABASE:-postgres}" \
    -f scripts/migrate.sql
else
  echo "❌ لا توجد بيانات اتصال بقاعدة البيانات."
  exit 1
fi

echo "✅ تم تحديث قاعدة البيانات بنجاح!"
