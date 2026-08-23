using Avalonia.Logging;
using ServerPickerX.Models;
using ServerPickerX.Services.Localizations;
using ServerPickerX.Services.Loggers;
using ServerPickerX.Services.MessageBoxes;
using ServerPickerX.Services.Processes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetFwTypeLib;

namespace ServerPickerX.Services.SystemFirewalls
{
    public class WindowsFirewallService(
        ILoggerService _loggerService,
        ILocalizationService _localizationService,
        IMessageBoxService _messageBoxService,
        IProcessService _processService
        ) : ISystemFirewallService
    {
        private const string _firewallRulePrefix = "server_picker_x_";
        private const NET_FW_RULE_DIRECTION_ _firewallRuleDirectionOutbound = NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_OUT;
        private const NET_FW_ACTION_ _firewallRuleActionBlock = NET_FW_ACTION_.NET_FW_ACTION_BLOCK;
        private const int _firewallRuleProtocolAny = 256;
        private const int _firewallRuleProfilesAll = int.MaxValue;

        public async Task BlockServersAsync(ObservableCollection<ServerModel> serverModels)
        {
            try
            {
                INetFwPolicy2 firewallPolicy = GetFirewallPolicyApi();
                INetFwRules firewallRules = firewallPolicy.Rules;

                foreach (var serverModel in serverModels)
                {
                    string ruleName = GetFirewallRuleName(serverModel);
                    string ipAddresses = string.Join(",", serverModel.RelayModels.Select(s => s.IPv4));

                    RemoveFirewallRuleByName(firewallRules, ruleName);

                    INetFwRule firewallRule = CreateFirewallRuleApi();
                    firewallRule.Name = ruleName;
                    firewallRule.Description = serverModel.Description;
                    firewallRule.Direction = _firewallRuleDirectionOutbound;
                    firewallRule.Action = _firewallRuleActionBlock;
                    firewallRule.Protocol = _firewallRuleProtocolAny;
                    firewallRule.RemoteAddresses = ipAddresses;
                    firewallRule.Enabled = true;
                    firewallRule.Profiles = _firewallRuleProfilesAll;

                    firewallRules.Add(firewallRule);
                }
            }
            catch (Exception ex)
            {
                await _loggerService.LogErrorAsync(ex.Message);
                throw;
            }
        }

        public async Task UnblockServersAsync(ObservableCollection<ServerModel> serverModels)
        {
            try
            {
                INetFwPolicy2 firewallPolicy = GetFirewallPolicyApi();
                INetFwRules firewallRules = firewallPolicy.Rules;

                foreach (var serverModel in serverModels)
                {
                    string ruleName = GetFirewallRuleName(serverModel);
                    RemoveFirewallRuleByName(firewallRules, ruleName);
                }
            }
            catch (Exception ex)
            {
                await _loggerService.LogErrorAsync(ex.Message);
                throw;
            }
        }

        // Removes every rule this app created, by name prefix, and leaves the rest of
        // the machine's firewall configuration alone. The previous implementation ran
        // "netsh advfirewall reset", which restores Windows defaults and deletes every
        // rule on the system, including ones other applications rely on
        public async Task ResetFirewallAsync(ObservableCollection<ServerModel> serverModels)
        {
            bool confirmed = await _messageBoxService.ShowMessageBoxConfirmationAsync(
                    _localizationService.GetLocaleValue("MessageBoxInfoTitle"),
                    _localizationService.GetLocaleValue("FirewallResetConfirmDialogue"),
                    MsBox.Avalonia.Enums.Icon.Warning
                );

            if (!confirmed) return;

            try
            {
                INetFwPolicy2 firewallPolicy = GetFirewallPolicyApi();
                INetFwRules firewallRules = firewallPolicy.Rules;

                // Names are collected first, removing while enumerating invalidates the iterator
                List<string> ownedRuleNames = [];

                foreach (INetFwRule firewallRule in firewallRules)
                {
                    string? ruleName = null;

                    try
                    {
                        ruleName = firewallRule.Name;
                    }
                    catch (Exception)
                    {
                        // A malformed rule elsewhere in the table must not abort the sweep
                    }

                    if (ruleName != null
                        && ruleName.StartsWith(_firewallRulePrefix, StringComparison.OrdinalIgnoreCase)
                        && !ownedRuleNames.Contains(ruleName))
                    {
                        ownedRuleNames.Add(ruleName);
                    }
                }

                foreach (string ruleName in ownedRuleNames)
                {
                    RemoveFirewallRuleByName(firewallRules, ruleName);
                }

                await _loggerService.LogInfoAsync($"Removed {ownedRuleNames.Count} firewall rules created by this app");

                await _messageBoxService.ShowMessageBoxAsync(
                    _localizationService.GetLocaleValue("MessageBoxInfoTitle"),
                    string.Format(
                        _localizationService.GetLocaleValue("FirewallResetSuccessDialogue"),
                        ownedRuleNames.Count
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

        public async Task<List<ServerModel>?> GetBlockedServersAsync(ObservableCollection<ServerModel> serverModels)
        {
            try
            {
                INetFwPolicy2 firewallPolicy = GetFirewallPolicyApi();
                INetFwRules firewallRules = firewallPolicy.Rules;

                List<ServerModel> blockedServerModels = [];

                foreach (var serverModel in serverModels)
                {
                    if (TryGetFirewallRule(firewallRules, GetFirewallRuleName(serverModel)) != null)
                    {
                        blockedServerModels.Add(serverModel);
                    }
                }

                return blockedServerModels;
            }
            catch (Exception ex)
            {
                await _loggerService.LogWarningAsync("Could not read firewall rules: " + ex.Message);

                return null;
            }
        }

        public INetFwPolicy2 GetFirewallPolicyApi()
            => (INetFwPolicy2)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("E2B3C97F-6AE1-41AC-817A-F6F92166D7DD"))!)!;

        public INetFwRule CreateFirewallRuleApi()
            => (INetFwRule)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("2C5BC43E-3369-4C33-AB0C-BE9469677AF4"))!)!;

        public string GetFirewallRuleName(ServerModel serverModel)
            => _firewallRulePrefix + serverModel.Description.Replace(" ", "");

        public void RemoveFirewallRuleByName(INetFwRules firewallRules, string ruleName)
        {
            while (TryGetFirewallRule(firewallRules, ruleName) != null)
            {
                firewallRules.Remove(ruleName);
            }
        }

        public INetFwRule? TryGetFirewallRule(INetFwRules firewallRules, string ruleName)
        {
            try 
            {
                return firewallRules.Item(ruleName);
            } catch { 
                return null;
            }
        }
    }
}
