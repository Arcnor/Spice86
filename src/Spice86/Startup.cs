namespace Spice86; 

using Microsoft.Extensions.DependencyInjection;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.DependencyInjection;
using Spice86.Shared.Interfaces;
/// <summary>
/// Provides a method to initialize services and set the logging level based on command line arguments.
/// </summary>
internal static class Startup {
    
    /// <summary>
    /// Initializes the service collection and sets the logging level.
    /// </summary>
    /// <param name="commandLineArgs">The command line arguments.</param>
    /// <returns>A <see cref="ServiceProvider"/> instance that can be used to retrieve registered services.</returns>
    public static ServiceProvider StartupInjectedServices(string[] commandLineArgs)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        SetLoggingLevel(serviceProvider, commandLineArgs);
        return serviceProvider;
    }

    /// <summary>
    /// Sets the logging level based on the command line arguments.
    /// </summary>
    /// <param name="serviceProvider">The <see cref="ServiceProvider"/> instance used to retrieve the <see cref="ILoggerService"/>.</param>
    /// <param name="commandLineArgs">The command line arguments.</param>
    private static void SetLoggingLevel(ServiceProvider serviceProvider, string[] commandLineArgs) {
        ILoggerService? loggerService = serviceProvider.GetService<ILoggerService>();
        if (loggerService is null) {
            return;
        }
        Configuration configuration = CommandLineParser.ParseCommandLine(commandLineArgs);

        if (configuration.SilencedLogs)
        {
            loggerService.AreLogsSilenced = true;
        }
        else if (configuration.WarningLogs)
        {
            loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Warning;
        }
        else if (configuration.VerboseLogs)
        {
            loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Verbose;
        }
    }
}
