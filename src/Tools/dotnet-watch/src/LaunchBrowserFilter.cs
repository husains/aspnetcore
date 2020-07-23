// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Tools.Internal;

namespace Microsoft.DotNet.Watcher.Tools
{
    public sealed class LaunchBrowserFilter : IWatchFilter, IAsyncDisposable
    {
        private readonly byte[] ReloadMessage = Encoding.UTF8.GetBytes("Reload");
        private readonly byte[] WaitMessage = Encoding.UTF8.GetBytes("Wait");
        private static readonly Regex NowListeningRegex = new Regex(@"^\s*Now listening on: (?<url>.*)$", RegexOptions.None | RegexOptions.Compiled, TimeSpan.FromSeconds(10));
        private static readonly Regex ApplicationStartedRegex = new Regex(@"^\s*Application started\. Press Ctrl\+C to shut down\.$", RegexOptions.None | RegexOptions.Compiled, TimeSpan.FromSeconds(10));

        private readonly bool _runningInTest;
        private readonly bool _suppressLaunchBrowser;
        private readonly string _browserPath;
        private bool _canLaunchBrowser;
        private Process _browserProcess;
        private bool _browserLaunched;
        private BrowserRefreshServer _refreshServer;
        private IReporter _reporter;
        private string _launchPath;

        public LaunchBrowserFilter()
        {
            var suppressLaunchBrowser = Environment.GetEnvironmentVariable("DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER");
            _suppressLaunchBrowser = (suppressLaunchBrowser == "1" || suppressLaunchBrowser == "true");
            _runningInTest = Environment.GetEnvironmentVariable("__DOTNET_WATCH_RUNNING_AS_TEST") == "true";
            _browserPath = Environment.GetEnvironmentVariable("DOTNET_WATCH_BROWSER_PATH");
        }

        public ValueTask ProcessAsync(DotNetWatchContext context, CancellationToken cancellationToken)
        {
            if (_suppressLaunchBrowser)
            {
                return default;
            }

            if (context.Iteration == 0)
            {
                _reporter = context.Reporter;

                if (CanLaunchBrowser(context, out var launchPath))
                {
                    _canLaunchBrowser = true;
                    _launchPath = launchPath;

                    _refreshServer = new BrowserRefreshServer(context.Reporter);
                    var serverUrl = _refreshServer.Start();

                    context.Reporter.Verbose($"Refresh server running at {serverUrl}.");
                    context.ProcessSpec.EnvironmentVariables["DOTNET_WATCH_REFRESH_URL"] = serverUrl;

                    context.ProcessSpec.OnOutput += OnOutput;
                }
            }

            if (_canLaunchBrowser)
            {
                if (context.Iteration > 0)
                {
                    // We've detected a change. Notify the browser.
                    _refreshServer.SendMessage(WaitMessage);
                }
            }

            return default;
        }

        private void OnOutput(object sender, DataReceivedEventArgs eventArgs)
        {
            // We've redirected the output, but want to ensure that continues to appear in the user's console.
            Console.WriteLine(eventArgs.Data);

            if (string.IsNullOrEmpty(eventArgs.Data))
            {
                return;
            }

            if (ApplicationStartedRegex.IsMatch(eventArgs.Data))
            {
                var process = (Process)sender;
                process.OutputDataReceived -= OnOutput;
                process.CancelOutputRead();
            }
            else
            {
                var match = NowListeningRegex.Match(eventArgs.Data);
                if (match.Success)
                {
                    var launchUrl = match.Groups["url"].Value;

                    if (!_browserLaunched)
                    {
                        _browserLaunched = true;
                        try
                        {
                            LaunchBrowser(launchUrl);
                        }
                        catch (Exception ex)
                        {
                            _reporter.Output($"Unable to launch browser: {ex}");
                            _canLaunchBrowser = false;
                        }
                    }
                    else
                    {
                        _reporter.Verbose($"Reloading browser");
                        _refreshServer.SendMessage(ReloadMessage);
                    }
                }
            }
        }

        private void LaunchBrowser(string launchUrl)
        {
            var fileName = launchUrl + "/" + _launchPath;
            var args = string.Empty;
            if (!string.IsNullOrEmpty(_browserPath))
            {
                args = fileName;
                fileName = _browserPath;
            }

            if (_runningInTest)
            {
                _reporter.Output($"Launching browser: {fileName} {args}");
                return;
            }

            _browserProcess = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = true,
            });
        }

        private static bool CanLaunchBrowser(DotNetWatchContext context, out string launchUrl)
        {
            launchUrl = null;

            if (!context.FileSet.IsNetCoreApp31OrNewer)
            {
                // Browser refresh middleware supports 3.1 or newer
                return false;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Launching a browser requires file associations that are not available in all operating systems.
                return false;
            }

            var reporter = context.Reporter;

            var dotnetCommand = context.ProcessSpec.Arguments.FirstOrDefault();
            if (!string.Equals(dotnetCommand, "run", StringComparison.Ordinal))
            {
                return false;
            }

            // We're executing the run-command. Determine if the launchSettings allows it
            var launchSettingsPath = Path.Combine(context.ProcessSpec.WorkingDirectory, "Properties", "launchSettings.json");
            if (!File.Exists(launchSettingsPath))
            {
                return false;
            }

            LaunchSettingsJson launchSettings;
            try
            {
                launchSettings = JsonSerializer.Deserialize<LaunchSettingsJson>(
                    File.ReadAllText(launchSettingsPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch
            {
                return false;
            }

            var defaultProfile = launchSettings.Profiles.FirstOrDefault(f => f.Value.CommandName == "Project").Value;
            if (defaultProfile is null)
            {
                return false;
            }

            launchUrl = defaultProfile.LaunchUrl;
            return defaultProfile.LaunchBrowser;
        }

        public async ValueTask DisposeAsync()
        {
            _browserProcess?.Dispose();
            if (_refreshServer != null)
            {
                await _refreshServer.DisposeAsync();
            }
        }
    }
}
