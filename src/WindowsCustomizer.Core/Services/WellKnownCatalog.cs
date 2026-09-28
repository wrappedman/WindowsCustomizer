namespace WindowsCustomizer.Core.Services;

using WindowsCustomizer.Core.Models;

/// <summary>
/// Well-known Windows content used to populate the UI immediately (no DISM/mount needed).
/// "Load from Image" enriches these lists with what actually exists in the source image.
/// </summary>
public static class WellKnownCatalog
{
    // ------------------------------------------------------ AppX packages (spec 8/14) ------------------------------------------------------

    public sealed record KnownApp(string DisplayName, string PackagePattern, string Description, bool Sensitive = false);

    public static readonly IReadOnlyList<KnownApp> ProvisionedApps = new[]
    {
        new KnownApp("Microsoft Edge", "Microsoft.MicrosoftEdge", "Web browser — required by Windows (remove only if you replace it).", true),
        new KnownApp("OneDrive", "Microsoft.OneDriveSync", "Cloud storage client (the stand-alone OneDriveSetup)."),
        new KnownApp("Xbox App", "Microsoft.GamingApp", "Xbox console companion app."),
        new KnownApp("Xbox Gaming Overlay", "Microsoft.XboxGamingOverlay", "Game Bar overlay (Win+G)."),
        new KnownApp("Xbox Identity Provider", "Microsoft.XboxIdentityProvider", "Xbox sign-in — needed by games from the Store.", true),
        new KnownApp("Xbox Speech to Text", "Microsoft.XboxSpeechToTextOverlay", "Accessibility overlay for Game Chat."),
        new KnownApp("Xbox Game Callable UI", "Microsoft.XboxGameCallableUI", "Xbox system UI — cannot be reinstalled easily.", true),
        new KnownApp("Microsoft Teams", "MicrosoftTeams", "Consumer Teams pre-install (Windows 11)."),
        new KnownApp("Microsoft Copilot", "Microsoft.Copilot", "AI assistant taskbar entry (Windows 11)."),
        new KnownApp("Widgets", "MicrosoftWindows.Client.WebExperience", "Taskbar Widgets board (Windows 11)."),
        new KnownApp("News", "Microsoft.BingNews", "News feed app."),
        new KnownApp("Weather", "Microsoft.BingWeather", "Weather app."),
        new KnownApp("Bing Search", "Microsoft.BingSearch", "Bing-powered search experiences."),
        new KnownApp("Solitaire Collection", "Microsoft.MicrosoftSolitaireCollection", "Card games."),
        new KnownApp("Clipchamp", "Microsoft.Clipchamp", "Video editor (Windows 11)."),
        new KnownApp("Get Help", "Microsoft.GetHelp", "Support app."),
        new KnownApp("Get Started", "Microsoft.Getstarted", "Welcome / tips app."),
        new KnownApp("Office Hub", "Microsoft.MicrosoftOfficeHub", "Office trial launcher."),
        new KnownApp("People", "Microsoft.People", "People app / contacts."),
        new KnownApp("Power Automate", "Microsoft.PowerAutomateDesktop", "Desktop automation tool."),
        new KnownApp("To Do", "Microsoft.Todos", "Microsoft To Do."),
        new KnownApp("Alarms & Clock", "Microsoft.WindowsAlarms", "Alarms and world clock."),
        new KnownApp("Camera", "Microsoft.WindowsCamera", "Camera app."),
        new KnownApp("Feedback Hub", "Microsoft.WindowsFeedbackHub", "Windows feedback app."),
        new KnownApp("Maps", "Microsoft.WindowsMaps", "Maps app."),
        new KnownApp("Sound Recorder", "Microsoft.WindowsSoundRecorder", "Voice recorder."),
        new KnownApp("Phone Link", "Microsoft.YourPhone", "Connect phone to PC."),
        new KnownApp("Media Player (legacy)", "Microsoft.ZuneMusic", "Groove/Music — Windows 11 reuses the name for the new Media Player.", true),
        new KnownApp("Movies & TV", "Microsoft.ZuneVideo", "Video player/store."),
        new KnownApp("Photos", "Microsoft.Windows.Photos", "Photo viewer/editor.", true),
        new KnownApp("Notepad", "Microsoft.WindowsNotepad", "Notepad (new store version)."),
        new KnownApp("Paint", "Microsoft.Paint", "Paint (new store version)."),
        new KnownApp("Windows Terminal", "Microsoft.WindowsTerminal", "Modern console host."),
        new KnownApp("Screen Sketch", "Microsoft.ScreenSketch", "Snipping tool / Snip & Sketch.", true),
        new KnownApp("Quick Assist", "MicrosoftCorporationII.QuickAssist", "Remote assistance tool."),
        new KnownApp("Windows Web Experience", "MicrosoftWindows.Client.AIX", "AI features pack (24H2+)."),
        new KnownApp("Outlook for Windows", "Microsoft.OutlookForWindows", "New Outlook pre-install.")
    };

