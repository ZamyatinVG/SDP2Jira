using Microsoft.EntityFrameworkCore;
using System.Configuration;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System;
using System.Net;
using System.Reflection;
using NLog;

namespace SDP2Jira
{
    public partial class LogDbContext : DbContext
    {
        public virtual DbSet<ISSUE> ISSUE { get; set; }
        public virtual DbSet<ISSUE_HISTORY> ISSUE_HISTORY { get; set; }
        public virtual DbSet<LOG> LOG { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(ConfigurationManager.ConnectionStrings["LOG_DB"].ToString());
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ISSUE>()
                .HasKey(x => x.JIRAIDENTIFIER);
            modelBuilder.Entity<ISSUE_HISTORY>()
                .HasKey(x => x.ID);
            modelBuilder.Entity<LOG>()
                .HasKey(x => x.ID);
        }
    }

    [Table("ISSUE")]
    public partial class ISSUE
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
        public int STORYPOINTS { get; set; }
        [StringLength(255)]
        public string CATEGORY { get; set; }
        [StringLength(255)]
        public string DIRECTION { get; set; }
        public DateTime? UPDATED { get; set; }
        [StringLength(255)]
        public string TYPE { get; set; }
    }

    [Table("ISSUE_HISTORY")]
    public partial class ISSUE_HISTORY
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

    [Table("LOG")]
    public partial class LOG
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
        [StringLength(20)]
        public string VERSION { get; set; }
    }

    public class DbLogger
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public string userName = Environment.UserName;
        public void SaveLog(string type, string message)
        {
            string hostName = Dns.GetHostName();
            string hostIP = "";
            IPHostEntry ipHostEntry = Dns.GetHostEntry(hostName);
            foreach (IPAddress ipAddress in ipHostEntry.AddressList)
                if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    hostIP = ipAddress.ToString();
            LogDbContext context = new LogDbContext();
            var log = new LOG()
            {
                LOGDATE = DateTime.Now,
                LOGLEVEL = type,
                USERNAME = userName,
                HOSTNAME = hostName,
                HOSTIP = hostIP,
                MESSAGE = message.Length > 4000 ? message.Substring(0, 4000) : message,
                VERSION = Assembly.GetExecutingAssembly().GetName().Version.ToString()
            };
            context.LOG.Add(log);
            try
            {
                context.SaveChanges();
            }
            catch (Exception ex)
            {
                Logger.Error("Connection error to logging server!\r\n" + ex.Message);
                Console.ReadKey();
                Environment.Exit(0);
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
        public void Warn(string message)
        {
            SaveLog("WARN", message);
            Logger.Warn(message);
        }
    }
}