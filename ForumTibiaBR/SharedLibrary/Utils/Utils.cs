using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace SharedLibrary.Utils
{
    public static class Utils
    {
        public static string LoadConfigurationSetting(string keyname, string defaultvalue)
        {
            string result = defaultvalue;
            try
            {
                result = ConfigurationManager.AppSettings[keyname];
            }
            catch
            {
                result = defaultvalue;
            }
            if (result == null)
                result = defaultvalue;
            return result;
        }

        public static int CountOccurences(string substring, string source)
        {
            int px = 0;
            int count = 0;
            while ((px = source.IndexOf(substring, px, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                px += substring.Length;
                count++;
            }

            return count;
        }

        public static bool IsValidJson(string strInput)
        {
            strInput = strInput.Trim();
            if ((strInput.StartsWith("{") && strInput.EndsWith("}")) || //For object
                (strInput.StartsWith("[") && strInput.EndsWith("]")))   //For array
            {
               
                JToken obj = JToken.Parse(strInput);
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// Returns a url based on the base uri and relative uri
        /// </summary>
        public static string AbsoluteUri(string baseuri, string relativeuri)
        {
            string result = string.Empty;

            if(!baseuri.StartsWith("https"))
                baseuri = String.Concat("https://", baseuri);

            result = (new Uri(new Uri(baseuri), relativeuri)).AbsoluteUri;
            result = HttpUtility.HtmlDecode(HttpUtility.UrlDecode(result));
         
            return result;
        }

        /// <summary>
        /// Sanitize text
        /// </summary>
        /// <param name="inputString"></param>
        /// <param name="encodeType"></param>
        /// <returns></returns>
        public static string Normalize(string inputString, Encoding encodeType = null)
        {
            if (encodeType == null)
            {
                inputString = HttpUtility.HtmlDecode(HttpUtility.UrlDecode(inputString));
            }
            else
            {
                inputString = HttpUtility.HtmlDecode(HttpUtility.UrlDecode(inputString, encodeType));
            }

            inputString = inputString.Replace("!*!", "");

            HtmlDocument map = new HtmlDocument();
            map.LoadHtml(inputString);

            inputString = map.DocumentNode.InnerText;

            while (inputString.IndexOf("<!--") != -1 && inputString.IndexOf("-->") != -1)
            {
                int comment = inputString.IndexOf("<!--");
                int endComment = inputString.IndexOf("-->") + 3;
                inputString = inputString.Substring(0, comment) + inputString.Substring(endComment);
            }

            inputString = inputString.Replace("\r\n", " ").Replace("\n", " ").Replace("\t", " ");
            
            // Removing more than one space from the string
            inputString = string.Join(" ", inputString.Split(' ').Where(s => !String.IsNullOrWhiteSpace(s))); 

            return inputString.Trim();
        }


        #region Compress/decompress
        /// <summary>
        /// The method compresses the value string using the GZipStream. The function returns
        /// the compressed representation of value. The first four bytes of the result
        /// contain the size of the compressed string
        /// </returns>
        public static string Compress(string value)
        {
            // Valid input?
            byte[] gzBuffer;
            if (string.IsNullOrWhiteSpace(value))
            {
                gzBuffer = Enumerable.Repeat<byte>(0, 4).ToArray<byte>();
                return Convert.ToBase64String(gzBuffer);
            }

            // Copy value to compressed stream
            byte[] buffer = Encoding.UTF8.GetBytes(value);
            MemoryStream ms = new MemoryStream();
            using (GZipStream zip = new GZipStream(ms, CompressionMode.Compress, true))
                zip.Write(buffer, 0, buffer.Length);
            ms.Position = 0;

            // Copy compressed string to result
            byte[] compressed = new byte[ms.Length];
            ms.Read(compressed, 0, compressed.Length);
            gzBuffer = new byte[compressed.Length + 4];
            System.Buffer.BlockCopy(compressed, 0, gzBuffer, 4, compressed.Length);
            System.Buffer.BlockCopy(BitConverter.GetBytes(buffer.Length), 0, gzBuffer, 0, 4);
            return Convert.ToBase64String(gzBuffer);
        }

        /// <summary>
        /// The method decompress the "compressedvalue" string. The first four bytes of the
        /// compressed string must contains its size.
        /// </summary>
        /// <param name="compressedvalue">Compressed string</param>
        /// <returns>
        /// The decompressed representation of "compressedvalue"
        /// </returns>
        public static string Decompress(string compressedvalue)
        {
            // Empty compressedvalue?
            if (compressedvalue.Length == 4)
                return string.Empty;

            // Decompress
            byte[] gzBuffer = Convert.FromBase64String(compressedvalue);
            using (MemoryStream ms = new MemoryStream())
            {
                int msgLength = BitConverter.ToInt32(gzBuffer, 0);
                ms.Write(gzBuffer, 4, gzBuffer.Length - 4);
                byte[] buffer = new byte[msgLength];
                ms.Position = 0;
                using (GZipStream zip = new GZipStream(ms, CompressionMode.Decompress))
                {
                    zip.Read(buffer, 0, buffer.Length);
                }
                return Encoding.UTF8.GetString(buffer);
            }
        }

        /// <summary>
        /// The method compresses the value string using the GZipStream. The function returns
        /// the compressed representation of value. The first four bytes of the result
        /// contain the size of the compressed string
        /// </returns>
        public static byte[] CompressToByteArray(string value)
        {
            // Valid input?
            byte[] gzBuffer;
            if (string.IsNullOrWhiteSpace(value))
            {
                gzBuffer = Enumerable.Repeat<byte>(0, 4).ToArray<byte>();
                return gzBuffer;
            }

            // Copy value to compressed stream
            byte[] buffer = Encoding.UTF8.GetBytes(value);
            MemoryStream ms = new MemoryStream();
            using (GZipStream zip = new GZipStream(ms, CompressionMode.Compress, true))
                zip.Write(buffer, 0, buffer.Length);
            ms.Position = 0;

            // Copy compressed string to result
            byte[] compressed = new byte[ms.Length];
            ms.Read(compressed, 0, compressed.Length);
            gzBuffer = new byte[compressed.Length + 4];
            System.Buffer.BlockCopy(compressed, 0, gzBuffer, 4, compressed.Length);
            System.Buffer.BlockCopy(BitConverter.GetBytes(buffer.Length), 0, gzBuffer, 0, 4);
            return gzBuffer;
        }

        /// <summary>
        /// The method decompress the "compressedvalue" byte array. The first four bytes of the
        /// compressed array must contain its size
        /// </returns>
        public static string DecompressFromByteArray(byte[] compressedvalue)
        {
            // Empty compressedvalue?
            if (compressedvalue.Length == 4)
                return string.Empty;

            // Decompress
            using (MemoryStream ms = new MemoryStream())
            {
                int msgLength = BitConverter.ToInt32(compressedvalue, 0);
                ms.Write(compressedvalue, 4, compressedvalue.Length - 4);
                byte[] buffer = new byte[msgLength];
                ms.Position = 0;
                using (GZipStream zip = new GZipStream(ms, CompressionMode.Decompress))
                {
                    zip.Read(buffer, 0, buffer.Length);
                }
                return Encoding.UTF8.GetString(buffer);
            }
        }
        #endregion

    }
}