    // ------------------------------------------------------ Features (spec 13) ------------------------------------------------------

    public sealed record KnownFeature(string FeatureName, string DisplayName, string Description);

    public static readonly IReadOnlyList<KnownFeature> Features = new[]
    {
        new KnownFeature("NetFx3", ".NET Framework 3.5", "Legacy .NET — required by many old apps. Install from Windows Update/SXS when possible."),
        new KnownFeature("NetFx4-Extended", ".NET Framework 4.x (Extended)", "Includes WCF/ASP.NET extras beyond the base runtime."),
        new KnownFeature("Microsoft-Hyper-V-All", "Hyper-V (all)", "Microsoft hypervisor + management tools. Requires hardware virtualization."),
        new KnownFeature("Microsoft-Hyper-V", "Hyper-V platform", "Hypervisor platform only (no management tools)."),
        new KnownFeature("Microsoft-Hyper-V-Tools-All", "Hyper-V management tools", "GUI + PowerShell management for Hyper-V."),
        new KnownFeature("Microsoft-Windows-Subsystem-Linux", "WSL (Windows Subsystem for Linux)", "Run Linux binaries. Needs VirtualMachinePlatform on WSL2."),
        new KnownFeature("VirtualMachinePlatform", "Virtual Machine Platform", "Base for WSL2 and other VM solutions."),
        new KnownFeature("Containers-DisposableClientVM", "Windows Sandbox", "Throw-away lightweight desktop for testing untrusted apps."),
        new KnownFeature("Microsoft-Windows-Hyper-V-Hypervisor", "Hyper-V Hypervisor", "Core hypervisor component."),
        new KnownFeature("IIS-WebServer", "IIS Web Server", "Internet Information Services web server."),
        new KnownFeature("IIS-WebServerRole", "IIS Web Server Role", "Web server role foundation."),
        new KnownFeature("SMB1Protocol", "SMB 1.0/CIFS File Sharing", "Legacy file sharing — security risk (EternalBlue). Only for old NAS devices."),
        new KnownFeature("SMB1Protocol-Client", "SMB 1.0 Client", "Client side of legacy SMB."),
        new KnownFeature("TelnetClient", "Telnet Client", "Legacy remote console client (unencrypted)."),
        new KnownFeature("TFTP", "TFTP Client", "Trivial FTP — used by network equipment firmware tools."),
        new KnownFeature("OpenSSH-Server*", "OpenSSH Server", "SSH server for remote administration."),
        new KnownFeature("OpenSSH-Client*", "OpenSSH Client", "ssh/scp/sftp command line tools."),
        new KnownFeature("WorkFolders-Client", "Work Folders", "Enterprise file sync client."),
        new KnownFeature("Printing-XPSServices", "Microsoft XPS Document Writer", "XPS print/PDF alternative."),
        new KnownFeature("Printing-PrintToPDFServices-Features", "Microsoft Print to PDF", "Print-to-PDF driver."),
        new KnownFeature("MicrosoftWindowsPowerShellV2", "Windows PowerShell 2.0", "Legacy PowerShell — security risk, disable unless needed."),
        new KnownFeature("MicrosoftWindowsPowerShellV2Root", "PowerShell 2.0 Engine", "Engine root for the legacy PowerShell."),
        new KnownFeature("SearchEngine-Client-Package", "Windows Search", "Start-menu/file indexing package (24H2+ packaging)."),
        new KnownFeature("MediaPlayback", "Media Features", "Windows Media Player foundation (WMP)."),
        new KnownFeature("WindowsMediaPlayer", "Windows Media Player", "Classic WMP."),
        new KnownFeature("Internet-Explorer-Optional-amd64", "Internet Explorer 11 (optional)", "Legacy IE mode components.")
    };

    // ------------------------------------------------------ Services (spec 17) ------------------------------------------------------

    public sealed record KnownService(string Name, string Display, StartupType Startup, bool Critical, string Warning, string Description);

