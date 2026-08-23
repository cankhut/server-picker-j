using MsBox.Avalonia.Enums;
using System.Threading.Tasks;

namespace ServerPickerX.Services.MessageBoxes
{
    public interface IMessageBoxService
    {
        Task ShowMessageBoxAsync(string title, string text, Icon icon = Icon.Info);
        Task<bool> ShowMessageBoxConfirmationAsync(string title, string text, Icon icon = Icon.Info);

        // Returns the name of the button the user pressed, or an empty string if the
        // dialog was dismissed without choosing
        Task<string> ShowMessageBoxChoiceAsync(string title, string text, string[] buttonNames, Icon icon = Icon.Info);
        Task ShowMessageBoxWithLinkAsync(string title, string text, string url, Icon icon = Icon.Info);
    }
}