using System;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VkPostman.Core.Services;
using VkPostman.Data;
using VkPostman.Templates;
using VkPostman.Wpf.Services;
using VkPostman.Wpf.ViewModels;

namespace VkPostman.Wpf;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface unhandled exceptions as a dialog instead of silently dying.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "VK Postman — unhandled error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            MessageBox.Show(args.ExceptionObject?.ToString() ?? "(null)",
                "VK Postman — fatal error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VkPostman",
            "VkPostman.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<VkPostmanDbContext>(options =>
                    options.UseSqlite($"Data Source={dbPath}"));

                services.AddSingleton<ITemplateEngine, ScribanTemplateEngine>();

                services.AddScoped<DraftService>();
                services.AddScoped<GroupService>();
                services.AddScoped<TemplateService>();
                services.AddScoped<ExchangeService>();

                services.AddTransient<MainViewModel>();
                services.AddTransient<DraftsViewModel>();
                services.AddTransient<GroupsViewModel>();
                services.AddTransient<TemplatesViewModel>();

                services.AddTransient<MainWindow>();
            })
            .ConfigureLogging(logging =>
            {
                logging.AddDebug();
            })
            .Build();

        // First run: create tables. On schema mismatch (older DB on disk), wipe + recreate.
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VkPostmanDbContext>();
            db.Database.EnsureCreated();
            try
            {
                // Smoke-test the current schema.
                _ = db.TargetGroups.Select(g => new { g.Id, g.PostTemplateId }).Take(0).ToList();
                _ = db.PostDrafts.Select(d => new { d.Id, d.CommonText }).Take(0).ToList();
            }
            catch
            {
                db.Database.EnsureDeleted();
                db.Database.EnsureCreated();
            }
        }

        await _host.StartAsync();

        var main = _host.Services.GetRequiredService<MainWindow>();
        main.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        main.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
