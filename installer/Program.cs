using System.Reflection;
using Microsoft.Win32;

const string AppName        = "wsl-import";
const string AppVersion     = "1.0.0";
const string Publisher      = "Fernando";
const string UninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Fernando.WslImport";

var installDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Programs", "wsl-import");

bool isUninstall = args.Length == 1 &&
    args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase);

if (isUninstall)
    RunUninstall();
else
    RunInstall();

// ── Install ──────────────────────────────────────────────────────────────────

void RunInstall()
{
    Console.Title = "wsl-import Setup";
    PrintHeader("wsl-import Setup");

    try
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("wsl-import.exe")
            ?? throw new InvalidOperationException("Embedded payload not found. Re-run build-installer.ps1 to rebuild.");

        Directory.CreateDirectory(installDir);

        // Extract wsl-import.exe
        var exeDest = Path.Combine(installDir, "wsl-import.exe");
        using (var fs = File.Create(exeDest))
            stream.CopyTo(fs);

        Print(ConsoleColor.Green, $"  Installed to  : {installDir}");

        // Copy this setup exe into the install dir so it can act as the uninstaller
        var uninstallerDest = Path.Combine(installDir, "wsl-import-setup.exe");
        File.Copy(Environment.ProcessPath!, uninstallerDest, overwrite: true);

        // Add to user PATH
        var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var parts = userPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (!parts.Contains(installDir, StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("Path",
                string.Join(';', parts.Append(installDir)),
                EnvironmentVariableTarget.User);
            Print(ConsoleColor.Green, $"  Added to PATH : {installDir}");
        }
        else
        {
            Print(ConsoleColor.Yellow, $"  PATH          : already contains {installDir}");
        }

        // Register in Add/Remove Programs (per-user, no admin needed)
        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegKey, writable: true);
        key.SetValue("DisplayName",          $"{AppName} {AppVersion}");
        key.SetValue("DisplayVersion",       AppVersion);
        key.SetValue("Publisher",            Publisher);
        key.SetValue("InstallLocation",      installDir);
        key.SetValue("UninstallString",      $"\"{uninstallerDest}\" --uninstall");
        key.SetValue("NoModify",             1, RegistryValueKind.DWord);
        key.SetValue("NoRepair",             1, RegistryValueKind.DWord);

        Print(ConsoleColor.Green, "  Registered    : Apps & features (Settings > Apps)");

        Console.WriteLine();
        Print(ConsoleColor.Green, "  Installation complete!");
        Print(ConsoleColor.Cyan,  "  Open a new terminal and run: wsl-import --help");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        PrintError(ex.Message);
        Pause();
        Environment.Exit(1);
    }

    Pause();
}

// ── Uninstall ────────────────────────────────────────────────────────────────

void RunUninstall()
{
    Console.Title = "wsl-import Uninstall";
    PrintHeader("wsl-import Uninstall");

    try
    {
        // Remove from user PATH
        var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var cleaned = string.Join(';',
            userPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => !p.Equals(installDir, StringComparison.OrdinalIgnoreCase)));
        Environment.SetEnvironmentVariable("Path", cleaned, EnvironmentVariableTarget.User);
        Print(ConsoleColor.Green, "  Removed from PATH.");

        // Remove registry entry
        Registry.CurrentUser.DeleteSubKey(UninstallRegKey, throwOnMissingSubKey: false);
        Print(ConsoleColor.Green, "  Removed from Apps & features.");

        // Schedule deletion of the install directory after this process exits
        // (can't delete the exe that's currently running, so we use cmd /c ping to delay)
        if (Directory.Exists(installDir))
        {
            var delCmd = $"/c ping -n 3 127.0.0.1 >nul & rd /s /q \"{installDir}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = delCmd,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            Print(ConsoleColor.Green, $"  Removed        : {installDir}");
        }

        Console.WriteLine();
        Print(ConsoleColor.Green, "  Uninstall complete!");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        PrintError(ex.Message);
        Pause();
        Environment.Exit(1);
    }

    Pause();
}

// ── Helpers ──────────────────────────────────────────────────────────────────

void PrintHeader(string title)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  {title}");
    Console.WriteLine($"  {new string('=', title.Length)}");
    Console.ResetColor();
    Console.WriteLine();
}

void Print(ConsoleColor color, string text)
{
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ResetColor();
}

void PrintError(string message)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  ERROR: {message}");
    Console.ResetColor();
    Console.WriteLine();
}

void Pause()
{
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(true);
}
