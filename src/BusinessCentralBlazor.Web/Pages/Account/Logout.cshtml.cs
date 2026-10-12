using BusinessCentralBlazor.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessCentralBlazor.Web.Pages.Account;

[Authorize]
public sealed class LogoutModel(SignInManager<ErpUser> signInManager) : PageModel
{
    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return LocalRedirect("/account/login");
    }
}
