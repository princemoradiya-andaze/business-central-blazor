using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessCentralBlazor.Web.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(
    SignInManager<ErpUser> signInManager,
    IActiveCompanyContext activeCompanyContext) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool PasswordStep { get; private set; }

    public IActionResult OnPostNext()
    {
        ReturnUrl = GetLocalReturnUrl(ReturnUrl);
        ModelState.Remove("Input.Password");
        PasswordStep = ModelState.IsValid;
        Input.Password = "";
        return Page();
    }

    public IActionResult OnPostBack()
    {
        ReturnUrl = GetLocalReturnUrl(ReturnUrl);
        Input.Password = "";
        ModelState.Clear();
        return Page();
    }

    public IActionResult OnGet()
    {
        ReturnUrl = GetLocalReturnUrl(ReturnUrl);
        return User.Identity?.IsAuthenticated == true
            ? LocalRedirect(ReturnUrl)
            : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        PasswordStep = true;
        ReturnUrl = GetLocalReturnUrl(ReturnUrl);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(
            Input.Email,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);
        if (result.Succeeded)
        {
            var user = await signInManager.UserManager.FindByEmailAsync(Input.Email)
                ?? throw new InvalidOperationException("The signed-in account could not be loaded.");
            var userPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
                IdentityConstants.ApplicationScheme));
            var availableCompanies = await activeCompanyContext.GetEligibleCompaniesAsync(userPrincipal);
            if (availableCompanies.Count == 1)
            {
                await signInManager.SignInWithClaimsAsync(
                    user,
                    Input.RememberMe,
                    [new Claim(ActiveCompanyClaim.Type, availableCompanies[0].CompanyId.ToString())]);
                return LocalRedirect(ReturnUrl);
            }

            if (availableCompanies.Count > 1)
            {
                return RedirectToPage("/Account/Company", new { returnUrl = ReturnUrl });
            }

            return LocalRedirect(ReturnUrl);
        }

        ModelState.AddModelError(string.Empty, "Sign-in failed. Check your credentials or contact an administrator.");
        return Page();
    }

    private string GetLocalReturnUrl(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

    public sealed class LoginInput
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