    public static readonly IReadOnlyList<KnownService> Services = new[]
    {
        new KnownService("DiagTrack", "Connected User Experiences and Telemetry", StartupType.Disabled, false, "",
            "Collects diagnostics data. Disabling stops telemetry uploads."),
        new KnownService("dmwappushservice", "WAP Push Message Routing Service", StartupType.Disabled, false, "",
            "Mobile device management routing — not needed on desktops."),
        new KnownService("SysMain", "SysMain (Superfetch)", StartupType.Manual, false, "",
            "Caches frequently used apps in RAM. Disable on SSD/low-RAM systems."),
        new KnownService("WSearch", "Windows Search", StartupType.Manual, false, "",
            "File and Start menu indexing. Disabling makes search much slower."),
        new KnownService("Spooler", "Print Spooler", StartupType.Manual, false, "",
            "Print queue. Disable ONLY on machines that never print."),
        new KnownService("RemoteRegistry", "Remote Registry", StartupType.Disabled, false, "",
            "Allows remote registry editing — disable unless managed centrally."),
        new KnownService("TermService", "Remote Desktop Services", StartupType.Manual, true,
            "Disabling blocks Remote Desktop completely (including third-party helpers that rely on it).",
            "RDP host service."),
        new KnownService("SharedAccess", "Internet Connection Sharing (ICS)", StartupType.Manual, false, "",
            "Shares one connection with other devices."),
        new KnownService("lfsvc", "Geolocation Service", StartupType.Disabled, false, "",
            "Location tracking for Windows apps."),
        new KnownService("MapsBroker", "Downloaded Maps Manager", StartupType.Disabled, false, "",
            "Caches offline maps."),
        new KnownService("RetailDemo", "Retail Demo Service", StartupType.Disabled, false, "",
            "Retail store demo mode — never needed at home."),
        new KnownService("XblAuthManager", "Xbox Live Auth Manager", StartupType.Disabled, false, "",
            "Xbox Live sign-in. Disabling breaks Xbox/Store games."),
        new KnownService("XblGameSave", "Xbox Live Game Save", StartupType.Disabled, false, "",
            "Xbox cloud saves."),
        new KnownService("XboxNetApiSvc", "Xbox Live Networking Service", StartupType.Disabled, false, "",
            "Xbox multiplayer networking."),
        new KnownService("XboxGipSvc", "Xbox Accessory Management Service", StartupType.Disabled, false, "",
            "Xbox controller accessories."),
        new KnownService("WbioSrvc", "Windows Biometric Service", StartupType.Manual, false, "",
            "Fingerprint / face sign-in."),
        new KnownService("wisvc", "Windows Insider Service", StartupType.Disabled, false, "",
            "Insider program flights — not needed on stable builds."),
        new KnownService("WerSvc", "Windows Error Reporting", StartupType.Manual, false, "",
            "Crash reporting to Microsoft."),
        new KnownService("PcaSvc", "Program Compatibility Assistant", StartupType.Manual, false, "",
            "Detects compatibility issues for old installers."),
        new KnownService("SCardSct", "Smart Card", StartupType.Disabled, false, "",
            "Smart-card reader support — only needed with smart cards."),
        new KnownService("SEMgrSvc", "Payments and NFC/SE Manager", StartupType.Disabled, false, "",
            "NFC payment support."),
        new KnownService("PhoneSvc", "Phone Service", StartupType.Disabled, false, "",
            "Phone/telephony integration (Your Phone)."),
        new KnownService("TabletInputService", "Touch Keyboard and Handwriting Panel", StartupType.Manual, false, "",
            "On-screen keyboard / pen input — keep on tablets."),
        new KnownService("WMPNetworkSvc", "Windows Media Player Network Sharing", StartupType.Disabled, false, "",
            "Streams media to DLNA devices."),
        new KnownService("Fax", "Fax", StartupType.Disabled, false, "",
            "Fax service — obsolete for most users."),
        new KnownService("SCPolicySvc", "Smart Card Removal Policy", StartupType.Disabled, false, "",
            "Locks the PC when a smart card is removed."),
        new KnownService("Bonjour Service", "Bonjour Service (Apple)", StartupType.Manual, false, "",
            "Apple mDNS — installed by iTunes/iCloud. Safe to disable if you use no Apple software."),
        new KnownService("AdobeARMservice", "Adobe Acrobat Update Service", StartupType.Manual, false, "",
            "Adobe Reader auto-updater."),
        new KnownService("gupdate", "Google Update Service", StartupType.Manual, false, "",
            "Chrome/Google auto-updater.")
    };

    // ------------------------------------------------------ Language / Region pick lists (spec 23) ------------------------------------------------------

