using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WslImport;

internal static class Program
{
    private const string FallbackRootfsUrl = "https://cloud-images.ubuntu.com/wsl/releases/24.04/current/ubuntu-noble-wsl-amd64-wsl.rootfs.tar.gz";
    private const string WslReleaseBase = "https://cloud-images.ubuntu.com/wsl/releases";

    private static int Main(string[] args)
    {
        try
        {
            if (!TryValidateArgs(args, out var error))
            {
                Console.Error.WriteLine(error);
                PrintUsage();
                return 2;
            }

            if (args.Length == 1 && args[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
            {
                PrintUsage();
                return 0;
            }

            return args[0].Equals("--delete", StringComparison.OrdinalIgnoreCase)
                ? DeleteDistro(args[1])
                : CreateDistro();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int CreateDistro()
    {
        var distroName = PromptRequired("Enter DistroName");
        var defaultInstallPath = $"C:\\WSL\\{distroName}";
        var installPath = PromptWithDefault("Enter save location", defaultInstallPath);
        var defaultRootfsUrl = ResolveLatestRootfsUrl();
        var rootfsUrl = PromptWithDefault("Enter rootfs download URL", defaultRootfsUrl);

        var downloadDirectory = @"C:\WSL\Distros";
        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(installPath);

        var rootfsFileName = Path.GetFileName(new Uri(rootfsUrl).AbsolutePath);
        var downloadPath = Path.Combine(downloadDirectory, rootfsFileName);

        var mainUser = PromptRequired("Enter main Linux username");
        var mainPass = PromptPassword($"Enter password for '{mainUser}'");

        Console.WriteLine();
        Console.WriteLine("Ready to import with the following settings:");
        Console.WriteLine($"  DistroName   : {distroName}");
        Console.WriteLine($"  Save location: {installPath}");
        Console.WriteLine($"  Rootfs URL   : {rootfsUrl}");
        Console.WriteLine($"  Rootfs file  : {downloadPath}");
        Console.WriteLine($"  Main user    : {mainUser}");

        var confirm = PromptWithDefault("Proceed with import? (Y/N)", "Y");
        if (!confirm.Equals("y", StringComparison.OrdinalIgnoreCase) &&
            !confirm.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Import cancelled.");
            return 0;
        }

        if (!File.Exists(downloadPath))
        {
            Console.WriteLine("Downloading Rootfs...");
            DownloadWithProgress(rootfsUrl, downloadPath);
        }

        Console.WriteLine("[1/10] Importing distro...");
        RunWslOrThrow("--import", distroName, installPath, downloadPath);

        Console.WriteLine("[2/10] Verifying distro OS...");
        RunWslOrThrow("-d", distroName, "--", "exec", "bash", "-c", "cat /etc/os-release | grep PRETTY_NAME; exit");

        var createUserCmd = $"if id -u '{BashEscape(mainUser)}' >/dev/null 2>&1; then usermod -aG sudo '{BashEscape(mainUser)}'; else useradd -m -G sudo -s /bin/bash '{BashEscape(mainUser)}'; fi; echo '{BashEscape(mainUser)}:{BashEscape(mainPass)}' | chpasswd";
        Console.WriteLine("[3/10] Creating main user and setting password...");
        RunWslOrThrow("-d", distroName, "--", "exec", "bash", "-lc", createUserCmd);

        Console.WriteLine("[4/10] Setting default WSL user...");
        var wslConfScript = string.Join("\n", new[]
        {
            "set -e",
            $"printf '[user]\\ndefault={BashEscape(mainUser)}\\n' > /etc/wsl.conf",
            "chmod 644 /etc/wsl.conf",
            "cat /etc/wsl.conf"
        });
        RunWslBashScriptOrThrow(distroName, null, wslConfScript);

        var bashrcPath = $"/home/{BashEscape(mainUser)}/.bashrc";
        var bashrcSetup = $"grep -q '# >>> wsl-home-on-start >>>' '{bashrcPath}' 2>/dev/null || printf '\\n# >>> wsl-home-on-start >>>\\nif [ -n \"$WSL_DISTRO_NAME\" ] && [ -t 1 ] && [[  \"$PWD\" == /mnt/* ]]; then\\n  cd ~\\nfi\\n# <<< wsl-home-on-start <<<\\n' >> '{bashrcPath}'";
        Console.WriteLine("[5/10] Configuring shell startup behavior...");
        RunWslOrThrow("-d", distroName, "--", "exec", "bash", "-lc", bashrcSetup);

        Console.WriteLine("[6/10] Updating package lists...");
        RunWslOrThrow("-d", distroName, "--", "exec", "bash", "-lc", "apt update");

        Console.WriteLine("[7/10] Upgrading base packages (this can take a few minutes)...");
        RunWslOrThrow("-d", distroName, "--", "exec", "bash", "-lc", "apt upgrade -y");

        var nvmTag = ResolveLatestNvmTag();
        var nvmRepoUrl = "https://github.com/nvm-sh/nvm.git";
        var userHome = $"/home/{BashEscape(mainUser)}";
        var nvmDir = $"{userHome}/.nvm";
        var nvmInstallScript = string.Join("\n", new[]
        {
            "set -e",
            $"export HOME='{userHome}'",
            $"export NVM_DIR='{nvmDir}'",
            "command -v git >/dev/null 2>&1 || { echo 'git is required for standard nvm install.' >&2; exit 1; }",
            $"if [ -d '{nvmDir}/.git' ]; then",
            $"  git -C '{nvmDir}' fetch --tags --force",
            $"  git -C '{nvmDir}' checkout '{nvmTag}'",
            "else",
            $"  rm -rf '{nvmDir}'",
            $"  git clone '{nvmRepoUrl}' '{nvmDir}'",
            $"  git -C '{nvmDir}' checkout '{nvmTag}'",
            "fi",
            $"BASHRC='{userHome}/.bashrc'",
            $"grep -q '### >>> wsl-import nvm >>>' \"$BASHRC\" 2>/dev/null || printf '\\n### >>> wsl-import nvm >>>\\nexport NVM_DIR=\"{nvmDir}\"\\n[ -s \"{nvmDir}/nvm.sh\" ] && . \"{nvmDir}/nvm.sh\"\\n### <<< wsl-import nvm <<<\\n' >> \"$BASHRC\"",
            $"[ -s '{nvmDir}/nvm.sh' ] || {{ echo 'nvm.sh not found at {nvmDir}/nvm.sh' >&2; exit 1; }}"
        });
        Console.WriteLine("[8/10] Installing NVM...");
        RunWslBashScriptOrThrow(distroName, mainUser, nvmInstallScript);

        var nodeScript = string.Join("\n", new[]
        {
            "set -e",
            $"export HOME='{userHome}'",
            $"export NVM_DIR='{nvmDir}'",
            $"[ -s '{nvmDir}/nvm.sh' ] || {{ echo 'nvm.sh not found' >&2; exit 1; }}",
            $". '{nvmDir}/nvm.sh'",
            "if ! command -v node >/dev/null 2>&1; then",
            "  nvm install --lts",
            "fi",
            "nvm alias default 'lts/*'"
        });
        Console.WriteLine("[9/10] Installing Node.js LTS...");
        RunWslBashScriptOrThrow(distroName, mainUser, nodeScript);

        Console.WriteLine("[10/10] Final tooling summary:");
        var summaryScript = string.Join("\n", new[]
        {
            "set -e",
            $"export HOME='{userHome}'",
            $"export NVM_DIR='{nvmDir}'",
            "echo \"git:  $(git --version 2>/dev/null || echo missing)\"",
            $"if [ -s '{nvmDir}/nvm.sh' ]; then",
            $"  . '{nvmDir}/nvm.sh'",
            "  echo \"nvm:  $(nvm --version 2>/dev/null || echo missing)\"",
            "  echo \"node: $(node --version 2>/dev/null || echo missing)\"",
            "  echo \"npm:  $(npm --version 2>/dev/null || echo missing)\"",
            "else",
            "  echo \"nvm:  missing\"",
            "  echo \"node: missing\"",
            "  echo \"npm:  missing\"",
            "fi"
        });
        RunWslBashScriptOrThrow(distroName, mainUser, summaryScript);

        // wsl.conf user settings apply on next launch; terminate now so the next shell opens as the configured user.
        RunProcessNoThrow("wsl.exe", "--terminate", distroName);
        Console.WriteLine("Distro finalized. New sessions will open as the configured default user.");

        return 0;
    }

    private static int DeleteDistro(string distroName)
    {
        // wsl.exe --list outputs UTF-16LE; use a dedicated capture that sets the correct encoding.
        var list = RunProcessCaptureUtf16("wsl.exe", "--list", "--quiet");
        var exists = list.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Any(x => x.Trim().Equals(distroName, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            RunProcessNoThrow("wsl.exe", "--terminate", distroName);
            RunWslOrThrow("--unregister", distroName);
            Console.WriteLine($"Unregistered distro '{distroName}'.");
        }
        else
        {
            Console.WriteLine($"Distro '{distroName}' not registered. Continuing cleanup...");
        }

        Console.WriteLine("Delete complete. Rootfs files were left intact.");
        return 0;
    }

    private static string ResolveLatestRootfsUrl()
    {
        try
        {
            using var http = new HttpClient();
            var releaseIndex = http.GetStringAsync(WslReleaseBase + "/").GetAwaiter().GetResult();
            var versions = Regex.Matches(releaseIndex, "href=\"(\\d+\\.\\d+)/\"")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .OrderByDescending(v => Version.Parse(v))
                .ToList();
            if (versions.Count == 0)
            {
                return FallbackRootfsUrl;
            }

            var latest = versions[0];
            var currentPage = http.GetStringAsync($"{WslReleaseBase}/{latest}/current/").GetAwaiter().GetResult();
            var fileMatch = Regex.Match(currentPage, @"href=""([^""]*wsl\.rootfs\.tar\.gz)""");
            if (!fileMatch.Success)
            {
                return FallbackRootfsUrl;
            }

            var name = fileMatch.Groups[1].Value.TrimStart('/');
            return name.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? name
                : $"{WslReleaseBase}/{latest}/current/{name}";
        }
        catch
        {
            return FallbackRootfsUrl;
        }
    }

    private static string ResolveLatestNvmTag()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("wsl-import");
        var json = http.GetStringAsync("https://api.github.com/repos/nvm-sh/nvm/releases/latest").GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString();
        if (string.IsNullOrWhiteSpace(tag) || !Regex.IsMatch(tag, "^v\\d+\\.\\d+\\.\\d+$"))
        {
            throw new InvalidOperationException("Unable to resolve latest nvm release tag.");
        }

        return tag;
    }

    private static string PromptRequired(string message)
    {
        while (true)
        {
            Console.Write(message + ": ");
            var value = Console.ReadLine()?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.WriteLine("Value is required.");
        }
    }

    private static string PromptWithDefault(string message, string defaultValue)
    {
        Console.Write($"{message} (press Enter for '{defaultValue}'): ");
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static string PromptPassword(string message)
    {
        while (true)
        {
            Console.Write(message + ": ");
            var sb = new StringBuilder();
            ConsoleKeyInfo key;
            do
            {
                key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
                {
                    sb.Length--;
                    Console.Write("\b \b");
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                    Console.Write("*");
                }
            } while (key.Key != ConsoleKey.Enter);

            Console.WriteLine();
            var pass = sb.ToString();
            if (!string.IsNullOrWhiteSpace(pass))
            {
                return pass;
            }

            Console.WriteLine("Password is required.");
        }
    }

    private static void DownloadWithProgress(string url, string outputPath)
    {
        using var http = new HttpClient();
        using var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        using var input = response.Content.ReadAsStream();
        using var output = File.Create(outputPath);

        var buffer = new byte[81920];
        long read = 0;
        var sw = Stopwatch.StartNew();

        while (true)
        {
            var n = input.Read(buffer, 0, buffer.Length);
            if (n <= 0)
            {
                break;
            }

            output.Write(buffer, 0, n);
            read += n;

            if (total.HasValue && total.Value > 0)
            {
                var pct = (int)(read * 100 / total.Value);
                var rate = read / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                var remainSec = (total.Value - read) / Math.Max(1, rate);
                var eta = TimeSpan.FromSeconds(Math.Max(0, remainSec)).ToString("hh\\:mm\\:ss");
                Console.Write($"\r{pct,3}%  {(read / 1024d / 1024d):N2} MB / {(total.Value / 1024d / 1024d):N2} MB  ETA {eta}   ");
            }
            else
            {
                Console.Write($"\r{(read / 1024d / 1024d):N2} MB downloaded   ");
            }
        }

        Console.WriteLine();
    }

    private static string BashEscape(string value)
    {
        return value.Replace("'", "'\"'\"'");
    }

    private static void RunWslOrThrow(params string[] args)
    {
        var task = Task.Run(() => RunProcessCapture("wsl.exe", args));
        Console.Write("  Working");
        while (!task.Wait(1000))
        {
            Console.Write(".");
        }
        Console.WriteLine();

        var result = task.Result;
        if (result.ExitCode != 0)
        {
            var detail = new[] { result.Output, result.Error }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .FirstOrDefault() ?? "(no output)";
            throw new InvalidOperationException($"wsl command failed: {string.Join(" ", args)}\n{detail}");
        }
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            Console.WriteLine(result.Output.TrimEnd());
        }
    }

    private static void RunWslBashScriptOrThrow(string distroName, string? user, string script)
    {
        var normalized = script.Replace("\r\n", "\n");
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(normalized));
        var runScript = $"echo '{base64}' | base64 -d | bash";

        if (string.IsNullOrWhiteSpace(user))
        {
            RunWslOrThrow("-d", distroName, "--", "exec", "bash", "-lc", runScript);
            return;
        }

        RunWslOrThrow("-d", distroName, "-u", user, "--", "exec", "bash", "-lc", runScript);
    }

    private static void RunProcessNoThrow(string fileName, params string[] args)
    {
        using var p = new Process
        {
            StartInfo = BuildProcessStartInfo(fileName, args)
        };
        p.Start();
        p.WaitForExit();
    }

    private static (int ExitCode, string Output, string Error) RunProcessCapture(string fileName, params string[] args)
    {
        using var p = new Process
        {
            StartInfo = BuildProcessStartInfo(fileName, args)
        };
        p.Start();
        var output = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output, error);
    }

    private static (int ExitCode, string Output, string Error) RunProcessCaptureUtf16(string fileName, params string[] args)
    {
        var psi = BuildProcessStartInfo(fileName, args);
        psi.StandardOutputEncoding = System.Text.Encoding.Unicode; // UTF-16LE
        psi.StandardErrorEncoding  = System.Text.Encoding.Unicode;
        using var p = new Process { StartInfo = psi };
        p.Start();
        var output = p.StandardOutput.ReadToEnd();
        var error  = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output, error);
    }

    private static ProcessStartInfo BuildProcessStartInfo(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        return psi;
    }

    private static bool TryValidateArgs(string[] args, out string? error)
    {
        error = null;

        if (args.Length == 0)
        {
            error = "Missing arguments.";
            return false;
        }
        if (args.Length == 1 && (args[0].Equals("--create", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        if (args.Length == 2 && args[0].Equals("--delete", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(args[1]))
        {
            return true;
        }

        error = "Invalid arguments.";
        return false;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("wsl-import usage:");
        Console.WriteLine("  wsl-import --create");
        Console.WriteLine("  wsl-import --delete <distro-name>");
        Console.WriteLine("  wsl-import --help");
    }
}
