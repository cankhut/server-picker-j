using Avalonia;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.FontAwesome;
using Optris.Icons.Avalonia.MaterialDesign;
using ServerPickerX.Constants;
using ServerPickerX.Settings;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;

namespace ServerPickerX
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            IconProvider.Current
            .Register<FontAwesomeIconProvider>()
            .Register<MaterialDesignIconProvider>();

            object platformOptions = OperatingSystem.IsWindows()
                ? new Win32PlatformOptions
                {
                    RenderingMode = ResolveWindowsRenderMode()
                }
                : new X11PlatformOptions
                {
                    RenderingMode = ResolveLinuxRenderMode()
                };

            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(platformOptions)
#if DEBUG
                .WithDeveloperTools()
#endif
                .LogToTrace();
        }

        // Settings are read directly here rather than through the DI container,
        // the rendering pipeline has to be chosen before Avalonia is initialised
        [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(Stream, JsonSerializerOptions)")]
        private static Win32RenderingMode[] ResolveWindowsRenderMode()
        {
            Win32RenderingMode[] defaultRenderMode = [Win32RenderingMode.Software];

            JsonSetting? localSettings = TryReadSettings();

            return localSettings is null
                ? defaultRenderMode
                : [RenderModes.ResolveWindowsRenderMode(localSettings.render_mode)];
        }

        [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(Stream, JsonSerializerOptions)")]
        private static X11RenderingMode[] ResolveLinuxRenderMode()
        {
            X11RenderingMode[] defaultRenderMode = [X11RenderingMode.Software];

            JsonSetting? localSettings = TryReadSettings();

            return localSettings is null
                ? defaultRenderMode
                : [RenderModes.ResolveLinuxRenderMode(localSettings.render_mode)];
        }

        [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(Stream, JsonSerializerOptions)")]
        private static JsonSetting? TryReadSettings()
        {
            try
            {
                string jsonFilePath = new JsonSetting().jsonFilePath;

                if (!File.Exists(jsonFilePath))
                {
                    return null;
                }

                using FileStream settingsFile = File.OpenRead(jsonFilePath);

                return JsonSerializer.Deserialize<JsonSetting>(settingsFile);
            }
            catch
            {
                return null;
            }
        }
    }
}
