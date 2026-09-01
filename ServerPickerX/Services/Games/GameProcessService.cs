using ServerPickerX.Constants;
using ServerPickerX.Services.Servers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ServerPickerX.Services.Games
{
    // Answers whether the game behind a given game mode currently has a running process.
    // This is polled rather than event driven, since a handful of name lookups every few
    // seconds costs far less than a WMI process watcher and needs no extra permissions.
    public class GameProcessService : IGameProcessService
    {
        private readonly ServerDefinitionProvider _serverDefinitionProvider;

        public GameProcessService(ServerDefinitionProvider serverDefinitionProvider)
        {
            _serverDefinitionProvider = serverDefinitionProvider;
        }

        public IReadOnlyList<string> GetProcessNames(string gameMode)
        {
            ServerDefinition? serverDefinition = _serverDefinitionProvider.GetServerDefinitionByGameMode(gameMode);

            if (serverDefinition == null)
            {
                return Array.Empty<string>();
            }

            // A definition can name its own processes, which is the escape hatch for a
            // game that renames its executable. Otherwise fall back to the built in map
            List<string> definitionProcessNames = (serverDefinition.ProcessNames ?? new List<string>())
                .Where(processName => !string.IsNullOrWhiteSpace(processName))
                .Select(processName => processName.Trim())
                .ToList();

            return definitionProcessNames.Count > 0
                ? definitionProcessNames
                : GameProcesses.GetByAppId(serverDefinition.AppId.ToString());
        }

        public bool IsGameRunning(string gameMode)
        {
            foreach (string processName in GetProcessNames(gameMode))
            {
                Process[] processes;

                try
                {
                    processes = Process.GetProcessesByName(NormalizeProcessName(processName));
                }
                catch (Exception)
                {
                    // A process list that cannot be read is treated as "not running"
                    // rather than throwing out of a timer tick
                    continue;
                }

                bool isRunning = processes.Length > 0;

                foreach (Process process in processes)
                {
                    process.Dispose();
                }

                if (isRunning)
                {
                    return true;
                }
            }

            return false;
        }

        // Process.GetProcessesByName never wants the extension, strip it in case a
        // hand edited ServerDefinitions.json includes one
        private static string NormalizeProcessName(string processName)
        {
            return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName[..^4]
                : processName;
        }
    }
}
