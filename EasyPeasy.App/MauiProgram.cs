using EasyPeasy.Data;
using EasyPeasy.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using EasyPeasy.Core.Interfaces.Services;
using EasyPeasy.Services;
using AutoMapper;
using EasyPeasy.Core.Mapping;
using MukhaLab.Database;
using MukhaLab.BrowserConsoleLogger;
using MukhaLab.LoggerExtensionDelegate;
using Microsoft.EntityFrameworkCore.Internal;
using EasyPeasy.Data.Extensions;
using EasyPeasy.Business.Extensions;
using EasyPeasy.App.Services;
using EasyPeasy.App.Interfaces;
using EasyPeasy.App.Models;
using EasyPeasy.Cache.Extensions;
using EasyPeasy.Core.Extensions;
using EasyPeasy.Core.Interfaces.Storage;
using Plugin.Maui.Audio;
using EasyPeasy.App.Services.Speech;
using EasyPeasy.App.Services.SpeechRecognition;

namespace EasyPeasy.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        RegisterGlobalExceptionHandlers();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Додаємо конфігурацію з appsettings.json
        LoadConfiguration(builder);

        builder.Services.AddMauiBlazorWebView();

        // === КОНФІГУРАЦІЯ БАЗИ ДАНИХ SQLite ===
        ConfigureDatabase(builder);

        ConfigureAutoMapper(builder.Services);

        // Реєстрація сервісів та репозиторіїв
        builder.Services.AddEasyPeasyRepositories();
        builder.Services.AddEasyPeasyDataServices();
        builder.Services.AddEasyPeasyCacheServices();
        RegisterServices(builder.Services);

        builder.Services.AddBrowserConsoleService();

        // ВАЖЛИВО: Замінюємо небезпечне логування на безпечне
        ConfigureLogging(builder);

        var app = builder.Build();

        // Ініціалізація бази даних при запуску (асинхронно, щоб не блокувати UI)
        _ = Task.Run(async () => await InitializeDatabaseAsync(app.Services));

        return app;
    }

    /// <summary>
    /// Крос-платформна страхувальна сітка: пише необроблені винятки у файл лога,
    /// щоб раптовий крах застосунку (напр. з нативного коду на Windows) можна було
    /// діагностувати замість тихого завершення процесу без жодного сліду.
    /// </summary>
    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            EasyPeasy.App.Diagnostics.CrashLogger.Log("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            EasyPeasy.App.Diagnostics.CrashLogger.Log("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Агресивна конфігурація логування для браузерної консолі
    /// </summary>
    private static void ConfigureLogging(MauiAppBuilder builder)
    {
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();

        // Стандартне логування для Debug консолі
        builder.Logging.AddDebug();

        // Агресивний браузерний логер
        builder.Logging.AddBrowserConsole();

        // Налаштування рівнів логування
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        // Фільтри для зменшення шуму
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Infrastructure", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
#else
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
#endif
    }

    /// <summary>
    /// Завантаження конфігурації з appsettings.json
    /// </summary>
    private static void LoadConfiguration(MauiAppBuilder builder)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("EasyPeasy.App.appsettings.json");
            if (stream != null)
            {
                var config = new ConfigurationBuilder()
                    .AddJsonStream(stream)
                    .Build();
                builder.Configuration.AddConfiguration(config);
            }
        }
        catch (Exception ex)
        {
            // Логування помилки завантаження конфігурації
            System.Diagnostics.Debug.WriteLine($"Помилка завантаження appsettings.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Конфігурація бази даних
    /// </summary>
    private static void ConfigureDatabase(MauiAppBuilder builder)
    {
        var connectionString = GetConnectionString(builder.Configuration);

        MoveDatabaseFromLegacyName(connectionString);

        // ✅ Factory з правильним типом
        builder.Services.AddDbContextFactory<EasyPeasyDbContext>(options =>
        {
            options.UseSqlite(connectionString);
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
#if DEBUG
            // options.EnableSensitiveDataLogging();
            // options.EnableDetailedErrors();
#endif
        });
    }

    /// <summary>
    /// Отримання connection string з конфігурації
    /// </summary>
    private static string GetConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? $"Data Source={DatabaseFileName}";

        // Якщо connection string містить {AppDataPath}, замінюємо на реальний шлях
        if (connectionString.Contains("{AppDataPath}"))
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            connectionString = connectionString.Replace("{AppDataPath}", appDataPath);
        }
        else if (!Path.IsPathRooted(connectionString.Replace("Data Source=", "")))
        {
            // Якщо шлях відносний, робимо його абсолютним
            var dbFileName = connectionString.Replace("Data Source=", "");
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbPath = Path.Combine(appDataPath, dbFileName);
            connectionString = $"Data Source={dbPath}";
        }

        return connectionString;
    }

    /// <summary>The database file. Named after the vendor and the app, like the ApplicationId.</summary>
    private const string DatabaseFileName = "mukhalab.easypeasy.db";

    /// <summary>What the file was called before the app was renamed from EasyEnglish.</summary>
    private const string LegacyDatabaseFileName = "EasyEnglish.db";

    /// <summary>
    /// Renames a database left over from before the app was renamed. The work is in
    /// <see cref="LegacyDatabaseMover"/>, which is plain file handling and therefore testable;
    /// here it only has to be harmless when it fails — a failed move leaves the old file where it
    /// was, and the app starts on an empty database rather than not starting at all.
    /// </summary>
    private static void MoveDatabaseFromLegacyName(string connectionString)
    {
        try
        {
            LegacyDatabaseMover.Move(connectionString, LegacyDatabaseFileName);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not move the database to its new name: {ex.Message}");
        }
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IUserContext, AnonymousUserContext>();
        services.AddScoped<CourseZipBackupService>();
        services.AddScoped<ConfirmationService>();
        services.AddScoped<DemoContentService>();

        services.AddSingleton<IStorageService, LocalStorageService>();
        services.AddSingleton<StreakService>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<PracticeQueueService>();
        services.AddSingleton<StorageMaintenanceService>();
        services.AddSingleton<RecentActivityService>();
        services.AddScoped<HomeStatsService>();

        services.AddSingleton(AudioManager.Current);
        services.AddTransient<IAudioService, AudioService>();

        //services.AddAudio();

        services.AddSingleton<ITextToSpeech>(_ => TextToSpeech.Default);
        services.AddSingleton<VoiceSettingsService>();
        services.AddSingleton<VoiceAvailabilityService>();
        services.AddScoped<VoicePickerViewModel>();
        services.AddScoped<SpeechPlayer>();

#if WINDOWS
        services.AddSingleton<IVoiceProvider, WindowsVoiceProvider>();
        services.AddSingleton<ISpeechEngine,  WindowsSpeechEngine>();
        services.AddTransient<IPronunciationCheckService, WindowsPronunciationCheckService>();
#else
        services.AddSingleton<IVoiceProvider, MauiVoiceProvider>();
        services.AddSingleton<ISpeechEngine, MauiSpeechEngine>();
        services.AddTransient<IPronunciationCheckService, UnsupportedPronunciationCheckService>();
#endif

        services.AddSingleton<ISpeechService, MauiSpeechService>();
    }

    private static void ConfigureAutoMapper(IServiceCollection services)
    {
        services.AddAutoMapper(config =>
        {
            config.AddProfile<MappingProfile>();
            config.AddProfile<MappingUIProfile>();
        });

        // AutoMapper builds the profile's mapping actions and type converters through the container,
        // so each one has to be registered or the map that uses it throws at map time.
        services.AddEasyPeasyMappingServices();
    }

    /// <summary>
    /// Асинхронна ініціалізація бази даних з повторними спробами
    /// </summary>
    private static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        const int maxRetries = 3;
        const int delayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var contextFactory = services.GetRequiredService<IDbContextFactory<EasyPeasyDbContext>>();
                await using var context = await contextFactory.CreateDbContextAsync();

                var loggerFactory = services.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger("DatabaseInitializer");

                logger?.LogInformation("🔄 Ініціалізація бази даних (спроба {Attempt}/{MaxRetries})...", attempt, maxRetries);

                var canConnect = await context.Database.CanConnectAsync();
                if (!canConnect)
                {
                    logger?.LogWarning("⚠️ Не вдалося підключитися до БД. Створюємо базу даних...");
                }

                var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                var pendingMigrationsList = pendingMigrations.ToList();

                if (pendingMigrationsList.Any())
                {
                    logger?.LogInformation("📋 Знайдено {Count} незастосованих міграцій: {Migrations}",
                        pendingMigrationsList.Count, string.Join(", ", pendingMigrationsList));

                    logger?.LogInformation("🚀 Застосування міграцій...");
                    await context.Database.MigrateAsync();
                    logger?.LogInformation("✅ Міграції застосовано успішно!");
                }
                else
                {
                    await context.Database.EnsureCreatedAsync();
                    logger?.LogInformation("✅ База даних готова до роботи!");
                }

                await SeedInitialDataAsync(context, logger);

                logger?.LogInformation("🎉 Ініціалізація бази даних завершена успішно!");
                return;
            }
            catch (Exception ex)
            {
                var loggerFactory = services.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger("DatabaseInitializer");

                logger?.LogError(ex, "❌ Помилка ініціалізації БД (спроба {Attempt}/{MaxRetries}): {Error}",
                    attempt, maxRetries, ex.Message);

                if (attempt == maxRetries)
                {
                    logger?.LogCritical("💥 Всі спроби ініціалізації БД вичерпано! Додаток може працювати нестабільно.");
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"Critical database error: {ex}");
#endif
                    return;
                }

                await Task.Delay(delayMs * attempt);
            }
        }
    }

    /// <summary>
    /// Заповнення початковими даними
    /// </summary>
    private static async Task SeedInitialDataAsync(EasyPeasyDbContext context, ILogger? logger)
    {
        try
        {
            // Перевіряємо чи база даних порожня
            var hasCourses = await context.Courses.AnyAsync();

            if (!hasCourses)
            {
                logger?.LogInformation("🌱 Заповнення початковими даними...");

                // Тут можна додати початкові дані
                // Наприклад, створити базовий словник:
                /*
                var defaultCourse = new CourseEntity
                {
                    Guid = Guid.NewGuid().ToString(),
                    Title = "Базовий словник",
                    Description = "Створено автоматично при першому запуску",
                    CreatedAt = DateTime.UtcNow.ToString("O"),
                    UpdatedAt = DateTime.UtcNow.ToString("O")
                };

                context.Courses.Add(defaultCourse);
                await context.SaveChangesAsync();
                */

                logger?.LogInformation("✅ Початкові дані додано успішно!");
            }
            else
            {
                logger?.LogDebug("📝 Початкові дані вже існують");
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "⚠️ Помилка при заповненні початковими даними: {Error}", ex.Message);
            // Не критична помилка, продовжуємо роботу
        }
    }
}