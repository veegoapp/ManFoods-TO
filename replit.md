# Workforce Intelligence — Crew Level

تطبيق تحليلات الموارد البشرية لمنظومة مطاعم ماكدونالدز (Manfoods).

## تشغيل التطبيق

- **الأمر**: `bash setup.sh` (مُضبوط كـ workflow تلقائي)
- **المنفذ**: 5000
- **صفحة الدخول**: `/login` (للمستخدمين)، `/adminlogin` (للمشرف)

## المتطلبات

| المتغير | الوصف | الحالة |
|---------|-------|--------|
| `PGHOST` / `PGUSER` / `PGPASSWORD` / `PGDATABASE` | قاعدة بيانات PostgreSQL | ✅ مُدار تلقائياً من Replit |
| `Gemini_API_Key` | مفتاح Google Gemini للمساعد الذكي | ✅ مُضبوط في Secrets |
| `SESSION_SECRET` | مفتاح تشفير الجلسات | ✅ مُضبوط في Secrets |

## التقنيات

- **إطار العمل**: ASP.NET Core 9 MVC (C#)
- **قاعدة البيانات**: PostgreSQL عبر Entity Framework Core + Npgsql
- **واجهة المستخدم**: Razor Views + Bootstrap 5
- **الذكاء الاصطناعي**: Google Gemini API (`gemini-2.0-flash-lite`)
- **اللغات**: عربي / إنجليزي (Localization بـ .resx)

## هيكل المشروع

| المجلد/الملف | الوصف |
|-------------|-------|
| `Areas/Home/` | واجهة المستخدم (تسجيل الدخول، الداشبورد، التحليلات) |
| `Areas/Admin/` | لوحة المشرف (المستخدمون، المتاجر، الرفع، الإعدادات) |
| `Controllers/` | الـ API controllers |
| `Services/` | منطق الأعمال (DI) |
| `Models/` | نماذج البيانات والـ ViewModels |
| `Data/AppDbContext.cs` | سياق قاعدة البيانات |
| `Program.cs` | نقطة الدخول وإعداد الخدمات |
| `scripts/migrate.sql` | مخطط قاعدة البيانات |
| `scripts/db-update.sh` | تطبيق المخطط على قاعدة البيانات |

## إعداد قاعدة البيانات

يستخدم `EnsureCreated()` عند بدء التشغيل لإنشاء الجداول تلقائياً، بالإضافة إلى `scripts/migrate.sql` لتطبيق التحديثات.

## تفضيلات المستخدم

- الردود دايماً بالعربي البسيط المفهوم، من غير مصطلحات تقنية إلا لما تكون ضرورية
- Pure C# ASP.NET Core MVC — no Node.js, no React, no frontend frameworks.