    public static readonly string[] Languages =
    {
        "en-US", "en-GB", "fa-IR", "ar-SA", "de-DE", "fr-FR", "es-ES", "es-MX", "it-IT", "pt-BR",
        "pt-PT", "ru-RU", "zh-CN", "zh-TW", "ja-JP", "ko-KR", "tr-TR", "nl-NL", "pl-PL", "sv-SE",
        "da-DK", "fi-FI", "nb-NO", "cs-CZ", "hu-HU", "el-GR", "he-IL", "hi-IN", "th-TH", "vi-VN",
        "uk-UA", "id-ID", "ms-MY", "ro-RO", "bg-BG", "sr-Latn-RS", "sl-SI", "sk-SK", "hr-HR", "lt-LT", "lv-LV", "et-EE"
    };

    /// <summary>Windows time-zone IDs (the strings used by Unattend/Specialize).</summary>
    public static readonly string[] TimeZones =
    {
        "UTC", "Iran Standard Time", "Afghanistan Standard Time", "West Asia Standard Time", "India Standard Time",
        "Nepal Standard Time", "Central Asia Standard Time", "SE Asia Standard Time", "China Standard Time",
        "Singapore Standard Time", "Tokyo Standard Time", "Korea Standard Time", "AUS Eastern Standard Time",
        "AUS Central Standard Time", "W. Australia Standard Time", "New Zealand Standard Time",
        "Arabian Standard Time", "Arabic Standard Time", "Israel Standard Time", "Turkey Standard Time",
        "E. Europe Standard Time", "GTB Standard Time", "FLE Standard Time", "Kaliningrad Standard Time",
        "Russian Standard Time", "N. Central Asia Standard Time", "North Asia Standard Time",
        "Central Europe Standard Time", "W. Europe Standard Time", "Romance Standard Time",
        "Central European Standard Time", "GMT Standard Time", "Greenwich Standard Time",
        "Morocco Standard Time", "South Africa Standard Time", "Egypt Standard Time", "E. South America Standard Time",
        "SA Pacific Standard Time", "SA Western Standard Time", "SA Eastern Standard Time",
        "Central Standard Time", "Central America Standard Time", "Mountain Standard Time", "US Mountain Standard Time",
        "Pacific Standard Time", "Alaskan Standard Time", "Hawaiian Standard Time", "Atlantic Standard Time",
        "Eastern Standard Time", "US Eastern Standard Time", "Canada Central Standard Time", "Mexico Standard Time",
        "Central Pacific Standard Time", "West Pacific Standard Time", "Fiji Standard Time", "Tonga Standard Time"
    };

    /// <summary>Keyboard layout options (KLID) for common locales — human-readable names.</summary>
    public sealed record KeyboardLayoutOption(string Code, string Name);

    public static readonly IReadOnlyList<KeyboardLayoutOption> KeyboardLayouts = new[]
    {
        new KeyboardLayoutOption("0409:00000409", "English (United States)"),
        new KeyboardLayoutOption("0409:00020409", "English (United States — International)"),
        new KeyboardLayoutOption("0409:00010409", "English (United States — Dvorak)"),
        new KeyboardLayoutOption("0809:00000809", "English (United Kingdom)"),
        new KeyboardLayoutOption("0429:00000429", "Persian (Farsi)"),
        new KeyboardLayoutOption("0429:00020429", "Persian (Farsi — Standard)"),
        new KeyboardLayoutOption("0401:00000401", "Arabic (101)"),
        new KeyboardLayoutOption("0401:00010401", "Arabic (102)"),
        new KeyboardLayoutOption("0407:00000407", "German (Germany)"),
        new KeyboardLayoutOption("040C:0000040C", "French (France)"),
        new KeyboardLayoutOption("0C0C:00001009", "French (Canada)"),
        new KeyboardLayoutOption("0410:00000410", "Italian (Italy)"),
        new KeyboardLayoutOption("040A:0000040A", "Spanish (Spain — Traditional)"),
        new KeyboardLayoutOption("0C0A:0000080A", "Spanish (Mexico)"),
        new KeyboardLayoutOption("0416:00000416", "Portuguese (Brazil — ABNT)"),
        new KeyboardLayoutOption("0816:00000816", "Portuguese (Portugal)"),
        new KeyboardLayoutOption("0419:00000419", "Russian"),
        new KeyboardLayoutOption("0411:00000411", "Japanese"),
        new KeyboardLayoutOption("0804:00000804", "Chinese (Simplified)"),
        new KeyboardLayoutOption("0404:00000404", "Chinese (Traditional)"),
        new KeyboardLayoutOption("0412:00000412", "Korean"),
        new KeyboardLayoutOption("041F:0000041F", "Turkish (Q)"),
        new KeyboardLayoutOption("0413:00000413", "Dutch (Netherlands)"),
        new KeyboardLayoutOption("0415:00000415", "Polish (Programmers)"),
        new KeyboardLayoutOption("041D:0000041D", "Swedish"),
        new KeyboardLayoutOption("0406:00000406", "Danish"),
        new KeyboardLayoutOption("040B:0000040B", "Finnish"),
        new KeyboardLayoutOption("0414:00000414", "Norwegian"),
        new KeyboardLayoutOption("0405:00000405", "Czech"),
        new KeyboardLayoutOption("040E:0000040E", "Hungarian"),
        new KeyboardLayoutOption("0408:00000408", "Greek"),
        new KeyboardLayoutOption("040D:0000040D", "Hebrew"),
        new KeyboardLayoutOption("0439:00000439", "Hindi (Devanagari)"),
        new KeyboardLayoutOption("041E:0000041E", "Thai"),
        new KeyboardLayoutOption("042A:0000042A", "Vietnamese"),
        new KeyboardLayoutOption("0422:00000422", "Ukrainian"),
        new KeyboardLayoutOption("041A:0000041A", "Croatian"),
        new KeyboardLayoutOption("0424:00000424", "Slovenian"),
        new KeyboardLayoutOption("041B:0000041B", "Slovak"),
        new KeyboardLayoutOption("0418:00000418", "Romanian"),
        new KeyboardLayoutOption("0402:00000402", "Bulgarian"),
        new KeyboardLayoutOption("0427:00000427", "Lithuanian"),
        new KeyboardLayoutOption("0426:00000426", "Latvian"),
        new KeyboardLayoutOption("0425:00000425", "Estonian")
    };

