# راه‌اندازی، بیلد و تست

## ۱. پیش‌نیازها

### اجرای برنامه (کاربر نهایی)
- Windows 10 (1809+) یا Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — نسخه Framework-Dependent
- (اختیاری) برای مراحل بعدی: Windows ADK با DISM/oscdimg، دسترسی Administrator

### توسعه و بیلد
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11 (پیشنهادی) یا لینوکس/CI (فقط cross-compile بیلد WPF؛ اجرای UI فقط روی ویندوز)

## ۲. بیلد (ویندوز)

```powershell
# بازیابی وابستگی‌ها + کامپایل
dotnet build WindowsCustomizer.sln -c Release

# اجرای تست‌ها
dotnet test tests\WindowsCustomizer.Core.Tests -c Release

# ساخت EXE تک‌فایل (Framework-Dependent — کوچک، نیاز به Desktop Runtime)
dotnet publish src\WindowsCustomizer.App -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o dist\win-x64

# یا همه با هم:
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

خروجی: `dist\win-x64\WindowsCustomizer.exe`

### نسخه Self-Contained (بدون نیاز به Runtime — حجم بیشتر)
```powershell
dotnet publish src\WindowsCustomizer.App -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o dist\win-x64-sc
```

## ۳. بیلد از لینوکس (Cross-Compile)

پروژه‌های WPF با `EnableWindowsTargeting=true` روی لینوکس هم کامپایل می‌شوند (فقط اجرا نمی‌شوند):

```bash
# نصب .NET 8 SDK (اگر نیست)
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0

# بیلد + تست + انتشار
bash scripts/build.sh
```

نکته: تست‌های Core (شامل تحلیل ISO با ISO مصنوعی) روی لینوکس هم اجرا می‌شوند.

## ۴. تست‌ها

| نوع | پوشش Phase 1 |
|-----|----------------|
| Unit | پارس هدر WIM، اعتبارسنجی نام پروژه، PathGuard |
| Integration | ساخت/بازکردن/ذخیره پروژه (.wcp)، ساختار Workspace |
| ISO | تحلیل ISO مصنوعی (بوت فایل‌ها، install.wim، هدر، خطاها) |
| Services | Recent Projects (حذف موارد قدیمی، فایل‌های حذف‌شده) |

```bash
dotnet test tests/WindowsCustomizer.Core.Tests -c Release
```

### تست دستی UI (چک‌لیست Phase 1)
1. اجرای `WindowsCustomizer.exe` — صفحه شروع نمایش داده شود.
2. **New Project** → نام + محل + انتخاب ISO → Create.
3. ساختار پوشه‌ها و فایل `.wcp` در Workspace ساخته شود.
4. **Analyze ISO** → Volume Label، Boot Files، Install Image و هدر WIM نمایش داده شود.
5. **Compute SHA-256** → در فایل `.wcp` ذخیره شود (بازکردن مجدد پروژه → مقدار بماند).
6. تم Dark/Light، پنل Logs، Recent Projects، Settings.
7. فایل ISO اصلی هیچ تغییری نکرده باشد (مقایسه SHA-256 قبل/بعد).

## ۵. ساختار Workspace پروژه

```
MyProject\
├── MyProject.wcp      # فایل پروژه (JSON)
├── Source\            # اشاره‌گر به ISO اصلی + کش تحلیل
├── Mount\             # نقاط Mount (Phase 3+)
├── Images\            # WIM/ESD کاری (Phase 2+)
├── Drivers\
├── Updates\
├── Packages\
├── Registry\
├── Scripts\
├── Config\            # Autounattend و تنظیمات Build
├── Logs\
├── Output\            # ISO نهایی
└── Temp\
```

## ۶. عیب‌یابی

| مشکل | راه‌حل |
|------|--------|
| `WindowsCustomizer.exe` اجرا نمی‌شود | نصب .NET 8 Desktop Runtime (نسخه FDD) |
| آنالیز ISO خطای "could not be opened as UDF" | فایل ISO سالم نیست یا ISO ویندوز نیست |
| "workspace already exists and is not empty" | نام پروژه دیگری انتخاب کنید |
| فضای دیسک کم | Workspace را روی درایو پرحجم بگذارید (برای Build حدود ۲-۳× حجم ISO) |

## ۷. متغیرهای محیطی

| متغیر | اثر |
|-------|-----|
| `WINCUSTOMIZER_HOME` | تغییر مسیر دیتای برنامه (settings, logs, recent) — برای حالت Portable و تست |
