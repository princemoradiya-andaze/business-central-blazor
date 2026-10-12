using System.Security.Claims;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessCentralBlazor.Web.Pages.Account;

[Authorize]
public sealed class CompanyModel(
    IActiveCompanyContext activeCompanyContext,
    SignInManager<ErpUser> signInManager) : PageModel
{
    [BindProperty]
    public Guid SelectedCompanyId { get; set; }

    public IReadOnlyList<CompanyMembershipOption> AvailableCompanies { get; private set; } = [];
    public CompanyMembershipOption? ActiveCompany { get; private set; }
    public string? StatusMessage { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadCompaniesAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await signInManager.UserManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        AvailableCompanies = await activeCompanyContext.GetEligibleCompaniesAsync(User);
        var selectedCompany = AvailableCompanies.SingleOrDefault(
            company => company.CompanyId == SelectedCompanyId);
        if (selectedCompany is null)
        {
            StatusMessage = "That company is not available to your account.";
            ActiveCompany = await activeCompanyContext.ResolveAsync(User);
            return Page();
        }

        var authentication = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        await signInManager.SignInWithClaimsAsync(
            user,
            authentication.Properties?.IsPersistent == true,
            [new Claim(ActiveCompanyClaim.Type, selectedCompany.CompanyId.ToString())]);

        return LocalRedirect("/");
    }

    private async Task LoadCompaniesAsync()
    {
        AvailableCompanies = await activeCompanyContext.GetEligibleCompaniesAsync(User);
        ActiveCompany = await activeCompanyContext.ResolveAsync(User);
        if (User.HasClaim(claim => claim.Type == ActiveCompanyClaim.Type) && ActiveCompany is null)
        {
            StatusMessage = "Your previous company selection is no longer valid. Select an available company.";
        }
    }
}
