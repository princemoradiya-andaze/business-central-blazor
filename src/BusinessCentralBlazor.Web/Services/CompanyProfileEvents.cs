namespace BusinessCentralBlazor.Web.Services;

public sealed class CompanyProfileEvents
{
    public event Action<string>? NameChanged;
    public void NotifyNameChanged(string name) => NameChanged?.Invoke(name);
}
