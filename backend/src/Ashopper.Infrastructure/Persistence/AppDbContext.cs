using Ashopper.Domain.Catalog;
using Ashopper.Domain.Lists;
using Ashopper.Domain.Matching;
using Ashopper.Domain.Pricing;
using Ashopper.Infrastructure.Collecting;
using Ashopper.Infrastructure.Identity;
using Ashopper.Infrastructure.Scraping;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ashopper.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Market> Markets => Set<Market>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductSourceRef> ProductSourceRefs => Set<ProductSourceRef>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();
    public DbSet<DailyPrice> DailyPrices => Set<DailyPrice>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<RequirementMatch> RequirementMatches => Set<RequirementMatch>();
    public DbSet<UserShopDiscount> UserShopDiscounts => Set<UserShopDiscount>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<CollectorRun> CollectorRuns => Set<CollectorRun>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<TrackedUrl> TrackedUrls => Set<TrackedUrl>();
    public DbSet<TrackedUrlSubscription> TrackedUrlSubscriptions => Set<TrackedUrlSubscription>();
    public DbSet<ScrapeHost> ScrapeHosts => Set<ScrapeHost>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Market>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(8);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Locale).HasMaxLength(16);
        });

        b.Entity<Category>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Attributes).HasJsonConversion();
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Product>(e =>
        {
            e.Property(x => x.Brand).HasMaxLength(100);
            e.Property(x => x.Model).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(400);
            e.Property(x => x.Ean).HasMaxLength(14);
            e.Property(x => x.Mpn).HasMaxLength(100);
            e.Property(x => x.Attributes).HasJsonConversion();
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.Ean);
            e.HasIndex(x => x.Mpn);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ProductSourceRef>(e =>
        {
            e.HasKey(x => new { x.SourceKey, x.ExternalId });
            e.Property(x => x.SourceKey).HasMaxLength(32);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Shop>(e =>
        {
            e.HasIndex(x => new { x.MarketCode, x.Domain }).IsUnique();
            e.Property(x => x.MarketCode).HasMaxLength(8);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Domain).HasMaxLength(253);
            e.HasOne<Market>().WithMany().HasForeignKey(x => x.MarketCode).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Offer>(e =>
        {
            e.HasIndex(x => new { x.SourceKey, x.ExternalId }).IsUnique();
            e.HasIndex(x => x.ProductId);
            e.HasIndex(x => x.ShopId);
            e.Property(x => x.SourceKey).HasMaxLength(32);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PriceObservation>(e =>
        {
            e.HasIndex(x => new { x.OfferId, x.ObservedAt });
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasOne<Offer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DailyPrice>(e =>
        {
            e.HasKey(x => new { x.OfferId, x.Day });
            e.Property(x => x.MinPrice).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasOne<Offer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShoppingList>(e =>
        {
            e.HasIndex(x => x.OwnerUserId);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.MarketCode).HasMaxLength(8);
            e.Property(x => x.AllowedShopIds).HasJsonConversion();
            e.HasMany(x => x.Requirements).WithOne().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Market>().WithMany().HasForeignKey(x => x.MarketCode).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Requirement>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Constraints).HasJsonConversion();
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RequirementMatch>(e =>
        {
            e.HasKey(x => new { x.RequirementId, x.ProductId });
            e.HasIndex(x => x.ProductId);
            e.HasOne<Requirement>().WithMany().HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Cascade);
            // No cascade from Product: SQL Server rejects multiple cascade paths. Products are never hard-deleted.
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<UserShopDiscount>(e =>
        {
            e.HasKey(x => new { x.UserId, x.ShopId });
            e.Property(x => x.Percent).HasPrecision(5, 2);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CollectorRun>(e =>
        {
            e.HasIndex(x => x.FinishedAt);
            e.Property(x => x.Sources).HasMaxLength(400);
        });

        b.Entity<TrackedUrl>(e =>
        {
            e.HasIndex(x => x.UrlHash).IsUnique();
            e.HasIndex(x => x.NextCheckAt);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.Property(x => x.UrlHash).HasMaxLength(64);
            e.Property(x => x.StoreKey).HasMaxLength(32);
            e.Property(x => x.LastStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.Property(x => x.LastSuccessVia).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(400);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            // Products and offers are never hard-deleted; avoid extra cascade paths.
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Offer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<TrackedUrlSubscription>(e =>
        {
            e.HasKey(x => new { x.UserId, x.TrackedUrlId });
            e.HasIndex(x => x.TrackedUrlId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TrackedUrl>().WithMany().HasForeignKey(x => x.TrackedUrlId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ScrapeHost>(e =>
        {
            e.HasKey(x => x.Host);
            e.Property(x => x.Host).HasMaxLength(253);
            e.Property(x => x.BlockReason).HasMaxLength(300);
        });
    }
}