    /// <summary>Friendly name for a KLID code (falls back to the code itself).</summary>
    public static string KeyboardLayoutName(string code) =>
        KeyboardLayouts.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Name ?? code;

    /// <summary>Region/culture names used by Unattend (InputLocale/UserLocale).</summary>
    public static readonly string[] Regions =
    {
        "en-US", "en-GB", "en-AU", "en-CA", "en-IN", "fa-IR", "ar-SA", "ar-AE", "de-DE", "de-AT", "de-CH",
        "fr-FR", "fr-CA", "es-ES", "es-MX", "es-AR", "it-IT", "pt-BR", "pt-PT", "ru-RU", "zh-CN", "zh-TW",
        "ja-JP", "ko-KR", "tr-TR", "nl-NL", "nl-BE", "pl-PL", "sv-SE", "da-DK", "fi-FI", "nb-NO", "cs-CZ",
        "hu-HU", "el-GR", "he-IL", "hi-IN", "th-TH", "vi-VN", "uk-UA", "id-ID", "ro-RO", "bg-BG", "sr-RS"
    };

    // ------------------------------------------------------ Sample scripts (spec 19) ------------------------------------------------------

    public sealed record ScriptSample(string Title, string FileName, string Kind, ScriptRunPhase Phase, string Description, string Code);

