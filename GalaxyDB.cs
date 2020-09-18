using Microsoft.EntityFrameworkCore;
using System.Configuration;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System;
using System.Net;
using NLog;

namespace SDP2Jira
{
    public partial class GalaxyDB : DbContext
    {
        public virtual DbSet<JIRA_ISSUE> JIRA_ISSUE { get; set; }
        public virtual DbSet<JIRA_ISSUE_HISTORY> JIRA_ISSUE_HISTORY { get; set; }
        public virtual DbSet<JIRA_LOG> JIRA_LOG { get; set; }
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
            modelBuilder.Entity<JIRA_LOG>()
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

    [Table("JIRA_LOG")]
    public partial class JIRA_LOG
    {
        public int ID { get; set; }
        public DateTime LOGDATE { get; set; }
        [StringLength(255)]
        public string LOGLEVEL { get; set; }
        [StringLength(255)]
        public string USERNAME { get; set; }
        [StringLength(255)]
        public string HOSTNAME { get; set; }
        [StringLength(20)]
        public string HOSTIP { get; set; }
        [StringLength(4000)]
        public string MESSAGE { get; set; }
    }

    public class GalaxyLogger
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public void SaveLog(string type, string message)
        {
            string hostName = Dns.GetHostName();
            string hostIP = "";
            IPHostEntry ipHostEntry = Dns.GetHostEntry(hostName);
            foreach (IPAddress ipAddress in ipHostEntry.AddressList)
                if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    hostIP = ipAddress.ToString();
            using (GalaxyDB galaxyDB = new GalaxyDB())
            {
                var jira_log = new JIRA_LOG()
                {
                    LOGDATE = DateTime.Now,
                    LOGLEVEL = type,
                    USERNAME = Environment.UserName,
                    HOSTNAME = hostName,
                    HOSTIP = hostIP,
                    MESSAGE = message.Length > 4000 ? message.Substring(0, 4000) : message
                };
                galaxyDB.JIRA_LOG.Add(jira_log);
                galaxyDB.SaveChanges();
            }
        }
        public void Info(string message)
        {
            SaveLog("INFO", message);
            Logger.Info(message);
        }

        public void Error(string message)
        {
            SaveLog("ERROR", message);
            Logger.Error(message);
        }
    }
}