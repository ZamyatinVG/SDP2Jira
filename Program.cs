using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.DirectoryServices;
using System.Configuration;
using System.Collections.Generic;
using System.IO;
using Atlassian.Jira;

namespace SDP2Jira
{
    class Program
    {
        private static Jira jira;
        private static readonly DbLogger Logger = new DbLogger();
        private static string LDAP_ASKONA = "LDAP://DC=hcaskona,DC=com";
        private static string LDAP_PMT = "LDAP://DC=gw-ad,DC=local";
        private static string HtmlToPlainText(string html)
        {
            //https://stackoverflow.com/questions/286813/how-do-you-convert-html-to-plain-text
            const string tagWhiteSpace = @"(>|$)(\W|\n|\r)+<"; //matches one or more (white space or line breaks) between '>' and '<'
            const string stripFormatting = @"<[^>]*(>|$)"; //match any character between '<' and '>', even when end tag is missing
            const string lineBreak = @"<(br|BR)\s{0,1}\/{0,1}>"; //matches: <br>,<br/>,<br />,<BR>,<BR/>,<BR />
            var lineBreakRegex = new Regex(lineBreak, RegexOptions.Multiline);
            var stripFormattingRegex = new Regex(stripFormatting, RegexOptions.Multiline);
            var tagWhiteSpaceRegex = new Regex(tagWhiteSpace, RegexOptions.Multiline);
            var text = html;
            //Decode html specific characters
            text = System.Net.WebUtility.HtmlDecode(text);
            //Remove tag whitespace/line breaks
            text = tagWhiteSpaceRegex.Replace(text, "><");
            //Replace <br /> with line breaks
            text = lineBreakRegex.Replace(text, Environment.NewLine);
            //Strip formatting
            text = stripFormattingRegex.Replace(text, string.Empty);
            return text;
        }
        private static bool IsLoginExists(string ldap, string mail, string displayname, out string login)
        {
            login = "";
            DirectoryEntry de = new DirectoryEntry(ldap);
            DirectorySearcher ds = new DirectorySearcher(de)
            {
                Filter = mail == null || mail == "" ? $"(displayname={displayname})" : $"(mail={mail})",
                PropertiesToLoad = { "samaccountname" }
            };
            SearchResult sr = ds.FindOne();
            if (sr == null)
                return false;
            else
            {
                if (sr.Properties["samaccountname"].Count == 0)
                    if (ldap == LDAP_ASKONA)
                        return IsLoginExists(LDAP_PMT, mail, displayname, out login);
                    else
                        return false;
                else
                {
                    login = sr.Properties["samaccountname"][0].ToString();
                    return true;
                }
            }
        }
        private static async void AddAttachmentsAsync(Issue issue, string path)
        {
            byte[] data = File.ReadAllBytes("files\\" + path);
            UploadAttachmentInfo info = new UploadAttachmentInfo(path, data);
            await issue.AddAttachmentAsync(new UploadAttachmentInfo [] { info });
        }
        private static void Main(string[] args)
        {
            Logger.Info("Program is running.");
            try
            {
                jira = Jira.CreateRestClient(ConfigurationManager.AppSettings["JIRA_SERVER"],
                                             ConfigurationManager.AppSettings["JIRA_LOGIN"],
                                             ConfigurationManager.AppSettings["JIRA_PASS"]);
                var project = jira.Projects.GetProjectAsync("ERP").Result;
                Logger.Info($"Connected to Jira {ConfigurationManager.AppSettings["JIRA_SERVER"]}");
            }
            catch (Exception ex)
            {
                Logger.Error("Connection error to Jira!\r\n" + ex.Message);
                return;
            }
            if (args[0] == "-r" && args[1]?.Length > 0)
                if (args.Length == 4 && args[2] == "-u" && args[3]?.Length > 0)
                    SyncRequest(args[1], "ERP", args[3]);
                else
                    if (args.Length == 6 && args[4] == "-proj" && args[5]?.Length > 0)
                        SyncRequest(args[1], args[5], args[3]);
                    else
                        SyncRequest(args[1], "ERP");
            if (args[0] == "-stats")
                GetStats();
            Logger.Info("Program closed.");
        }
        private static void SyncRequest(string request_id, string project)
        {
            int warning = 0;
            SDP.Request request = new SDP.Request();
            try
            {
                request = SDP.GetRequest(request_id);
                if (request == null)
                    throw new Exception("Request not found!");
                else
                    //Logger.Info("Data received for request:\r\n" + JsonConvert.SerializeObject(request, Formatting.Indented));
                    Logger.Info($"Data received for request {request.Id}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error receiving data for request {request_id} from SDP!\r\n" + ex.Message);
                return;
            }
            if (IsLoginExists(LDAP_ASKONA, request.Requester.Email_id, null, out string login))
            {
                Logger.Info($"Author: {login}");
                request.AuthorLogin = login;
            }
            else
            {
                Logger.Error("Could not determine the author's login!");
                return;
            }
            if (request.Technician != null)
                if (IsLoginExists(LDAP_ASKONA, request.Technician.Email_id, null, out login))
                {
                    request.SpecialistLogin = login;
                }
                else
                {
                    Logger.Error("Could not determine specialist login!");
                    return;
                }
            if (jira.Issues.Queryable.Where(x => x["Номер заявки SD"] == new LiteralMatch(request.Id)).Count() > 0)
            {
                Logger.Error($"Jira already has issue {jira.Issues.Queryable.Where(x => x["Номер заявки SD"] == new LiteralMatch(request.Id)).FirstOrDefault().Key} for request {request.Id}!");
            }
            else
            {
                request.ParseDescription(request.Description ?? "", 0);
                var issue = jira.CreateIssue(project);
                issue.Reporter = request.AuthorLogin;
                if (request.Technician != null)
                    issue.Assignee = request.SpecialistLogin;
                issue.Description = SDP.GetRequestUrl(request.Id) + "\r\n" + HtmlToPlainText(request.Description ?? "");
                issue["Номер заявки SD"] = request.Id;
                string summary = $"{request.Id} {request.Subject}";
                issue.Summary = summary.Length > 255 ? summary.Substring(0, 255) : summary;
                if (project == "ERP")
                {
                    if (request.Udf_fields.Udf_pick_3901 == null)
                    {
                        Logger.Error("Field \"Direction\" is not filled!");
                        return;
                    }
                    else
                        issue["Направление"] = request.Udf_fields.Udf_pick_3901;
                    if (request.Item == null)
                    {
                        Logger.Error("Field \"Category/Subcategory/Position\" is not filled!");
                        return;
                    }
                    else
                    {
                        issue["Категория"] = request.Item.Name;
                        if (issue["Категория"] == "Доработка нового функционала" ||
                            issue["Категория"] == "Развитие собственной доработки" ||
                            issue["Категория"] == "Развитие стандартного функционала")
                            issue.Type = "10100"; //Improvement
                        else
                            issue.Type = "10206"; //Analysis
                    }
                }
                else
                {
                    issue.Type = "10301"; //Task
                }
                try
                {
                    issue.SaveChanges();
                    Logger.Info($"Issue {issue.Key} created for request {request.Id}.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Issue creation error in Jira for request {request.Id}!\r\n" + ex.Message);
                    return;
                }
                if (request.Has_notes)
                {
                    try
                    {
                        SDP.GetNotes(request.Id);
                        Logger.Info($"For request {request.Id} recieved {SDP.noteList.notes.Count} notes.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Error getting the list of notes for request {request.Id}!\r\n" + ex.Message);
                        SDP.noteList.notes = null;
                        warning++;
                    }
                    foreach (var nt in SDP.noteList.notes)
                    {
                        SDP.Note note = new SDP.Note();
                        try
                        {
                            note = SDP.GetNote(request.Id, nt.Id);
                            Logger.Info($"For request {request.Id} received note {note.Id}.");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Error getting a note {nt.Id} for request {request.Id}!\r\n" + ex.Message);
                            warning++;
                            continue;
                        }
                        if (IsLoginExists(LDAP_ASKONA, note.Created_by.Email_id, note.Created_by.Name, out string notelogin))
                        {
                            Logger.Info($"Author of note: {notelogin}.");
                            note.AuthorLogin = notelogin;
                        }
                        else
                        {
                            Logger.Warn("Could not determine the login of the author of the note!");
                            warning++;
                            continue;
                        }
                        request.ParseDescription(note.Description ?? "", 1);
                        Comment comment = new Comment()
                        {
                            Author = note.AuthorLogin,
                            Body = HtmlToPlainText(note.Description ?? "")
                        };
                        foreach (SDP.Request.Attachment attachment in request.Attachments)
                            if (attachment.Type == 1)
                                comment.Body += $"[^{attachment.File_name}]";
                        issue.AddCommentAsync(comment);
                    }
                }
                if (request.Attachments.Count > 0)
                {
                    Logger.Info($"Loading of attachments has started for request {request.Id}.");
                    foreach (var attachment in request.Attachments)
                    {
                        try
                        {
                            SDP.DowloadFile(attachment.Content_url, attachment.File_name);
                            Logger.Info($"File {attachment.File_name} apploaded.");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"File {attachment.File_name} appload error!\r\n" + ex.Message);
                            warning++;
                            continue;
                        }
                        try
                        {
                            AddAttachmentsAsync(issue, attachment.File_name);
                            Logger.Info($"File {attachment.File_name} added to issue {issue.Key}.");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Error adding file {attachment.File_name} to issue {issue.Key}!\r\n" + ex.Message);
                            warning++;
                        }
                        File.Delete("files\\" + attachment.File_name);
                    }
                }
                string request_result = SDP.CloseRequest(request.Id, out string status_code);
                if (status_code == "2000")
                    Logger.Info(request_result);
                else
                {
                    Logger.Warn(request_result);
                    warning++;
                }
                Logger.Info($"Issue {issue.Key} created with {warning} warnings.");
            }
        }
        private static void SyncRequest(string request_id, string project, string username)
        {
            Logger.userName = username;
            SyncRequest(request_id, project);
        }
        private static void GetStats()
        {
            int stop_count = 0;
            jira.Issues.MaxIssuesPerRequest = 100;
            List<Issue> jira_issues = new List<Issue>();
            string[] ji = new string[jira.Issues.MaxIssuesPerRequest];
            for (int j = 0; j < 1000; j++)
            {
                for (int i = 0; i < jira.Issues.MaxIssuesPerRequest; i++)
                    ji[i] = (10000 + jira.Issues.MaxIssuesPerRequest * j + i).ToString();
                try
                {
                    jira_issues.Clear();
                    ICollection<Issue> issues = jira.Issues.GetIssuesAsync(ji).Result.Values;
                    jira_issues.AddRange(issues);
                    Logger.Info($"Загружены {j} x {jira.Issues.MaxIssuesPerRequest} задач");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка загрузки {j} x {jira.Issues.MaxIssuesPerRequest} задач!\r\n" + ex.Message);
                }
                if (jira_issues.Count == 0)
                    stop_count++;
                else
                    stop_count = 0;
                if (stop_count == 10)
                {
                    Logger.Info($"Выгрузка статистики по задачам завершена");
                    return;
                }
                jira_issues = jira_issues.Where(x => x.Updated.Value.CompareTo(DateTime.Now.AddDays(-7)) > 0).ToList();
                GetStat(jira_issues);
                Logger.Info($"Обработаны {j} x {jira.Issues.MaxIssuesPerRequest} задач");
            }
        }
        private static void GetStat(List<Issue> jira_issues)
        {
            LogDbContext context = new LogDbContext();
            foreach (Issue jira_issue in jira_issues)
            {
                try
                {
                    ISSUE issue = context.ISSUE.Where(x => x.JIRAIDENTIFIER == jira_issue.JiraIdentifier).FirstOrDefault();
                    if (issue == null)
                    {
                        issue = new ISSUE
                        {
                            JIRAIDENTIFIER = jira_issue.JiraIdentifier
                        };
                        context.ISSUE.Add(issue);
                    }
                    if (jira_issue.Updated != issue.UPDATED)
                    {
                        issue.KEY = jira_issue.Key.Value;
                        issue.PRIORITY = jira_issue.Priority?.Name;
                        issue.CREATED = jira_issue.Created;
                        issue.REPORTERUSER = jira_issue.ReporterUser.DisplayName;
                        issue.ASSIGNEEUSER = jira_issue.AssigneeUser?.DisplayName;
                        issue.SUMMARY = jira_issue.Summary;
                        issue.STATUSNAME = jira_issue.Status.Name;
                        issue.STORYPOINTS = jira_issue["Story Points"] == null ? 0 : Convert.ToDecimal(jira_issue["Story Points"].Value.Replace('.', ','));
                        issue.CATEGORY = jira_issue["Категория"]?.Value;
                        issue.DIRECTION = jira_issue["Направление"]?.Value;
                        issue.UPDATED = jira_issue.Updated;
                        issue.TYPE = jira_issue.Type.Name;

                        var changeLog = jira_issue.GetChangeLogsAsync().Result;
                        foreach (var history in changeLog)
                            foreach (var item in history.Items)
                                if (item.FieldName == "status"/* || item.FieldName == "issuetype"*/)
                                {
                                    ISSUE_HISTORY issue_History = context.ISSUE_HISTORY.Where(x => x.ID == history.Id && x.FIELDNAME == item.FieldName).FirstOrDefault();
                                    if (issue_History == null)
                                    {
                                        issue_History = new ISSUE_HISTORY
                                        {
                                            ID = history.Id,
                                            FIELDNAME = item.FieldName
                                        };
                                        context.ISSUE_HISTORY.Add(issue_History);
                                    }
                                    issue_History.JIRAIDENTIFIER = jira_issue.JiraIdentifier;
                                    issue_History.CREATEDDATE = history.CreatedDate;
                                    issue_History.FROMVALUE = item.FromValue;
                                    issue_History.TOVALUE = item.ToValue;
                                }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка обработки задачи {jira_issue.Key}!\r\n" + ex.Message);
                }
            }
            context.SaveChanges();
        }
    }
}