using System.Collections.Generic;
using System.Configuration;
using System.IO;
using RestSharp;
using Newtonsoft.Json;

namespace SDP2Jira
{
    public class SDP
    {
        public class Request
        {
            public class Sub
            {
                public string Id { get; set; }
                public string Name { get; set; }
                public string Email_id { get; set; }
                public string Udf_pick_3901 { get; set; }
            }
            public class Attachment
            {
                public string File_name { get; set; }
                public string Content_url { get; set; }
                public int Type { get; set; }
            }
            public string Id { get; set; }
            public Sub Requester { get; set; }
            public Sub Technician { get; set; }
            public string Subject { get; set; }
            public string Description { get; set; }
            public Sub Subcategory { get; set; }
            public Sub Udf_fields { get; set; }
            public Sub Status { get; set; }

            public List<Attachment> Attachments = new List<Attachment>();
            public string AuthorLogin { get; set; }
            public string SpecialistLogin { get; set; }
            public bool Has_notes { get; set; }
            public void ParseDescription(string description, int type)
            {
                //парсим визуальные вложения в описании заявки
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
                        attachment.Type = type;
                        this.Attachments.Add(attachment);
                    }
                }
            }
        }
        public class Response_status
        {
            public class Msg
            {
                public string Status_code { get; set; }
                public string Message { get; set; }
            }
            public string Status_code { get; set; }
            public string Status { get; set; }

            public List<Msg> Messages = new List<Msg>();
        }
        public class RequestList
        {
            public List<Request> requests = new List<Request>();
        }
        public class RequestMessage
        {
            public Request Request { get; set; }
            public Response_status Response_status { get; set; }
        }

        public class Note
        {
            public string Id { get; set; }
            public string Description { get; set; }
            public Request.Sub Created_by { get; set; }
            public string AuthorLogin { get; set; }
        }
        public class NoteList
        {
            public List<Note> notes = new List<Note>();
        }
        public class NoteMessage
        {
            public Note request_note = new Note();
        }

        public static RequestList requestList;
        public static NoteList noteList;
        public static void GetRequestList(string technicianName)
        {
            var client = new RestClient(@$"{ConfigurationManager.AppSettings["SDP_SERVER"]}/api/v3/requests?format=json&input_data= 
                                        {{
                                            ""list_info"": 
                                            {{
                                                ""row_count"": 50,
                                                ""start_index"": 1,
                                                ""sort_field"": ""id"",
                                                ""sort_order"": ""desc"",
                                                ""get_total_count"": true,
                                                ""search_fields"": 
                                                {{
                                                    ""technician.name"": ""{technicianName}"",
			                                        ""status.name"": ""Открыт""
                                                }}
                                            }}
                                        }}")
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.GET);
            request.AddHeader("TECHNICIAN_KEY", ConfigurationManager.AppSettings["TECHNICIAN_KEY"]);
            request.AlwaysMultipartFormData = true;
            IRestResponse response = client.Execute(request);
            requestList = JsonConvert.DeserializeObject<RequestList>(response.Content);
        }
        public static Request GetRequest(string request_id)
        {
            var client = new RestClient($"{ConfigurationManager.AppSettings["SDP_SERVER"]}/api/v3/requests/{request_id}")
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.GET);
            request.AddHeader("TECHNICIAN_KEY", ConfigurationManager.AppSettings["TECHNICIAN_KEY"]);
            IRestResponse response = client.Execute(request);
            var requestMessage = JsonConvert.DeserializeObject<RequestMessage>(response.Content);
            return requestMessage.Request;
        }
        public static void DowloadFile(string url, string filename)
        {
            var client = new RestClient(ConfigurationManager.AppSettings["SDP_SERVER"] + url)
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.GET);
            request.AddHeader("TECHNICIAN_KEY", ConfigurationManager.AppSettings["TECHNICIAN_KEY"]);
            byte[] response = client.DownloadData(request);
            if (!Directory.Exists("files\\"))
                Directory.CreateDirectory("files\\");
            File.WriteAllBytes("files\\" + filename, response);
        }
        public static string CloseRequest(string request_id, out string status_code)
        {
            var client = new RestClient(@$"{ConfigurationManager.AppSettings["SDP_SERVER"]}/api/v3/requests/{request_id}?format=json&input_data=
                                        {{
                                            ""request"": 
                                            {{
                                                ""status"": 
                                                {{
                                                    ""id"": ""2101""
                                                }}
                                            }}
                                        }}")
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.PUT);
            request.AddHeader("TECHNICIAN_KEY", ConfigurationManager.AppSettings["TECHNICIAN_KEY"]);
            IRestResponse response = client.Execute(request);
            var requestMessage = JsonConvert.DeserializeObject<RequestMessage>(response.Content);
            status_code = requestMessage.Response_status.Status_code;
            if (status_code == "2000")
                return $"Заявка {request_id} успешно переведена в статус \"Передано в Jira\"";
            else
                return $"Заявка {request_id} не закрыта. Причина:\n" + requestMessage.Response_status.Messages[0].Message;
        }
        public static string GetRequestUrl(string request_id)
        {
            return $"{ConfigurationManager.AppSettings["SDP_SERVER"]}/WorkOrder.do?woMode=viewWO&woID={request_id}";
        }
        public static void GetNotes(string request_id)
        {
            var client = new RestClient($"{ConfigurationManager.AppSettings["SDP_SERVER"]}/api/v3/requests/{request_id}/notes")
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.GET);
            request.AddHeader("TECHNICIAN_KEY", ConfigurationManager.AppSettings["TECHNICIAN_KEY"]);
            IRestResponse response = client.Execute(request);
            noteList = JsonConvert.DeserializeObject<NoteList>(response.Content);
        }
        public static Note GetNote(string request_id, string note_id)
        {
            var client = new RestClient($"{ConfigurationManager.AppSettings["SDP_SERVER"]}/api/v3/requests/{request_id}/notes/{note_id}")
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.GET);
            request.AddHeader("TECHNICIAN_KEY", ConfigurationManager.AppSettings["TECHNICIAN_KEY"]);
            IRestResponse response = client.Execute(request);
            var noteMessage = JsonConvert.DeserializeObject<NoteMessage>(response.Content);
            return noteMessage.request_note;
        }
        
    }
}