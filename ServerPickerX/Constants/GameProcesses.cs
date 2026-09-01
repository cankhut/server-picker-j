using System;
using System.Collections.Generic;

namespace ServerPickerX.Constants
{
    // Process names to watch per Steam app id, without the .exe suffix. These live here
    // rather than in ServerDefinitions.json because that file is only written when it is
    // missing, so installs made before this feature would carry no names at all. A
    // definition may still override them with its own ProcessNames entry.
    public static class GameProcesses
    {
        private static readonly Dictionary<string, string[]> ProcessNamesByAppId = new(StringComparer.OrdinalIgnoreCase)
        {
            // Counter Strike 2, shared by the global and the Perfect World definitions
            { "730", new string[] { "cs2" } },
            // Deadlock, project8 is the name the client shipped under before the rename
            { "1422450", new string[] { "deadlock", "project8" } },
            { "3065800", new string[] { "marathon" } },
        };

        public static IReadOnlyList<string> GetByAppId(string appId)
        {
            return ProcessNamesByAppId.TryGetValue(appId ?? string.Empty, out string[]? processNames)
                ? processNames
                : Array.Empty<string>();
        }
    }
}
