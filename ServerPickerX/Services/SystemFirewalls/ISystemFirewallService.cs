using ServerPickerX.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace ServerPickerX.Services.SystemFirewalls
{
    public interface ISystemFirewallService
    {
        Task BlockServersAsync(ObservableCollection<ServerModel> serverModels);
        Task UnblockServersAsync(ObservableCollection<ServerModel> serverModels);
        // Removes only the rules this app created. Never touches unrelated rules
        Task ResetFirewallAsync(ObservableCollection<ServerModel> serverModels);

        // Servers that currently have a rule, or null when the platform cannot say.
        // Rules outlive the process, so the saved state can be stale by the next launch
        Task<List<ServerModel>?> GetBlockedServersAsync(ObservableCollection<ServerModel> serverModels);
    }
}