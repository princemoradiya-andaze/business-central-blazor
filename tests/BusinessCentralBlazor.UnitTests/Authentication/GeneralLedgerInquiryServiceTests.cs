using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Infrastructure.Persistence;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class GeneralLedgerInquiryServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task EntrySearchAccountActivityAndTrialBalanceUseCompanyScopedPostedEntries()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("gl-inquiry-owner");
        var (otherPrincipal, otherCompanyId) = await CreateUserAndCompanyAsync("gl-inquiry-other");
        var cashId = await AddAccountAsync(companyId, "1000", "Cash");
        var equityId = await AddAccountAsync(companyId, "3000", "Equity");
        var expenseId = await AddAccountAsync(companyId, "6000", "Expense");
        var otherCashId = await AddAccountAsync(otherCompanyId, "1000", "Other cash");
        var otherEquityId = await AddAccountAsync(otherCompanyId, "3000", "Other equity");
        var periodId = await CreatePeriodAsync(principal);
        var otherPeriodId = await CreatePeriodAsync(otherPrincipal);

        await PostBatchAsync(principal, periodId, cashId, equityId, new DateOnly(2026, 10, 1), "OPEN-1", 100m);
        await PostBatchAsync(principal, periodId, expenseId, cashId, new DateOnly(2026, 10, 5), "RCPT-42", 40m);
        await PostBatchAsync(
            otherPrincipal,
            otherPeriodId,
            otherCashId,
            otherEquityId,
            new DateOnly(2026, 10, 5),
            "PRIVATE-1",
            900m);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralLedgerInquiryService>();

        var filteredEntries = await service.GetEntriesAsync(principal, new GeneralLedgerEntryFilter
        {
            FromDate = new DateOnly(2026, 10, 2),
            ThroughDate = new DateOnly(2026, 10, 5),
            AccountId = cashId,
            DocumentNumber = "RCPT"
        });
        var filteredEntry = Assert.Single(filteredEntries.Entries);
        Assert.Equal("RCPT-42", filteredEntry.DocumentNumber);
        Assert.Equal(cashId, filteredEntry.AccountId);
        Assert.Equal(0m, filteredEntries.TotalDebit);
        Assert.Equal(40m, filteredEntries.TotalCredit);
        Assert.DoesNotContain(filteredEntries.Entries, entry => entry.AccountId == otherCashId);

        var activity = await service.GetAccountActivityAsync(
            principal,
            cashId,
            new DateOnly(2026, 10, 2),
            new DateOnly(2026, 10, 5));
        Assert.NotNull(activity);
        Assert.Equal(100m, activity.OpeningDebit);
        Assert.Equal(0m, activity.OpeningCredit);
        Assert.Equal(0m, activity.PeriodDebit);
        Assert.Equal(40m, activity.PeriodCredit);
        Assert.Equal(60m, activity.ClosingDebit);
        Assert.Equal(0m, activity.ClosingCredit);

        var trialBalance = await service.GetTrialBalanceAsync(principal, new DateOnly(2026, 10, 5));
        Assert.Equal(100m, trialBalance.TotalDebit);
        Assert.Equal(100m, trialBalance.TotalCredit);
        Assert.DoesNotContain(trialBalance.Lines, line => line.AccountId == otherCashId);
        Assert.Equal(60m, Assert.Single(trialBalance.Lines, line => line.AccountId == cashId).DebitBalance);
        Assert.Equal(40m, Assert.Single(trialBalance.Lines, line => line.AccountId == expenseId).DebitBalance);
        Assert.Equal(100m, Assert.Single(trialBalance.Lines, line => line.AccountId == equityId).CreditBalance);

        var emptyReport = await service.GetEntriesAsync(principal, new GeneralLedgerEntryFilter
        {
            FromDate = new DateOnly(2026, 11, 1),
            ThroughDate = new DateOnly(2026, 11, 30)
        });
        Assert.Empty(emptyReport.Entries);
        Assert.Equal(0m, emptyReport.TotalDebit);
        Assert.Equal(0m, emptyReport.TotalCredit);
        Assert.Null(await service.GetAccountActivityAsync(
            principal,
            otherCashId,
            null,
            null));
    }

    [Fact]
    public async Task InquiryRequiresActiveCompanyAndRejectsInvalidDateRanges()
    {
        var (principal, _) = await CreateUserAndCompanyAsync("gl-inquiry-validation");
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var noCompanyPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "test"));
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralLedgerInquiryService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAccountsAsync(noCompanyPrincipal));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetTrialBalanceAsync(
            noCompanyPrincipal,
            new DateOnly(2026, 10, 31)));
        await Assert.ThrowsAsync<ValidationException>(() => service.GetEntriesAsync(principal, new GeneralLedgerEntryFilter
        {
            FromDate = new DateOnly(2026, 11, 1),
            ThroughDate = new DateOnly(2026, 10, 31)
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.GetAccountActivityAsync(
            principal,
            Guid.NewGuid(),
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 10, 31)));
    }

    private async Task<(ClaimsPrincipal Principal, Guid CompanyId)> CreateUserAndCompanyAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(
            email,
            $"G{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
            $"{prefix} company");
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return (
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())
            ], "test")),
            company.CompanyId);
    }

    private async Task<Guid> AddAccountAsync(Guid companyId, string number, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var account = new GeneralLedgerAccount
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Number = number,
            Name = name,
            Category = AccountCategory.Asset,
            Type = AccountType.Posting,
            AllowsDirectPosting = true
        };
        dbContext.GeneralLedgerAccounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }

    private async Task<Guid> CreatePeriodAsync(ClaimsPrincipal principal)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        return await service.CreatePeriodAsync(principal, new AccountingPeriodInput
        {
            Name = "2026-10",
            StartsOn = new DateOnly(2026, 10, 1),
            EndsOn = new DateOnly(2026, 10, 31)
        });
    }

    private async Task PostBatchAsync(
        ClaimsPrincipal principal,
        Guid periodId,
        Guid debitAccountId,
        Guid creditAccountId,
        DateOnly postingDate,
        string documentNumber,
        decimal amount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var batchId = await service.CreateBatchAsync(principal, new GeneralJournalBatchInput
        {
            Name = $"B{Guid.NewGuid():N}"[..10],
            Description = "Inquiry test posting",
            DefaultAccountingPeriodId = periodId
        });
        await service.CreateLineAsync(principal, batchId, new GeneralJournalLineInput
        {
            LineNumber = 10000,
            AccountingPeriodId = periodId,
            AccountId = debitAccountId,
            PostingDate = postingDate,
            DocumentNumber = documentNumber,
            Description = "Test debit",
            DebitAmount = amount
        });
        await service.CreateLineAsync(principal, batchId, new GeneralJournalLineInput
        {
            LineNumber = 20000,
            AccountingPeriodId = periodId,
            AccountId = creditAccountId,
            PostingDate = postingDate,
            DocumentNumber = documentNumber,
            Description = "Test credit",
            CreditAmount = amount
        });
        await service.PostBatchAsync(principal, batchId);
    }
}
