using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace ArchivesTools.Synology {
    public class SessionObject {
        public string sid { get; set; }
    }
    /// <summary>
    /// Piotr Kuliński (c) 2020
    /// Prosty klient do podstawowych operacji na serwerze SYNOLOGY
    /// </summary>
    public class SynologyClient {

        HttpClient client = new HttpClient();

        public delegate void ShowProgressHandler(String message, params object[] args);
        public event ShowProgressHandler OnShowProgress;

        public string Scheme { get; set; } = "https";
        public string Host { get; set; }
        public int Port { get; set; } = 5001;
        public string Username { get; set; }
        public string Password { get; set; }
        private string BasePath { get; }
        public Uri BaseAddress { get; set; }
        public string Version = "7";

        private string sid = "";

        private void SetBaseAddress() {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            this.BaseAddress = new Uri($"{Scheme}://{Host}:{Port}/");
            client.Timeout = new TimeSpan(0, 60, 0);
        }

        public SynologyClient(string scheme, string host, int port, string username, string password) {
            this.Scheme = scheme;
            this.Host = host;
            this.Port = port;
            this.Username = username;
            this.Password = password;
            SetBaseAddress();
        }
        public SynologyClient() {
            Username = Encoding.ASCII.GetString(Convert.FromBase64String("SmFraXNfVXp5dGtvd25pa19TeW5vbG9ndQ"));
            Password = Encoding.ASCII.GetString(Convert.FromBase64String("THhOOHJOMkFGZmcyMzQ1VW9vdHJ4NDY3aTZYTkxkY090RjV2SGdpVCQ"));
            Host = Encoding.ASCII.GetString(Convert.FromBase64String("c3lub2xvZ3kucHJvZHVjZW50LnBs"));
            BasePath = Encoding.ASCII.GetString(Convert.FromBase64String("L3dlYmFwaS9hdXRoLmNnaQ=="));
            SetBaseAddress();
        }

        public async Task<string> GetRequest(Uri richiesta) {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, richiesta);
            HttpResponseMessage response = await client.SendAsync(request);

            response.EnsureSuccessStatusCode();

            var risposta = await response.Content.ReadAsByteArrayAsync();
            string json = Encoding.UTF8.GetString(risposta, 0, risposta.Length);

            return json;
        }

        public string GetParameters<T>(T Class) {
            Type ClassType = Class.GetType();
            PropertyInfo[] properties = ClassType.GetProperties();

            string result = "";

            foreach (PropertyInfo property in properties) {
                string name = property.Name;
                string value = property.GetValue(Class, null)?.ToString();

                if (value != null) {
                    result += $"&{name}={value}";
                }
            }
            return result;
        }

        public bool Login(string session = "DownloadStation,FileStation", string format = "cookie", string otp_code = null) {
            string APIList =
                $"api=SYNO.API.Auth&version={Version}" +
                "&method=login" +
                $"&account={Username}" +
                $"&passwd={Password}" +
                $"&session={session}" +
                $"&format={format}";
            if (otp_code != null)
                APIList += $"&otp_code={otp_code}";

            Uri fullPath = new UriBuilder(BaseAddress) {
                Path = BasePath,
                Query = APIList,
            }.Uri;

            try {
                string json = GetRequest(fullPath).Result;
                if (json.StartsWith("{\"error\"")) {
                    Console.Error.WriteLine(json);
                    return false;
                }

                Match m = Regex.Match(json, "\"sid\":\"(.+?)\",", RegexOptions.IgnoreCase);
                if (m.Success) {
                    sid = m.Groups[1].Value;
                    return true;
                }
            }
            catch (Exception ex) {
                Console.Error.WriteLine(ex.Message);
            }

            return false;
        }

        public bool Logout(string session = "DownloadStation") {
            string APIList = $"api=SYNO.API.Auth&version={Version}&method=logout&session={session}";

            Uri fullPath = new UriBuilder(BaseAddress) {
                Path = BasePath,
                Query = APIList,
            }.Uri;
            //Console.WriteLine(fullPath);

            string json = GetRequest(fullPath).Result;
            if (json.StartsWith("{\"error\"")) {
                Console.Error.WriteLine(json);
                return false;
            }
            return true;
        }

        public bool SharingCreate(string path) {
            //    GET /webapi/entry.cgi?api=SYNO.FileStation.Sharing&version=3&method=create& 
            //path=%22%2Ftest%2FITEMA_20445972-0.mp3%22&date_expired%222021-12-21%22 
            string APIList = $"api=SYNO.FileStation.Sharing" +
                "&version=3" +
                "&method=create" +
                $"&path=\"/Sciezka/{path}\"" +
                "&date_expired=\"2024-02-21\"";

            Uri fullPath = new UriBuilder(BaseAddress) {
                Path = BasePath,
                Query = APIList,
            }.Uri;
            //Console.WriteLine(fullPath);

            string json = GetRequest(fullPath).Result;
            if (json.StartsWith("{\"error\"")) {
                Console.Error.WriteLine(json);
                return false;
            }
            return true;
        }

        public async Task<bool> DownloadFileAsync(string remoteFilePath, string localFilePath) {
            bool result = false;
            Uri cmd = new UriBuilder(BaseAddress) {
                Path = BasePath,
                Query = $"api=SYNO.FileStation.Download&version=2&method=download&path={Uri.EscapeDataString(remoteFilePath)}&_sid={sid}",
            }.Uri;
            //Console.WriteLine(cmd.ToString());

            HttpResponseMessage response = await client.GetAsync(cmd.ToString(), HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode) {
                OnShowProgress?.Invoke("Problem z pobieraniem danych", string.Format("HTTP status code {0}", response.StatusCode));
                throw new Exception(string.Format("The request returned with HTTP status code {0}", response.StatusCode));
            }

            string fnDownload = Path.GetFileName(remoteFilePath);
            var total = response.Content.Headers.ContentLength.HasValue ? response.Content.Headers.ContentLength.Value : -1L;
            var canReportProgress = total != -1;
            StreamWriter writer = new StreamWriter(localFilePath, false);
            using (var stream = await response.Content.ReadAsStreamAsync()) {
                var totalRead = 0L;
                var buffer = new byte[8192];
                var isMoreToRead = true;
                try {
                    do {
                        //token.ThrowIfCancellationRequested();

                        var read = await stream.ReadAsync(buffer, 0, buffer.Length); //, token);

                        if (read == 0) {
                            isMoreToRead = false;
                        }
                        else {
                            var data = new byte[read];
                            for (int i = 0; i < read; i++) {
                                data[i] = buffer[i];
                            }
                            writer.BaseStream.Write(data, 0, read);

                            totalRead += read;

                            if (OnShowProgress != null) {
                                double prc = (totalRead * 1d) / (total * 1d) * 100;
                                OnShowProgress?.Invoke(String.Format("{0}", fnDownload), String.Format(System.Globalization.CultureInfo.GetCultureInfo("en-GB"), "\r{0,6:0.00} %", prc));
                            }
                        }
                    } while (isMoreToRead);
                    result = true;
                } catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                }
            }
            writer.Close();
            return result;
        }

        public async Task<bool> UploadFile(string filepath, string toFolder = "") {

            StringContent NewHeader(string header) {
                StringContent sc = new StringContent(header);
                sc.Headers.ContentType = null;
                return sc;
            }
            void AddHeader(MultipartFormDataContent cont, string value, string method) {
                StringContent sc = new StringContent(method);
                sc.Headers.ContentType = null;
                cont.Add(sc, $"\"{value}\"");
            }

            int FS_maxversion = 2;
            Uri cmd = new UriBuilder(BaseAddress) {
                Path = BasePath,
                Query = $"api=SYNO.FileStation.Upload&_sid={sid}",
            }.Uri;

            var content = new MultipartFormDataContent("AaB03x");
            content.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data"); // "multipart/form-data"); 
            content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", "AaB03x"));

            AddHeader(content, "api", "SYNO.FileStation.Upload");
            AddHeader(content, "version", $"{FS_maxversion}");
            AddHeader(content, "method", "upload");
            AddHeader(content, "path", $"/LSI_apps_logs/{toFolder}");
            AddHeader(content, "create_parents", "true");
            AddHeader(content, "overwrite", "true");

            var bytes = File.ReadAllBytes(filepath);
            var fileContent = new ByteArrayContent(bytes); //
            fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") {
                Name = "\"filename\"",
                FileName = $"\"{filepath}\"",
                Size = bytes.Length
            };
            //Console.WriteLine($"plik: {filepath}, {bytes.Length}");
            content.Add(fileContent);

            //Console.WriteLine(cmd);

            var message = client.PostAsync(cmd.ToString(), content).Result;
            if (message.IsSuccessStatusCode) {
                var risposta = await message.Content.ReadAsByteArrayAsync();
                string json = Encoding.UTF8.GetString(risposta, 0, risposta.Length);
                if (!json.StartsWith("{\"error\"")) {
                    return true;
                }
                Console.WriteLine(json);
            }
            return false;
        }
    }
}
