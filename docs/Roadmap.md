# نقشه راه توسعه (Phase-Based)

اصل: هر Phase پس از **Build → Run → Test → Fix → Document** کامل، وارد Phase بعدی می‌شود.
**وضعیت: تمام مراحل پیاده‌سازی شده‌اند (نسخه 1.0).**

| Phase | عنوان | محتوای اصلی | وضعیت |
|-------|-------|--------------|-------|
| **1** | Project + UI + ISO Detection | Shell برنامه، Start Page، ساخت پروژه/Workspace، تحلیل فقط‌خواندنی ISO، لاگ، امنیت پایه | ✅ |
| **2** | ISO Extraction + WIM/ESD Metadata | استخراج payload، لیست Editionها از طریق DISM، تبدیل ESD→WIM | ✅ |
| **3** | WIM Mount / Unmount / Commit | DismEngine، Mount/Unmount/Commit/Discard، Command Execution Layer، Cancel امن | ✅ |
| **4** | Basic Customization | User/Computer/Language، Wallpaper/Branding/OEM، حذف AppX با Keep/Remove/Optional | ✅ |
| **5** | Drivers | افزودن INF/فولدر، Validation قبل از Injection، لیست/حذف/خروجی | ✅ |
| **6** | Updates | MSU/CAB با اطلاعات KB، ترتیب SSU اول، حذف Package | ✅ |
| **7** | Applications + Scripts | پکیج نرم‌افزار (Silent Args، ترتیب) + اسکریپت‌ها با زمان اجرا + Trust | ✅ |
| **8** | Registry + Features | RegFileBuilder (REGEDIT5)، Hive آفلاین، Preset Library، Optional Features | ✅ |
| **9** | Services | حالت‌های Startup + هشدار سرویس‌های Critical با تأیید | ✅ |
| **10** | Preset System | Privacy/Performance/Security/UI — اعمال/حذف/Reset/Duplicate/Export/Import + IPresetRepository (Marketplace-ready) | ✅ |
| **11** | Autounattend + Review + Safety | مولد Autounattend، صفحه Review Changes، DependencyEngine، Risk Level، Snapshot/Undo | ✅ |
| **12** | Build Engine + ISO Builder | Pipeline کامل ۱۵ مرحله‌ای، oscdimg (BIOS+UEFI) + fallback مدیریتی (BIOS)، Split/Export | ✅ |
| **13** | Validation Engine | اعتبارسنجی پروژه قبل از Build + ISO خروجی (بوت، payload، ساختار) + گزارش Build (MD/JSON) | ✅ |
| **14** | VM Testing | تشخیص Hyper-V/QEMU + راهنمای Test in VM | ✅ |
| **15** | Polishing | Hash فایل‌های خارجی، لاگ کامل، مدیریت خطا، تم روشن ثابت، مستندات | ✅ |

## وابستگی‌های زمان اجرا (روی ویندوز)

| ابزار | کاربرد | الزامی؟ |
|-------|--------|---------|
| DISM (`dism.exe` — داخل ویندوز) | سرویس‌دهی ایمیج، لیست Edition/Driver/Package/Appx | بله (برای Build) |
| `oscdimg.exe` (Windows ADK) | ISO بوت‌پذیر UEFI+BIOS | اختیاری — بدون آن fallback مدیریتی (BIOS) |
| `reg.exe` / `regedit.exe` | اعمال رجیستری روی Hive آفلاین | بله (داخل ویندوز) |
| Hyper-V / QEMU | تست VM | اختیاری |

## قواعد کلی
1. هر تغییر Risk Level دارد (Safe → Critical) و تغییر Critical تأیید صریح می‌خواهد.
2. ISO اصلی هرگز تغییر نمی‌کند؛ تمام کار روی Workspace/Copy است.
3. هر عملیات Log + Report می‌گیرد؛ خطاها ساخت‌یافته (چه شد/چرا/چه کنیم) نمایش داده می‌شوند.
4. Cancel در نقطه امن: Mount ناتمام همیشه Discard می‌شود.
