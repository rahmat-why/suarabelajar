using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

// ===============================
// SERVICES
// ===============================
builder.Services.AddControllersWithViews();

// Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// ===============================
// PIPELINE
// ===============================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".mp3"] = "audio/mpeg";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider,
    ServeUnknownFileTypes = true
});

app.UseRouting();

// Session MUST be before filters
app.UseSession();

// Even if you don't use cookie auth now,
// keep this order correct
app.UseAuthentication();
app.UseAuthorization();

// ===============================
// ROUTING
// ===============================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Audiobook}/{action=RedeemCode}/{id?}");

app.Run();