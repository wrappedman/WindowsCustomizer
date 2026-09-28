using System.Text;
using System.Xml.Linq;
using WindowsCustomizer.Core.Models;

namespace WindowsCustomizer.Core.Services;

// =================================================================================================
// Autounattend.xml generator (spec 10). The user never edits XML — settings come from the Users /
// Setup pages and this class emits a valid unattend file placed at the ISO root (runs during
// Windows Setup) or as unattend.xml inside the image (specialize pass). Pure and unit-tested.
// =================================================================================================

public sealed class UnattendGenerator
{
    private static readonly XNamespace Ns = "urn:schemas-microsoft-com:unattend";
    private static readonly XNamespace Wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";

    public string Generate(CustomizationState state)
    {
        var u = state.Unattend;
        var user = state.User;
        var lang = state.Language;

        // Locale resolution: the Users page (User.*) is primary; the Language page (Language.*)
        // is the fallback for projects saved before the Users page stored these values.
        var uiLanguage = FirstNonEmpty(user.DefaultLanguage, lang.UiLanguage);
        var systemLocale = FirstNonEmpty(user.Region, lang.SystemLocale);
        var userLocale = FirstNonEmpty(user.Region, lang.UserLocale);
        var timeZone = FirstNonEmpty(user.TimeZone, lang.TimeZone);
        var inputLocale = BuildInputLocale(user, lang.InputLocale);

        var settings = new List<XElement>();

        // ----------------------------------------------- windowsPE pass -----------------------------------------------
        var peComponents = new List<XElement>();

        peComponents.Add(Component("Microsoft-Windows-International-Core-WinPE", "windowsPE",
            Add("InputLocale", inputLocale),
            Add("SystemLocale", systemLocale),
            Add("UILanguage", uiLanguage),
            Add("UserLocale", userLocale),
            Child("SetupUILanguage", Add("UILanguage", uiLanguage))));

        var setupChildren = new List<XElement>();
        setupChildren.Add(Child("UserData",
            Add("AcceptEula", u.SkipEula ? "true" : "false"),
            Add("FullName", string.IsNullOrWhiteSpace(user.Owner) ? user.DisplayName : user.Owner),
            Add("Organization", user.Organization)));
        if (!string.IsNullOrWhiteSpace(u.ProductKey))
        {
            setupChildren.Add(Child("ProductKey", Add("Key", u.ProductKey), Add("WillShowUI", "OnError")));
        }

        if (u.ConfigureDisk)
        {
            setupChildren.Add(DiskConfiguration(u));
        }

        setupChildren.Add(Child("ImageInstall",
            Child("OSImage",
                Child("InstallFrom",
                    Child("MetaData",
                        Add("Key", "/IMAGE/NAME"),
                        Add("Value", state.SelectedEditionNames.FirstOrDefault() ?? "Windows 11 Pro"))),
                Child("InstallTo",
                    Add("DiskID", "0"),
                    Add("PartitionID", u.DiskLayout == "GptUefi" ? "3" : "1")))));

        peComponents.Add(Component("Microsoft-Windows-Setup", "windowsPE", setupChildren.ToArray()));

        settings.Add(Pass("windowsPE", peComponents.ToArray()));

        // ----------------------------------------------- specialize pass -----------------------------------------------
        var specialize = new List<XElement>();
        specialize.Add(Component("Microsoft-Windows-Shell-Setup", "specialize",
            Add("ComputerName", user.ComputerName),
            Add("RegisteredOrganization", user.Organization),
            Add("RegisteredOwner", string.IsNullOrWhiteSpace(user.Owner) ? user.DisplayName : user.Owner),
            Add("TimeZone", timeZone)));

        specialize.Add(Component("Microsoft-Windows-Deployment", "specialize",
            Child("RunSynchronous",
                RunSynchronousCommand(1, @"%WINDIR%\Setup\Scripts\WC\Specialize.cmd", "Windows Customizer - specialize scripts"))));

        settings.Add(Pass("specialize", specialize.ToArray()));

        // ----------------------------------------------- oobeSystem pass -----------------------------------------------
        var oobe = new List<XElement>();
        var shellChildren = new List<XElement>
        {
            Child("OOBE",
                Add("HideEULAPage", u.SkipEula ? "true" : "false"),
                Add("HideOEMRegistrationScreen", "true"),
                Add("HideOnlineAccountScreens", u.HideOobePrivacyPages ? "true" : "false"),
                Add("HideWirelessSetupInOOBE", "true"),
                Add("NetworkLocation", "Work"),
                Add("ProtectYourPC", u.HideOobePrivacyPages ? "1" : "3")),
            Child("UserAccounts",
                Child("LocalAccounts",
                    Child("LocalAccount",
                        Child("Password", Add("Value", ToBase64(user.Password))), 
                        Add("Description", "Created by Windows Customizer Studio"),
                        Add("DisplayName", user.DisplayName),
                        Add("Group", user.CreateAdministrator ? "Administrators" : "Users"),
                        Add("Name", user.DefaultUsername))))
        };

        if (user.AutoLogin)
        {
            shellChildren.Add(Child("AutoLogon",
                Add("Enabled", "true"),
                Add("LogonCount", "1"),
                Add("Username", user.DefaultUsername),
                Child("Password", Add("Value", ToBase64(user.Password)))));
        }

        shellChildren.Add(FirstLogonCommands());

        oobe.Add(Component("Microsoft-Windows-Shell-Setup", "oobeSystem", shellChildren.ToArray()));
        oobe.Add(Component("Microsoft-Windows-International-Core", "oobeSystem",
            Add("InputLocale", inputLocale),
            Add("SystemLocale", systemLocale),
            Add("UILanguage", uiLanguage),
            Add("UserLocale", userLocale)));

        settings.Add(Pass("oobeSystem", oobe.ToArray()));

        var doc = new XDocument(
            new XElement(Ns + "unattend",
                new XAttribute(XNamespace.Xmlns + "wcm", Wcm.NamespaceName),
                settings));

        return Declaration() + "\r\n" + doc.ToString(SaveOptions.None) + "\r\n";
    }

