using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.DirectoryServices;
using System.Configuration;
using System.IO;
using Atlassian.Jira;
using System.Data;
using Excel = Microsoft.Office.Interop.Excel;
using NLog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SDP2Jira
{
    class Program
    {
        private static Jira jira;
        private static readonly string LDAP_ASKONA = "LDAP://DC=hcaskona,DC=com";
        private static readonly string LDAP_PMT = "LDAP://DC=gw-ad,DC=local";
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static List<string> supportList = new();
        private static DateTime startDate;
        private static DateTime endDate;
        private static int threadCount = 10;
        private static readonly object lockObject = new object();
        private static string HtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;
            // Декодирование HTML-символов
            var text = System.Net.WebUtility.HtmlDecode(html);
            // Удаление пробелов между тегами
            text = Regex.Replace(text, @"(>|$)(s|\n|\r)+<", "><");
            // Замена <br> на переносы строк
            text = Regex.Replace(text, @"<brs*/?>", Environment.NewLine, RegexOptions.IgnoreCase);
            // Удаление всех остальных тегов
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);
            return text.Trim(); // Удаляем лишние пробелы в начале и конце
        }
        private static bool IsLoginExists(string ldap, string mail, string displayname, out string login)
        {
            login = string.Empty;
            using DirectoryEntry de = new(ldap);
            using DirectorySearcher ds = new(de);
            ds.Filter = string.IsNullOrWhiteSpace(mail)
                ? $"(displayname={displayname})"
                : $"(mail={mail})";
            ds.PropertiesToLoad.Add("samaccountname");
            SearchResult sr = ds.FindOne();
            // Если результат не найден, возвращаем false
            if (sr == null || sr.Properties["samaccountname"].Count == 0)
            {
                // Если ldap - это LDAP_ASKONA, выполняем рекурсивный вызов
                if (ldap == LDAP_ASKONA)
                    return IsLoginExists(LDAP_PMT, mail, displayname, out login);
                return false;
            }
            // Получаем логин
            login = sr.Properties["samaccountname"][0].ToString();
            return true;
        }
        private static async void AddAttachmentsAsync(Issue issue, string path)
        {
            byte[] data = File.ReadAllBytes("files\\" + path);
            UploadAttachmentInfo info = new(path, data);
            await issue.AddAttachmentAsync(new UploadAttachmentInfo [] { info });
        }
        private static void Main(string[] args)
        {
            Logger.Info("Program is running.");
            try
            {
                string jiraServer = ConfigurationManager.AppSettings["JIRA_SERVER"];
                string jiraLogin = ConfigurationManager.AppSettings["JIRA_LOGIN"];
                string jiraPass = ConfigurationManager.AppSettings["JIRA_PASS"];
                jira = Jira.CreateRestClient(jiraServer, jiraLogin, jiraPass);
                var project = jira.Projects.GetProjectAsync("ERP").Result;
                Logger.Info($"Connected to Jira {jiraServer}");
            }
            catch (Exception ex)
            {
                Logger.Error("Connection error to Jira!\r\n" + ex.Message);
                return;
            }
            if (args.Length > 0 && args[0] == "-wl")
            {
                GetWorkLog();
                return;
            }
            if (args.Length > 0 && args[0] == "-r" && !string.IsNullOrEmpty(args[1]))
            {
                string projectKey = args.Length == 4 && args[2] == "-proj" && !string.IsNullOrEmpty(args[3])
                    ? args[3]
                    : "ERP";
                SyncRequest(args[1], projectKey);
            }
            Logger.Info("Program closed.");
        }
        private static void SyncRequest(string request_id, string project)
        {
            int warning = 0;
            SDP.Request request = new();
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
            if (jira.Issues.Queryable.Where(x => x["Номер заявки SD"] == new LiteralMatch(request.Id)).Any())
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
                issue.Summary = summary.Length > 255 ? summary[..255] : summary;
                if (project == "ERP")
                {
                    if (request.Udf_fields.Udf_pick_3901 == null)
                    {
                        Logger.Error("Field \"Direction\" is not filled!");
                        return;
                    }
                    else
                        issue["Направление"] = request.Udf_fields.Udf_pick_3901;
                    issue.Type = "10206"; //Analysis
                }
                else
                    issue.Type = "10301"; //Task
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
                        SDP.Note note = new();
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
                        Comment comment = new()
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
                Console.WriteLine($"Issue {issue.Key} created with {warning} warnings.");
            }
        }
        private static void GetWorkLog()
        {
            supportList = ConfigurationManager.AppSettings["SUPPORT_LIST"].Split(';').ToList();
            startDate = Convert.ToDateTime(ConfigurationManager.AppSettings["WORKLOG_STARTDATE"]);
            endDate = Convert.ToDateTime(ConfigurationManager.AppSettings["WORKLOG_ENDDATE"]);
            DataTable dt = new();
            dt.Columns.Add("Задача");
            dt.Columns.Add("Бизнес-юнит");
            dt.Columns.Add("Тип задачи");
            dt.Columns.Add("ФИО");
            dt.Columns.Add("Рабочее время", typeof(double));
            dt.Columns.Add("Дата");
            dt.Columns.Add("ITP тип цели");
            dt.Columns.Add("ITP эффект");
            dt.Columns.Add("ITP статус");
            dt.Columns.Add("ITP тема");
            dt.Columns.Add("ITP оценка Галактики");
            // JQL-запрос для получения задач с worklog за период
            var worklogJQL = $"project in (ERP, AT) and worklogDate >= '{startDate:yyyy-MM-dd}' and worklogDate <= '{endDate:yyyy-MM-dd}'";
            IssueSearchOptions options = new(worklogJQL)
            {
                MaxIssuesPerRequest = 10000
            };
            // Получаем задачи по JQL
            var issues = jira.Issues.GetIssuesFromJqlAsync(options).Result.ToList();
            Logger.Info($"Всего получено {issues.Count} задач");

            List<Task> taskList = new();
            for (int i = 0; i < threadCount; i++)
            {
                int threadId = i;
                Action<object> action = x => ProcessIssue((int)x, issues, dt);
                taskList.Add(new TaskFactory().StartNew(action, threadId));
            }
            Task.WaitAll(taskList.ToArray());
            ExportToExcel(dt);
        }
        private static void ProcessIssue(int threadId, List<Issue> issues, DataTable dt)
        {
            int j = 0;
            // Обработка результатов
            for (int i = 0; i < issues.Count; i++)
            {
                if (i % threadCount == threadId)
                {
                    j++;
                    Issue issue = issues[i];
                    string type = issue.Type.ToString();
                    if (type == "Подзадача")
                    {
                        issue = jira.Issues.GetIssueAsync(issue.ParentIssueKey).Result;
                        type = issue.Type.ToString();
                    }
                    type = type switch
                    {
                        "Улучшение" => "CHANGE",
                        "Тех.долг" => "TECH DEBT",
                        _ => "RUN",
                    };
                    // Получаем связанную инициативу
                    var linkedIssueLink = issue.GetIssueLinksAsync().Result
                        .FirstOrDefault(x => x.InwardIssue.Key.ToString().StartsWith("ITP") || x.OutwardIssue.Key.ToString().StartsWith("ITP"));
                    Issue linkedIssue = linkedIssueLink != null ? 
                        linkedIssueLink.InwardIssue.Key.ToString().StartsWith("ITP") ? 
                        linkedIssueLink.InwardIssue : 
                        linkedIssueLink.OutwardIssue : 
                        null;
                    string issueKey = linkedIssue != null ? linkedIssue.Key.ToString() : issue.Key.ToString();
                    // Получаем все worklog для задачи
                    var worklogs = issues[i].GetWorklogsAsync().Result.ToList();
                    foreach (var worklog in worklogs)
                        if ((supportList.Contains(worklog.AuthorUser.DisplayName) || string.IsNullOrEmpty(supportList[0])) &&
                            worklog.StartDate?.Date >= startDate && worklog.StartDate?.Date <= endDate)
                            lock(lockObject)
                            {
                                dt.Rows.Add(issueKey,
                                            issue.CustomFields["Направление"]?.Values.GetValue(0),
                                            type,
                                            worklog.AuthorUser.DisplayName, Math.Round(TimeSpan.FromSeconds(worklog.TimeSpentInSeconds).TotalHours, 2),
                                            worklog.StartDate?.Date,
                                            linkedIssue?.CustomFields["Тип цели"]?.Values.GetValue(0),
                                            linkedIssue?.CustomFields["Эффект для компании"]?.Values.GetValue(0),
                                            linkedIssue?.Status.Name,
                                            linkedIssue?.Summary,
                                            linkedIssue?.CustomFields["Оценка Галактики"]?.Values.GetValue(0));
                            }
                    if (j % 10 == 0)
                        Logger.Info($"Поток {threadId} - обработано {j} задач");
                }
            }
        }
        private static void ExportToExcel(DataTable dt)
        {
            // Создаем экземпляр Excel
            Excel.Application exApp = new();
            Excel.Workbook exWB = exApp.Workbooks.Add();
            Excel.Worksheet exWS = (Excel.Worksheet)exWB.Worksheets[1];
            exWS.Name = "Рабочее время";
            // Установка заголовков
            for (int j = 0; j < dt.Columns.Count; j++)
                exWS.Cells[1, j + 1] = dt.Columns[j].ColumnName;
            // Заполнение данными
            var objectData = new object[dt.Rows.Count, dt.Columns.Count];
            for (int i = 0; i < dt.Rows.Count; i++)
                for (int j = 0; j < dt.Columns.Count; j++)
                    objectData[i, j] = dt.Rows[i][j];
            // Запись данных в диапазон
            Excel.Range startCell = exWS.Cells[2, 1]; // Начинаем со второй строки
            Excel.Range endCell = startCell.Offset[dt.Rows.Count - 1, dt.Columns.Count - 1];
            Excel.Range writeRange = exWS.Range[startCell, endCell];
            writeRange.Value2 = objectData;
            // Форматирование
            Excel.Range headerRange = exWS.Range["A1", "A1"].Resize[1, dt.Columns.Count];
            headerRange.Font.Bold = true;
            headerRange.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
            // Установка границ
            writeRange.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
            // Фильтрация
            object missingValue = System.Reflection.Missing.Value;
            headerRange.AutoFilter(1, missingValue, Excel.XlAutoFilterOperator.xlAnd, missingValue, true);
            // Авторазмер колонок
            exWS.Columns.AutoFit();
            // Показать Excel
            exApp.Visible = true;
            exApp.Interactive = true;
        }
    }
}