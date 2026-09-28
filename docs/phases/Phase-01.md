# Phase 1 — Project + UI + ISO Detection ✅

## هدف
ساخت اسکلت حرفه‌ای برنامه (Shell، ناوبری، تم، لاگ)، جریان کامل پروژه (ساخت Workspace با ساختار استاندارد + فایل `.wcp`)، و تحلیل **فقط‌خواندنی** فایل ISO ویندوز — بدون هیچ تغییری در ISO اصلی.

## فایل‌های ایجادشده

### `src/WindowsCustomizer.Core/` (منطق دامنه — قابل تست روی هر سیستم‌عامل)
| فایل | نقش |
|------|-----|
| `Logging/AppLogger.cs` | LogEntry، IAppLogger، FileLogger (thread-safe + فایل + بافر UI) |
| `Models/ProjectModel.cs` | ProjectModel (.wcp)، SourceIsoInfo، ProjectSettings، WorkspaceLayout |
| `Models/IsoAnalysis.cs` | IsoAnalysisResult، IsoFileEntry، BootFileInfo، WimHeaderSummary، enumها |
| `Services/ProjectService.cs` | ساخت/بازکردن/ذخیره پروژه، heal ساختار Workspace، نوشتن اتمیک |
| `Services/IsoAnalyzer.cs` | تحلیل ISO با DiscUtils (UDF سپس ISO9660): بوت‌فایل‌ها، install.wim/esd، هدر WIM |
| `Services/WimHeaderReader.cs` | پارس هدر ۲۰۸ بایتی WIM/ESD (تعداد Image، فشرده‌سازی، span) |
| `Services/RecentProjectsService.cs` | لیست پروژه‌های اخیر با حذف خودکار موارد قدیمی |
| `Utilities/PathGuard.cs` | مقاومت در برابر Path Traversal، اعتبارسنجی نام پروژه |
| `Utilities/AppPaths.cs` | مسیرهای دیتای برنامه + `WINCUSTOMIZER_HOME` |
| `Utilities/HashUtil.cs` | SHA-256 جریانی با گزارش پیشرفت |

### `src/WindowsCustomizer.App/` (رابط WPF — MVVM)
| فایل | نقش |
|------|-----|
| `App.xaml(.cs)` | Composition root، DataTemplateها، مدیریت خطای سراسری |
| `app.manifest` | asInvoker، DPI، LongPath |
| `Views/MainWindow.xaml(.cs)` | شل برنامه: سایدبار گروه‌بندی‌شده (PROJECT/CUSTOMIZE/REVIEW/BUILD)، نوار وضعیت، پنل لاگ، تم تایتل‌بار |
| `Themes/Dark.xaml`, `Light.xaml`, `Styles.xaml` | پالت رنگ و استایل‌های مدرن (دکمه، کارت، ورودی، Nav، ProgressBar) |
| `ViewModels/*` | MainViewModel (ناوبری/وضعیت)، StartViewModel، NewProjectViewModel، IsoAnalysisViewModel، SettingsViewModel |
| `Views/*View.xaml` | صفحات Start، New Project، ISO Analysis، Settings |
| `Infrastructure/*` | ThemeService، DialogService (خطای ساخت‌یافته What/Why/WhatToDo)، AppSettingsStore، PrivilegeService |
| `Converters/Converters.cs` | مبدل‌های نمایش |

### `tests/WindowsCustomizer.Core.Tests/`
| فایل | پوشش |
|------|-------|
| `WimHeaderReaderTests.cs` | هدر معتبر/نامعتبر، LZMS/ESD، span |
| `ProjectServiceTests.cs` | ساخت/بازکردن/ذخیره، ساختار Workspace، heal، اعتبار نام |
| `IsoAnalyzerTests.cs` | ISO مصنوعی: بوت‌فایل‌ها، install.wim، معماری، خطا/هشدار، لغو |
| `PathGuardTests.cs` | Traversal، نام‌های رزروشده، sanitize |
| `RecentProjectsServiceTests.cs` | افزودن/حذف/فیلتر فایل‌های حذف‌شده |

## تصمیم‌های کلیدی
1. **خواندن ISO با DiscUtils (مدیریتی)** به‌جای Mount کردن — بدون دسترسی Administrator، بدون تغییر سیستم، قابل تست روی لینوکس.
2. **هدر WIM** بدون نیاز به wimgapi خوانده می‌شود (حتی داخل ESD) — اطلاعات Editionها به Phase 2 موکول شد.
3. **`.wcp` با SchemaVersion + Metadata bag** برای سازگاری آینده.
4. **نوشتن اتمیک** فایل‌های پروژه/تنظیمات (tmp + rename).
5. UI فقط از طریق ViewModel/Service با دنیای بیرون حرف می‌زند (قانون معماری).

## نحوه Build و Test
در [`docs/Setup.md`](../Setup.md) کامل توضیح داده شده:
```powershell
dotnet build WindowsCustomizer.sln -c Release
dotnet test tests\WindowsCustomizer.Core.Tests -c Release
powershell -File scripts\build.ps1
```

## نتایج تست این Phase
- همه تست‌های واحد/یکپارچه اجرا و سبز شدند (جزئیات در خروجی `dotnet test`).
- تحلیل ISO مصنوعی (ساخته‌شده با CDBuilder شامل bootmgr، etfsboot.com، bootx64.efi، bootaa64.efi، install.wim با هدر واقعی) تأیید شد.

## مشکلات احتمالی و راه‌حل
| موضوع | برخورد |
|-------|--------|
| ISOهای UDF خالص/غیراستاندارد | تلاش دوم با ISO9660/Joliet + پیام خطای ساخت‌یافته |
| فایل‌های ISO قفل‌شده توسط نرم‌افزار دیگر | FileShare.ReadWrite هنگام باز کردن |
| نسخه‌های آینده ویندوز | عدم hardcode کردن نسخه؛ Phase 2 از XML داخل WIM می‌خواند |
| اجرای بدون Administrator | فقط badge نمایش داده می‌شود؛ عملیات سرویس‌دهی (Phase 3+) جداگانه درخواست elevation می‌کند |
| حجم فایل‌های بزرگ ISO | خواندن جریانی + بدون هش اجباری (هش با دکمه/بعد از تحلیل) |

## Phase بعدی (پس از تأیید شما)
**Phase 2 — ISO Extraction + WIM/ESD Metadata:** استخراج فایل‌ها به Workspace، پارس کامل متادیتای WIM (لیست دقیق Editionها، Build، Architecture، حجم هر Image)، Export/تبدیل ESD→WIM و صفحه انتخاب Edition (Single/Multiple).
