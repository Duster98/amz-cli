using AmazonScraper.Installer;

namespace AmazonScraper.Installer.Tests;

public class InstallerShortcutTests
{
    [Fact]
    public void Linux_desktop_entry_opens_the_web_app()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"amz-cli-shortcut-tests-{Guid.NewGuid():N}");
        var previousDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            var installDir = Path.Combine(root, "install");
            Directory.CreateDirectory(installDir);
            var executable = Path.Combine(installDir, "amz-cli");
            File.WriteAllText(executable, "#!/bin/sh\nexit 0\n");
            var dataHome = Path.Combine(root, "data");
            Environment.SetEnvironmentVariable(
                "XDG_DATA_HOME",
                dataHome);

            var applications = Path.Combine(dataHome, "applications");
            Directory.CreateDirectory(applications);
            var legacyEntry = Path.Combine(applications, "amazon.desktop");
            File.WriteAllText(legacyEntry, "legacy");

            InstallerPathIntegration.CreateApplicationShortcuts(
                installDir,
                quiet: true);

            var desktopEntry = Path.Combine(
                applications,
                "amz-cli.desktop");
            Assert.True(File.Exists(desktopEntry));
            Assert.False(File.Exists(legacyEntry));
            var content = File.ReadAllText(desktopEntry);
            Assert.Contains("Name=AMZ CLI", content);
            Assert.Contains($"Exec=\"{executable}\" web --desktop", content);
            Assert.Contains("Icon=system-search", content);
            Assert.Contains("Categories=Utility;", content);
            Assert.Contains("Terminal=false", content);

            InstallerPathIntegration.RemoveApplicationShortcuts();
            Assert.False(File.Exists(desktopEntry));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousDataHome);
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }
}
