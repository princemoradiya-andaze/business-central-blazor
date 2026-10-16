using System.Security.Claims;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessCentralBlazor.Web.Services;

public sealed record HomeDashboardSummary(
    int CustomerCount,
    int VendorCount,
    int OpenSalesOrderCount,
    int OpenPurchaseOrderCount,
    int InventoryItemCount,
    int AccountCount,
    int OpenReceivableCount,
    int OpenPayableCount);

public interface IHomeDashboardService
{
    Task<HomeDashboardSummary> GetSummaryAsync(ClaimsPrincipal user);
}

public sealed class HomeDashboardService(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ICompanyScopeAuthorizer companyScopeAuthorizer) : IHomeDashboardService
{
    public async Task<HomeDashboardSummary> GetSummaryAsync(ClaimsPrincipal user)
    {
        var company = await companyScopeAuthorizer.ResolveActiveCompanyAsync(user)
            ?? throw new UnauthorizedAccessException("An active company is required to load the home dashboard.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var companyId = company.CompanyId;

        var customerCount = await dbContext.Customers
            .CountAsync(customer => customer.CompanyId == companyId && !customer.IsInactive);
        var vendorCount = await dbContext.Vendors
            .CountAsync(vendor => vendor.CompanyId == companyId);
        var openSalesOrderCount = await dbContext.SalesOrders
            .CountAsync(order => order.CompanyId == companyId && order.Status == SalesOrderStatus.Open);
        var openPurchaseOrderCount = await dbContext.PurchaseOrders
            .CountAsync(order => order.CompanyId == companyId && order.Status == PurchaseOrderStatus.Open);
        var inventoryItemCount = await dbContext.Items
            .CountAsync(item => item.CompanyId == companyId && item.Type == ItemType.Inventory);
        var accountCount = await dbContext.GeneralLedgerAccounts
            .CountAsync(account => account.CompanyId == companyId);
        var openReceivableCount = await dbContext.SalesInvoices
            .CountAsync(invoice => invoice.CompanyId == companyId && invoice.PostedAtUtc != null);
        var openPayableCount = await dbContext.PurchaseInvoices
            .CountAsync(invoice => invoice.CompanyId == companyId && invoice.PostedAtUtc != null);

        return new HomeDashboardSummary(
            customerCount,
            vendorCount,
            openSalesOrderCount,
            openPurchaseOrderCount,
            inventoryItemCount,
            accountCount,
            openReceivableCount,
            openPayableCount);
    }
}
