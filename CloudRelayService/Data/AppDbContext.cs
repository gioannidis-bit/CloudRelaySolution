using Microsoft.EntityFrameworkCore;
using CloudRelayService.Models;

namespace CloudRelayService.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<AgentData> AgentDatas { get; set; }
        public DbSet<Table1Record> Table1Records { get; set; }  // For Table1

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure AgentDatas as before.
            modelBuilder.Entity<AgentData>()
                .HasKey(a => a.Id);
            modelBuilder.Entity<AgentData>()
                .Property(a => a.Id)
                .ValueGeneratedOnAdd();

            // Configure Table1Records to map to the existing table called "table1".
            // This will instruct EF Core to use the existing structure.
            modelBuilder.Entity<Table1Record>()
                .ToTable("table1");

            // If the table already has a defined primary key, you can configure it.
            // For example, if the PK in Table1 is "Id":
            modelBuilder.Entity<Table1Record>()
                .HasKey(t => t.Id);

            base.OnModelCreating(modelBuilder);
        }
    }
}
