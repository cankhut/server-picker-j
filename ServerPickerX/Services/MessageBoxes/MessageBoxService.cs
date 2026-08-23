using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ServerPickerX.Helpers;
using ServerPickerX.Services.Loggers;
using ServerPickerX.Services.Processes;
using ServerPickerX.Views;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ServerPickerX.Services.MessageBoxes
{
    public class MessageBoxService : IMessageBoxService
    {
        private readonly IProcessService _processService;
        private readonly ILoggerService _logger;

        public MessageBoxService(ILoggerService logger, IProcessService processService)
        {
            _logger = logger;
            _processService = processService;
        }

        public async Task ShowMessageBoxAsync(string title, string text, Icon icon = Icon.Info)
        {
            try
            {
                var customMbsParams = new MessageBoxCustomParams
                {
                    ContentTitle = title,
                    ContentMessage = text,
                    ButtonDefinitions = [
                        new() { Name = "Ok", },
                    ],
                    WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                    ShowInCenter = true,
                    CanResize = false,
                    Icon = title == "Error" ? Icon.Error : icon,
                    Topmost = true,
                };

                var box = MessageBoxManager.GetMessageBoxCustom(customMbsParams);

                await box.ShowWindowDialogAsync(MainWindow.Instance!);
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync("Failed to show message box", ex.Message);
            }
        }

        public async Task<bool> ShowMessageBoxConfirmationAsync(string title, string text, Icon icon = Icon.Info)
        {
            try
            {
                var customMbsParams = new MessageBoxCustomParams
                {
                    ContentTitle = title,
                    ContentMessage = text,
                    ButtonDefinitions = [
                        new() { Name = "Ok", },
                        new() { Name = "Cancel", },
                    ],
                    WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                    ShowInCenter = true,
                    CanResize = false,
                    Icon = title == "Error" ? Icon.Error : icon,
                    Topmost = true,
                };

                var box = MessageBoxManager.GetMessageBoxCustom(customMbsParams);

                var result = await box.ShowWindowDialogAsync(MainWindow.Instance!);

                return result == "Ok";
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync("Failed to show message box confirmation", ex.Message);
                return false;
            }
        }

        public async Task<string> ShowMessageBoxChoiceAsync(string title, string text, string[] buttonNames, Icon icon = Icon.Info)
        {
            try
            {
                var customMbsParams = new MessageBoxCustomParams
                {
                    ContentTitle = title,
                    ContentMessage = text,
                    ButtonDefinitions = buttonNames
                        .Select(buttonName => new MsBox.Avalonia.Models.ButtonDefinition { Name = buttonName })
                        .ToList(),
                    WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                    ShowInCenter = true,
                    CanResize = false,
                    Icon = title == "Error" ? Icon.Error : icon,
                    Topmost = true,
                };

                var box = MessageBoxManager.GetMessageBoxCustom(customMbsParams);

                return await box.ShowWindowDialogAsync(MainWindow.Instance!) ?? string.Empty;
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync("Failed to show message box choice", ex.Message);

                return string.Empty;
            }
        }

        public async Task ShowMessageBoxWithLinkAsync(string title, string text, string url, Icon icon = Icon.Info)
        {
            try
            {
                var customMbsParams = new MessageBoxCustomParams
                {
                    ContentTitle = title,
                    ContentMessage = text,
                    ButtonDefinitions = [
                            new() { Name = "Ok", },
                            new() { Name = "Cancel", },
                        ],
                    WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                    ShowInCenter = true,
                    CanResize = false,
                    Icon = title == "Error" ? Icon.Error : icon,
                    Topmost = true,
                };

                var box = MessageBoxManager.GetMessageBoxCustom(customMbsParams);

                var result = await box.ShowWindowDialogAsync(MainWindow.Instance!);

                if (result != "Ok")
                {
                    return;
                }

                await _processService.OpenUrl(url);
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync("Failed to show message box with hyperlink", ex.Message);
            }
        }
    }
}