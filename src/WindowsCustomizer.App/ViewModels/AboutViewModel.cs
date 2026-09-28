namespace WindowsCustomizer.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Reflection;

/// <summary>One highlighted capability on the About page (icon + title + short text).</summary>
public sealed record AboutFeature(string Glyph, string Title, string Description);

/// <summary>One contact row on the About page.</summary>
public sealed record AboutContact(string Glyph, string Label, string Value);

/// <summary>Static "about the program" page — description, version and author contact.</summary>
public sealed class AboutViewModel : ViewModelBase
{
    /// <summary>Main capabilities, each with its own icon card.</summary>
    public IReadOnlyList<AboutFeature> Features { get; } = new[]
    {
        new AboutFeature("\uE7B8", "ISO Analysis",
            "Boot files, volume label, architecture, build number and every edition inside install.wim / install.esd — read automatically from your ISO."),
        new AboutFeature("\uE77B", "Users & Accounts",
            "Default account, password, auto-login, computer name, language, time zone and multiple keyboard layouts — generated into Autounattend.xml."),
        new AboutFeature("\uE71D", "Apps & Features",
            "Keep / Remove / Optional for pre-installed apps, install your own AppX/EXE/MSI packages, enable or remove Windows features."),
        new AboutFeature("\uE950", "Drivers & Updates",
            "Inject INF driver packs and MSU/CAB updates into the image with DISM — validated and hashed before the build."),
        new AboutFeature("\uE943", "Registry & Presets",
            "20 documented presets (privacy, performance, security, UI) with per-tweak descriptions and Safe→Critical risk levels."),
        new AboutFeature("\uE90F", "Services & Scripts",
            "Configure, create or delete services; run PowerShell/Batch/REG scripts at the right setup phase — with ready-made samples."),
        new AboutFeature("\uE790", "Branding & Setup",
            "Wallpaper, lock screen, OEM logo and info; silent setup options — all behind the UI, never hand-editing XML."),
        new AboutFeature("\uE73E", "Review & Safety",
            "Every change listed and toggleable, dependency conflicts and blocking issues detected before the build starts."),
        new AboutFeature("\uE768", "Build Pipeline",
            "Validate → mount → apply → commit → optimize → bootable BIOS/UEFI ISO → validate → report. Fully logged, resumable projects."),
        new AboutFeature("\uE9D5", "Logs & Snapshots",
            "Timestamped logs for every operation, project snapshots with rollback — nothing happens behind your back.")
    };

    /// <summary>Build pipeline stages shown as chips.</summary>
    public IReadOnlyList<string> Pipeline { get; } = new[]
    {
        "Validate", "Prepare", "Mount", "Drivers", "Updates", "Registry", "Services",
        "Remove Apps", "Files", "Scripts", "Setup Config", "Cleanup", "Commit",
        "Optimize", "Bootable ISO", "Validate", "Report"
    };

    public IReadOnlyList<AboutContact> Contacts { get; }

    public string HowItWorks { get; } =
        "Everything the UI does is done with the standard Microsoft deployment tools (DISM, reg, setup): drivers are " +
        "injected with DISM /Add-Driver, updates with /Add-Package, registry changes are written into the offline hives, " +
        "provisioned apps are removed with /Remove-ProvisionedAppxPackage, and the bootable ISO is assembled with " +
        "BIOS (etfsboot) + UEFI (efisys) boot sectors. Projects are saved as resumable .wcp files.";

    public string RuntimeNotes { get; } =
        "Requirements: Windows 10/11 with administrator rights, DISM available (Windows ADK or built-in), " +
        "and enough free disk space (the Build page shows an estimate before you start).";

    public string Version => "Version " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    public string Author => "Behnam Ehsani";

    public string Email => "inbox.ehsani@gmail.com";

    public string GitHub => "https://github.com/wrappedman";

    public string Copyright => $"© {DateTime.Now.Year} Behnam Ehsani. All rights reserved.";

    public AboutViewModel()
    {
        Title = "About Windows Customizer Studio";
        Subtitle = "A professional, wizard-style studio for building customized Windows install images — without ever touching DISM, registry or XML by hand.";

        Contacts = new[]
        {
            new AboutContact("\uE77B", "Author", Author),
            new AboutContact("\uE715", "Email", Email),
            new AboutContact("\uE774", "GitHub", GitHub),
            new AboutContact("\uE713", "Version", Version)
        };
    }
}
