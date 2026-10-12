# ADR 0001: Start with a modular monolith

- Status: Accepted for the initial implementation
- Date: 2026-10-12

## Context

The product needs financial and operational modules with shared company boundaries, authentication, and transactional posting. The repository has no existing application code.

## Decision

Build one Blazor Server application with separate Domain, Application, Infrastructure, and Web projects. Keep Domain independent of infrastructure and UI. Use SQL Server through Entity Framework Core when persistence is introduced. Target .NET 10 because the SDK and runtime are installed and .NET 10 is an LTS release.

## Consequences

- One deployment unit keeps early development and financial transactions straightforward.
- Project boundaries provide a path to isolate modules later without introducing distributed systems now.
- Azure hosting compatibility, production scale, and database sizing remain to be verified before deployment.
- Business Central artifacts may inform terminology and relationships only after applicable license terms are confirmed; this project will use independently written code and an independently designed schema.
