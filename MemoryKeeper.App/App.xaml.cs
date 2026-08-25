using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Services;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.App.Views;
using MemoryKeeper.Application;
using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Navigation;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure;
using MemoryKeeper.Infrastructure.Database;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace MemoryKeeper.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private IHost? _host;
    private Window? _mainWindow;

    public Window? MainWindow => _mainWindow;

    /// <summary>
    /// Setup status resolved during OnLaunched (step [4]). MainWindow may reuse this.
    /// </summary>
    public static SetupStatusDto? LaunchSetupStatus { get; private set; }

    public static string DatabaseDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MemoryKeeper");

    public App()
    {
        StartupDiagnostics.WriteStep("[1] App constructor start");

        try
        {
            StartupDiagnostics.WriteStep("[2] App InitializeComponent 시작");
            InitializeComponent();
            UnhandledException += OnUnhandledException;
            StartupDiagnostics.WriteStep("[2] App InitializeComponent 완료");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("[2] App Initialize", ex);
            ErrorDialog.Show(
                ErrorReportSource.Startup,
                "Memory Keeper — 초기화 실패",
                ex,
                stage: "[2] App Initialize");
            throw;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.Exception is not null)
            {
                StartupDiagnostics.WriteException("App.UnhandledException", e.Exception);
                GalleryDiagnostics.WriteException("App.UnhandledException", e.Exception);
                ErrorDialog.Show(
                    ErrorReportSource.Unhandled,
                    "Memory Keeper — 예기치 않은 오류",
                    e.Exception,
                    stage: "App.UnhandledException");
            }
            else
            {
                StartupDiagnostics.WriteStep($"App.UnhandledException (no Exception object): {e.Message}");
                ErrorDialog.ShowMessage(
                    ErrorReportSource.Unhandled,
                    "Memory Keeper — 예기치 않은 오류",
                    e.Message ?? "Unknown unhandled exception",
                    stage: "App.UnhandledException");
            }

            // Keep the app alive so Gallery/navigation failures do not terminate the process.
            e.Handled = true;
        }
        catch
        {
            // Ignore diagnostics failures during crash handling.
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            StartupDiagnostics.WriteStep("[3] DI Container 생성 시작");
            _host = CreateHost();
            StartupDiagnostics.WriteStep("[3] DI Container 생성 완료");

            var logger = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MemoryKeeper.App");
            HttpImageLoader.Configure(_host.Services.GetRequiredService<BackendMediaDownloader>());
            _ = CheckBackendConnectionAsync(_host.Services, logger);
            logger.LogInformation(
                "Memory Keeper starting. DatabaseDirectory={DatabaseDirectory}, StartupLog={StartupLog}",
                DatabaseDirectory,
                StartupDiagnostics.LogFilePath);

            try
            {
                var initializationResult = await DatabaseInitializer.InitializeAsync(
                    _host.Services,
                    DatabaseDirectory);
                StartupDiagnostics.WriteStep($"[3.1] Database Initialize 완료: {initializationResult.Summary}");
                logger.LogInformation("Database status: {Summary}", initializationResult.Summary);

                StartupDiagnostics.WriteStep("[4] SetupWizardService 호출");
                using (var scope = _host.Services.CreateScope())
                {
                    var setupWizard = scope.ServiceProvider.GetRequiredService<SetupWizardService>();
                    LaunchSetupStatus = await setupWizard.GetStatusAsync();
                    StartupDiagnostics.WriteStep(
                        $"[4] SetupWizardService 완료 NeedsSetup={LaunchSetupStatus.NeedsSetup}, HasHomeLocation={LaunchSetupStatus.HasHomeLocation}");

                }

                StartupDiagnostics.WriteStep("[5] MainWindow 생성 시작");
                MainWindow mainWindow;
                try
                {
                    mainWindow = _host.Services.GetRequiredService<MainWindow>();
                    mainWindow.ViewModel.ApplyDatabaseStatus(initializationResult.Summary);
                    StartupDiagnostics.WriteStep("[5] MainWindow 생성 완료");
                }
                catch (Exception ex)
                {
                    StartupDiagnostics.WriteException("[5] MainWindow 생성", ex);
                    ErrorDialog.Show(
                        ErrorReportSource.Startup,
                        "Memory Keeper — MainWindow 생성 실패",
                        ex,
                        stage: "[5] MainWindow 생성");
                    return;
                }

                try
                {
                    using var scope = _host.Services.CreateScope();
                    var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();
                    var issues = await storageService.ValidateStoragesAsync();
                    if (issues.Count > 0)
                    {
                        var names = string.Join(", ", issues.Select(item => $"{item.StorageName} ({item.PhotoRoot})"));
                        mainWindow.ViewModel.SetUiStatus(
                            $"MemoryKeeper 저장소에 접근할 수 없습니다: {names}. 설정에서 폴더를 다시 선택하세요.");
                        logger.LogWarning("Storage PhotoRoot validation failed. Count={Count}", issues.Count);
                        StartupDiagnostics.WriteStep($"[5.1] Storage validation issues: {issues.Count}");
                    }
                    else
                    {
                        var pathSync = scope.ServiceProvider.GetRequiredService<IMediaLibraryPathSyncService>();
                        var moved = await pathSync.SyncAllAsync();
                        StartupDiagnostics.WriteStep($"[5.2] Library path sync moved={moved}");
                        if (moved > 0)
                        {
                            logger.LogInformation("Synced library folders to place classification. Moved={Moved}", moved);
                        }
                    }
                }
                catch (Exception ex)
                {
                    StartupDiagnostics.WriteException("[5.1] Storage validation", ex);
                    logger.LogWarning(ex, "Storage validation skipped due to error.");
                }

                _mainWindow = mainWindow;
                ErrorDialog.RegisterUiDispatcher(DispatcherQueue.GetForCurrentThread());
                _mainWindow.Activate();
                StartupDiagnostics.WriteStep("[6] MainWindow Activate 완료");
            }
            catch (Exception ex)
            {
                StartupDiagnostics.WriteException("OnLaunched (post-DI)", ex);
                ErrorDialog.Show(
                    ErrorReportSource.Startup,
                    "Memory Keeper — 시작 실패",
                    ex,
                    stage: "OnLaunched (post-DI)");
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("[3] DI Container 생성", ex);
            ErrorDialog.Show(
                ErrorReportSource.Startup,
                "Memory Keeper — DI 실패",
                ex,
                stage: "[3] DI Container 생성");
        }
    }

    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddApplicationServices();
                services.AddInfrastructureServices();
                services.AddTcBackendApiClient(context.Configuration);
                services.Configure<ImportUploadOptions>(
                    context.Configuration.GetSection(ImportUploadOptions.SectionName));
                services.AddSingleton<IGalleryApiRepository, GalleryApiRepository>();
                services.AddSingleton<IGalleryPhotoCatalog, GalleryPhotoCatalog>();
                services.AddSingleton<IUploadApiRepository, UploadApiRepository>();
                services.AddSingleton<IUploadJobApiRepository, UploadJobApiRepository>();
                services.AddSingleton<IMemoryKeeperPlaceApiRepository, MemoryKeeperPlaceApiRepository>();
                services.AddSingleton<IMemoryKeeperWriteApiRepository, MemoryKeeperWriteApiRepository>();
                services.AddSingleton<IMemoryKeeperOperationsApiRepository, MemoryKeeperOperationsApiRepository>();
                services.AddTransient<IPhotoExportSource, NasPhotoExportSource>();
                services.AddSingleton<IBackendChangeFeed, BackendChangeFeedRepository>();
                services.AddMemoryKeeperDatabase(DatabaseDirectory);

                services.AddSingleton<IFolderPickerService, FolderPickerService>();
                services.AddSingleton<IFileDialogService, FileDialogService>();
                services.AddTransient<StorageUiOperations>();
                services.AddSingleton<IPlaceFocusState, PlaceFocusState>();
                services.AddSingleton<IPlaceEditorSeedState, PlaceEditorSeedState>();
                services.AddSingleton<IGalleryFocusState, GalleryFocusState>();
                services.AddSingleton<IPhotoNavigationState, PhotoNavigationState>();
                services.AddSingleton<ITravelRecordsNavigationState, TravelRecordsNavigationState>();
                services.AddSingleton<IShellFileService, ShellFileService>();
                services.AddSingleton<IThumbnailService, ThumbnailService>();
                services.AddSingleton<ILocalPreviewCacheService>(sp =>
                {
                    var thumbnail = sp.GetRequiredService<IThumbnailService>();
                    return new MemoryKeeper.Infrastructure.Storage.LocalPreviewCacheService(
                        thumbnail.CacheRootPath,
                        sp.GetRequiredService<ILogger<MemoryKeeper.Infrastructure.Storage.LocalPreviewCacheService>>());
                });
                services.AddSingleton<IResponsiveLayoutService, ResponsiveLayoutService>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IPrototypeMaintenanceService>(sp =>
                {
                    var thumbnail = sp.GetRequiredService<IThumbnailService>();
                    return new MemoryKeeper.Infrastructure.Database.PrototypeMaintenanceService(
                        sp.GetRequiredService<IServiceScopeFactory>(),
                        DatabaseDirectory,
                        thumbnail.CacheRootPath,
                        sp.GetRequiredService<ILogger<MemoryKeeper.Infrastructure.Database.PrototypeMaintenanceService>>());
                });
                services.AddTransient<MainViewModel>();
                services.AddSingleton<StorageManagementViewModel>();
                services.AddTransient<StorageManagementPage>();
                services.AddSingleton<ImportViewModel>();
                services.AddTransient<ImportView>();
                services.AddTransient<ImportPage>();
                services.AddTransient<PhotoManagementView>();
                services.AddTransient<PhotoManagementPage>();
                services.AddTransient<PlaceManagementViewModel>();
                services.AddTransient<PlaceManagementView>();
                services.AddTransient<PlaceManagementPage>();
                services.AddTransient<TimelineViewModel>();
                services.AddTransient<TimelinePage>();
                services.AddTransient<VisitRecordViewModel>();
                services.AddTransient<VisitRecordPage>();
                services.AddTransient<HomeViewModel>();
                services.AddTransient<HomePage>();
                services.AddTransient<TravelRecordsViewModel>();
                services.AddTransient<TravelRecordsPage>();
                services.AddTransient<TravelRecordsDetailViewModel>();
                services.AddTransient<TravelRecordsDetailPage>();
                services.AddTransient<PlaceMapViewModel>();
                services.AddTransient<PlaceMapPage>();
                services.AddTransient<GalleryViewModel>();
                services.AddTransient<GalleryPage>();
                services.AddTransient<FavoritesViewModel>();
                services.AddTransient<FavoritesPage>();
                services.AddTransient<PendingMemoryViewModel>();
                services.AddTransient<PendingMemoryView>();
                services.AddTransient<PendingMemoryPage>();
                services.AddTransient<PhotoDetailViewModel>();
                services.AddTransient<PhotoDetailView>();
                services.AddTransient<PhotoDetailPage>();
                services.AddTransient<PhotoViewerViewModel>();
                services.AddTransient<PhotoViewerPage>();
                services.AddTransient<TagManagementViewModel>();
                services.AddTransient<TagManagementView>();
                services.AddTransient<TagManagementPage>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<SetupWizardViewModel>();
                services.AddTransient<SetupWizardPage>();
                services.AddTransient<MainWindow>();
            })
            .Build();
    }

    private static async Task CheckBackendConnectionAsync(
        IServiceProvider services,
        ILogger logger)
    {
        try
        {
            var connection = services.GetRequiredService<BackendConnectionService>();
            var status = await connection.CheckAsync();
            if (status.IsConnected)
            {
                logger.LogInformation(
                    "TC-Backend connected. Version={Version}, ApiVersion={ApiVersion}",
                    status.Health?.Version,
                    status.Capabilities?.ApiVersion);
            }
            else
            {
                logger.LogWarning(
                    "TC-Backend unavailable; local features remain available. Category={Category}",
                    status.ErrorCategory);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TC-Backend startup check was skipped after an unexpected error.");
        }
    }
}
