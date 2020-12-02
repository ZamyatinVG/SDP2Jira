﻿using System;
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

        private static List<string> supportList;
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
        private static bool IsLoginExists(string mail, string displayname, out string login)
        {
            login = "";
            DirectorySearcher ds = new DirectorySearcher()
            {
                Filter = mail == null ? $"(displayname={displayname})" : $"(mail={mail})",
                PropertiesToLoad = { "samaccountname" }
            };
            SearchResult sr = ds.FindOne();
            if (sr == null)
                return false;
            else
            {
                login = sr.Properties["samaccountname"][0].ToString();
                return true;
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
            Logger.Info("Запуск программы.");
            try
            {
                jira = Jira.CreateRestClient(ConfigurationManager.AppSettings["JIRA_SERVER"],
                                             ConfigurationManager.AppSettings["JIRA_LOGIN"],
                                             ConfigurationManager.AppSettings["JIRA_PASS"]);
                var project = jira.Projects.GetProjectAsync("ERP").Result;
                Logger.Info($"Подключились к Jira: {ConfigurationManager.AppSettings["JIRA_SERVER"]} к проекту {project.Name}.");
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка подключения к Jira!\r\n" + ex.Message);
                return;
            }
            GetSupportList();
            if (args.Length == 0)
                SyncRequests();
            else
            {
                if (args[0] == "-stats")
                    GetStats();
                if (args[0] == "-week")
                    UpdateWeeklyPriority();
                if (args[0] == "-sprint")
                    CleanSprint();
            }
            //UpdateStoryPoints();
            Logger.Info("Завершение работы программы.");
        }
        private static void GetSupportList()
        {
            try
            {
                string[] supportListArray = ConfigurationManager.AppSettings["SUPPORT_LIST"].Split(';');
                supportList = new List<string>(supportListArray);
                Logger.Info("Загружен список специалистов.");
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка загрузки списка специалистов!\r\n" + ex.Message);
                return;
            }
        }
        private static void SyncRequests()
        {           
            foreach (string supportName in supportList)
            {
                try
                {
                    SDP.GetRequestList(supportName);
                    Logger.Info($"По специалисту {supportName} загружено {SDP.requestList.requests.Count} заявок.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка получения списка заявок из SDP по специалисту {supportName}!\r\n" + ex.Message);
                }
                foreach (var req in SDP.requestList.requests)
                {
                    SDP.Request request = new SDP.Request();
                    try
                    {
                        request = SDP.GetRequest(req.Id);
                        //Logger.Info("Получены данные по заявке:\r\n" + JsonConvert.SerializeObject(request, Formatting.Indented));
                        Logger.Info($"Получены данные по заявке {request.Id}.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Ошибка получения заявки {req.Id} из SDP!\r\n" + ex.Message);
                    }
                    if (IsLoginExists(request.Requester.Email_id, null, out string login))
                    {
                        Logger.Info($"Автор: {login}");
                        request.AuthorLogin = login;
                    }
                    else
                    {
                        Logger.Error("Не удалось определить логин автора!");
                        continue;
                    }
                    if (IsLoginExists(request.Technician.Email_id, null, out login))
                    {
                        request.SpecialistLogin = login;
                    }
                    else
                    {
                        Logger.Error("Не удалось определить логин специалиста!");
                        continue;
                    }

                    if (jira.Issues.Queryable.Where(x => x["Номер заявки SD"] == new LiteralMatch(request.Id)).Count() > 0)
                        Logger.Info($"В Jira уже есть задача {jira.Issues.Queryable.Where(x => x["Номер заявки SD"] == new LiteralMatch(request.Id)).FirstOrDefault().Key} по заявке {request.Id}!");
                    else
                    {
                        request.ParseDescription(request.Description ?? "", 0);
                        var issue = jira.CreateIssue("ERP");
                        issue.Reporter = request.AuthorLogin;
                        issue.Assignee = request.SpecialistLogin;
                        issue.Description = SDP.GetRequestUrl(request.Id) + "\r\n" + HtmlToPlainText(request.Description ?? "");
                        issue["Номер заявки SD"] = request.Id;
                        issue.Type = "10301"; //Task
                        issue.Summary = $"{request.Id} {request.Subject}";
                        if (request.Udf_fields.Udf_pick_3901 == null)
                        {
                            Logger.Error("Не заполнено поле \"Направление\"!");
                            continue;
                        } 
                        else
                            issue["Направление"] = request.Udf_fields.Udf_pick_3901;
                        if (request.Subcategory == null)
                        {
                            Logger.Error("Не заполнены поля \"Категория/Подкатегория\"!");
                            continue;
                        }
                        else
                            issue["Категория"] = request.Subcategory.Name;
                        try
                        {
                            issue.SaveChanges();
                            Logger.Info($"Задача {issue.Key} создана по заявке {request.Id}.");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Ошибка создания задачи в Jira по заявке {request.Id}!\r\n" + ex.Message);
                            continue;
                        }
                        if (request.Has_notes)
                        {
                            try
                            {
                                SDP.GetNotes(request.Id);
                                Logger.Info($"По заявке {request.Id} получено {SDP.noteList.notes.Count} примечаний.");
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Ошибка получения списка примечаний по заявке {request.Id}!\r\n" + ex.Message);
                            }
                            foreach (var nt in SDP.noteList.notes)
                            {
                                SDP.Note note = new SDP.Note();
                                try
                                {
                                    note = SDP.GetNote(request.Id, nt.Id);
                                    Logger.Info($"По заявке {request.Id} получено примечание {note.Id}");
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Ошибка получения примечания {nt.Id} по заявке {request.Id}!\r\n" + ex.Message);
                                }
                                if (IsLoginExists(note.Created_by.Email_id, null, out string notelogin))
                                {
                                    Logger.Info($"Автор примечания: {notelogin}");
                                    note.AuthorLogin = notelogin;
                                }
                                else
                                {
                                    Logger.Error("Не удалось определить логин автора примечания!");
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
                            Logger.Info($"Начата загрузка вложений по заявке {request.Id}.");
                            foreach (var attachment in request.Attachments)
                            {
                                try
                                {
                                    SDP.DowloadFile(attachment.Content_url, attachment.File_name);
                                    Logger.Info($"Загружен файл {attachment.File_name}.");
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Ошибка загрузки файла {attachment.File_name}!\r\n" + ex.Message);
                                    continue;
                                }
                                try
                                {
                                    AddAttachmentsAsync(issue, attachment.File_name);
                                    Logger.Info($"К задаче {issue.Key} добавлен файл {attachment.File_name}.");
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Ошибка добавления файла {attachment.File_name} к задача {issue.Key}!\r\n" + ex.Message);
                                }
                                File.Delete("files\\" + attachment.File_name);
                            }
                        }
                        string result = SDP.CloseRequest(request.Id, out string status_code);
                        if (status_code == "2000")
                            Logger.Info(result);
                        else
                            Logger.Error(result);
                    }
                }
            }
        }
        private static void GetStats()
        {
            foreach (string supportName in supportList)
                if (IsLoginExists(null, supportName, out string login))
                {
                    jira.Issues.MaxIssuesPerRequest = 1000;
                    var jira_issues = jira.Issues.Queryable.Where(x => x.Assignee == new LiteralMatch(login)).ToList();
                    using (BIDbContext context = new BIDbContext())
                    {
                        foreach (Issue jira_issue in jira_issues)
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
                                issue.PRIORITY = jira_issue.Priority.Name;
                                issue.CREATED = jira_issue.Created;
                                issue.REPORTERUSER = jira_issue.ReporterUser.DisplayName;
                                issue.ASSIGNEEUSER = jira_issue.AssigneeUser.DisplayName;
                                issue.SUMMARY = jira_issue.Summary;
                                issue.STATUSNAME = jira_issue.Status.Name;
                                issue.STORYPOINTS = jira_issue["Story Points"] == null ? 0 : Convert.ToInt32(jira_issue["Story Points"].Value);
                                issue.CATEGORY = jira_issue["Категория"]?.Value.ToString();
                                issue.DIRECTION = jira_issue["Направление"]?.Value.ToString();
                                issue.UPDATED = jira_issue.Updated;

                                var changeLog = jira_issue.GetChangeLogsAsync().Result;
                                foreach (var history in changeLog)
                                    foreach (var item in history.Items)
                                        if (item.FieldName == "status")
                                        {
                                            ISSUE_HISTORY issue_History = context.ISSUE_HISTORY.Where(x => x.ID == history.Id).FirstOrDefault();
                                            if (issue_History == null)
                                            {
                                                issue_History = new ISSUE_HISTORY
                                                {
                                                    ID = history.Id
                                                };
                                                context.ISSUE_HISTORY.Add(issue_History);
                                            }
                                            issue_History.JIRAIDENTIFIER = jira_issue.JiraIdentifier;
                                            issue_History.CREATEDDATE = history.CreatedDate;
                                            issue_History.FIELDNAME = item.FieldName;
                                            issue_History.FROMVALUE = item.FromValue;
                                            issue_History.TOVALUE = item.ToValue;
                                        }
                            }
                        }
                        context.SaveChanges();
                    }
                    Logger.Info($"Загружено {jira_issues.Count} задач по специалисту {supportName}.");
                }
        }
        private static void UpdateWeeklyPriority()
        {
            foreach (string supportName in supportList)
                if (IsLoginExists(null, supportName, out string login))
                {
                    jira.Issues.MaxIssuesPerRequest = 1000;
                    var jira_issues = jira.Issues.Queryable.Where(x => x.Assignee == new LiteralMatch(login)).ToList();
                    foreach (Issue jira_issue in jira_issues.Where(x => x.Type.Id == "10003" && //SubTask
                                                                        x.Status.StatusCategory.Key != "done")) 
                    {
                        var parent_issue = jira.Issues.Queryable.Where(x => x.Key == jira_issue.ParentIssueKey).First();
                        if (jira_issue["Неделя, приоритет"]?.Value != parent_issue["Неделя, приоритет"]?.Value)
                        {
                            jira_issue["Неделя, приоритет"] = parent_issue["Неделя, приоритет"]?.Value;
                            jira_issue.SaveChanges();
                            Logger.Info($"По специалисту {supportName} у подзадачи {jira_issue.Key} обновлен атрибут \"Неделя, приоритет\".");
                        }
                    }
                }
        }
        private static void UpdateStoryPoints()
        {
            foreach (string supportName in supportList)
                if (IsLoginExists(null, supportName, out string login))
                {
                    jira.Issues.MaxIssuesPerRequest = 1000;
                    var jira_issues = jira.Issues.Queryable.Where(x => x.Assignee == new LiteralMatch(login)).ToList();
                    foreach (Issue jira_issue in jira_issues)
                    {
                        if (jira_issue["Story Points"]?.Value != jira_issue["Оценка"]?.Value)
                        {
                            jira_issue["Story Points"] = jira_issue["Оценка"]?.Value;
                            jira_issue.SaveChanges();
                            Logger.Info($"По специалисту {supportName} у задачи {jira_issue.Key} обновлен атрибут \"Story Points\".");
                        }
                    }
                }
        }
        private static void CleanSprint()
        {
            foreach (string supportName in supportList)
                if (IsLoginExists(null, supportName, out string login))
                {
                    jira.Issues.MaxIssuesPerRequest = 1000;
                    var jira_issues = jira.Issues.Queryable.Where(x => x.Assignee == new LiteralMatch(login)).ToList();
                    foreach (Issue jira_issue in jira_issues.Where(x => x.Status.StatusCategory.Key != "done"))
                    {
                        if (jira_issue["Sprint"] != null && !jira_issue["Sprint"].Value.Contains("Galaxy") &&
                            jira_issue.Key != "PIM-210" && jira_issue.Key != "PIM-219" && jira_issue.Key != "PIM-221")
                        {
                            jira_issue["Sprint"] = null;
                            jira_issue.SaveChanges();
                            Comment comment = new Comment()
                            {
                                Body = $"Прошу добавлять задачи для специалистов команды Галактики ({jira_issue.AssigneeUser.DisplayName}) только в спринты Galaxy. Либо по согласованию в текущий, либо в будущий."
                            };
                            jira_issue.AddCommentAsync(comment);
                            Logger.Info($"По специалисту {supportName} у задачи {jira_issue.Key} удален атрибут \"Sprint\".");
                        }
                    }
                }
        }
    }
}