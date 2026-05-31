namespace D2CompanionMvc.Services.Assets;

public static class AssetPackMiddlewareExtensions
{
    public static IApplicationBuilder UseD2AssetPackFallback(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var assetPack = context.RequestServices.GetRequiredService<AssetPackService>();
            if (await assetPack.TryServeAsync(context, context.RequestAborted))
            {
                return;
            }

            await next();
        });
    }
}
