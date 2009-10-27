using System;
using System.IO;
using System.Net;
using System.Text;

namespace LocalizameApi
{
    /// <summary>
    /// Class that implements the Localizame service via web. 
    /// </summary>
    class Localizame
    {
        private string _lastError = string.Empty;
        private string _cookie = null;

        /// <summary>
        /// Create a new instance
        /// </summary>
        public Localizame(){}

        /// <summary>
        /// </summary>
        /// <returns>returns the last error description</returns>
        public string GetLastError() { return _lastError; }
        
        /// <summary>
        /// Performs login to the server
        /// </summary>
        /// <param name="login">String with user's telephone number</param>
        /// <param name="pwd">String with user's password</param>
        /// <returns></returns>
        public bool Login(string login, string pwd)
        {
            try
            {
                string loginData = string.Format(
                        "usuario={0}&clave={1}&submit.x=36&submit.y=6",
                        login,
                        pwd
                        );
                
                
                // Login
                HttpWebResponse response = HttpHelper.ExecuteRequest(
                    "http://www.localizame.movistar.es/login.do",
                    "application/x-www-form-urlencoded",
                    "POST",
                    loginData,
                    null,
                    false
                    );

                if (response == null)
                {
                    _lastError = "Unable to connect web service";
                    return false;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _lastError = ((int)response.StatusCode).ToString() + " " + response.StatusDescription;
                    return false;
                }

                string responseData = HttpHelper.ReadBody(response, System.Text.Encoding.Default);
                if (responseData.IndexOf("Acceso Restringido") > -1)
                {
                    _lastError = "Restricted Access";
                    return false;
                }

                /*
                FileStream fs = new FileStream("c:\\aa.txt", FileMode.Create, FileAccess.Write);
                fs.Write(responseData, 0, responseData.Length);
                fs.Close();
                */

                _cookie = response.Headers["Set-Cookie"];
                if (_cookie == null)
                {
                    _lastError = "Cookie not found!";
                    return false;
                }
                
                HttpHelper.Header[] headers = new HttpHelper.Header[]{new HttpHelper.Header("Referer","http://www.localizame.movistar.es/login.do"),new HttpHelper.Header("Cookie",_cookie)};

                // NewUser page access
                response = HttpHelper.ExecuteRequest(
                    "http://www.localizame.movistar.es/nuevousuario.do",
                    "",
                    "GET",
                    null,
                    headers,
                    false
                    );

                if (response == null)
                {
                    _lastError = "Unable to connect web service";
                    return false;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _lastError = ((int)response.StatusCode).ToString() + " " + response.StatusDescription;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _lastError = "Internal Error: " + ex.Message;
                return false;
            }

        }

        /// <summary>
        /// Performs location search of a user
        /// </summary>
        /// <param name="number">String with user's telephone number to locate</param>
        /// <returns>Location text</returns>
        public string Locate(string number)
        {
            try
            {
                string loginData = string.Format(
                        "telefono={0}",
                        number
                        );

                // Search
                HttpWebResponse response = HttpHelper.ExecuteRequest(
                    "http://www.localizame.movistar.es/buscar.do",
                    "application/x-www-form-urlencoded",
                    "POST",
                    loginData,
                    new HttpHelper.Header[] { new HttpHelper.Header("cookie",_cookie)},
                    false
                    );

                if (response == null)
                {
                    _lastError = "Unable to connect web service";
                    return null;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _lastError = ((int)response.StatusCode).ToString() + " " + response.StatusDescription;
                    return null;
                }

                string responseData = HttpHelper.ReadBody(response, System.Text.Encoding.Default);
                if (responseData.IndexOf("Acceso Restringido") > -1)
                {
                    _lastError = "Restricted Access";
                    return null;
                }

                string ret = "";

                int iStart = responseData.IndexOf(number);
                if (iStart > 0 - 1)
                {
                    int iEnd = responseData.IndexOf("metros", iStart);
                    if (iEnd > -1)
                        ret = responseData.Substring(iStart, iEnd - iStart + 6);
                }
                
                

                return ret;
            }
            catch (Exception ex)
            {
                _lastError = "Internal Error: " + ex.Message;
                return null;
            }

        }

        /// <summary>
        /// Authorizes another user to locate us
        /// </summary>
        /// <param name="number">String with user's telephone number</param>
        /// <returns></returns>
        public bool Authorize(string number)
        {
            try
            {
                
                HttpWebResponse response = HttpHelper.ExecuteRequest(
                    "http://www.localizame.movistar.es/insertalocalizador.do?telefono=" + number + "&submit.x=40&submit.y=5",
                    "application/x-www-form-urlencoded",
                    "GET",
                    null,
                    new HttpHelper.Header[] { new HttpHelper.Header("cookie", _cookie) , new HttpHelper.Header("Referer","http://www.localizame.movistar.es/buscalocalizadorespermisos.do")},
                    false
                    );

                if (response == null)
                {
                    _lastError = "Unable to connect web service";
                    return false;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _lastError = ((int)response.StatusCode).ToString() + " " + response.StatusDescription;
                    return false;
                }

                string responseData = HttpHelper.ReadBody(response, System.Text.Encoding.Default);
                if (responseData.IndexOf("Acceso Restringido") > -1)
                {
                    _lastError = "Restricted Access";
                    return false;
                }


                return true;
            }
            catch (Exception ex)
            {
                _lastError = "Internal Error: " + ex.Message;
                return false;
            }

        }

        /// <summary>
        /// Unauthorizes another user to locate us
        /// </summary>
        /// <param name="number">String with user's telephone number</param>
        /// <returns></returns>
        public bool Unauthorize(string number)
        {
            try
            {
                
                HttpWebResponse response = HttpHelper.ExecuteRequest(
                    "http://www.localizame.movistar.es/borralocalizador.do?telefono=" + number + "&submit.x=44&submit.y=8",
                    "application/x-www-form-urlencoded",
                    "GET",
                    null,
                    new HttpHelper.Header[] { new HttpHelper.Header("cookie", _cookie), new HttpHelper.Header("Referer", "http://www.localizame.movistar.es/buscalocalizadorespermisos.do") },
                    false
                    );

                if (response == null)
                {
                    _lastError = "Unable to connect web service";
                    return false;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _lastError = ((int)response.StatusCode).ToString() + " " + response.StatusDescription;
                    return false;
                }

                string responseData = HttpHelper.ReadBody(response, System.Text.Encoding.Default);
                if (responseData.IndexOf("Acceso Restringido") > -1)
                {
                    _lastError = "Restricted Access";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _lastError = "Internal Error: " + ex.Message;
                return false;
            }

        }
        /// <summary>
        /// Logs out from server
        /// </summary>
        /// <returns></returns>
        public bool Logout()
        {
            try
            {

                HttpWebResponse response = HttpHelper.ExecuteRequest(
                    "http://www.localizame.movistar.es/logout.do",
                    "application/x-www-form-urlencoded",
                    "GET",
                    null,
                    new HttpHelper.Header[] { new HttpHelper.Header("cookie", _cookie) },
                    false
                    );

                if (response == null)
                {
                    _lastError = "Unable to connect web service";
                    return false;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _lastError = ((int)response.StatusCode).ToString() + " " + response.StatusDescription;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _lastError = "Internal Error: " + ex.Message;
                return false;
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
                        {
                            switch (header.Name.ToLower())
                            {
                                case "referer":
                                    request.Referer = header.Value;
                                    break;
                                default:
                                    request.Headers.Add(header.Name, header.Value);
                                    break;
                            }
                        }

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
                    resStream.Close();
                    try
                    {
                        response.GetResponseStream().Close();
                    }
                    catch { }
                    response.Close();
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
