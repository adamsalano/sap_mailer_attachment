using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web.Script.Serialization;

namespace AutoMail
{
    internal sealed class ServiceLayerException : Exception
    {
        public ServiceLayerException(string message, int statusCode = 0) : base(message)
        {
            StatusCode = statusCode;
        }

        /// <summary>HTTP status returned by Service Layer; 0 when not from an HTTP response.</summary>
        public int StatusCode { get; }
    }

    /// <summary>
    /// Minimal SAP B1 Service Layer client used for every write: attachments, UDF flags and UDF creation.
    /// Re-logs in once and retries when the session has expired.
    /// </summary>
    internal sealed class ServiceLayerClient : IDisposable
    {
        private static readonly HttpMethod Patch = new HttpMethod("PATCH");
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private readonly CompanyCredential _credential;
        private readonly Uri _baseUri;
        private readonly HttpClient _http;
        private readonly CookieContainer _cookies = new CookieContainer();
        private bool _loggedIn;

        public ServiceLayerClient(CompanyCredential credential)
        {
            _credential = credential;
            _baseUri = new Uri(credential.ResolvedServiceLayerUrl);

            var handler = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true };
            if (Settings.TrustServiceLayerCertificate)
                handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true;

            _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
            // .NET Framework adds "Expect: 100-continue" to POSTs; Service Layer then answers the first Login on a
            // new connection with "Invalid login credential" even when the credentials are correct.
            _http.DefaultRequestHeaders.ExpectContinue = false;
        }

        public void Login()
        {
            var body = new { CompanyDB = _credential.Database, UserName = _credential.SapUser, Password = _credential.SapPassword };
            // Many workers logging in at once occasionally get "500 SAML Login Failed" from the SLD authentication
            // service; that is transient, so retry server errors. Wrong credentials (400) still fail at once.
            int[] delaysMs = { 2000, 5000 };
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Send(HttpMethod.Post, "Login", () => JsonContent(body), retryOnUnauthorized: false);
                    break;
                }
                catch (ServiceLayerException ex) when (ex.StatusCode >= 500 && attempt < delaysMs.Length)
                {
                    Log.Warn(_credential.Database, $"Service Layer login attempt {attempt + 1} failed ({ex.Message}); retrying in {delaysMs[attempt] / 1000}s");
                    System.Threading.Thread.Sleep(delaysMs[attempt]);
                }
            }
            _loggedIn = true;
        }

        public void Logout()
        {
            if (!_loggedIn) return;
            try { Send(HttpMethod.Post, "Logout", null, retryOnUnauthorized: false); }
            catch (Exception ex) { Log.Warn(_credential.Database, "Service Layer logout failed: " + ex.Message); }
            _loggedIn = false;
        }

        public Dictionary<string, object> Get(string path)
        {
            return (Dictionary<string, object>)Json.DeserializeObject(Send(HttpMethod.Get, path, null));
        }

        public Dictionary<string, object> Post(string path, object body)
        {
            string text = Send(HttpMethod.Post, path, () => JsonContent(body));
            return string.IsNullOrWhiteSpace(text) ? null : (Dictionary<string, object>)Json.DeserializeObject(text);
        }

        public void Update(string path, object body)
        {
            Send(Patch, path, () => JsonContent(body));
        }

        /// <summary>
        /// Uploads a file. Appends a line to <paramref name="attachmentEntry"/> when it is &gt; 0,
        /// otherwise creates a new attachment entry. Returns the attachment entry.
        /// </summary>
        public int UploadAttachment(int attachmentEntry, string fileName, byte[] content, string contentType)
        {
            Func<HttpContent> body = () => MultipartFile(fileName, content, contentType);
            if (attachmentEntry > 0)
            {
                Send(Patch, $"Attachments2({attachmentEntry})", body);
                return attachmentEntry;
            }
            var created = (Dictionary<string, object>)Json.DeserializeObject(Send(HttpMethod.Post, "Attachments2", body));
            return Convert.ToInt32(created["AbsoluteEntry"]);
        }

        private string Send(HttpMethod method, string path, Func<HttpContent> content, bool retryOnUnauthorized = true)
        {
            using (var request = new HttpRequestMessage(method, new Uri(_baseUri, path)))
            {
                if (content != null) request.Content = content();
                using (HttpResponseMessage response = _http.SendAsync(request).GetAwaiter().GetResult())
                {
                    StoreCookies(response);
                    string text = response.Content == null ? "" : response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (response.StatusCode == HttpStatusCode.Unauthorized && retryOnUnauthorized)
                    {
                        Login();
                        return Send(method, path, content, retryOnUnauthorized: false);
                    }
                    if (!response.IsSuccessStatusCode)
                        throw new ServiceLayerException($"{method} {path} failed ({(int)response.StatusCode}): {ErrorMessage(text)}",
                                                        (int)response.StatusCode);
                    return text;
                }
            }
        }

        /// <summary>
        /// Service Layer sends "B1SESSION=...;HttpOnly;;Secure;SameSite=None"; .NET Framework drops that cookie because of
        /// the empty attribute, so every call after Login would be unauthenticated. Store name=value ourselves.
        /// </summary>
        private void StoreCookies(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> headers)) return;
            var site = new Uri(_baseUri.GetLeftPart(UriPartial.Authority));
            foreach (string header in headers)
            {
                int eq = header.IndexOf('=');
                if (eq <= 0) continue;
                int semi = header.IndexOf(';', eq);
                string value = semi < 0 ? header.Substring(eq + 1) : header.Substring(eq + 1, semi - eq - 1);
                _cookies.Add(site, new Cookie(header.Substring(0, eq).Trim(), value.Trim(), "/"));
            }
        }

        private static string ErrorMessage(string body)
        {
            try
            {
                var root = (Dictionary<string, object>)Json.DeserializeObject(body);
                var error = (Dictionary<string, object>)root["error"];
                object message = error["message"];
                string text = message is Dictionary<string, object> m ? Convert.ToString(m["value"]) : Convert.ToString(message);
                return error.TryGetValue("code", out object code) ? $"{text} [SL code {code}]" : text;
            }
            catch
            {
                return body;
            }
        }

        private static HttpContent JsonContent(object body)
        {
            return new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
        }

        private static HttpContent MultipartFile(string fileName, byte[] content, string contentType)
        {
            // Service Layer rejects the quoted boundary and the filename* parameter that
            // MultipartFormDataContent emits by default, so both headers are written by hand.
            string boundary = "AutoMail" + Guid.NewGuid().ToString("N");
            var multipart = new MultipartFormDataContent(boundary);
            multipart.Headers.Remove("Content-Type");
            multipart.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=" + boundary);

            // Header order matters: Service Layer answers "Bad Content Disposition" unless Content-Disposition comes first.
            var file = new ByteArrayContent(content);
            file.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"files\"; filename=\"{fileName}\"");
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            multipart.Add(file);
            return multipart;
        }

        public void Dispose()
        {
            Logout();
            _http.Dispose();
        }
    }
}
