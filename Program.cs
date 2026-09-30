using hfparquet.Services;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient<HuggingFaceClient>(client =>
{
    client.BaseAddress = new Uri("https://datasets-server.huggingface.co/");
    client.Timeout = TimeSpan.FromSeconds(90);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("HuggingFaceParquetBrowser/1.0");
});
builder.Services.AddHttpClient("HuggingFaceFiles", client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddSingleton<DownloadFileStore>();
builder.Services.AddSingleton<RecentRepositoryStore>();
builder.Services.AddSingleton<DownloadManager>();
builder.Services.AddHostedService<DownloadWorker>();
builder.Services.AddSingleton<ParquetPreviewService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

//app.Use(async (context, next) =>
//{
//    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
//    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
//    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
//    await next();
//});

app.UseRouting();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var request = context.Context.Request;
        var cacheControl = context.Context.Response.Headers["Cache-Control"];
        if (request.Path.StartsWithSegments("/download", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers["Cache-Control"] = "no-cache";
        }
        else if (request.Query.ContainsKey("v"))
        {
            context.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        }
        else if (string.IsNullOrEmpty(cacheControl))
        {
            context.Context.Response.Headers["Cache-Control"] = "no-cache";
        }
    }
});
app.UseAuthorization();

app.MapControllers();

app.MapGet("/_app/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();
