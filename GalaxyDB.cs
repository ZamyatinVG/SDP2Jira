using Microsoft.EntityFrameworkCore;
using System.Configuration;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System;

namespace SDP2Jira
{
    public partial class GalaxyDB : DbContext
    {
        public virtual DbSet<JIRA_ISSUE> JIRA_ISSUE { get; set; }
        public virtual DbSet<JIRA_ISSUE_HISTORY> JIRA_ISSUE_HISTORY { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseOracle(ConfigurationManager.ConnectionStrings["GalaxyDB"].ToString(), x => x.UseOracleSQLCompatibility("11"));
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("GAL_ASUP");
            modelBuilder.Entity<JIRA_ISSUE>()
                .HasKey(x => x.JIRAIDENTIFIER);
            modelBuilder.Entity<JIRA_ISSUE_HISTORY>()
                .HasKey(x => x.ID);
        }
    }

    [Table("JIRA_ISSUE")]
    public partial class JIRA_ISSUE
    {
        [StringLength(255)]
        public string JIRAIDENTIFIER { get; set; }
        [StringLength(255)]
        public string KEY { get; set; }
        [StringLength(255)]
        public string PRIORITY { get; set; }
        public DateTime? CREATED { get; set; }
        [StringLength(255)]
        public string REPORTERUSER { get; set; }
        [StringLength(255)]
        public string ASSIGNEEUSER { get; set; }
        [StringLength(255)]
        public string SUMMARY { get; set; }
        [StringLength(255)]
        public string STATUSNAME { get; set; }
        public int RATE { get; set; }
        [StringLength(255)]
        public string CATEGORY { get; set; }
        [StringLength(255)]
        public string DIRECTION { get; set; }
    }

    
    [Table("JIRA_ISSUE_HISTORY")]
    public partial class JIRA_ISSUE_HISTORY
    {
        [StringLength(255)]
        public string ID { get; set; }
        [StringLength(255)]
        public string JIRAIDENTIFIER { get; set; }
        public DateTime CREATEDDATE { get; set; }
        [StringLength(255)]
        public string FIELDNAME { get; set; }
        [StringLength(255)]
        public string FROMVALUE { get; set; }
        [StringLength(255)]
        public string TOVALUE { get; set; }
    }
}