    /// <summary>Windows unattend passwords must be Base64 of UTF-16LE plaintext.</summary>
    public static string ToBase64(string plaintext) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(plaintext ?? string.Empty));

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    /// <summary>
    /// InputLocale = semicolon-separated KLID list, FIRST = default keyboard.
    /// Falls back to the single layout, then to the Language page value.
    /// </summary>
    public static string BuildInputLocale(UserSettings user, string fallback)
    {
        var layouts = (user.KeyboardLayouts ?? new List<string>())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        if (layouts.Count == 0 && !string.IsNullOrWhiteSpace(user.KeyboardLayout))
        {
            layouts.Add(user.KeyboardLayout);
        }

        return layouts.Count > 0 ? string.Join(';', layouts) : FirstNonEmpty(fallback, "en-US");
    }

    private static string Declaration() =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>";

    private static XElement Pass(string name, params XElement[] components) =>
        new(Ns + "settings", new XAttribute("pass", name), components);

    private static XElement Component(string name, string pass, params XElement[] children) =>
        new(Ns + "component",
            new XAttribute("name", name),
            new XAttribute("processorArchitecture", "amd64"),
            new XAttribute("publicKeyToken", "31bf3856ad364e35"),
            new XAttribute("language", "neutral"),
            new XAttribute("versionScope", "nonSxS"),
            children);

    private static XElement Add(string key, string value) => new(Ns + key, value);

    private static XElement Child(string name, params XElement[] children) => new(Ns + name, children);

    private static XElement RunSynchronousCommand(int order, string path, string description) =>
        new(Ns + "RunSynchronousCommand",
            new XAttribute(Wcm + "action", "add"),
            Add("Order", order.ToString()),
            Add("Path", path),
            Add("Description", description));

    private static XElement FirstLogonCommands() =>
        Child("FirstLogonCommands",
            new XElement(Ns + "SynchronousCommand",
                new XAttribute(Wcm + "action", "add"),
                Add("Order", "1"),
                Add("CommandLine", @"%WINDIR%\Setup\Scripts\WC\FirstLogon.cmd"),
                Add("Description", "Windows Customizer - first logon tasks"),
                Add("RequiresUserInput", "false")));

    private static XElement DiskConfiguration(UnattendSettings u)
    {
        var wipe = new XElement(Ns + "Disk",
            new XAttribute(Wcm + "action", "add"),
            Add("DiskID", "0"),
            Child("CreatePartitions", CreatePartitions(u).ToArray()),
            Child("ModifyPartitions", ModifyPartitions(u).ToArray()));

        return Child("DiskConfiguration",
            Add("WillShowUI", "OnError"),
            wipe);
    }

    private static IEnumerable<XElement> CreatePartitions(UnattendSettings u)    {
        if (u.DiskLayout == "GptUefi")
        {
            yield return Part("1", "Primary", "300");
            yield return Part("2", "Primary", "100"); // MSR
            yield return Part("3", "Primary", null);  // Windows
            if (u.RecoveryPartitionMb > 0)
            {
                yield return Part("4", "Primary", u.RecoveryPartitionMb.ToString());
            }
        }
        else
        {
            yield return Part("1", "Primary", "350"); // System
            yield return Part("2", "Primary", null);  // Windows
        }
    }

    private static XElement Part(string id, string type, string? sizeMb) =>
        new(Ns + "CreatePartition",
            new XAttribute(Wcm + "action", "add"),
            Add("Order", id),
            Add("Type", type),
            sizeMb is null ? Add("Extend", "true") : Add("Size", sizeMb));

    private static IEnumerable<XElement> ModifyPartitions(UnattendSettings u)
    {
        if (u.DiskLayout == "GptUefi")
        {
            yield return ModPart("1", "System", "NTFS", "System");
            yield return ModPart("3", "Primary", "NTFS", "Windows");
            if (u.RecoveryPartitionMb > 0)
            {
                yield return ModPart("4", "Primary", "NTFS", "Recovery");
            }
        }
        else
        {
            yield return ModPart("1", "System", "NTFS", "System");
            yield return ModPart("2", "Primary", "NTFS", "Windows");
        }
    }

    private static XElement ModPart(string id, string format, string fs, string label) =>
        new(Ns + "ModifyPartition",
            new XAttribute(Wcm + "action", "add"),
            Add("Order", id),
            Add("PartitionID", id),
            Add("Format", format == "System" ? "NTFS" : fs),
            Add("Label", label),
            Add("Letter", id == "3" || id == "2" ? "C" : "W"));
}
