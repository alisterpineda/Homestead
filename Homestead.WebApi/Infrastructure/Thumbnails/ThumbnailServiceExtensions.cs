namespace Homestead.WebApi.Infrastructure.Thumbnails;

public static class ThumbnailServiceExtensions
{
    public static IServiceCollection AddThumbnails(this IServiceCollection services)
    {
        services.AddSingleton<IThumbnailGenerator, ImageThumbnailGenerator>();
        services.AddSingleton<IThumbnailGenerator, PdfThumbnailGenerator>();
        services.AddSingleton<ThumbnailService>();

        return services;
    }
}
