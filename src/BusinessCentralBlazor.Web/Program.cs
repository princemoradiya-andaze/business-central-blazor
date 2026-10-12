using BusinessCentralBlazor.Web.Components;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Infrastructure.Persistence;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextFactory<ErpDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("ErpDatabase");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Set ConnectionStrings__ErpDatabase before accessing identity-backed application features.");
    }

    options.UseSqlServer(connectionString);
});
builder.Services.AddIdentity<ErpUser, ErpRole>(ErpIdentityDefaults.Configure)
    .AddEntityFrameworkStores<ErpDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/access-denied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});
builder.Services.AddAuthorization(options =>
    options.AddPolicy(
        CompanyAuthorizationPolicies.ActiveCompany,
        policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveCompanyRequirement())));
builder.Services.AddScoped<IAuthorizationHandler, ActiveCompanyAuthorizationHandler>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IActiveCompanyContext, ActiveCompanyContext>();
builder.Services.AddScoped<ICompanyScopeAuthorizer, CompanyScopeAuthorizer>();
builder.Services.AddScoped<IWorkspaceNoteService, WorkspaceNoteService>();
builder.Services.AddScoped<IGeneralLedgerAccountService, GeneralLedgerAccountService>();
builder.Services.AddScoped<IDimensionService, DimensionService>();
builder.Services.AddScoped<IGeneralJournalService, GeneralJournalService>();
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorPages();
app.MapRazorComponents<App>()
    .RequireAuthorization(CompanyAuthorizationPolicies.ActiveCompany)
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
