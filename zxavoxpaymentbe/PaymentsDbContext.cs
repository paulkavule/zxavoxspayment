using Microsoft.EntityFrameworkCore;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe;

/// <summary>The payment service's own database, zxavoxpayments, schema pay. Nothing here is shared with Zxadocs.</summary>
public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();
    public DbSet<ProviderAccount> ProviderAccounts => Set<ProviderAccount>();
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<ProviderCall> ProviderCalls => Set<ProviderCall>();
    public DbSet<BeneficiaryLookupLog> BeneficiaryLookups => Set<BeneficiaryLookupLog>();
    public DbSet<CallbackReceipt> CallbackReceipts => Set<CallbackReceipt>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<DeadLetterItem> DeadLetterItems => Set<DeadLetterItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("pay");

        modelBuilder.Entity<ApiClient>().HasIndex(c => c.Name).IsUnique();
        modelBuilder.Entity<ApiClient>().HasIndex(c => c.KeyHash).IsUnique();
        modelBuilder.Entity<ApiClient>().Property(c => c.CreatedAt).HasColumnType("timestamptz");

        // One platform account per provider too: NULLS NOT DISTINCT makes null organisations collide.
        modelBuilder.Entity<ProviderAccount>().HasIndex(a => new { a.OrganisationId, a.Provider }).IsUnique().AreNullsDistinct(false);
        modelBuilder.Entity<ProviderAccount>().Property(a => a.CreatedAt).HasColumnType("timestamptz");
        modelBuilder.Entity<ProviderAccount>().Property(a => a.UpdatedAt).HasColumnType("timestamptz");

        modelBuilder.Entity<PaymentOrder>().HasIndex(o => new { o.ClientId, o.ExternalRef }).IsUnique();
        modelBuilder.Entity<PaymentOrder>().HasIndex(o => o.ProviderReferenceId).IsUnique();
        modelBuilder.Entity<PaymentOrder>().HasIndex(o => new { o.Status, o.NextStatusCheckAt });
        modelBuilder.Entity<PaymentOrder>().Property(o => o.CreatedAt).HasColumnType("timestamptz");
        modelBuilder.Entity<PaymentOrder>().Property(o => o.UpdatedAt).HasColumnType("timestamptz");
        modelBuilder.Entity<PaymentOrder>().Property(o => o.CompletedAt).HasColumnType("timestamptz");
        modelBuilder.Entity<PaymentOrder>().Property(o => o.NextStatusCheckAt).HasColumnType("timestamptz");
        modelBuilder.Entity<PaymentOrder>().HasOne<ProviderAccount>().WithMany()
            .HasForeignKey(o => o.ProviderAccountId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProviderCall>().HasIndex(c => c.PaymentOrderId);
        modelBuilder.Entity<ProviderCall>().Property(c => c.At).HasColumnType("timestamptz");
        modelBuilder.Entity<BeneficiaryLookupLog>().Property(l => l.LookedUpAt).HasColumnType("timestamptz");
        modelBuilder.Entity<CallbackReceipt>().Property(r => r.ReceivedAt).HasColumnType("timestamptz");

        modelBuilder.Entity<OutboxMessage>().HasIndex(m => m.EventId);
        modelBuilder.Entity<OutboxMessage>().HasIndex(m => m.Id).HasFilter("\"PublishedAt\" IS NULL").HasDatabaseName("IX_OutboxMessages_Unpublished");
        modelBuilder.Entity<OutboxMessage>().Property(m => m.CreatedAt).HasColumnType("timestamptz");
        modelBuilder.Entity<OutboxMessage>().Property(m => m.PublishedAt).HasColumnType("timestamptz");
        modelBuilder.Entity<OutboxMessage>().Property(m => m.NextAttemptAt).HasColumnType("timestamptz");
        modelBuilder.Entity<ProcessedMessage>().HasKey(m => new { m.EventId, m.Consumer });
        modelBuilder.Entity<ProcessedMessage>().Property(m => m.ProcessedAt).HasColumnType("timestamptz");
        modelBuilder.Entity<DeadLetterItem>().HasIndex(d => d.Status);
        modelBuilder.Entity<DeadLetterItem>().Property(d => d.DeadLetteredAt).HasColumnType("timestamptz");
        modelBuilder.Entity<DeadLetterItem>().Property(d => d.ActionedAt).HasColumnType("timestamptz");
    }
}
