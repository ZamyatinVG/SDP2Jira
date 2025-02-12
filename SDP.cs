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
                public string Udf_pick_901 { get; set; }
            }
            public class Attachment
            {
                public string Content_url { get; set; }
                public int Type { get; set; }
                public string Name { get; set; }
            }
            public string Id { get; set; }
            public Sub Requester { get; set; }
            public Sub Technician { get; set; }
            public string Subject { get; set; }
            public string Description { get; set; }
            public Sub Udf_fields { get; set; }

            public List<Attachment> Attachments = new();
            public string AuthorLogin { get; set; }
            public string SpecialistLogin { get; set; }
            public bool Has_notes { get; set; }
            public void ParseDescription(string description, int type)
            {
                //парсим визуальные вложения в описании заявки
                while (description.Length > 0)
                {
                    int index = description.IndexOf("img src=\"");
                    if (index < 0)
                        description = "";
                    else
                    {
                        Attachment attachment = new();
                        description = description.Substring(index + 9);
                        index = description.IndexOf("\"");
                        attachment.Content_url = description.Substring(0, index);
                        index = attachment.Content_url.LastIndexOf("/");
                        attachment.Name = attachment.Content_url.Substring(index + 1);
                        attachment.Type = type;
                        if (attachment.Name != string.Empty)
                            Attachments.Add(attachment);
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

            public List<Msg> Messages = new();
        }
        public class RequestList
        {
            public List<Request> requests = new();
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
            public Request.Sub Added_by { get; set; }
            public string AuthorLogin { get; set; }
        }
        public class NoteList
        {
            public List<Note> notes = new();
        }
        public class NoteMessage
        {
            public Note note = new();
        }
        public static RequestList requestList;
        public static NoteList noteList;
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
        public static string DowloadFile(string url, string filename)
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
            if (Path.GetExtension(filename) == string.Empty)
                filename += GetFileExtension(response);
            File.WriteAllBytes("files\\" + filename, response);
            return filename;
        }
        public static string GetFileExtension(byte[] fileBytes)
        {
            if (fileBytes.Length < 4)
                return string.Empty;
            // PNG
            if (fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47)
                return ".png";
            // JPEG
            if (fileBytes[0] == 0xFF && fileBytes[1] == 0xD8 && fileBytes[2] == 0xFF)
                return ".jpg";
            // Если формат не распознан
            return string.Empty; 
        }
        public static string CloseRequest(string request_id, out string status_code)
        {
            var client = new RestClient(@$"{ConfigurationManager.AppSettings["SDP_SERVER"]}/api/v3/requests/{request_id}?format=json&input_data=
                                        {{
                                            ""request"": 
                                            {{
                                                ""status"": 
                                                {{
                                                    ""id"": ""303""
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
                return $"Request {request_id} succefully closed.";
            else
                return $"Request {request_id} not closed. Check required fields and subtasks.";
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
            return noteMessage.note;
        }
        
    }
}