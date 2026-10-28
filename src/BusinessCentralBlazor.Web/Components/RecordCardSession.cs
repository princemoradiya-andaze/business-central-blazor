using System.Text.Json;

namespace BusinessCentralBlazor.Web.Components;

public sealed class RecordCardSession<T>(T model, Func<bool> validate, Func<T, Task> save,
    Action<Exception> reportError) where T : class
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string saved = JsonSerializer.Serialize(model);

    public bool HasChanges => JsonSerializer.Serialize(model) != saved;
    public bool IsSaving { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public async Task<bool> FlushAsync(bool create = false)
    {
        await gate.WaitAsync();
        try
        {
            while (create || HasChanges)
            {
                if (!validate())
                {
                    Error = "Correct the highlighted fields. Your changes have not been saved.";
                    Changed?.Invoke();
                    return false;
                }

                // Never pass the live form to an asynchronous service: edits may continue while saving.
                var snapshot = JsonSerializer.Serialize(model);
                var input = JsonSerializer.Deserialize<T>(snapshot)!;
                IsSaving = true;
                Error = null;
                Changed?.Invoke();
                try
                {
                    await save(input);
                    saved = snapshot;
                    create = false;
                }
                catch (Exception exception)
                {
                    reportError(exception);
                    Error = exception is System.ComponentModel.DataAnnotations.ValidationException or UnauthorizedAccessException
                        ? exception.Message
                        : "The record could not be saved. Your changes are retained. Retry or discard changes.";
                    return false;
                }
                finally
                {
                    IsSaving = false;
                    Changed?.Invoke();
                }
            }

            Error = null;
            Changed?.Invoke();
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WaitAsync()
    {
        await gate.WaitAsync();
        gate.Release();
    }
}

public static class RecordSelection
{
    public static Guid? Reconcile(IEnumerable<Guid> visible, Guid? selected)
    {
        var ids = visible.ToArray();
        return selected.HasValue && ids.Contains(selected.Value) ? selected : ids.Cast<Guid?>().FirstOrDefault();
    }

    public static Guid? Adjacent(IEnumerable<Guid> visible, Guid current, int offset)
    {
        var ids = visible.ToArray();
        var index = Array.IndexOf(ids, current);
        var next = index + offset;
        // A saved name/status change can remove the open record from the filter.
        if (index < 0)
            return offset > 0 ? ids.Cast<Guid?>().FirstOrDefault() : ids.Cast<Guid?>().LastOrDefault();
        return next >= 0 && next < ids.Length ? ids[next] : null;
    }
}
