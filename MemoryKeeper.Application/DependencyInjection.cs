using MemoryKeeper.Application.Services;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MemoryKeeper.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ICatalogInvalidation, CatalogInvalidation>();
        services.AddTransient<MediaService>();
        services.AddTransient<GalleryHierarchyService>();
        services.AddTransient<MediaImportService>();
        services.AddTransient<StorageService>();
        services.AddTransient<PlaceAssignmentService>();
        services.AddTransient<PlaceService>();
        services.AddTransient<PlacePickerService>();
        services.AddTransient<VisitRecordService>();
        services.AddTransient<IMemorySearchAnalyzer, RuleBasedMemorySearchAnalyzer>();
        services.AddTransient<MemorySearchService>();
        services.AddTransient<MemoryGroupingService>();
        services.AddTransient<MediaPlaceAssignmentService>();
        services.AddTransient<PendingMemoryService>();
        services.AddTransient<PhotoDetailService>();
        services.AddTransient<TagService>();
        services.AddTransient<VisitRecordQueryService>();
        services.AddTransient<HomeDashboardService>();
        services.AddTransient<HomeLocationService>();
        services.AddTransient<TravelRecordsService>();
        services.AddTransient<SetupWizardService>();
        services.AddTransient<IPlaceReclassificationService, PlaceReclassificationService>();
        services.AddTransient<IMediaLibraryPathSyncService, MediaLibraryPathSyncService>();
        services.AddTransient<LibraryCopyIntegrityService>();
        services.AddTransient<PlaceRenormalizationService>();
        services.AddTransient<IPlaceDisplayNameRefreshService, PlaceDisplayNameRefreshService>();
        services.AddTransient<GetLibraryUseCase>();
        return services;
    }
}
