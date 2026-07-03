#!/usr/bin/env bash
# =============================================
# Manfoods McDonald's — DB Update Script
# الاستخدام: bash scripts/db-update.sh
# =============================================
set -e

if [ -z "$NEON_DATABASE_URL" ]; then
  echo "❌ NEON_DATABASE_URL غير موجود. تأكد من إعداد الـ Configuration."
  exit 1
fi

echo "⏳ جاري تطبيق التحديثات على قاعدة البيانات..."
psql "$NEON_DATABASE_URL" -f scripts/migrate.sql
echo "✅ تم تحديث قاعدة البيانات بنجاح!"
