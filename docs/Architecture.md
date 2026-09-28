# معماری Windows Customizer Studio

## ۱. انتخاب تکنولوژی و دلیل آن

### گزینه‌های بررسی‌شده

| گزینه | مزایا | معایب | نتیجه |
|------|-------|-------|-------|
| **C# / .NET 8 / WPF** | یکپارچگی عالی با ویندوز (DISM، Registry، UAC، Shell)، پایداری بالا در کار با فایل‌های بزرگ، اکوسیستم بالغ، MVVM رسمی، انتشار تک‌فایل | تعداد فایل متوسط | ✅ **انتخاب شد** |
| C# / WinUI 3 | مدرن‌تر | پیچیدگی بسته‌بندی (MSIX)، بلوغ کمتر برای ابزار سیستمی، مشکلات Deployment | رد شد |
| Go + GUI (Fyne/Walk) | باینری کوچک، فایل کم | ضعف در APIهای ویندوز، عدم وجود الگوی آماده DISM/WIM، GUI غیربومی، هزینه توسعه بیشتر در درازمدت | رد شد |
| Rust + egui/Tauri | کارایی | پیچیدگی زیاد، کتابخانه‌های نابالغ Windows Imaging، سرعت توسعه پایین | رد شد |
| Python + PySide | سرعت نمونه‌سازی | نیاز به Runtime، مصرف RAM، حس غیرحرفه‌ای، مشکل بسته‌بندی | رد شد |
| C++ / Win32 | کنترل کامل | هزینه توسعه بسیار بالا، مدیریت حافظه، تعداد فایل/کد زیاد | رد شد |

### انتخاب نهایی
**C# + .NET 8 (LTS) + WPF + MVVM (CommunityToolkit.Mvvm)**

نکته «تعداد کم فایل‌ها»: ساختار Modular ولی فشرده نگه داشته شده (کلاس‌های مرتبط در یک فایل)، وابستگی‌های NuGet حداقلی (`DiscUtils` برای خواندن ISO، `CommunityToolkit.Mvvm` برای MVVM، `xunit` برای تست). خروجی نهایی یک `WindowsCustomizer.exe` تک‌فایل است.

WPF به WinUI 3 ترجیح داده شد چون:
1. استاندارد ابزارهای سیستمی ویندوز است (Visual Studio Installer, Rufus, DISM GUIها).
2. برای بیلد روی CI لینوکس هم قابل‌کامپایل است (`EnableWindowsTargeting`).
3. بدون MSIX مستقر می‌شود؛ برای ابزاری که Administrator می‌خواهد ساده‌تر است.

## ۲. معماری لایه‌ای

```
┌────────────────────────────────────────────────────────┐
│  UI (WPF Views + ViewModels — فقط MVVM)                 │
└──────────────────────────┬─────────────────────────────┘
                           ↓
┌────────────────────────────────────────────────────────┐
│  Application Services (StatusReporter, Dialogs, Theme)  │
└──────────────────────────┬─────────────────────────────┘
                           ↓
┌────────────────────────────────────────────────────────┐
│  Core / Domain (Models, ProjectService, IsoAnalyzer,    │
│                 PresetEngine (future), Validation…)      │
└──────────────────────────┬─────────────────────────────┘
                           ↓
┌────────────────────────────────────────────────────────┐
│  Engines (Phase 3+): DismEngine, IsoEngine,             │
│  RegistryEngine, DriverEngine, UpdateEngine, …          │
│  + Command Execution Layer (safe args, timeout, cancel)  │
└──────────────────────────┬─────────────────────────────┘
                           ↓
┌────────────────────────────────────────────────────────┐
│  Windows APIs / DISM / File System / Registry            │
└────────────────────────────────────────────────────────┘
```

**قانون**: UI هرگز مستقیماً با DISM یا File System کار نمی‌کند. هر عملیات از مسیر ViewModel → Core Service/Engine → Command Layer انجام می‌شود.

## ۳. ماژول‌ها

