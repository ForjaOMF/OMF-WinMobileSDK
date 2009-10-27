using System;
using System.IO;
using System.Net;
using System.Text;

namespace SmsSendApi
{
    /// <summary>
    /// Utility class for SMS sendind using movistar web service
    /// </summary>
    class SMSSender
    {
        private string _lastError = string.Empty;

        /// <summary>
        /// Create a new instance
        /// </summary>
        public SMSSender(){}
        
        /// <summary>
        /// </summary>
        /// <returns>returns the last error description</returns>
        public string GetLastError() { return _lastError; }

        /// <summary>
        /// Sends the SMS
        /// </summary>
        /// <param name="login">User login</param>
        /// <param name="pwd">User Password</param>
        /// <param name="dest">Destination Phone number</param>
        /// <param name="msg">Text message (max 160 characters)</param>
        /// <returns>Server response</returns>
        public string SendMessage(string login, string pwd, string dest, string msg)
        {
            try
            {
                string loginData = string.Format(
                        "TM_ACTION=AUTHENTICATE&TM_LOGIN={0}&TM_PASSWORD={1}&to={2}&message={3}",
                        login,
                        pwd,
                        dest,
                        msg
                        );

                HttpWebResponse response = HttpHelper.ExecuteRequest(
                    "https://opensms.movistar.es/aplicacionpost/loginEnvio.jsp",
                    "application/x-www-form-urlencoded",
                    "POST",
                    loginData,
                    null,
                    false
                    );

                if (response == null)
                {
                    _lastError = "Unable to connect web service";
                    return null;
                }

                string responseBody = string.Empty;
                if (response.ContentLength > 0)
                {
                    responseBody = HttpHelper.ReadBody(response,System.Text.Encoding.Default);
                    while (responseBody.StartsWith("\r") || responseBody.StartsWith("\n"))
                        responseBody = responseBody.Substring(1);
                    while (responseBody.EndsWith("\r") || responseBody.EndsWith("\n"))
                        responseBody = responseBody.Substring(0, responseBody.Length - 1);
                }

                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        
                        return responseBody;
                        
                    default:
                        _lastError = "Server Error: " + response.StatusCode.ToString() + " " + response.StatusDescription;
                        return null;
                }
            }
            catch (Exception ex)
            {
                _lastError = "Internal Error: " + ex.Message;
                return null;
            }

        }
        /// <summary>
        /// Utility class for simplify http parsing
        /// </summary>
        private static class HttpHelper
        {
            /// <summary>
            /// Performs a HTTP request
            /// </summary>
            /// <param name="url">Target URL</param>
            /// <param name="contentType">Content-Type Header</param>
            /// <param name="method">HTTP Method (POST/GET)</param>
            /// <param name="body">string data to send when method is POST</param>
            /// <param name="optionalHeaders">Array of optional headers needed by the request</param>
            /// <param name="autoRedirect">Allows automatically performig autoredirect when the server invokes it</param>
            /// <returns>HttpResponse</returns>
            public static HttpWebResponse ExecuteRequest(string url, string contentType, string method, string body, Header[] optionalHeaders, bool autoRedirect)
            {

                ServicePointManager.CertificatePolicy = new CertificateMovistar();
                HttpWebRequest request = null;

                try
                {

                    request = (HttpWebRequest)WebRequest.Create(url);
                    request.Accept = "image/gif, image/x-xbitmap, image/jpeg, image/pjpeg, application/x-shockwave-flash, application/vnd.ms-excel, application/vnd.ms-powerpoint, application/msword, */*";
                    request.Headers.Add("Accept-Encoding", "gzip, deflate");
                    request.UserAgent = "Mozilla/4.0 (compatible; MSIE 7.0; Windows NT 5.1; .NET CLR 1.1.4322; .NET CLR 2.0.50727)";

                    request.Method = method;
                    request.AllowAutoRedirect = autoRedirect;
                    if (contentType != null && contentType.Length > 0)
                        request.ContentType = contentType;
                    if (body != null)
                        request.ContentLength = (long)body.Length;

                    if (optionalHeaders != null)
                    {
                        foreach (Header header in optionalHeaders)
                            request.Headers.Add(header.Name, header.Value);

                    }

                    if (body != null && body.Length > 0)
                    {
                        Stream stream = request.GetRequestStream();
                        byte[] buffer = System.Text.Encoding.ASCII.GetBytes(body);
                        stream.Write(buffer, 0, buffer.Length);
                        stream.Flush();
                        stream.Close();
                    }
                }
                catch (Exception ex)
                {
                    //Shared.WriteLog("REQ ERR: " + ex.ToString());
                    return null;
                }
                try
                {
                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                    return response;
                }
                catch (WebException webEx)
                {
                    //Shared.WriteLog("HTTP ERR:" + webEx.ToString());
                    return (HttpWebResponse)webEx.Response;
                }
                catch (Exception ex)
                {
                    //Shared.WriteLog("RES ERR:" + ex.ToString());
                    return null;
                }

            }
            public static string ParseCookie(string cookie)
            {
                return "s=" + HttpHelper.ExtractValue(cookie, "s=", ";") + ";skf=" + HttpHelper.ExtractValue(cookie, "skf=", ";");
            }
            /// <summary>
            /// Extract text value between the two provided strings
            /// </summary>
            /// <param name="data">source string</param>
            /// <param name="from">Start string chunk</param>
            /// <param name="to">End string chunk</param>
            /// <returns>If found, return the string. Otherwise return null</returns>
            public static string ExtractValue(string data, string from, string to)
            {
                int i1 = data.IndexOf(from);
                if (i1 > -1)
                {
                    int i2 = data.IndexOf(to, from.Length + i1);
                    if (i2 > -1)
                    {
                        return data.Substring(i1 + from.Length, i2 - i1 - from.Length);
                    }
                }
                return null;
            }
            /// <summary>
            /// Read received body
            /// </summary>
            /// <param name="response">Http response received from the server</param>
            /// <returns>Response body as byte array if available. Otherwise returns null</returns>
            public static byte[] ReadBody(HttpWebResponse response)
            {
                try
                {
                    long lenght = response.ContentLength;
                    MemoryStream ms = new MemoryStream();
                    Stream resStream = response.GetResponseStream();
                    byte[] readBuffer = new byte[8192];
                    while (lenght == -1 || ms.Length < lenght)
                    {
                        int i = resStream.Read(readBuffer, 0, readBuffer.Length);
                        if (i > 0)
                            ms.Write(readBuffer, 0, i);
                        else
                            break;
                    }
                    return ms.ToArray();
                }
                catch (Exception ex)
                {
                    //Shared.WriteLog("ERR BODY:" + ex.ToString());
                    return null;
                }

            }
            /// <summary>
            /// Read received body
            /// </summary>
            /// <param name="response">Http response received from the server</param>
            /// <param name="encoding">Content-Encoding used to decode the buffer</param>
            /// <returns>Response body as string if available. Otherwise returns null</returns>
            public static string ReadBody(HttpWebResponse response, System.Text.Encoding encoding)
            {
                byte[] data = ReadBody(response);
                return encoding.GetString(data, 0, data.Length);

            }
            public static void Save2disk(string filename, byte[] data)
            {
                FileStream fs = new FileStream(filename, FileMode.Create, FileAccess.Write);
                fs.Write(data, 0, data.Length);
                fs.Close();
                //fs.Dispose();
            }
            /// <summary>
            /// Helper class to store named-value pairs
            /// </summary>
            public class Header
            {
                public string Name;
                public string Value;

                public Header(string name, string value)
                {
                    this.Name = name;
                    this.Value = value;
                }
            }
            /// <summary>
            /// Helper class neccesary for dealing width Movistar SSL certificate (out of date, not signed ..)
            /// </summary>
            private class CertificateMovistar : ICertificatePolicy
            {
                public bool CheckValidationResult(ServicePoint srvPoint, System.Security.Cryptography.X509Certificates.X509Certificate certificate, WebRequest request, int certificateProblem)
                {
                    //Shared.WriteLog("Certificate Acepted");
                    return true;
                }
            }

        }

    }
 
}
