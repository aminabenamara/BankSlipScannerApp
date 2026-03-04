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
    }
}
