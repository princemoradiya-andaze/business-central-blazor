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

public sealed class BankServiceTests(AuthenticationWebApplicationFactory factory) : IClassFixture<AuthenticationWebApplicationFactory>
{
    private static readonly DateOnly Date = new(2026, 10, 12);
    private const string Password = "Valid-Test-Password-2026!";

    [Fact]
    public async Task ReceiptsAndPaymentsCreateBalancedTraceableJournalsAndPostUsingLedgerWorkflow()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        var journal = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        foreach (var receipt in new[] { true, false })
        {
            var id = await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, receipt ? "REC-1" : "PAY-1", receipt));
            var batch = await journal.GetBatchAsync(data.User, id);
            Assert.NotNull(batch);
            Assert.Equal(JournalBatchStatus.Open, batch.Status);
            Assert.Equal(2, batch.Lines.Count);
            Assert.Equal(100m, batch.Lines.Sum(line => line.DebitAmount));
            Assert.Equal(100m, batch.Lines.Sum(line => line.CreditAmount));
            var bankLine = Assert.Single(batch.Lines, line => line.AccountId == data.Cash);
            Assert.Equal(receipt ? 100m : 0m, bankLine.DebitAmount);
            await journal.PostBatchAsync(data.User, id);
        }
        var links = await bank.GetJournalsAsync(data.User, data.Bank);
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.Equal(JournalBatchStatus.Posted, link.Status));
        await using var db = await ContextAsync();
        Assert.Equal(4, await db.GeneralLedgerEntries.CountAsync(entry => entry.CompanyId == data.Company));
        Assert.True(await db.AuditEntries.AnyAsync(entry => entry.CompanyId == data.Company && entry.Action == "BankJournalCreated"));
    }

    [Fact]
    public async Task BankSetupRequiresBaseCurrencyEligibleDedicatedAssetAccountAndUniqueCode()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateAccountAsync(data.User,
            Account(data.Cash, "DUPLICATE-GL")));
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateAccountAsync(data.User,
            Account(data.Offset, "INCOME")));
        await using var db = await ContextAsync();
        var another = new GeneralLedgerAccount { Id = Guid.NewGuid(), CompanyId = data.Company,
            Number = "1010", Name = "Petty cash", Category = AccountCategory.Asset, Type = AccountType.Posting };
        db.GeneralLedgerAccounts.Add(another);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateAccountAsync(data.User, Account(another.Id, " main ")));
        another.IsBlocked = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateAccountAsync(data.User, Account(another.Id, "BLOCKED")));
        another.IsBlocked = false;
        another.AllowsDirectPosting = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateAccountAsync(data.User, Account(another.Id, "INDIRECT")));
        another.AllowsDirectPosting = true;
        another.Type = AccountType.Heading;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateAccountAsync(data.User, Account(another.Id, "HEADING")));
        another.Type = AccountType.Posting;
        db.CompanySettings.Remove(await db.CompanySettings.SingleAsync(item => item.CompanyId == data.Company));
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateAccountAsync(data.User, Account(another.Id, "NO-CURRENCY")));
    }

    [Fact]
    public async Task JournalValidationRejectsInvalidPrecisionDatesDuplicatesAndBlockedBanksWithoutPartialWrites()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        foreach (var amount in new[] { 0m, -1m, 1.00001m, 1000000000000000m })
        {
            var input = Journal(data, $"BAD-{amount}");
            input.Amount = amount;
            await Assert.ThrowsAsync<ValidationException>(() => bank.CreateJournalAsync(data.User, data.Bank, input));
        }
        var badDate = Journal(data, "DATE");
        badDate.PostingDate = new DateOnly(2026, 11, 1);
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateJournalAsync(data.User, data.Bank, badDate));
        var sameAccount = Journal(data, "SAME");
        sameAccount.OffsetAccountId = data.Cash;
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateJournalAsync(data.User, data.Bank, sameAccount));
        Assert.Empty(await bank.GetJournalsAsync(data.User, data.Bank));
        await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "ONCE"));
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "ONCE")));
        var account = Assert.Single(await bank.GetAccountsAsync(data.User));
        var update = Account(data.Cash);
        update.IsBlocked = true;
        await bank.UpdateAccountAsync(data.User, data.Bank, account.Revision, update);
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "BLOCKED")));
        Assert.Single(await bank.GetJournalsAsync(data.User, data.Bank));
    }

    [Fact]
    public async Task PostingRevalidatesBankStatusCurrencyAndJournalTrace()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        var journal = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var id = await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "TRACE"));
        await using var db = await ContextAsync();
        var account = await db.BankAccounts.SingleAsync(item => item.Id == data.Bank);
        account.IsBlocked = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => journal.PostBatchAsync(data.User, id));
        account.IsBlocked = false;
        var settings = await db.CompanySettings.SingleAsync(item => item.CompanyId == data.Company);
        settings.BaseCurrencyCode = "EUR";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => journal.PostBatchAsync(data.User, id));
        settings.BaseCurrencyCode = "USD";
        var line = await db.GeneralJournalLines.SingleAsync(item => item.JournalBatchId == id && item.AccountId == data.Cash);
        line.DocumentNumber = "CHANGED";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => journal.PostBatchAsync(data.User, id));
        Assert.False(await db.GeneralLedgerEntries.AnyAsync(item => item.CompanyId == data.Company));
    }

    [Fact]
    public async Task ManualImportMatchUnmatchAndCompletionExposeUnmatchedActivityAndTimingDifferences()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        var journal = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var received = await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "RECEIVED"));
        await journal.PostBatchAsync(data.User, received);
        var paid = await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "OUTSTANDING", false));
        await journal.PostBatchAsync(data.User, paid);
        var id = await bank.CreateStatementAsync(data.User, data.Bank, Statement("OCT", 100m));
        await bank.ImportLinesAsync(data.User, id, 0, "2026-10-12\tS1\tDeposit\t100.0000\r\n");
        var detail = await bank.GetStatementAsync(data.User, id);
        Assert.Equal(0m, detail.StatementDifference);
        Assert.Equal(100m, detail.BookDifference);
        Assert.Equal(2, detail.Activity.Count);
        var line = Assert.Single(detail.Lines);
        var entry = Assert.Single(detail.Activity, item => item.Amount == 100m);
        await bank.MatchAsync(data.User, id, detail.Revision, line.Id, entry.Id);
        detail = await bank.GetStatementAsync(data.User, id);
        Assert.Equal(entry.Id, Assert.Single(detail.Lines).EntryId);
        await bank.MatchAsync(data.User, id, detail.Revision, line.Id, null);
        detail = await bank.GetStatementAsync(data.User, id);
        Assert.Null(Assert.Single(detail.Lines).EntryId);
        await Assert.ThrowsAsync<ValidationException>(() => bank.CompleteAsync(data.User, id, detail.Revision));
        await bank.MatchAsync(data.User, id, detail.Revision, line.Id, entry.Id);
        detail = await bank.GetStatementAsync(data.User, id);
        await bank.CompleteAsync(data.User, id, detail.Revision);
        detail = await bank.GetStatementAsync(data.User, id);
        Assert.True(detail.IsCompleted);
        Assert.Single(detail.Activity, item => item.MatchedLineId is null);
        await Assert.ThrowsAsync<ValidationException>(() => bank.MatchAsync(data.User, id, detail.Revision, line.Id, null));
        await Assert.ThrowsAsync<ValidationException>(() => bank.AddLinesAsync(data.User, id, detail.Revision, [Line("NEW")]));
        await Assert.ThrowsAsync<ValidationException>(() => bank.UpdateStatementAsync(data.User, id, detail.Revision, Statement("EDIT", 100)));
    }

    [Fact]
    public async Task InvalidImportIsAtomicAndDuplicateStatementLinesOrOverlapsAreRejected()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        var id = await bank.CreateStatementAsync(data.User, data.Bank, Statement("OCT", 100));
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateStatementAsync(data.User, data.Bank, Statement("OVERLAP", 100)));
        await Assert.ThrowsAsync<ValidationException>(() => bank.ImportLinesAsync(data.User, id, 0,
            "2026-10-12\tS1\tDeposit\t100\nbad row"));
        await Assert.ThrowsAsync<ValidationException>(() => bank.ImportLinesAsync(data.User, id, 0,
            "2026-10-12\tS1\tDeposit\t100\n2026-11-12\tS2\tOutside period\t100"));
        await Assert.ThrowsAsync<ValidationException>(() => bank.AddLinesAsync(data.User, id, 0, [Line("S1"), Line("s1")]));
        Assert.Empty((await bank.GetStatementAsync(data.User, id)).Lines);
        await bank.AddLinesAsync(data.User, id, 0, [Line("S1")]);
        await Assert.ThrowsAsync<ValidationException>(() => bank.AddLinesAsync(data.User, id, 1, [Line("s1")]));
        Assert.Single((await bank.GetStatementAsync(data.User, id)).Lines);
        foreach (var amount in new[] { 0m, 0.00001m, 1000000000000000m })
        {
            var line = Line("BAD");
            line.Amount = amount;
            await Assert.ThrowsAsync<ValidationException>(() => bank.AddLinesAsync(data.User, id, 1, [line]));
        }
    }

    [Fact]
    public async Task MatchesRejectWrongAmountWrongBankDraftOrFutureEntriesAndDuplicates()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        var journal = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var batch = await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "MATCH"));
        var id = await bank.CreateStatementAsync(data.User, data.Bank, Statement("OCT", 200));
        await bank.AddLinesAsync(data.User, id, 0, [Line("A"), Line("B")]);
        var detail = await bank.GetStatementAsync(data.User, id);
        Assert.Empty(detail.Activity);
        await Assert.ThrowsAsync<ValidationException>(() => bank.MatchAsync(data.User, id, 1, detail.Lines[0].Id, batch));
        await journal.PostBatchAsync(data.User, batch);
        detail = await bank.GetStatementAsync(data.User, id);
        var entry = Assert.Single(detail.Activity);
        await using var db = await ContextAsync();
        var wrongAccountEntry = await db.GeneralLedgerEntries.SingleAsync(item => item.JournalBatchId == batch && item.AccountId == data.Offset);
        await Assert.ThrowsAsync<ValidationException>(() => bank.MatchAsync(data.User, id, 1, detail.Lines[0].Id, wrongAccountEntry.Id));
        await bank.MatchAsync(data.User, id, 1, detail.Lines[0].Id, entry.Id);
        await Assert.ThrowsAsync<ValidationException>(() => bank.MatchAsync(data.User, id, 2, detail.Lines[1].Id, entry.Id));
        var paymentBatch = await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "WRONG-SIGN", false));
        await journal.PostBatchAsync(data.User, paymentBatch);
        detail = await bank.GetStatementAsync(data.User, id);
        var paymentEntry = Assert.Single(detail.Activity, item => item.Amount < 0);
        await Assert.ThrowsAsync<ValidationException>(() => bank.MatchAsync(data.User, id, 2, detail.Lines[1].Id, paymentEntry.Id));
        var futureBatchInput = Journal(data, "FUTURE");
        futureBatchInput.PostingDate = new DateOnly(2026, 10, 31);
        var futureBatch = await bank.CreateJournalAsync(data.User, data.Bank, futureBatchInput);
        await journal.PostBatchAsync(data.User, futureBatch);
        await bank.UpdateStatementAsync(data.User, id, 2, new BankStatementInput { Reference = "OCT", StartsOn = new(2026, 10, 1),
            EndsOn = new(2026, 10, 20), ClosingBalance = 200 });
        var futureEntry = await db.GeneralLedgerEntries.SingleAsync(item => item.JournalBatchId == futureBatch && item.AccountId == data.Cash);
        await Assert.ThrowsAsync<ValidationException>(() => bank.MatchAsync(data.User, id, 3, detail.Lines[1].Id, futureEntry.Id));
    }

    [Fact]
    public async Task StaleRevisionsRejectAccountAndStatementCommandsAndContextConcurrencyIsEnforced()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        var savedRevision = await bank.UpdateAccountAsync(data.User, data.Bank, 0, Account(data.Cash));
        Assert.Equal(1, savedRevision);
        Assert.Equal(savedRevision, Assert.Single(await bank.GetAccountsAsync(data.User)).Revision);
        var nextRevision = await bank.UpdateAccountAsync(data.User, data.Bank, savedRevision, Account(data.Cash));
        Assert.Equal(2, nextRevision);
        await Assert.ThrowsAsync<ValidationException>(() => bank.UpdateAccountAsync(data.User, data.Bank, savedRevision, Account(data.Cash)));
        await Assert.ThrowsAsync<ValidationException>(() => bank.UpdateAccountAsync(data.User, data.Bank, 0, Account(data.Cash)));
        var id = await bank.CreateStatementAsync(data.User, data.Bank, Statement("OCT", 100));
        await bank.AddLinesAsync(data.User, id, 0, [Line("A")]);
        await Assert.ThrowsAsync<ValidationException>(() => bank.AddLinesAsync(data.User, id, 0, [Line("B")]));
        await Assert.ThrowsAsync<ValidationException>(() => bank.CompleteAsync(data.User, id, 0));
        await using var first = await ContextAsync();
        await using var second = await ContextAsync();
        var a = await first.BankStatements.SingleAsync(item => item.Id == id);
        var b = await second.BankStatements.SingleAsync(item => item.Id == id);
        a.Revision++;
        b.Revision++;
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UsedBankAccountCannotBeRemappedEvenWhenTheNewGlAccountIsEligible(bool journalActivity)
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        if (journalActivity) await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "USED"));
        else await bank.CreateStatementAsync(data.User, data.Bank, Statement("USED", 100));
        var newCash = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>().CreateAsync(data.User,
            new GeneralLedgerAccountInput { Number = "1020", Name = "New cash", Category = AccountCategory.Asset, Type = AccountType.Posting });
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            bank.UpdateAccountAsync(data.User, data.Bank, 0, Account(newCash)));
        Assert.Contains("mapping cannot change", error.Message);
        Assert.Equal(data.Cash, Assert.Single(await bank.GetAccountsAsync(data.User)).GeneralLedgerAccountId);
    }

    [Fact]
    public async Task StatementHeaderCorrectionAndLineRemovalResolveDifferencesButMatchedLinesCannotBeRemoved()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        var journal = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var batch = await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "BALANCE"));
        await journal.PostBatchAsync(data.User, batch);
        var id = await bank.CreateStatementAsync(data.User, data.Bank, Statement("OCT", 99));
        await bank.AddLinesAsync(data.User, id, 0, [Line("A"), Line("REMOVE")]);
        var detail = await bank.GetStatementAsync(data.User, id);
        await bank.RemoveLineAsync(data.User, id, 1, detail.Lines.Single(line => line.Reference == "REMOVE").Id);
        detail = await bank.GetStatementAsync(data.User, id);
        await bank.MatchAsync(data.User, id, 2, Assert.Single(detail.Lines).Id, Assert.Single(detail.Activity).Id);
        await Assert.ThrowsAsync<ValidationException>(() => bank.RemoveLineAsync(data.User, id, 3, detail.Lines[0].Id));
        await Assert.ThrowsAsync<ValidationException>(() => bank.CompleteAsync(data.User, id, 3));
        await bank.UpdateStatementAsync(data.User, id, 3, Statement("CORRECTED", 100));
        await bank.CompleteAsync(data.User, id, 4);
        Assert.True((await bank.GetStatementAsync(data.User, id)).IsCompleted);
        await Assert.ThrowsAsync<ValidationException>(() => bank.UpdateAccountAsync(data.User, data.Bank, 0, Account(Guid.NewGuid())));
    }

    [Fact]
    public async Task CompanyIsolationAndRevokedMembershipApplyToEveryBankingSurface()
    {
        var own = await SetupAsync();
        var other = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        var ownStatement = await bank.CreateStatementAsync(own.User, own.Bank, Statement("OWN", 100));
        var foreignStatement = await bank.CreateStatementAsync(other.User, other.Bank, Statement("OTHER", 100));
        await bank.AddLinesAsync(other.User, foreignStatement, 0, [Line("FOREIGN")]);
        var foreignLine = Assert.Single((await bank.GetStatementAsync(other.User, foreignStatement)).Lines);
        Assert.Equal(own.Bank, Assert.Single(await bank.GetAccountsAsync(own.User)).Id);
        await Assert.ThrowsAsync<ValidationException>(() => bank.GetStatementAsync(own.User, foreignStatement));
        await Assert.ThrowsAsync<ValidationException>(() => bank.GetStatementsAsync(own.User, other.Bank));
        await Assert.ThrowsAsync<ValidationException>(() => bank.GetJournalsAsync(own.User, other.Bank));
        await Assert.ThrowsAsync<ValidationException>(() => bank.UpdateAccountAsync(own.User, other.Bank, 0, Account(own.Cash)));
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateJournalAsync(own.User, other.Bank, Journal(own, "FOREIGN")));
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateStatementAsync(own.User, other.Bank, Statement("FOREIGN", 100)));
        await Assert.ThrowsAsync<ValidationException>(() => bank.AddLinesAsync(own.User, foreignStatement, 1, [Line("NO")]));
        await Assert.ThrowsAsync<ValidationException>(() => bank.RemoveLineAsync(own.User, ownStatement, 0, foreignLine.Id));
        await Assert.ThrowsAsync<ValidationException>(() => bank.MatchAsync(own.User, foreignStatement, 1, foreignLine.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<ValidationException>(() => bank.CompleteAsync(own.User, foreignStatement, 1));
        await Assert.ThrowsAsync<ValidationException>(() => bank.CreateAccountAsync(own.User, Account(other.Cash, "CROSS")));
        await factory.RemoveCompanyMembershipAsync(Guid.Parse(own.User.FindFirstValue(ClaimTypes.NameIdentifier)!), own.Company);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bank.GetAccountsAsync(own.User));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bank.CreateJournalAsync(own.User, own.Bank, Journal(own, "REVOKED")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bank.ImportLinesAsync(own.User, ownStatement, 0, "bad"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bank.GetAccountsAsync(new ClaimsPrincipal()));
    }

    [Fact]
    public async Task ReversalsRemainPostedBankActivityAndPriorMatchesAreUnavailableToLaterStatements()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        var journal = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var batch = await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "ORIGINAL"));
        await journal.PostBatchAsync(data.User, batch);
        var original = await bank.CreateStatementAsync(data.User, data.Bank, new BankStatementInput {
            Reference = "FIRST", StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 15), ClosingBalance = 100 });
        await bank.AddLinesAsync(data.User, original, 0, [Line("A")]);
        var detail = await bank.GetStatementAsync(data.User, original);
        var matched = Assert.Single(detail.Activity);
        await bank.MatchAsync(data.User, original, 1, detail.Lines[0].Id, matched.Id);
        await bank.CompleteAsync(data.User, original, 2);
        var reversal = await journal.ReverseBatchAsync(data.User, batch, new GeneralJournalReversalInput {
            Name = "REVERSAL", AccountingPeriodId = data.Period, PostingDate = new(2026, 10, 20) });
        var next = await bank.CreateStatementAsync(data.User, data.Bank, new BankStatementInput {
            Reference = "SECOND", StartsOn = new(2026, 10, 16), EndsOn = new(2026, 10, 31), OpeningBalance = 100, ClosingBalance = 0 });
        detail = await bank.GetStatementAsync(data.User, next);
        var reversed = Assert.Single(detail.Activity);
        Assert.Equal(-100m, reversed.Amount);
        Assert.Equal(reversal, reversed.BatchId);
        Assert.Equal(0m, detail.BookDifference);
        var reversalLine = Line("REV");
        reversalLine.TransactionDate = new(2026, 10, 20);
        reversalLine.Amount = -100;
        await bank.AddLinesAsync(data.User, next, 0, [reversalLine]);
        detail = await bank.GetStatementAsync(data.User, next);
        await Assert.ThrowsAsync<ValidationException>(() => bank.MatchAsync(data.User, next, 1, detail.Lines[0].Id, matched.Id));
        await bank.MatchAsync(data.User, next, 1, detail.Lines[0].Id, reversed.Id);
        await bank.CompleteAsync(data.User, next, 2);
    }

    [Fact]
    public async Task EarlierUnclearedEntryCanBeMatchedButCannotBeUsedAgainInAnotherStatement()
    {
        var data = await SetupAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
        var journal = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var batch = await bank.CreateJournalAsync(data.User, data.Bank, Journal(data, "EARLIER"));
        await journal.PostBatchAsync(data.User, batch);
        var first = await bank.CreateStatementAsync(data.User, data.Bank, new BankStatementInput {
            Reference = "LATER", StartsOn = new(2026, 10, 16), EndsOn = new(2026, 10, 20), ClosingBalance = 100 });
        var line = Line("CLEARED");
        line.TransactionDate = new(2026, 10, 18);
        await bank.AddLinesAsync(data.User, first, 0, [line]);
        var detail = await bank.GetStatementAsync(data.User, first);
        var entry = Assert.Single(detail.Activity);
        await bank.MatchAsync(data.User, first, 1, detail.Lines[0].Id, entry.Id);
        var second = await bank.CreateStatementAsync(data.User, data.Bank, new BankStatementInput {
            Reference = "NEXT", StartsOn = new(2026, 10, 21), EndsOn = new(2026, 10, 31), ClosingBalance = 100 });
        line.TransactionDate = new(2026, 10, 22);
        await bank.AddLinesAsync(data.User, second, 0, [line]);
        detail = await bank.GetStatementAsync(data.User, second);
        Assert.Empty(detail.Activity);
        await Assert.ThrowsAsync<ValidationException>(() => bank.MatchAsync(data.User, second, 1, detail.Lines[0].Id, entry.Id));
    }

    private sealed record Setup(ClaimsPrincipal User, Guid Company, Guid Bank, Guid Cash, Guid Offset, Guid Period);
    private async Task<Setup> SetupAsync()
    {
        var email = $"bank-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var company = await factory.AddCompanyMembershipAsync(email, $"B{Guid.NewGuid():N}"[..8], "Bank Company");
        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>().FindByEmailAsync(email);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user!.Id.ToString()),
            new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())], "test"));
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        if (!await db.Currencies.AnyAsync(item => item.Code == "USD"))
            db.Currencies.Add(new Currency { Code = "USD", Name = "US Dollar", MinorUnit = 2 });
        db.CompanySettings.Add(new CompanySettings { CompanyId = company.CompanyId, BaseCurrencyCode = "USD" });
        await db.SaveChangesAsync();
        var accounts = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>();
        var cash = await accounts.CreateAsync(principal, new GeneralLedgerAccountInput {
            Number = "1000", Name = "Bank cash", Category = AccountCategory.Asset, Type = AccountType.Posting });
        var offset = await accounts.CreateAsync(principal, new GeneralLedgerAccountInput {
            Number = "4000", Name = "Income", Category = AccountCategory.Income, Type = AccountType.Posting });
        var period = await scope.ServiceProvider.GetRequiredService<IGeneralJournalService>().CreatePeriodAsync(principal,
            new AccountingPeriodInput { Name = "OCT", StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 31) });
        var bank = await scope.ServiceProvider.GetRequiredService<IBankService>().CreateAccountAsync(principal, Account(cash));
        return new Setup(principal, company.CompanyId, bank, cash, offset, period);
    }
    private static BankAccountInput Account(Guid cash, string code = "MAIN") =>
        new() { Code = code, Name = "Main bank", GeneralLedgerAccountId = cash, AccountIdentifier = "Local test identifier" };
    private static BankJournalInput Journal(Setup data, string document, bool receipt = true) =>
        new() { AccountingPeriodId = data.Period, OffsetAccountId = data.Offset, PostingDate = Date,
            DocumentNumber = document, Description = "Bank transaction", Amount = 100, IsReceipt = receipt };
    private static BankStatementInput Statement(string reference, decimal closing) =>
        new() { Reference = reference, StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 31), ClosingBalance = closing };
    private static BankStatementLineInput Line(string reference) =>
        new() { Reference = reference, TransactionDate = Date, Description = "Deposit", Amount = 100 };
    private async Task<ErpDbContext> ContextAsync() =>
        await factory.Services.GetRequiredService<IDbContextFactory<ErpDbContext>>().CreateDbContextAsync();
}
