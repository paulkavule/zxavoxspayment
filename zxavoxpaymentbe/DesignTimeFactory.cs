using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using zxavoxpaymentbe.Helpers;

namespace zxavoxpaymentbe;

public class DesignTimeFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        ServiceEnvironment.Load(".env");
        return new(new DbContextOptionsBuilder<PaymentsDbContext>().UseNpgsql(ServiceEnvironment.DatabaseConnection()).Options);
    }
}
