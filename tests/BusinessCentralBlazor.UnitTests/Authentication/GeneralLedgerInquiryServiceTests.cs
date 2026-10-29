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
        Assert.NotEqual(Guid.Empty, filteredEntry.JournalBatchId);
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

    [Fact]
    public async Task TrialBalanceFiltersCumulativeBalancesAndRejectsOversizedReportsWithoutTruncation()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("trial-request");
        var cash = await AddAccountAsync(companyId, "1000", "Cash");
        var equity = await AddAccountAsync(companyId, "3000", "Opening equity", AccountCategory.Equity);
        var expense = await AddAccountAsync(companyId, "6000", "Office expense", AccountCategory.Expense);
        var zero = await AddAccountAsync(companyId, "6100", "Zero expense", AccountCategory.Expense);
        await AddAccountAsync(companyId, "9000", "Heading", AccountCategory.Asset, AccountType.Heading);
        var (otherPrincipal, otherCompanyId) = await CreateUserAndCompanyAsync("trial-private");
        var privateAccount = await AddAccountAsync(otherCompanyId, "1000", "Private cash");
        var period = await CreatePeriodAsync(principal);
        await PostBatchAsync(principal, period, cash, equity, new(2026, 10, 1), "OPEN-1", 100m);
        await PostBatchAsync(principal, period, expense, cash, new(2026, 10, 5), "COST-2", 40m);
        await PostBatchAsync(principal, period, expense, cash, new(2026, 10, 20), "LATER-3", 10m);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralLedgerInquiryService>();
        var request = new TrialBalanceRequest { AsOfDate = new(2026, 10, 5), MaximumRows = 4 };
        var full = await service.GetTrialBalanceAsync(principal, request);
        Assert.Equal(4, full.Lines.Count);
        Assert.Equal(100m, full.TotalDebit);
        Assert.Equal(100m, full.TotalCredit);
        Assert.Equal(60m, full.Lines.Single(line => line.AccountId == cash).DebitBalance);
        Assert.Contains(full.Lines, line => line.AccountId == zero);
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.GetTrialBalanceAsync(principal, request with { MaximumRows = 2 }));
        Assert.Contains("Report contains 4 rows", exception.Message);
        Assert.Contains("No partial totals", exception.Message);
        var nonzero = await service.GetTrialBalanceAsync(principal, request with { IncludeZeroBalances = false, MaximumRows = 3 });
        Assert.Equal(3, nonzero.Lines.Count);
        Assert.Equal(full.TotalDebit, nonzero.TotalDebit);
        var filtered = await service.GetTrialBalanceAsync(principal, request with
        {
            Category = AccountCategory.Expense, AccountName = "Office", DocumentNumber = "COST", MaximumRows = 1
        });
        var line = Assert.Single(filtered.Lines);
        Assert.Equal(expense, line.AccountId);
        Assert.Equal(40m, filtered.TotalDebit);
        Assert.Equal(0m, filtered.TotalCredit);
        var oneAccount = await service.GetTrialBalanceAsync(principal, request with { AccountId = cash, MaximumRows = 1 });
        Assert.Equal(60m, Assert.Single(oneAccount.Lines).DebitBalance);
        Assert.Empty((await service.GetTrialBalanceAsync(principal, request with { AccountId = privateAccount })).Lines);
        Assert.Empty((await service.GetTrialBalanceAsync(otherPrincipal, request with { AccountId = cash })).Lines);
        var before = await service.GetTrialBalanceAsync(principal, request with { AsOfDate = new(2026, 9, 30) });
        Assert.All(before.Lines, line => Assert.Equal(0m, line.DebitBalance + line.CreditBalance));
        Assert.Empty((await service.GetTrialBalanceAsync(principal, request with { AsOfDate = new(2026, 9, 30), IncludeZeroBalances = false })).Lines);
    }

    [Fact]
    public async Task TrialBalanceRejectsInvalidRequestSettings()
    {
        var (principal, _) = await CreateUserAndCompanyAsync("trial-invalid");
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralLedgerInquiryService>();
        foreach (var request in new TrialBalanceRequest[]
        {
            new() { MaximumRows = 0 }, new() { MaximumRows = 500001 }, new() { AsOfDate = default },
            new() { FormatRegion = "not-a-region" }, new() { Category = (AccountCategory)999 },
            new() { DocumentNumber = new string('x', 65) }, new() { AccountName = new string('x', 101) }
        })
        {
            await Assert.ThrowsAsync<ValidationException>(() => service.GetTrialBalanceAsync(principal, request));
        }
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

    private async Task<Guid> AddAccountAsync(Guid companyId, string number, string name,
        AccountCategory category = AccountCategory.Asset, AccountType type = AccountType.Posting)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var account = new GeneralLedgerAccount
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Number = number,
            Name = name,
            Category = category,
            Type = type,
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
