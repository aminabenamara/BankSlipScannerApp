using BankSlipScannerApp.Models;

using Microsoft.EntityFrameworkCore;

namespace BankSlipScannerApp.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<PdfUpload> PdfUploads { get; set; }
        public DbSet<PdfTransaction> PdfTransactions { get; set; }
    }
}
