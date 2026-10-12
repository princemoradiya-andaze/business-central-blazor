## Summary

<!-- Describe the change and why it is needed. -->

## Verification

- [ ] `dotnet build BusinessCentralBlazor.sln --no-restore`
- [ ] `dotnet test BusinessCentralBlazor.sln --no-build`
- [ ] `dotnet ef migrations has-pending-model-changes` (for EF model changes)
- [ ] Relevant migration or manual verification documented

## Review checklist

- [ ] No credentials, local databases, proprietary artifacts, or unrelated files are included.
- [ ] Authorization, company boundaries, validation, and audit implications were considered.
- [ ] Documentation and roadmap are updated when applicable.
