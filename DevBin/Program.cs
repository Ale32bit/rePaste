global using DevBin.Models;
using AspNetCoreRateLimit;
using DevBin.Data;
using DevBin.Services.HCaptcha;
using DevBin.Services.SMTP;
using DevBin.Utils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Prometheus;
using System.Globalization;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

var configurationPath = Path.Combine(Environment.CurrentDirectory, "Configuration");
if (!File.Exists(Path.Combine(configurationPath, "appsettings.json")))
{
    File.Copy(Path.Combine(Environment.CurrentDirectory, "Setup", "appsettings.json"), Path.Combine(configurationPath, "appsettings.json"));
}

builder.Configuration
    .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "Configuration", "appsettings.json"), optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "Configuration", $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: true);

// Add services to the container.

// Setup database and cache connections
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var serverVersion = ServerVersion.AutoDetect(connectionString);
    options.UseMySql(connectionString, serverVersion);
    //options.UseLazyLoadingProxies();
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
// builder.Services.AddStackExchangeRedisCache(o =>
// {
//     o.Configuration = redisConnectionString;
//     o.InstanceName = "DevBin:";
// });

// Rate limit
// builder.Services.AddSingleton<IConnectionMultiplexer>(provider => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.Configure<IpRateLimitPolicies>(builder.Configuration.GetSection("IpRateLimitPolicies"));
// builder.Services.AddRedisRateLimiting();
builder.Services.AddInMemoryRateLimiting();


// Persist logins between restarts
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<ApplicationDbContext>()
    .SetApplicationName("rePaste");

// Add email sender support
builder.Services.Configure<SMTPConfig>(builder.Configuration.GetSection("SMTP"));
builder.Services.AddTransient<IEmailSender, EmailSender>();

// Add HCaptcha
builder.Services.AddSingleton(new HCaptchaOptions()
{
    SiteKey = builder.Configuration["HCaptcha:SiteKey"],
    SecretKey = builder.Configuration["HCaptcha:SecretKey"],
});

builder.Services.AddScoped<HCaptcha>();

// Configure user logins and requirements
builder.Services.AddDefaultIdentity<ApplicationUser>((IdentityOptions options) =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
    options.Password = new PasswordOptions
    {
        RequireDigit = true,
        RequiredLength = 8,
        RequireLowercase = false,
        RequireUppercase = false,
        RequiredUniqueChars = 1,
        RequireNonAlphanumeric = false,
    };
    options.User.RequireUniqueEmail = true;
})
    .AddRoles<IdentityRole<int>>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Resolve /xxxxxxxx to pastes
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AddPageRoute("/Paste", $"/{{code:length({builder.Configuration["Paste:CodeLength"]})}}");
})
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();

// Configure external logins

var authenticationBuilder = builder.Services.AddAuthentication();
var authenticationConfig = builder.Configuration.GetSection("Authentication");

// GitHub Authentication
if (authenticationConfig.GetValue<bool>("GitHub:Enabled"))
{
    authenticationBuilder.AddGitHub(o =>
    {
        o.ClientId = builder.Configuration["Authentication:GitHub:ClientID"];
        o.ClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"];
        o.SaveTokens = true;
    });
}

// Discord Authentication
if (authenticationConfig.GetValue<bool>("Discord:Enabled"))
{
    authenticationBuilder.AddDiscord(o =>
    {
        o.ClientId = builder.Configuration["Authentication:Discord:ClientID"];
        o.ClientSecret = builder.Configuration["Authentication:Discord:ClientSecret"];
        o.Scope.Add("identify");
        o.Scope.Add("email");
        o.SaveTokens = true;
    });
}

builder.Services.AddAuthorization();

