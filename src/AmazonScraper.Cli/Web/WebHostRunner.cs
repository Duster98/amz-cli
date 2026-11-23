using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AmazonScraper.Cli.Web;

public static class WebHostRunner
{
    public static Task<int> RunAsync(
        WebSettings settings,
        CancellationToken cancellationToken) =>
        RunInternalAsync(settings, cancellationToken, resume: null);

    public static Task<int> RunResumeAsync(
        WebResumeState resume,
        CancellationToken cancellationToken)
    {
        var settings = new WebSettings
        {
            Port = resume.Port,
            Remote = resume.Remote,
            NoOpen = true,
            Desktop = resume.Desktop,
            Dev = resume.Development,
            WebDirectory = resume.WebDirectory,
            LogPath = resume.LogPath,
            IdleMinutes = resume.IdleMinutes,
            Quiet = true,
        };
        return RunInternalAsync(settings, cancellationToken, resume);
    }

    private static async Task<int> RunInternalAsync(
        WebSettings settings,
        CancellationToken cancellationToken,
        WebResumeState? resume)
    {
        var desktop = resume?.Desktop ?? settings.Desktop;
        var remote = resume?.Remote ?? settings.Remote;
        var open = resume is null
            && (settings.Open || (desktop && !settings.NoOpen));
        var logPath = resume?.LogPath ?? settings.LogPath ?? WebPaths.LogPath();
        if (resume is not null || desktop)
        {
            if (WebLog.RedirectToFile(logPath) && desktop)
            {
                WebConsole.HideOwnedConsole();
            }
        }

        if (resume is not null)
        {
            if (await WebControl.TryOpenExistingAsync(false, cancellationToken)
                    .ConfigureAwait(false))
            {
                WebResumeStateStore.Delete();
                return 0;
            }
        }
        else
        {
            if (await WebControl.HasIncompatibleInstanceAsync(
                    remote,
                    settings.Port,
                    cancellationToken).ConfigureAwait(false))
            {
                return 1;
            }

            if (await WebControl.TryOpenExistingAsync(open, cancellationToken)
                    .ConfigureAwait(false))
            {
                return 0;
            }
        }

        WebInstanceLock instanceLock;
        try
        {
            instanceLock = WebInstanceLock.Acquire();
        }
        catch (WebException ex)
        {
            if (await WebControl.HasIncompatibleInstanceAsync(
                    remote,
                    settings.Port,
                    cancellationToken).ConfigureAwait(false))
            {
                return 1;
            }

            if (await WebControl.TryOpenExistingAsync(open, cancellationToken)
                    .ConfigureAwait(false))
            {
                return 0;
            }

            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        using (instanceLock)
        {
            var options = new WebOptions
            {
                Port = resume?.Port ?? settings.Port,
                Remote = remote,
                Open = open,
                Desktop = desktop,
                Development = resume?.Development ?? settings.Dev,
                WebDirectory = resume?.WebDirectory ?? settings.WebDirectory,
                LogPath = logPath,
                IdleMinutes = resume?.IdleMinutes ?? settings.IdleMinutes,
                Quiet = resume is null ? settings.Quiet : true,
            };
            var session = resume is null
                ? new WebSessionState { LogPath = logPath }
                : new WebSessionState(resume.InstanceId, resume.Token)
                {
                    LogPath = logPath,
                };
            var assets = WebAssetStore.Load(settings.WebDirectory);
            if (assets.IsFallback && !settings.Quiet)
            {
                Console.WriteLine(
                    "Web UI not built: run './web/build.sh' and try again.");
            }

            var bundle = WebHostFactory.Create(options, session, assets);
            var app = bundle.App;
            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
                var address = ResolveAddress(app);
                session.Port = new Uri(address).Port;

                var lanAddress = remote ? WebNetwork.FindLanAddress() : null;
                if (!string.IsNullOrWhiteSpace(lanAddress))
                {
                    session.Host = WebNetwork.FormatHostForUrl(lanAddress);
                }

                var info = new WebSessionInfo
                {
                    InstanceId = session.InstanceId,
                    Pid = Environment.ProcessId,
                    Port = session.Port,
                    Token = session.Token,
                    StartedAt = session.StartedAt.ToUniversalTime().ToString("O"),
                    Version = VersionInfo.Informational,
                    Url = session.Url,
                    LocalUrl = session.LocalUrl,
                    LanUrl = !string.IsNullOrWhiteSpace(lanAddress)
                        ? session.Url
                        : string.Empty,
                    Remote = remote,
                };
                WebSessionStore.Write(info);
                if (resume is not null)
                {
                    WebResumeStateStore.Delete();
                }

                if (!settings.Quiet)
                {
                    Console.WriteLine($"AMZ CLI Web {VersionInfo.Informational}");
                    Console.WriteLine($"Listening on: http://127.0.0.1:{session.Port}/");
                    if (remote)
                    {
                        if (!string.IsNullOrWhiteSpace(lanAddress))
                        {
                            Console.WriteLine($"LAN: {session.Url}");
                        }
                        else
                        {
                            Console.WriteLine(
                                "LAN: enabled, but no usable network address was detected.");
                        }
                        Console.WriteLine(
                            "LAN access is enabled. Keep the URL private: " +
                            "the token grants access to the interface.");
                    }
                    if (!open)
                    {
                        Console.WriteLine($"Open: {session.Url}");
                    }
                    if (options.Development)
                    {
                        Console.WriteLine(
                            $"Vite: http://localhost:5173/#t={session.Token}");
                    }
                    Console.WriteLine("Press Ctrl+C to stop the interface.");
                }

                if (open && !WebBrowser.Open(session.LocalUrl) && !settings.Quiet)
                {
                    Console.WriteLine(
                        $"Could not open the browser. Open: {session.LocalUrl}");
                }

                using var idleCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var idleTask = StartIdleTask(
                    app,
                    options,
                    session,
                    bundle.Jobs,
                    idleCancellation.Token);
                try
                {
                    await app.WaitForShutdownAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    idleCancellation.Cancel();
                    try
                    {
                        await idleTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when the host stops.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C / SIGTERM: normal shutdown.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Web interface error: {ex.Message}");
                return 1;
            }
            finally
            {
                WebSessionStore.Delete();
                try
                {
                    await app.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The host may already be stopped.
                }
                await app.DisposeAsync().ConfigureAwait(false);
                bundle.Jobs.Dispose();
            }
        }

        return 0;
    }

    private static string ResolveAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features
            .Get<IServerAddressesFeature>()?
            .Addresses;
        var address = addresses?.FirstOrDefault(item =>
            item.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new WebException(
                "Kestrel did not expose a local HTTP address.");
        }
        return address;
    }

    private static Task StartIdleTask(
        WebApplication app,
        WebOptions options,
        WebSessionState session,
        SearchJobManager jobs,
        CancellationToken cancellationToken)
    {
        if (!options.Desktop || options.IdleMinutes <= 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken)
                           .ConfigureAwait(false))
                {
                    if (jobs.HasActiveJobs)
                    {
                        session.Touch();
                        continue;
                    }

                    if (DateTimeOffset.UtcNow - session.LastActivity
                        < TimeSpan.FromMinutes(options.IdleMinutes))
                    {
                        continue;
                    }

                    app.Lifetime.StopApplication();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }, cancellationToken);
    }
}
