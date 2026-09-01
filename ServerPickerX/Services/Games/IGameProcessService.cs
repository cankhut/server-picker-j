using System.Collections.Generic;

namespace ServerPickerX.Services.Games
{
    public interface IGameProcessService
    {
        IReadOnlyList<string> GetProcessNames(string gameMode);

        bool IsGameRunning(string gameMode);
    }
}
