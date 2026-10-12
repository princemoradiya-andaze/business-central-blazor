using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessCentralBlazor.Web.Services;

public sealed class WorkspaceNoteInput
{
    [Required]
    [StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Body { get; set; } = string.Empty;
}

public sealed record WorkspaceNoteListItem(Guid Id, string Title, DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceNoteDetail(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public interface IWorkspaceNoteService
{
    Task<IReadOnlyList<WorkspaceNoteListItem>> GetAllAsync(ClaimsPrincipal user);
    Task<WorkspaceNoteDetail?> GetAsync(ClaimsPrincipal user, Guid noteId);
    Task<Guid> CreateAsync(ClaimsPrincipal user, WorkspaceNoteInput input);
    Task<bool> UpdateAsync(ClaimsPrincipal user, Guid noteId, WorkspaceNoteInput input);
    Task<bool> DeleteAsync(ClaimsPrincipal user, Guid noteId);
}

public sealed class WorkspaceNoteService(
    ErpDbContext dbContext,
    ICompanyScopeAuthorizer companyScopeAuthorizer) : IWorkspaceNoteService
{
    public async Task<IReadOnlyList<WorkspaceNoteListItem>> GetAllAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        return await dbContext.WorkspaceNotes
            .AsNoTracking()
            .Where(note => note.TenantId == company.TenantId && note.CompanyId == company.CompanyId)
            .OrderByDescending(note => note.UpdatedAtUtc)
            .ThenBy(note => note.Title)
            .Select(note => new WorkspaceNoteListItem(note.Id, note.Title, note.UpdatedAtUtc))
            .ToListAsync();
    }

    public async Task<WorkspaceNoteDetail?> GetAsync(ClaimsPrincipal user, Guid noteId)
    {
        var company = await GetRequiredCompanyAsync(user);
        return await dbContext.WorkspaceNotes
            .AsNoTracking()
            .Where(note =>
                note.TenantId == company.TenantId &&
                note.CompanyId == company.CompanyId &&
                note.Id == noteId)
            .Select(note => new WorkspaceNoteDetail(
                note.Id,
                note.Title,
                note.Body,
                note.CreatedAtUtc,
                note.UpdatedAtUtc))
            .SingleOrDefaultAsync();
    }

    public async Task<Guid> CreateAsync(ClaimsPrincipal user, WorkspaceNoteInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        Validate(input);

        var now = DateTimeOffset.UtcNow;
        var note = new WorkspaceNote
        {
            Id = Guid.NewGuid(),
            TenantId = company.TenantId,
            CompanyId = company.CompanyId,
            Title = input.Title.Trim(),
            Body = input.Body.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.WorkspaceNotes.Add(note);
        await dbContext.SaveChangesAsync();
        return note.Id;
    }

    public async Task<bool> UpdateAsync(ClaimsPrincipal user, Guid noteId, WorkspaceNoteInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        Validate(input);

        var note = await dbContext.WorkspaceNotes.SingleOrDefaultAsync(item =>
            item.TenantId == company.TenantId &&
            item.CompanyId == company.CompanyId &&
            item.Id == noteId);
        if (note is null)
        {
            return false;
        }

        note.Title = input.Title.Trim();
        note.Body = input.Body.Trim();
        note.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(ClaimsPrincipal user, Guid noteId)
    {
        var company = await GetRequiredCompanyAsync(user);
        var note = await dbContext.WorkspaceNotes.SingleOrDefaultAsync(item =>
            item.TenantId == company.TenantId &&
            item.CompanyId == company.CompanyId &&
            item.Id == noteId);
        if (note is null)
        {
            return false;
        }

        dbContext.WorkspaceNotes.Remove(note);
        await dbContext.SaveChangesAsync();
        return true;
    }

    private async Task<CompanyMembershipOption> GetRequiredCompanyAsync(ClaimsPrincipal user) =>
        await companyScopeAuthorizer.ResolveActiveCompanyAsync(user)
        ?? throw new UnauthorizedAccessException("An active company is required for workspace notes.");

    private static void Validate(WorkspaceNoteInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
    }
}
