using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace AK.BuildingBlocks.Swagger;

public static class SwaggerExtensions
{
    // Gates Swagger middleware to Development only.
    // AddSwaggerGen() remains in each service's DI registration — only the middleware
    // that serves the JSON spec and the UI is suppressed in non-Development environments.
    // title: shown in the Swagger UI header, e.g. "AK.Products API v1"
    public static WebApplication UseSwaggerInDevelopment(this WebApplication app, string title)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", title));
        }
        return app;
    }
}
