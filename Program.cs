using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.DirectoryServices;
using System.Configuration;
using System.Collections.Generic;
using Atlassian.Jira;
using NLog;
using Newtonsoft.Json;

namespace SDP2Jira
{
    class Program
    {
        private static Jira jira;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
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
        private static bool IsLoginExists(string mail, out string login)
        {
            login = "";
            DirectorySearcher ds = new DirectorySearcher()
            {
                Filter = $"(mail={mail})",
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
        static void Main(/*string[] args*/)
        {
            Logger.Info("Запуск программы.");
            try
            {
                jira = Jira.CreateRestClient(ConfigurationManager.AppSettings["JIRA_SERVER"],
                                             ConfigurationManager.AppSettings["JIRA_LOGIN"],
                                             ConfigurationManager.AppSettings["JIRA_PASS"]);
                Logger.Info($"Подключились к Jira: {ConfigurationManager.AppSettings["JIRA_SERVER"]}");
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка подключения к Jira!\n" + ex.Message);
                return;
            }
            List<string> supportList;
            try
            {
                string[] supportListArray = ConfigurationManager.AppSettings["SUPPORT_LIST"].Split(';');
                supportList = new List<string>(supportListArray);
                Logger.Info("Загружен список специалистов.");
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка загрузки списка специалистов!\n" + ex.Message);
                return;
            }
            foreach (string supportName in supportList)
            {
                try
                {
                    SDP.GetRequestList(supportName);
                    Logger.Info($"По специалисту {supportName} загружено {SDP.requestList.requests.Count} заявок.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка получения списка заявок из SDP по специалисту {supportName}!\n" + ex.Message);
                }
                foreach (var req in SDP.requestList.requests)
                {
                    SDP.Request request = new SDP.Request();
                    try
                    {
                        request = SDP.GetRequest(req.Id);
                        //Logger.Info("Получены данные по заявке:\n" + JsonConvert.SerializeObject(req, Formatting.Indented));
                        Logger.Info($"Получены данные по заявке {request.Id}.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Ошибка получения заявки {req.Id} из SDP!\n" + ex.Message);
                    }
                    if (IsLoginExists(request.Requester.Email_id, out string login))
                    {
                        Logger.Info($"Автор: {login}");
                        request.AuthorLogin = login;
                    }
                    else
                    {
                        Logger.Error("Не удалось определить логин автора!");
                        continue;
                    }
                    if (IsLoginExists(request.Technician.Email_id, out login))
                    {
                        //Logger.Info($"Специалист: {login}");
                        request.SpecialistLogin = login;
                    }
                    else
                    {
                        Logger.Error("Не удалось определить логин специалиста!");
                        continue;
                    }
                    //if (request.Id == "278617")
                    {
                        if (jira.Issues.Queryable.Where(x => x["Номер заявки SD"] == new LiteralMatch(request.Id)).Count() > 0)
                            Logger.Info($"В Jira уже есть задача {jira.Issues.Queryable.Where(x => x["Номер заявки SD"] == new LiteralMatch(request.Id)).FirstOrDefault().Key} по заявке {request.Id}!");
                        else
                        {
                            var issue = jira.CreateIssue("ERP");
                            issue.Reporter = request.AuthorLogin;
                            issue.Assignee = request.SpecialistLogin;
                            issue.Description = HtmlToPlainText(request.Description);
                            issue["Номер заявки SD"] = request.Id;
                            issue.Type = "10301"; //Task
                            issue.Summary = $"{request.Id} {request.Subject}";
                            issue["Направление"] = request.Udf_fields.Udf_pick_3901;
                            issue["Категория"] = request.Subcategory.Name;
                            try
                            {
                                issue.SaveChanges();
                                Logger.Info($"Задача {issue.Key} создана по заявке {request.Id}.");
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Ошибка создания задачи в Jira по заявке {request.Id}!\n" + ex.Message);
                            }
                        }
                    }
                }
            }
            Logger.Info("Завершение работы программы.");
        }
    }
}