    public static readonly IReadOnlyList<ScriptSample> ScriptSamples = new ScriptSample[]
    {
        new("Remove OneDrive completely",
            "Remove-OneDrive.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Uninstalls OneDrive, removes its Explorer icons and blocks auto-reinstall. Review before use.",
            """
            # Remove OneDrive (runs as SYSTEM at first logon)
            taskkill /f /im OneDrive.exe 2>$null
            & "$env:SystemRoot\SysWOW64\OneDriveSetup.exe" /uninstall 2>$null
            & "$env:SystemRoot\System32\OneDriveSetup.exe" /uninstall 2>$null
            Remove-Item "$env:ProgramData\Microsoft OneDrive" -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item "$env:LOCALAPPDATA\Microsoft\OneDrive" -Recurse -Force -ErrorAction SilentlyContinue
            $clsid = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{018D5C66-4533-4307-9B53-224DE2ED1FE6}'
            Remove-Item $clsid -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host 'OneDrive removed.'
            """),
        new("Disable telemetry & diagnostics",
            "Disable-Telemetry.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Stops DiagTrack/dmwappushservice and sets the telemetry policy to Security. Low risk.",
            """
            # Disable telemetry services and policy
            Set-Service DiagTrack -StartupType Disabled -ErrorAction SilentlyContinue
            Stop-Service DiagTrack -Force -ErrorAction SilentlyContinue
            Set-Service dmwappushservice -StartupType Disabled -ErrorAction SilentlyContinue
            Stop-Service dmwappushservice -Force -ErrorAction SilentlyContinue
            New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' -Force | Out-Null
            Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' -Name AllowTelemetry -Value 0 -Type DWord
            Write-Host 'Telemetry disabled.'
            """),
        new("Enable long file paths",
            "Enable-LongPaths.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Removes the 260-character MAX_PATH limit. Safe and recommended for developers.",
            """
            # Enable Win32 long paths
            New-Item -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Force | Out-Null
            Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name LongPathsEnabled -Value 1 -Type DWord
            Write-Host 'Long paths enabled.'
            """),
        new("Set power plan to High Performance",
            "Set-HighPerformance.cmd", "Cmd", ScriptRunPhase.FirstBoot,
            "Activates the High Performance power scheme (good for desktops/gaming rigs).",
            """
            @echo off
            rem Activate the High Performance power plan
            powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
            powercfg /hibernate off
            echo Power plan set to High Performance.
            """),
        new("Clean temporary files",
            "Clean-Temp.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Deletes user/temp caches to shrink the image footprint. Safe.",
            """
            # Clean temporary locations
            Remove-Item "$env:TEMP\*" -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item "$env:SystemRoot\Temp\*" -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item "$env:SystemRoot\Prefetch\*" -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item "$env:LOCALAPPDATA\Microsoft\Windows\INetCache\*" -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host 'Temp files cleaned.'
            """),
        new("Show file extensions & hidden files",
            "Explorer-ShowExtensions.ps1", "PowerShell", ScriptRunPhase.FirstUserLogin,
            "Classic Explorer behavior: always show extensions and hidden files. Very safe.",
            """
            # Explorer: show file extensions and hidden files
            $adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
            Set-ItemProperty $adv -Name HideFileExt -Value 0 -Type DWord
            Set-ItemProperty $adv -Name Hidden -Value 1 -Type DWord
            Set-ItemProperty $adv -Name LaunchTo -Value 1 -Type DWord   # open Explorer to This PC
            Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
            """),
        new("Classic right-click menu (Windows 11)",
            "Classic-ContextMenu.reg", "Registry", ScriptRunPhase.FirstUserLogin,
            "Restores the Windows 10 context menu on Windows 11. Windows 11 only.",
            """
            Windows Registry Editor Version 5.00

            [HKEY_CURRENT_USER\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32]
            @=""
            """),
        new("Create a maintenance scheduled task",
            "Add-MaintenanceTask.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Registers a weekly cleanup task (example of EveryBoot/scheduled-task style automation).",
            """
            # Weekly temp cleanup task at Sundays 03:00
            $action  = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -WindowStyle Hidden -Command "Remove-Item $env:TEMP\* -Recurse -Force -ErrorAction SilentlyContinue"'
            $trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At 3:00am
            Register-ScheduledTask -TaskName 'WCS-Weekly-Cleanup' -Action $action -Trigger $trigger -RunLevel Highest -Force | Out-Null
            Write-Host 'Maintenance task registered.'
            """),
        new("Remove common bloatware (all-in-one)",
            "Remove-Bloatware.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Removes a wide list of consumer apps (games, trials, news). Review the list — anything you keep can be deleted from the array.",
            """
            # Remove common consumer apps for every user (review the list before use!)
            $apps = @(
                'Microsoft.BingNews', 'Microsoft.BingWeather', 'Microsoft.BingSearch',
                'Microsoft.GetHelp', 'Microsoft.Getstarted', 'Microsoft.MicrosoftOfficeHub',
                'Microsoft.MicrosoftSolitaireCollection', 'Microsoft.People',
                'Microsoft.PowerAutomateDesktop', 'Microsoft.Todos',
                'Microsoft.WindowsAlarms', 'Microsoft.WindowsFeedbackHub', 'Microsoft.WindowsMaps',
                'Microsoft.WindowsSoundRecorder', 'Microsoft.YourPhone',
                'Microsoft.ZuneVideo', 'Microsoft.GamingApp', 'Microsoft.XboxGamingOverlay',
                'Microsoft.XboxSpeechToTextOverlay', 'MicrosoftTeams', 'Microsoft.Clipchamp',
                'MicrosoftCorporationII.QuickAssist', 'Microsoft.OutlookForWindows'
            )
            foreach ($app in $apps) {
                Get-AppxPackage -AllUsers -Name $app -ErrorAction SilentlyContinue | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue
                Get-AppxProvisionedPackage -Online | Where-Object DisplayName -EQ $app |
                    Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Out-Null
            }
            Write-Host "Bloatware removal finished."
            """),
        new("Disable Cortana and web search",
            "Disable-Cortana.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Turns Cortana off and stops Start menu searches from hitting the web. Safe.",
            """
            # Disable Cortana + web search in Start
            $search = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'
            New-Item -Path $search -Force | Out-Null
            Set-ItemProperty $search -Name AllowCortana -Value 0 -Type Dword
            Set-ItemProperty $search -Name DisableWebSearch -Value 1 -Type Dword
            Set-ItemProperty $search -Name ConnectedSearchUseWeb -Value 0 -Type Dword
            Set-ItemProperty $search -Name AllowSearchToUseLocation -Value 0 -Type Dword
            Write-Host 'Cortana and web search disabled.'
            """),
        new("Set static DNS servers",
            "Set-DNS-Servers.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Points every active network card at Cloudflare/Google DNS. Edit the IPs to use your own.",
            """
            # Set DNS for all active adapters (edit the servers to taste)
            $primary   = '1.1.1.1'
            $secondary = '8.8.8.8'
            Get-NetAdapter | Where-Object Status -eq 'Up' | ForEach-Object {
                Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses ($primary, $secondary)
            }
            ipconfig /flushdns | Out-Null
            Write-Host "DNS set to $primary / $secondary."
            """),
        new("Enable .NET Framework 3.5",
            "Enable-DotNet35.cmd", "Cmd", ScriptRunPhase.FirstBoot,
            "Enables .NET 3.5 from the Windows side-by-side store (needs the SXS source or Windows Update).",
            """
            @echo off
            rem Enable .NET Framework 3.5 (source: Windows Update)
            DISM /Online /Enable-Feature /FeatureName:NetFx3 /All /LimitAccess /NoRestart
            if %errorlevel% neq 0 (
                echo Trying with Windows Update as source...
                DISM /Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart
            )
            echo .NET 3.5 feature request finished.
            """),
        new("Restore classic Windows Photo Viewer",
            "Restore-PhotoViewer.reg", "Registry", ScriptRunPhase.FirstUserLogin,
            "Brings back the classic Photo Viewer in the Open-with menu (works on Win10/11).",
            """
            Windows Registry Editor Version 5.00

            [HKEY_CLASSES_ROOT\Applications\photoviewer.dll\shell\open]
            "MuiVerb"="@photoviewer.dll,-3043"

            [HKEY_CLASSES_ROOT\Applications\photoviewer.dll\shell\open\command]
            @=hex(2):25,00,53,00,79,00,73,00,74,00,65,00,6d,00,52,00,6f,00,6f,00,74,00,25,00,5c,00,53,00,79,00,73,00,74,00,65,00,6d,00,33,00,32,00,5c,00,72,00,75,00,6e,00,64,00,6c,00,6c,00,33,00,32,00,2e,00,65,00,78,00,65,00,20,00,22,00,25,00,50,00,72,00,6f,00,67,00,72,00,61,00,6d,00,46,00,69,00,6c,00,65,00,73,00,25,00,5c,00,57,00,69,00,6e,00,64,00,6f,00,77,00,73,00,20,00,50,00,68,00,6f,00,74,00,6f,00,20,00,56,00,69,00,65,00,77,00,65,00,72,00,5c,00,50,00,68,00,6f,00,74,00,6f,00,56,00,69,00,65,00,77,00,65,00,72,00,2e,00,64,00,6c,00,6c,00,22,00,2c,00,20,00,49,00,6d,00,61,00,67,00,65,00,56,00,69,00,65,00,77,00,5f,00,46,00,75,00,6c,00,6c,00,73,00,63,00,72,00,65,00,65,00,6e,00,20,00,25,00,31,00,00,00

            [HKEY_CLASSES_ROOT\Applications\photoviewer.dll\shell\open\DropTarget]
            "Clsid"="{FFE2A43C-56B9-4bf5-9A79-CC6D4285608A}"
            """),
        new("Add 'Open PowerShell here' to folder context menu",
            "Add-OpenPowerShellHere.reg", "Registry", ScriptRunPhase.FirstUserLogin,
            "Adds 'Open PowerShell here' when right-clicking a folder (shift+right-click classic menu).",
            """
            Windows Registry Editor Version 5.00

            [HKEY_CLASSES_ROOT\Directory\shell\OpenPowerShellHere]
            @="Open PowerShell here"
            "Icon"="powershell.exe"

            [HKEY_CLASSES_ROOT\Directory\shell\OpenPowerShellHere\command]
            @="powershell.exe -NoExit -Command Set-Location -LiteralPath '%V'"

            [HKEY_CLASSES_ROOT\Directory\Background\shell\OpenPowerShellHere]
            @="Open PowerShell here"
            "Icon"="powershell.exe"

            [HKEY_CLASSES_ROOT\Directory\Background\shell\OpenPowerShellHere\command]
            @="powershell.exe -NoExit -Command Set-Location -LiteralPath '%V'"
            """),
        new("Disable startup app launch delay",
            "Disable-StartupDelay.reg", "Registry", ScriptRunPhase.FirstUserLogin,
            "Windows waits a few seconds before launching startup apps — removes the wait.",
            """
            Windows Registry Editor Version 5.00

            [HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize]
            "StartupDelayInMSec"=dword:00000000
            """),
        new("Disable Action Center / notification sidebar",
            "Disable-ActionCenter.reg", "Registry", ScriptRunPhase.FirstUserLogin,
            "Hides the notification center button entirely (Win10/11).",
            """
            Windows Registry Editor Version 5.00

            [HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\Explorer]
            "DisableNotificationCenter"=dword:00000001

            [HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer]
            "DisableNotificationCenter"=dword:00000001
            """),
        new("Disable hibernation (free up disk space)",
            "Disable-Hibernation.cmd", "Cmd", ScriptRunPhase.FirstBoot,
            "Turns hibernation off and deletes hiberfil.sys — frees RAM-sized disk space.",
            """
            @echo off
            rem Disable hibernation and delete hiberfil.sys
            powercfg /hibernate off
            echo Hibernation disabled.
            """),
        new("Activate Ultimate Performance power plan",
            "Ultimate-Performance.cmd", "Cmd", ScriptRunPhase.FirstBoot,
            "Unlocks and activates the Ultimate Performance power scheme (desktops/workstations).",
            """
            @echo off
            rem Unlock + activate Ultimate Performance
            powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 >nul 2>&1
            for /f "tokens=4" %%G in ('powercfg -list ^| findstr /i "Ultimate"') do powercfg /setactive %%G
            echo Ultimate Performance plan activated (if available on this SKU).
            """),
        new("Clear all Windows event logs",
            "Clear-EventLogs.cmd", "Cmd", ScriptRunPhase.FirstBoot,
            "Wipes Application/System/Security event logs — a clean log state on first boot.",
            """
            @echo off
            rem Clear standard event logs (keeps the channels, empties the history)
            for %%L in (Application Security Setup System) do wevtutil cl %%L 2>nul
            echo Event logs cleared.
            """),
        new("Create a system restore point",
            "Create-RestorePoint.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Makes a baseline restore point right after setup ('Windows Customizer Baseline').",
            """
            # Baseline restore point after first boot
            Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
            Checkpoint-Computer -Description 'Windows Customizer Baseline' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction SilentlyContinue
            Write-Host 'Restore point created.'
            """),
        new("Network tuning for low latency",
            "Network-Optimize.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Enables TCP auto-tuning and disables throttling — better throughput on modern networks.",
            """
            # Modern network defaults
            netsh int tcp set global autotuninglevel=normal
            netsh int tcp set global chimney=disabled
            netsh int tcp set global dca=enabled
            netsh int tcp set global netdma=disabled
            netsh int tcp set global ecncapability=disabled
            netsh int tcp set global timestamps=disabled
            Set-NetTCPSetting -SettingName Internet -AutoTuningLevelLocal Normal -ErrorAction SilentlyContinue
            Write-Host 'Network tuning applied.'
            """),
        new("Disable consumer experiences / suggested apps",
            "Disable-ConsumerFeatures.ps1", "PowerShell", ScriptRunPhase.FirstBoot,
            "Stops Windows from auto-installing suggested Store apps and showing tips/ads.",
            """
            # Disable Windows consumer features + tips
            $cloud = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent'
            New-Item -Path $cloud -Force | Out-Null
            Set-ItemProperty $cloud -Name DisableWindowsConsumerFeatures -Value 1 -Type Dword
            Set-ItemProperty $cloud -Name DisableSoftLanding -Value 1 -Type Dword
            Set-ItemProperty $cloud -Name DisableTailoredExperiencesWithDiagnosticDataEnabled -Value 1 -Type Dword
            $content = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
            Set-ItemProperty $content -Name SystemPaneSuggestionsEnabled -Value 0 -Type Dword -ErrorAction SilentlyContinue
            Set-ItemProperty $content -Name SilentInstalledAppsEnabled -Value 0 -Type Dword -ErrorAction SilentlyContinue
            Set-ItemProperty $content -Name SubscribedContent-338388Enabled -Value 0 -Type Dword -ErrorAction SilentlyContinue
            Write-Host 'Consumer features disabled.'
            """)
    };
}
