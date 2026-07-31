using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.DirectoryServices;
using System.Configuration;
using System.IO;
using Atlassian.Jira;
using System.Data;
using ClosedXML.Excel;
using NLog;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;
using System.Threading;
using System.Diagnostics;

namespace SDP2Jira
{
    class Program
    {
        private static Jira jira;
        private static string jiraServer = string.Empty;
        private static readonly string LDAP_ASKONA = "LDAP://DC=hcaskona,DC=com";
        private static readonly string LDAP_PMT = "LDAP://DC=gw-ad,DC=local";
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static List<string> supportList = new();
        private static DateTime startDate;
        private static DateTime endDate;
        private static readonly int threadCount = 5;
        private static readonly object lockObject = new();
        private static string HtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;
            // Декодирование HTML-символов
            var text = System.Net.WebUtility.HtmlDecode(html);
            // Удаление пробелов между тегами
            text = Regex.Replace(text, @"(>|$)\s+<", "><");
            // Замена <br> на переносы строк
            text = Regex.Replace(text, @"<br\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase);
            // Удаление всех остальных тегов
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);
            return text.Trim(); // Удаляем лишние пробелы в начале и конце
        }
        private static bool IsLoginExists(string ldap, string mail, string displayname, out string login)
        {
            login = string.Empty;
            // Без email и без имени построить фильтр поиска невозможно
            if (string.IsNullOrWhiteSpace(mail) && string.IsNullOrWhiteSpace(displayname))
                return false;
            try
            {
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
            catch (Exception ex)
            {
                Logger.Warn($"LDAP lookup error on {ldap} (mail={mail}, displayname={displayname})!\r\n" + ex.Message);
                if (ldap == LDAP_ASKONA)
                    return IsLoginExists(LDAP_PMT, mail, displayname, out login);
                return false;
            }
        }
        private static void AddAttachments(Issue issue, string path)
        {
            byte[] data = File.ReadAllBytes("files\\" + path);
            UploadAttachmentInfo info = new(path, data);
            issue.AddAttachmentAsync(new UploadAttachmentInfo [] { info }).GetAwaiter().GetResult();
        }
        private static void Main(string[] args)
        {
            Logger.Info("Program is running.");
            try
            {
                jiraServer = ConfigurationManager.AppSettings["JIRA_SERVER"];
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
            if (args.Length == 0)
            {
                Logger.Info("Program closed. Invalid arguments.");
                return;
            }
            try
            {
                switch (args[0])
                {
                    case "-wl":
                        GetWorkLog();
                        break;
                    case "-itp":
                        GetWorkLogITP();
                        break;
                    case "-r" when args.Length > 1:
                        string projectKey = args.Length == 4 && args[2] == "-proj" && !string.IsNullOrEmpty(args[3])
                            ? args[3]
                            : "ERP";
                        SyncRequest(args[1], projectKey);
                        break;
                    default:
                        Logger.Info("Invalid arguments.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Unhandled error while running {args[0]}!\r\n" + ex.Message);
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
            var existingIssue = jira.Issues.Queryable.Where(x => x["Номер заявки SD"] == new LiteralMatch(request.Id) && x.Created >= new DateTime(2025, 2, 1)).FirstOrDefault();
            if (existingIssue != null)
            {
                Logger.Error($"Jira already has issue {existingIssue.Key} for request {request.Id}!");
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
                    if (request.Udf_fields.Udf_pick_615 == null)
                    {
                        Logger.Error("Field \"Business unit\" is not filled!");
                        return;
                    }
                    else
                        issue["Business unit"] = request.Udf_fields.Udf_pick_615;
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
                //Чтение примечаний и заполнение комментариев
                var commentDictionary = new Dictionary<string, Comment>();
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
                        if (IsLoginExists(LDAP_ASKONA, note.Added_by?.Email_id, note.Added_by?.Name, out string notelogin))
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
                        request.ParseDescription(note.Description ?? "", Convert.ToInt32(note.Id));
                        Comment comment = new()
                        {
                            Author = note.AuthorLogin,
                            Body = "{color:#ffab00}*" + note.Added_by.Name + "* добавил(а) примечание:{color}\r\n\r\n" + HtmlToPlainText(note.Description ?? "")
                        };
                        commentDictionary.Add(note.Id, comment);
                    }
                }
                //Чтение и запись вложений
                if (request.Attachments.Count > 0)
                {
                    Logger.Info($"Loading of attachments has started for request {request.Id}.");
                    foreach (var attachment in request.Attachments)
                    {
                        try
                        {
                            attachment.Name = SDP.DowloadFile(attachment.Content_url, attachment.Name);
                            Logger.Info($"File {attachment.Name} apploaded.");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"File {attachment.Name} appload error!\r\n" + ex.Message);
                            warning++;
                            continue;
                        }
                        try
                        {
                            AddAttachments(issue, attachment.Name);
                            Logger.Info($"File {attachment.Name} added to issue {issue.Key}.");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Error adding file {attachment.Name} to issue {issue.Key}!\r\n" + ex.Message);
                            warning++;
                        }
                        File.Delete("files\\" + attachment.Name);
                    }
                }
                //Запись комментариев и связь с вложениями
                foreach (var pair in commentDictionary)
                {
                    foreach (SDP.Request.Attachment attachment in request.Attachments)
                        if (attachment.Type.ToString() == pair.Key)
                            pair.Value.Body += $"\r\n[^{attachment.Name}]";
                    try
                    {
                        issue.AddCommentAsync(pair.Value).GetAwaiter().GetResult();
                        Logger.Info($"Note {pair.Key} added.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Error adding note {pair.Key} to issue {issue.Key}!\r\n" + ex.Message);
                        warning++;
                    }
                }
                //Закрытие заявки
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
            dt.Columns.Add("Неделя");
            dt.Columns.Add("Тема");
            dt.Columns.Add("Проект");
            dt.Columns.Add("ITP link");
            // JQL-запрос для получения задач с worklog за период
            var worklogJQL = $"project in (ERP, AT, ITP) and worklogDate >= '{startDate:yyyy-MM-dd}' and worklogDate <= '{endDate:yyyy-MM-dd}'";
            IssueSearchOptions options = new(worklogJQL)
            {
                MaxIssuesPerRequest = 10000
            };
            // Получаем задачи по JQL
            var issues = jira.Issues.GetIssuesFromJqlAsync(options).Result.ToList();
            Logger.Info($"Всего получено {issues.Count} задач");

            // Счетчик для отслеживания прогресса
            int processedCount = 0;
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = threadCount };
            Parallel.ForEach(issues, parallelOptions, issue =>
            {
                try
                {
                    ProcessIssue(issue, dt);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Ошибка обработки задачи {issue.Key} для отчета worklog, задача пропущена!\r\n" + ex.Message);
                }
                // Атомарно увеличиваем счетчик
                int currentCount = Interlocked.Increment(ref processedCount);
                if (currentCount % 100 == 0)
                    Logger.Info($"Обработано {currentCount} задач из {issues.Count}");
            });
            ExportToExcel(dt);
        }
        private static void ProcessIssue(Issue issue, DataTable dt)
        {
            string issueType = issue.Type.ToString();
            Issue parentIssue = issueType == "Подзадача" ? jira.Issues.GetIssueAsync(issue.ParentIssueKey).Result : issue;
            Issue linkedIssue = null, linkedEpicIssue = null;
            if (issue.Project == "ITP")
                issueType = "CHANGE";
            else
            { 
                issueType = parentIssue.Type.ToString() switch
                {
                    "Улучшение" => "CHANGE",
                    "Тех.долг" => "TECH DEBT",
                    _ => "RUN",
                };
                // Получаем связанную инициативу
                var linkedIssueLink = parentIssue.GetIssueLinksAsync().Result
                    .FirstOrDefault(x => x.InwardIssue.Project == "ITP" || x.OutwardIssue.Project == "ITP");
                linkedIssue = linkedIssueLink != null ?
                    linkedIssueLink.InwardIssue.Project == "ITP" ?
                    linkedIssueLink.InwardIssue :
                    linkedIssueLink.OutwardIssue :
                    null;
                //Проверяем Epic
                var epicLink = issue.CustomFields["Epic Link"]?.Values?.FirstOrDefault(); // по имени поля
                var epicIssue = !string.IsNullOrEmpty(epicLink) ? jira.Issues.GetIssueAsync(epicLink).Result : null;
                // Получаем связанную инициативу по Epic
                var linkedEpicIssueLink = epicIssue?.GetIssueLinksAsync().Result
                    .FirstOrDefault(x => x.InwardIssue.Project == "ITP" || x.OutwardIssue.Project == "ITP");
                linkedEpicIssue = linkedEpicIssueLink != null ?
                    linkedEpicIssueLink.InwardIssue.Project == "ITP" ?
                    linkedEpicIssueLink.InwardIssue :
                    linkedEpicIssueLink.OutwardIssue :
                    null;
            }
            string issueKey = issue.Project == "ITP" ? issue.Key.ToString() :
                linkedIssue != null ? linkedIssue.Key.ToString() :
                linkedEpicIssue != null ? linkedEpicIssue.Key.ToString() :
                parentIssue.Key.ToString();
            string issueSummary = issue.Project == "ITP" ? issue.Summary :
                linkedIssue != null ? linkedIssue.Summary :
                linkedEpicIssue != null ? linkedEpicIssue.Summary :
                parentIssue.Summary;
            // Получаем все worklog для задачи
            var worklogs = issue.GetWorklogsAsync().Result.ToList();
            foreach (var worklog in worklogs)
            {
                DateTime wlDate = worklog.StartDate.Value.Date;
                string week = ISOWeek.GetYear(wlDate).ToString() + "_" + ISOWeek.GetWeekOfYear(wlDate).ToString("D2");
                if ((supportList.Contains(worklog.AuthorUser.DisplayName) || string.IsNullOrEmpty(supportList[0])) &&
                    wlDate >= startDate && wlDate <= endDate)
                    lock (lockObject)
                    {
                        dt.Rows.Add($"{jiraServer}/browse/{issueKey}",
                                    parentIssue.CustomFields["Business unit"]?.Values?.FirstOrDefault(),
                                    issueType,
                                    worklog.AuthorUser.DisplayName, Math.Round(TimeSpan.FromSeconds(worklog.TimeSpentInSeconds).TotalHours, 2),
                                    wlDate,
                                    week,
                                    issueSummary.Length < 125 ? issueSummary : issueSummary[..125],
                                    issue.Project,
                                    issueKey.StartsWith("ITP") ? "да" : "нет"
                                   );
                    }
            }
        }
        private static void GetWorkLogITP()
        {
            startDate = Convert.ToDateTime(ConfigurationManager.AppSettings["WORKLOG_STARTDATE"]);
            endDate = Convert.ToDateTime(ConfigurationManager.AppSettings["WORKLOG_ENDDATE"]);
            DataTable dt = new();
            dt.Columns.Add("ITP Задача");
            dt.Columns.Add("Бизнес-юнит");
            dt.Columns.Add("Задача");
            dt.Columns.Add("Статус");
            dt.Columns.Add("Рабочее время", typeof(double));
            dt.Columns.Add("Проект");
            dt.Columns.Add("Дата");
            dt.Columns.Add("Тема");
            dt.Columns.Add("Тип цели");
            dt.Columns.Add("Эффект");
            dt.Columns.Add("Оценка Галактики");
            dt.Columns.Add("Коэффициент");
            dt.Columns.Add("Приведённая EBITDA");
            var worklogJQL = $"project = ITP AND Platform = Галактика AND (status changed during ('{startDate:yyyy-MM-dd}', '{endDate:yyyy-MM-dd}') to (Deployed) AND statusCategory = Done OR Sprint in openSprints())";
            var issues = jira.Issues.GetIssuesFromJqlAsync(new IssueSearchOptions(worklogJQL)
            {
                MaxIssuesPerRequest = 100
            }).Result.ToList();
            Logger.Info($"Всего получено {issues.Count} задач");
            // Счетчик для отслеживания прогресса
            int processedCount = 0;
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = threadCount };
            Parallel.ForEach(issues, parallelOptions, issue =>
            {
                try
                {
                    ProcessIssueITP(issue, dt);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Ошибка обработки задачи {issue.Key} для отчета ITP, задача пропущена!\r\n" + ex.Message);
                }
                // Атомарно увеличиваем счетчик
                int currentCount = Interlocked.Increment(ref processedCount);
                if (currentCount % 10 == 0)
                    Logger.Info($"Обработано {currentCount} задач из {issues.Count}");
            });
            ExportToExcel(dt);
        }
        private static void ProcessIssueITP(Issue issue, DataTable dt)
        {
            List<Issue> linkedIssueList =
            [
                issue
            ];
            // Отслеживаем уже добавленные задачи, чтобы не учесть одну и ту же задачу дважды
            var seenKeys = new HashSet<string> { issue.Key.ToString() };
            void AddUnique(IEnumerable<Issue> items)
            {
                foreach (var item in items)
                    if (seenKeys.Add(item.Key.ToString()))
                        linkedIssueList.Add(item);
            }
            //Ищем связанные с ITP задачи
            AddUnique(issue.GetIssueLinksAsync().Result
                .Where(x => !(x.InwardIssue.Project == "ITP") || !(x.OutwardIssue.Project == "ITP"))
                .Select(x => x.InwardIssue.Project == "ITP" ? x.OutwardIssue : x.InwardIssue));
            //Если подвязан эпик, то добавляем задачи из него
            foreach (var linkedIssue in linkedIssueList.Where(x => x.Type.Name == "Эпик").ToList())
            {
                string epicJQL = $"\"Epic Link\" = {linkedIssue.Key}";
                var epicIssues = jira.Issues.GetIssuesFromJqlAsync(new IssueSearchOptions(epicJQL)
                {
                    MaxIssuesPerRequest = 100
                }).Result.ToList();
                AddUnique(epicIssues);
            }
            //Если есть подзадачи, добавляем их
            foreach (var linkedIssue in linkedIssueList.ToList())
                AddUnique(linkedIssue.GetSubTasksAsync().Result);
            foreach (var linkedIssue in linkedIssueList)
            {
                var worklogs = linkedIssue.GetWorklogsAsync().Result.ToList();
                foreach (var worklog in worklogs)
                    lock (lockObject)
                    {
                        dt.Rows.Add($"{jiraServer}/browse/{issue.Key}",
                                    issue.CustomFields["Business unit"]?.Values?.FirstOrDefault(),
                                    linkedIssue.Key,
                                    issue.Status.Name,
                                    Math.Round(TimeSpan.FromSeconds(worklog.TimeSpentInSeconds).TotalHours, 2),
                                    linkedIssue.Project,
                                    worklog.StartDate.Value.Date,
                                    issue.Summary.Length < 125 ? issue.Summary : issue.Summary[..125],
                                    issue.CustomFields["Тип цели"]?.Values?.FirstOrDefault(),
                                    issue.CustomFields["Эффект для компании"]?.Values?.FirstOrDefault(),
                                    issue.CustomFields["Оценка Галактики"]?.Values?.FirstOrDefault(),
                                    issue.CustomFields["Коэффициент"]?.Values?.FirstOrDefault(),
                                    issue.CustomFields["Приведённая EBITDA"]?.Values?.FirstOrDefault()
                                   );
                    }
            }
        }
        private static void ExportToExcel(DataTable dt)
        {
            using var workbook = new XLWorkbook();
            // Add(DataTable) оформляет диапазон как Excel-таблицу: жирная шапка,
            // чередующиеся строки и автофильтр уже включены стилем таблицы.
            var ws = workbook.Worksheets.Add(dt, "Рабочее время");
            // Авторазмер колонок
            ws.Columns().AdjustToContents();
            if (!Directory.Exists("reports\\"))
                Directory.CreateDirectory("reports\\");
            string path = "reports\\" + $"worklog_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            workbook.SaveAs(path);
            // Открываем файл в приложении, ассоциированном с .xlsx (обычно Excel)
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Logger.Info($"Отчет сохранен: {path}");
        }
    }
}