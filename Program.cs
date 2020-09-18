﻿using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.DirectoryServices;
using System.Configuration;
using System.Collections.Generic;
using System.IO;
using Atlassian.Jira;
using NLog;

namespace SDP2Jira
{
    class Program
    {
        private static Jira jira;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
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
                if (args[0] == "-stats")
                    GetStats();
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
                        //парсим визуальные вложения в описании заявки
                        string description = request.Description;
                        while (description.Length > 0)
                        {
                            int index = description.IndexOf("src=\"");
                            if (index < 0)
                                description = "";
                            else
                            {
                                SDP.Request.Attachment attachment = new SDP.Request.Attachment();
                                description = description.Substring(index + 5);
                                index = description.IndexOf("\"");
                                attachment.Content_url = description.Substring(0, index);
                                index = attachment.Content_url.LastIndexOf("/");
                                attachment.File_name = attachment.Content_url.Substring(index + 1);
                                request.Attachments.Add(attachment);
                                Logger.Info($"Найдено вложение в описании: Content_url = {attachment.Content_url}, File_name = {attachment.File_name}");
                            }
                        }
                        var issue = jira.CreateIssue("ERP");
                        issue.Reporter = request.AuthorLogin;
                        issue.Assignee = request.SpecialistLogin;
                        issue.Description = SDP.GetRequestUrl(request.Id) + "\r\n" + HtmlToPlainText(request.Description);
                        issue["Номер заявки SD"] = request.Id;
                        issue.Type = "10301"; //Task
                        issue.Summary = $"{request.Id} {request.Subject}";
                        issue["Направление"] = request.Udf_fields.Udf_pick_3901;
                        issue["Категория"] = request.Subcategory == null ? "" : request.Subcategory.Name;
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
            {
                if (IsLoginExists(null, supportName, out string login))
                {
                    jira.Issues.MaxIssuesPerRequest = 1000;
                    var issues = jira.Issues.Queryable.Where(x => x.Assignee == new LiteralMatch(login)).ToList();
                    using (GalaxyDB galaxyDB = new GalaxyDB())
                    {
                        foreach (Issue issue in issues)
                        {
                            JIRA_ISSUE jira_Issue = galaxyDB.JIRA_ISSUE.Where(x => x.JIRAIDENTIFIER == issue.JiraIdentifier).FirstOrDefault();
                            if (jira_Issue == null)
                            {
                                jira_Issue = new JIRA_ISSUE();
                                jira_Issue.JIRAIDENTIFIER = issue.JiraIdentifier;
                                galaxyDB.JIRA_ISSUE.Add(jira_Issue);
                            }
                            jira_Issue.KEY = issue.Key.Value;
                            jira_Issue.PRIORITY = issue.Priority.Name;
                            jira_Issue.CREATED = issue.Created;
                            jira_Issue.REPORTERUSER = issue.ReporterUser.DisplayName;
                            jira_Issue.ASSIGNEEUSER = issue.AssigneeUser.DisplayName;
                            jira_Issue.SUMMARY = issue.Summary;
                            jira_Issue.STATUSNAME = issue.Status.Name;
                            jira_Issue.RATE = issue["Оценка"] == null ? 0 : Convert.ToInt32(issue["Оценка"].Value);
                            jira_Issue.CATEGORY = issue["Категория"] == null ? null : issue["Категория"].Value.ToString();
                            jira_Issue.DIRECTION = issue["Направление"] == null ? null : issue["Направление"].Value.ToString();


                            var changeLog = issue.GetChangeLogsAsync().Result;
                            foreach (var history in changeLog)
                                foreach (var item in history.Items)
                                    if (item.FieldName == "status")
                                    {
                                        JIRA_ISSUE_HISTORY jira_Issue_History = galaxyDB.JIRA_ISSUE_HISTORY.Where(x => x.ID == history.Id).FirstOrDefault();
                                        if (jira_Issue_History == null)
                                        {
                                            jira_Issue_History = new JIRA_ISSUE_HISTORY();
                                            jira_Issue_History.ID = history.Id;
                                            galaxyDB.JIRA_ISSUE_HISTORY.Add(jira_Issue_History);
                                        }
                                        jira_Issue_History.JIRAIDENTIFIER = issue.JiraIdentifier;
                                        jira_Issue_History.CREATEDDATE = history.CreatedDate;
                                        jira_Issue_History.FIELDNAME = item.FieldName;
                                        jira_Issue_History.FROMVALUE = item.FromValue;
                                        jira_Issue_History.TOVALUE = item.ToValue;
                                    }
                        }
                        galaxyDB.SaveChanges();
                    }
                    Logger.Info($"Загружено {issues.Count} задач по специалисту {supportName}.");
                }
            }
        }
    }
}