| ماژول | مسئولیت | وضعیت |
|-------|---------|-------|
| `Core/Logging` | لاگ ساخت‌یافته (Timestamp, Operation, Status, Command, ExitCode) | ✅ Phase 1 |
| `Core/Models` | ProjectModel (.wcp), IsoAnalysis, WorkspaceLayout | ✅ Phase 1 |
| `Core/Services/ProjectService` | ساخت/بازکردن/ذخیره Workspace و پروژه | ✅ Phase 1 |
| `Core/Services/IsoAnalyzer` | تحلیل فقط‌خواندنی ISO (UDF/ISO9660) | ✅ Phase 1 |
| `Core/Services/WimHeaderReader` | پارس هدر WIM/ESD | ✅ Phase 1 |
| `Core/Services/RecentProjects` | لیست پروژه‌های اخیر | ✅ Phase 1 |
| `Core/Utilities/PathGuard` | مقاومت در برابر Path Traversal | ✅ Phase 1 |
| `Core/Utilities/HashUtil` | SHA-256 جریانی | ✅ Phase 1 |
| `App/Infrastructure` | Theme, Dialogs (خطای ساخت‌یافته), Privilege | ✅ Phase 1 |
| `ImageEngine/DismEngine` | Mount/Commit/Export/Inject | Phase 3-6 |
| `IsoEngine` | ساخت ISO بوت‌پذیر (BIOS+UEFI) | Phase 12 |
| `RegistryEngine` | Hive آفلاین + Preset | Phase 8 |
| `PresetEngine` | Preset/Template + Marketplace-ready | Phase 10 |
| `ValidationEngine` | بررسی پروژه/ISO خروجی | Phase 11-13 |
| `ProjectEngine` | Build pipeline مرکزی | Phase 12 |

## ۴. جریان داده‌ها

- **`.wcp`**: فایل JSON (SchemaVersion دار) — شامل مسیر ISO اصلی، SHA-256، تنظیمات و در آینده تمام انتخاب‌های شخصی‌سازی. نوشتن اتمیک (tmp + rename).
- **Workspace**: ۱۲ پوشه استاندارد (Source, Mount, Images, Drivers, Updates, Packages, Registry, Scripts, Config, Logs, Output, Temp). هر Engine فقط داخل Workspace می‌نویسد (`PathGuard.EnsureInsideWorkspace`).
- **لاگ‌ها**: هم فایل (`%APPDATA%\WindowsCustomizer\logs`) و هم پنل زنده در UI.
- **Hash**: همه فایل‌های ورودی خارجی (ISO, Driver, Update, Script, Installer) ثبت و در گزارش Build می‌آیند.

## ۵. امنیت (Security by Design)

1. **هرگز ISO اصلی تغییر نمی‌کند** — فقط‌خواندنی باز می‌شود.
2. **Path Traversal**: تمام مسیرهای نوشتنی از PathGuard عبور می‌کنند.
3. **Command Injection**: لایه Command Execution با Argument Array (بدون string concatenation) — Phase 12.
4. **Risk Level**: هر تغییر Safe/LOW/Medium/High/Critical برچسب می‌خورد و تغییر Critical تأیید صریح می‌خواهد — Phase 11.
5. **Script/Driver/Update**: Hash + هشدار قبل از اعمال — Phase 5+.
6. **Backup/Rollback**: Snapshot قبل از عملیات برگشت‌ناپذیر — Phase 11.

## ۶. محدودیت‌های شناخته‌شده Windows/DISM

| محدودیت | برخورد ما |
|---------|-----------|
| DISM فقط با Administrator + فقط روی ویندوز | تشخیص از Phase 1، ارتقای UAC درخواستی، جداسازی Engine تا اجرای واقعی روی ویندوز |
| Mount به NTFS و فضای خالی ~۲× حجم Image نیاز دارد | تخمین فضای دیسک قبل از Build (Phase 12)، بررسی در Phase 1 |
| `install.esd` فشرده Solid است و Mount نمی‌شود | تبدیل خودکار ESD→WIM در Build (Phase 2/12) با هشدار کاربر |
| WIM > 4GB روی USB فرمت FAT32 | Split به SWM (قابلیت Build) |
| ساخت ISO بوت‌پذیر UDF+El Torito | استفاده از `oscdimg` (ADK) در صورت وجود + Builder مدیریتی به‌عنوان fallback (Phase 12) |
| حذف AppX سیستمی می‌تواند Setup/Boot را بشکند | Dependency Engine + لیست Critical + هشدار (Phase 4/11) |
| تفاوت Windows 10/11 در Policy/Feature | مدل Capability انتزاعی (Phase 44 نقشه راه) |
| Hive آفلاین رجیستری | `reg.exe load/unload` یا Offreg API در لایه RegistryEngine (Phase 8) |

## ۷. قابلیت توسعه

- هر Engine پشت یک Interface است؛ UI به Implementation وابسته نیست.
- `ProjectModel.Metadata` کیسه‌ی کلید/مقدار برای نگهداری داده نسخه‌های آینده.
- Presetها JSON + Version + Signature آماده برای Repository آینده (Marketplace).
- Multi-Version: مدل‌های Capability به‌جای Hardcode کردن نسخه ویندوز.
