using ServerPickerX.Models;
using ServerPickerX.Services.Localizations;
using ServerPickerX.Services.Loggers;
using ServerPickerX.Services.MessageBoxes;
using ServerPickerX.Services.Processes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ServerPickerX.Services.SystemFirewalls
{
    public class LinuxFirewallService(
        ILoggerService _loggerService,
        ILocalizationService _localizationService,
        IMessageBoxService _messageBoxService,
        IProcessService _processService
        ) : ISystemFirewallService
    {
        public async Task BlockServersAsync(ObservableCollection<ServerModel> serverModels)
        {
            foreach (var serverModel in serverModels)
            {
                string ipAddresses = string.Join(",", serverModel.RelayModels.Select(s => s.IPv4).ToList());

                using var process = _processService.CreateProcess("sudo");

                try
                {
                    process.StartInfo.Arguments = "iptables " +
                        "-A INPUT -s " + ipAddresses + " -j DROP";

                    process.Start();
                    await process.WaitForExitAsync();

                    string stdOut = process.StandardOutput.ReadToEnd().Trim();
                    string stdErr = process.StandardError.ReadToEnd().Trim();

                    if (process.ExitCode > 0)
                    {
                        await _loggerService.LogWarningAsync("StdOut: " + stdOut + " StdErr: " + stdErr);
                    }
                }
                catch (Exception ex)
                {
                    // Perform debugging here if necessary (log error or through debugger breakpoints)
                    throw;
                }
            }
        }

        public async Task UnblockServersAsync(ObservableCollection<ServerModel> serverModels)
        {
            foreach (var serverModel in serverModels)
            {
                string ipAddresses = string.Join(",", serverModel.RelayModels.Select(s => s.IPv4).ToList());

                using var process = _processService.CreateProcess("sudo");

                try
                {
                    process.StartInfo.Arguments = "iptables " +
                       "-D INPUT -s " + ipAddresses + " -j DROP";

                    process.Start();
                    await process.WaitForExitAsync();

                    string stdOut = process.StandardOutput.ReadToEnd().Trim();
                    string stdErr = process.StandardError.ReadToEnd().Trim();

                    if (process.ExitCode > 0)
                    {
                        await _loggerService.LogWarningAsync("StdOut: " + stdOut + " StdErr: " + stdErr);
                    }
                }
                catch (Exception ex)
                {
                    // Perform debugging here if necessary (log error or through debugger breakpoints)
                    throw;
                }
            }
        }

        // Deletes only the DROP rules this app added, one per server. The previous
        // implementation ran "iptables -F", which flushes every rule in the table and
        // would take unrelated rules, including ones protecting remote access, with it
        public async Task ResetFirewallAsync(ObservableCollection<ServerModel> serverModels)
        {
            bool confirmed = await _messageBoxService.ShowMessageBoxConfirmationAsync(
                _localizationService.GetLocaleValue("MessageBoxInfoTitle"),
                _localizationService.GetLocaleValue("FirewallResetConfirmDialogue"),
                MsBox.Avalonia.Enums.Icon.Warning
                );

            if (!confirmed)
            {
                return;
            }

            try
            {
                await UnblockServersAsync(serverModels);

                await _loggerService.LogInfoAsync($"Removed firewall rules for {serverModels.Count} servers");

                await _messageBoxService.ShowMessageBoxAsync(
                    _localizationService.GetLocaleValue("MessageBoxInfoTitle"),
                    string.Format(
                        _localizationService.GetLocaleValue("FirewallResetSuccessDialogue"),
                        serverModels.Count
                        ),
                    MsBox.Avalonia.Enums.Icon.Success
                    );
            }
            catch (Exception ex)
            {
                await _loggerService.LogErrorAsync(ex.Message);
                throw;
            }
        }

        // iptables rules carry no marker tying them back to a server, so the saved
        // state stays authoritative here rather than being reconciled against nothing
        public Task<List<ServerModel>?> GetBlockedServersAsync(ObservableCollection<ServerModel> serverModels)
            => Task.FromResult<List<ServerModel>?>(null);
    }
}