using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class WorkspaceNoteServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task WorkspaceNotesSupportScopedCreateReadUpdateAndDelete()
    {
        var email = $"workspace-notes-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "NOTES", "Notes Company");
        var principal = await CreatePrincipalAsync(email, company.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkspaceNoteService>();

        var noteId = await service.CreateAsync(principal, new WorkspaceNoteInput
        {
            Title = "Close checklist",
            Body = "Review the daily close."
        });

        var listedNotes = await service.GetAllAsync(principal);
        var note = await service.GetAsync(principal, noteId);
        Assert.Equal("Close checklist", Assert.Single(listedNotes).Title);
        Assert.NotNull(note);
        Assert.Equal("Review the daily close.", note.Body);

        var updated = await service.UpdateAsync(principal, noteId, new WorkspaceNoteInput
        {
            Title = "Updated checklist",
            Body = "Review and reconcile the daily close."
        });
        Assert.True(updated);
        Assert.Equal("Updated checklist", (await service.GetAsync(principal, noteId))?.Title);

        var deleted = await service.DeleteAsync(principal, noteId);
        Assert.True(deleted);
        Assert.Empty(await service.GetAllAsync(principal));
    }

    [Fact]
    public async Task DirectServiceCallsCannotReadUpdateOrDeleteAnotherCompanyNote()
    {
        var ownerEmail = $"workspace-note-owner-{Guid.NewGuid():N}@example.invalid";
        var otherEmail = $"workspace-note-other-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(ownerEmail, TestPassword);
        await factory.CreateTestUserAsync(otherEmail, TestPassword);
        var ownerCompany = await factory.AddCompanyMembershipAsync(ownerEmail, "NOTEOWNER", "Owner Company");
        var otherCompany = await factory.AddCompanyMembershipAsync(otherEmail, "NOTEOTHER", "Other Company");
        var ownerPrincipal = await CreatePrincipalAsync(ownerEmail, ownerCompany.CompanyId);
        var otherPrincipal = await CreatePrincipalAsync(otherEmail, otherCompany.CompanyId);

        Guid noteId;
        await using (var otherScope = factory.Services.CreateAsyncScope())
        {
            var otherService = otherScope.ServiceProvider.GetRequiredService<IWorkspaceNoteService>();
            noteId = await otherService.CreateAsync(otherPrincipal, new WorkspaceNoteInput
            {
                Title = "Private note",
                Body = "Only the other company should see this."
            });
        }

        await using (var ownerScope = factory.Services.CreateAsyncScope())
        {
            var ownerService = ownerScope.ServiceProvider.GetRequiredService<IWorkspaceNoteService>();
            Assert.Empty(await ownerService.GetAllAsync(ownerPrincipal));
            Assert.Null(await ownerService.GetAsync(ownerPrincipal, noteId));
            Assert.False(await ownerService.UpdateAsync(ownerPrincipal, noteId, new WorkspaceNoteInput
            {
                Title = "Tampered note",
                Body = "Must not be saved."
            }));
            Assert.False(await ownerService.DeleteAsync(ownerPrincipal, noteId));
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyService = verifyScope.ServiceProvider.GetRequiredService<IWorkspaceNoteService>();
        Assert.Equal("Private note", (await verifyService.GetAsync(otherPrincipal, noteId))?.Title);
    }

    [Fact]
    public async Task ServiceRejectsMissingActiveCompanyAndInvalidInput()
    {
        var email = $"workspace-note-validation-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "NOTEVALID", "Validation Company");
        var userId = await GetUserIdAsync(email);
        var principalWithoutCompany = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "test"));
        var principalWithCompany = await CreatePrincipalAsync(email, company.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkspaceNoteService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAllAsync(principalWithoutCompany));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(principalWithCompany, new WorkspaceNoteInput
        {
            Title = "   ",
            Body = "A body"
        }));
    }

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(string email, Guid companyId)
    {
        var userId = await GetUserIdAsync(email);
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ActiveCompanyClaim.Type, companyId.ToString())
        ], "test"));
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user.Id;
    }
}
