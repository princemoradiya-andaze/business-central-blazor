using System.ComponentModel.DataAnnotations;
using BusinessCentralBlazor.Web.Components;
using BusinessCentralBlazor.Web.Services;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class RecordCardSessionTests
{
    [Fact]
    public async Task UnchangedRecordDoesNotWriteAndCreateIsExplicit()
    {
        var writes = 0;
        var session = new RecordCardSession<SalesCustomerInput>(new(), () => true,
            _ => { writes++; return Task.CompletedTask; }, _ => Assert.Fail("Unexpected save failure"));
        Assert.True(await session.FlushAsync());
        Assert.Equal(0, writes);
        Assert.True(await session.FlushAsync(create: true));
        Assert.Equal(1, writes);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public async Task PendingSaveUsesDetachedSnapshotsAndDrainsNewEditsBeforeNavigation()
    {
        var model = new SalesCustomerInput { Name = "Original" };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshots = new List<string>();
        var concurrent = 0;
        var maximum = 0;
        var session = new RecordCardSession<SalesCustomerInput>(model, () => true, async snapshot =>
        {
            maximum = Math.Max(maximum, ++concurrent);
            if (snapshots.Count == 0)
            {
                started.SetResult();
                await release.Task;
            }
            snapshots.Add(snapshot.Name);
            concurrent--;
        }, _ => Assert.Fail("Unexpected save failure"));
        model.Name = "First edit";
        var save = session.FlushAsync();
        await started.Task;
        Assert.True(session.IsSaving);
        model.Name = "Edited during save";
        var navigation = session.FlushAsync();
        Assert.False(navigation.IsCompleted);
        release.SetResult();
        Assert.True(await save);
        Assert.True(await navigation);
        Assert.Equal(["First edit", "Edited during save"], snapshots);
        Assert.Equal(1, maximum);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public async Task InvalidEditDuringPendingSaveBlocksTheWaitingNavigation()
    {
        var model = new SalesCustomerInput { Name = "Original" };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var session = new RecordCardSession<SalesCustomerInput>(model, () => !string.IsNullOrWhiteSpace(model.Name),
            async _ =>
            {
                writes++;
                started.SetResult();
                await release.Task;
            }, _ => Assert.Fail("Unexpected save failure"));
        model.Name = "Valid first edit";
        var save = session.FlushAsync();
        await started.Task;
        model.Name = "";
        var navigation = session.FlushAsync();
        release.SetResult();
        Assert.False(await save);
        Assert.False(await navigation);
        Assert.Equal(1, writes);
        Assert.True(session.HasChanges);
        Assert.False(session.IsSaving);
        Assert.NotNull(session.Error);
        Assert.Equal("", model.Name);
    }

    [Fact]
    public async Task InvalidAndFailedChangesAreRetainedAndRetryCanSucceed()
    {
        var model = new SalesCustomerInput { Name = "Original" };
        var valid = false;
        var fail = true;
        var attempts = 0;
        Exception? reported = null;
        var session = new RecordCardSession<SalesCustomerInput>(model, () => valid, _ =>
        {
            attempts++;
            if (fail) throw new ValidationException("Duplicate number");
            return Task.CompletedTask;
        }, exception => reported = exception);
        model.Name = "Draft";
        Assert.False(await session.FlushAsync());
        Assert.Equal(0, attempts);
        Assert.True(session.HasChanges);
        Assert.Contains("highlighted", session.Error);
        valid = true;
        Assert.False(await session.FlushAsync());
        Assert.IsType<ValidationException>(reported);
        Assert.Equal("Duplicate number", session.Error);
        Assert.Equal("Draft", model.Name);
        Assert.True(session.HasChanges);
        fail = false;
        Assert.True(await session.FlushAsync());
        Assert.Equal(2, attempts);
        Assert.Null(session.Error);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public async Task DiscardWaitsForInFlightWriteWithoutSavingLaterDraft()
    {
        var model = new BankAccountInput { Name = "Original" };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var session = new RecordCardSession<BankAccountInput>(model, () => true, async _ =>
        {
            calls++;
            started.SetResult();
            await release.Task;
            throw new ValidationException("Unavailable account");
        }, _ => { });
        model.Name = "Draft";
        var save = session.FlushAsync();
        await started.Task;
        var discard = session.WaitAsync();
        Assert.False(discard.IsCompleted);
        release.SetResult();
        Assert.False(await save);
        await discard;
        Assert.Equal(1, calls);
        Assert.True(session.HasChanges);
    }

    [Fact]
    public async Task UnexpectedFailuresAreReportedAndNeverLookSaved()
    {
        var model = new InventoryItemInput { Description = "Original" };
        Exception? reported = null;
        var session = new RecordCardSession<InventoryItemInput>(model, () => true,
            _ => throw new InvalidOperationException("Database offline"), exception => reported = exception);
        model.Description = "Draft";
        Assert.False(await session.FlushAsync());
        Assert.IsType<InvalidOperationException>(reported);
        Assert.Contains("could not be saved", session.Error);
        Assert.True(session.HasChanges);
    }

    [Fact]
    public void SelectionAndBrowsingFollowVisibleOrderingAndBoundaries()
    {
        var first = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var last = Guid.NewGuid();
        Guid[] visible = [first, middle, last];
        Assert.Equal(first, RecordSelection.Reconcile(visible, null));
        Assert.Equal(middle, RecordSelection.Reconcile(visible, middle));
        Assert.Equal(last, RecordSelection.Reconcile([last], middle));
        Assert.Null(RecordSelection.Reconcile([], middle));
        Assert.Null(RecordSelection.Adjacent(visible, first, -1));
        Assert.Null(RecordSelection.Adjacent(visible, last, 1));
        Assert.Equal(middle, RecordSelection.Adjacent(visible, first, 1));
        Assert.Equal(middle, RecordSelection.Adjacent(visible, last, -1));
        Assert.Equal(first, RecordSelection.Adjacent([last, middle, first], middle, 1));
        Assert.Equal(first, RecordSelection.Adjacent(visible, Guid.NewGuid(), 1));
        Assert.Null(RecordSelection.Adjacent([], first, 1));
    }
}
