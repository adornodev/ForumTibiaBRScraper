using NLog;
using SharedLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebUtilsLib;

namespace SharedLibrary.Utils
{
    public class WebRequestsUtils
    {
        public string WebrequestUserAgent      = "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/60.0.3112.90 Safari/537.36 OPR/47.0.2631.71";
        public string WebrequestAccept         = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8";
        public string WebrequestAcceptEncoding = String.Empty;
        public string WebrequestAcceptLanguage = String.Empty;
        public string WebrequestContentType    = String.Empty;
        public string WebrequestEncoding       = String.Empty;


        // Randomizer for sleeping logic
        private static Random _rnd = new Random();

        private const int MaxPageSize = 5 * (1024 * 1024);


        public static void InitializeWebRequest (string url, out WebRequests webreq, BootstrapperConfig Config)
        {
            // Create new
            webreq = new WebRequests();

            //Sanit Check
            if (Config == null || !Config.VerifyMandatoryFields(Config))
                return;

            // Basic initialization
            webreq.KeepAlive         = Config.KeepAlive;
            webreq.UserAgent         = Config.UserAgent;
            webreq.Accept            = Config.Accept;
            webreq.AllowAutoRedirect = true;
            webreq.Timeout           = Config.Timeout == 0 ? 30000 : Config.Timeout;

            // Accept-Encoding
            if (!(String.IsNullOrEmpty(Config.AcceptEncoding)))
            {
                webreq.Headers.Add("Accept-Encoding", Config.AcceptEncoding);
            }

            // Accept-Language
            if (!(String.IsNullOrEmpty(Config.AcceptLanguage)))
            {
                webreq.Headers.Add("Accept-Language", Config.AcceptLanguage);
            }

            // Content-Type
            if (!(String.IsNullOrEmpty(Config.ContentType)))
            {
                webreq.ContentType = Config.ContentType;
            }

            // Encoding
            if (!(String.IsNullOrEmpty(Config.Charset)))
                webreq.Encoding = Config.Charset;
            else
                webreq.Encoding = "utf-8";

            webreq.EncodingDetection = WebRequests.CharsetDetection.DefaultCharset;

            // Initialize host property
            Uri uri = new Uri(url);
            webreq.Host = Config.Host;

            // Request max response size
            webreq.MaxResponseSize = MaxPageSize;
        }


        /// <summary>
        /// Initializes the "webrequest" object
        /// </summary>
        public void InitializeWebRequest (string url, out WebRequests webreq)
        {
            // Create new
            webreq = new WebRequests();

            // Basic initialization
            webreq.KeepAlive         = true;
            webreq.UserAgent         = WebrequestUserAgent;
            webreq.Accept            = WebrequestAccept;
            webreq.AllowAutoRedirect = true;
            webreq.Timeout           = 500000;

            // Accept-Encoding
            if (!(String.IsNullOrEmpty(WebrequestAcceptEncoding)))
            {
                webreq.Headers.Add("Accept-Encoding", WebrequestAcceptEncoding);
            }

            // Accept-Language
            if (!(String.IsNullOrEmpty(WebrequestAcceptLanguage)))
            {
                webreq.Headers.Add("Accept-Language", WebrequestAcceptLanguage);
            }

            // Content-Type
            if (!(String.IsNullOrEmpty(WebrequestContentType)))
            {
                webreq.ContentType = WebrequestContentType;
            }

            // Encoding
            if (String.IsNullOrEmpty(WebrequestEncoding))
            {
                webreq.Encoding = "utf-8";
            }
            else
            {
                webreq.Encoding = WebrequestEncoding;
            }

            webreq.EncodingDetection = WebUtilsLib.WebRequests.CharsetDetection.DefaultCharset;

            // Initialize host property
            Uri uri = new Uri(url);
            webreq.Host = uri.Host;

            // Request max response size
            webreq.MaxResponseSize = MaxPageSize;
        }

        public static string Get(ref WebRequests client, Logger logger, string url)
        {
            int    retry        = 10;
            string htmlResponse = String.Empty;
            do
            {
                // Get html of the current category main page
                try
                {
                    htmlResponse = client.Get(url, true);
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                }

                // Sanity check
                if (!String.IsNullOrWhiteSpace(htmlResponse) && client.StatusCode == HttpStatusCode.OK)
                {
                    break;
                }

                retry -= 1;
                logger.Debug(String.Format("Status Code not OK. Retries left: {0}", retry));

                logger.Debug("StatusCode = " + client.StatusCode + " Message = " + client.Error);

                logger.Debug("Html Response = " + htmlResponse);

                // Polite Sleeping
                Thread.Sleep(TimeSpan.FromSeconds(_rnd.Next(2, 5)));

            } while (retry >= 0);
            return htmlResponse;
        }

        public static string Post(ref WebRequests client, Logger logger, string url, string data)
        {
            int retry = 10;
            string htmlResponse = String.Empty;
            do
            {
                // Get html of the current category main page
                try
                {
                    htmlResponse = client.Post(url, data, true);
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                }

                // Sanity check
                if (!String.IsNullOrWhiteSpace(htmlResponse) && client.StatusCode == HttpStatusCode.OK)
                {
                    break;
                }

                retry -= 1;
                logger.Debug(String.Format("Status Code not OK. Retries left: {0}", retry));

                logger.Debug("StatusCode = " + client.StatusCode + " Message = " + client.Error);

                logger.Debug("Html Response = " + htmlResponse);

                // Polite Sleeping
                Thread.Sleep(TimeSpan.FromSeconds(_rnd.Next(2, 5)));

            } while (retry >= 0);
            return htmlResponse;
        }
    }
}

