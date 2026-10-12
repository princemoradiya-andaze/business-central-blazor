using System.ComponentModel.DataAnnotations;
using BusinessCentralBlazor.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusinessCentralBlazor.Web.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(SignInManager<ErpUser> signInManager) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        ReturnUrl = GetLocalReturnUrl(ReturnUrl);
        return User.Identity?.IsAuthenticated == true
            ? LocalRedirect(ReturnUrl)
            : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
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