// Add developer docs
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var openApiInfo = new OpenApiInfo()
    {
        Title = "rePaste",
        Version = "v3",
        Description = "This API provides access to the most common features of the service.<br/>" +
        "A developer API token is required and must be put in the request header as \"Authorization\"." +
        "<h4>API Rate limit</h4>" +
        "<p>POST API requests are limited to max 10 requests every 60 seconds.<br/>" +
        "All other methods are limited to max 10 requests every 10 seconds.</p>",
        License = new()
        {
            Name = "GNU AGPLv3",
            Url = new("https://github.com/Ale32bit/rePaste/blob/main/LICENSE"),
        },
    };
    options.SwaggerDoc("v3", openApiInfo);

    options.DocumentFilter<ApiNameNormalizeFilter>();

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Authorization header",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header,

            },
            new List<string>()
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}API.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});

// Support for reverse proxies, like NGINX
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.All;
});

builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    var locales = JsonConvert.DeserializeObject<string[]>(File.ReadAllText("Setup/Locale.json"));
    var cultures = locales.Select(locale => new CultureInfo(locale)).ToList();

    o.DefaultRequestCulture = new RequestCulture("en");
    o.SupportedCultures = cultures;
    o.SupportedUICultures = cultures;
    o.FallBackToParentUICultures = true;
});

builder.Services.AddLocalization(options => { options.ResourcesPath = "Resources"; });

#if DEBUG
builder.Services.AddSassCompiler();
#endif

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");


    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}


app.UseStatusCodePages();
app.UseStatusCodePagesWithReExecute("/Error");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization();

app.UseIpRateLimiting();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseHttpMetrics();

app.MapControllers();
app.MapRazorPages();
if (app.Configuration.GetValue("EnablePrometheus", false))
{
    app.MapMetrics();
}

app.UseSwagger(c => { c.RouteTemplate = "docs/{documentname}/swagger.json"; });
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "rePaste";
    options.SwaggerEndpoint("/docs/v3/swagger.json", "rePaste v3");
    options.RoutePrefix = "docs";
    options.HeadContent = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "wwwroot", "swagger", "menu.html"));
    options.InjectStylesheet("/lib/bootstrap/dist/css/bootstrap.css");
    options.InjectStylesheet("/lib/font-awesome/css/all.min.css");
    //options.InjectStylesheet("/css/site.css");

});

app.Logger.LogInformation("Working directory: {directory}", Environment.CurrentDirectory);

app.Logger.LogInformation("Setting up...");
await using var scope = app.Services.CreateAsyncScope();
var services = scope.ServiceProvider;
var context = services.GetRequiredService<ApplicationDbContext>();

await context.Database.MigrateAsync();

var roleManager = services.GetRequiredService<RoleManager<IdentityRole<int>>>();
if (!await roleManager.RoleExistsAsync("Administrator"))
{
    var administratorRole = new IdentityRole<int>("Administrator");
    var result = await roleManager.CreateAsync(administratorRole);
    if (!result.Succeeded)
    {
        foreach (var error in result.Errors)
        {
            app.Logger.LogError($"[{error.Code}] {error.Description}");
        }
    }
}

if (!await context.Exposures.AnyAsync())
{
    var exposures = JsonConvert.DeserializeObject<Exposure[]>(
        await File.ReadAllTextAsync(Path.Combine(Environment.CurrentDirectory, "Setup", "Exposures.json"))
    );

    if (exposures == null)
    {
        app.Logger.LogError("Could not parse Setup/Exposures.json");
        return;
    }

    foreach (var exposure in exposures.OrderBy(q => q.Id))
    {
        context.Add(exposure);
    }

    await context.SaveChangesAsync();

    app.Logger.LogInformation("Populated exposures");
}

if (!await context.Syntaxes.AnyAsync())
{
    var syntaxes = JsonConvert.DeserializeObject<Syntax[]>(
        await File.ReadAllTextAsync(Path.Combine(Environment.CurrentDirectory, "Setup", "Syntaxes.json"))
    );

    if (syntaxes == null)
    {
        app.Logger.LogError("Could not parse Setup/Syntaxes.json");
        return;
    }

    foreach (var syntax in syntaxes)
    {
        context.Add(syntax);
    }

    await context.SaveChangesAsync();

    app.Logger.LogInformation("Populated syntaxes");
}

app.Run();
