using HtmlAgilityPack;
using Newtonsoft.Json;
using NLog;
using SharedLibrary.Models;
using SharedLibrary.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Messaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WebUtilsLib;

namespace TopicsParser
{
    public class TopicsParser
    {
        #region Private Attributes

        private static string Source = "TibiaBR";

        private static Logger logger = null;
        private static string SectionsQueueName;
        private static string TopicsQueueName;
        private static string ConfigurationQueueName;
        private static InputConfig Config;
        
        private static Dictionary<string, string> _mapURLs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // {0} - Section.Url until find "?",  {1} - Number of Page
            {"SectionTopics"        ,   "https://forums.tibiabr.com/forums/{0}/page{1}?pp=50&sort=dateline&order=desc&daysprune=-1" }
        };

        #endregion


        public static void Main(string[] args)
        {
            // Loading New Logger
            logger = LogManager.GetCurrentClassLogger();

            //Initialization AppConfig
            InitializeAppConfig();


            // Get Content of Configuration Queue
            try
            {
                Config = MSMQUtils.GetContentConfigurationQueue(ConfigurationQueueName);
            }
            catch (MessageQueueException mqex)
            {
                logger.Fatal(mqex, mqex.Message);
                goto Exit;
            }


            // Sanit Check
            if (Config == null)
            {
                logger.Fatal("Error to process the Configuration Queue.");
                goto Exit;
            }


            try
            {
                // Main
                Execute(logger);
            }
            catch (Exception ex)
            {
                logger.Fatal(ex,"General Exception. \"Execute\" Method");
                goto Exit;
            }


            Exit:
                Console.Write("Press any key...");
                Console.ReadKey();
        }

        private static void Execute(Logger logger)
        {
            logger.Info("Start");

            // Initialization WebRequests
            WebRequests client = new WebRequests();
            InitializeWebRequest(ref client);

            // Lists that will be filled with compressed serialized object
            List<Section> sections = new List<Section>();
            List<Topic>   topics   = new List<Topic>();

            Section section;
            while ( (section = ReadQueue(SectionsQueueName)) != null)
            {
                if (section == null)
                    break;

                logger.Trace("Processing \"{0}\" section ...", section.Title);

                // Parse Sections
                ParseSections(ref section, ref topics, client);

                // Insert into sections list
                sections.Add(section);

                if (sections.Count % 5 == 0)
                {
                    //SendMessage();
                    sections.Clear();
                }
            }
            
            // Has more?
            if (sections.Count != 0)
            {
                //SendMessage();
            }
        }

        private static void InitializeWebRequest(ref WebRequests client)
        {
            client.Host         = Config.Host;
            client.KeepAlive    = Config.KeepAlive;
            client.Accept       = Config.Accept;
            client.UserAgent    = Config.UserAgent;
            client.Encoding     = Config.Charset;
            client.Timeout      = Config.Timeout;
            client.Headers.Clear();
            client.Headers.Add("AcceptEncoding", Config.AcceptEncoding);
            client.Headers.Add("AcceptLanguage", Config.AcceptLanguage);

        }

        private static void SendMessage(string queuename, List<Topic> topics)
        {
            MSMQUtils MSMQ = new MSMQUtils();

            // Trying to open the queue
            MessageQueue queue = MSMQ.OpenOrCreatePrivateQueue(queuename, typeof(TopicsParser).Namespace);

            // Sanit check
            if (queue == null)
            {
                logger.Fatal("Error to open a private queue. The field \"queue\" is null.");
                return;
            }

            // Iterate over all topics
            foreach (Topic topic in topics)
            {
                string serializedTopic = Utils.Compress(JsonConvert.SerializeObject(topic));
                queue.Send(serializedTopic);
            }

            queue.Dispose();
        }

        private static void SendMessage(string queuename, List<Section> sections)
        {
            MSMQUtils MSMQ = new MSMQUtils();

            // Trying to open the queue
            MessageQueue queue = MSMQ.OpenOrCreatePrivateQueue(queuename, typeof(TopicsParser).Namespace);

            // Sanit check
            if (queue == null)
            {
                logger.Fatal("Error to open a private queue. The field \"queue\" is null.");
                return;
            }

            // Iterate over all sections
            foreach (Section section in sections)
            {
                string serializedSection = Utils.Compress(JsonConvert.SerializeObject(section));
                queue.Send(serializedSection);
            }

            queue.Dispose();
        }

        private static void ParseSections(ref Section section, ref List<Topic> topics, WebRequests client)
        {
            int numberOfPage = 1;

            string url             = String.Empty;
            string sectionPieceUrl = String.Empty;

            // Insert Source
            section.Source = Source;

            // Find the right URL
            Regex locationRegex = new Regex(@"\/(\d{1,4}.*)\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Match match = locationRegex.Match(section.Url);
            if (match.Success)
            {
                sectionPieceUrl = match.Groups[1].Value.Trim();
                url = String.Format(_mapURLs["SectionTopics"], sectionPieceUrl, numberOfPage);
            }

            while (!String.IsNullOrWhiteSpace(url))
            {
                logger.Trace("Section {0} ... Page: {1}", section.Title, numberOfPage);

                // Get Request
                string htmlResponse = SharedLibrary.Utils.WebRequestsUtils.Get(ref client, logger, url);

                //TODO Preciso checar se estou na útlima página... Página XXX de XXX . Se sim, sair do loop

                // Checking if html response is valid
                if (String.IsNullOrWhiteSpace(htmlResponse))
                {
                    logger.Warn("HtmlResponse is null or empty. URL: " + url);
                    numberOfPage += 1;
                    continue;
                }

                // Loading HtmlDocument
                HtmlDocument htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(htmlResponse);

                // Extract Topics
                HtmlNodeCollection topicsNode = htmlDoc.DocumentNode.SelectNodes(".//li[contains(@class,'threadbit')]");
                if (topicsNode != null && topicsNode.Count > 0)
                {
                    ParseTopicsInitInfo(topicsNode, ref section, numberOfPage, ref topics);

                    if (topics.Count % 10 == 0)
                    {
                        SendMessage(TopicsQueueName, topics);
                        topics.Clear();
                    }
                }
                else
                {
                    logger.Warn("Problem to extract topicsNode");
                    numberOfPage += 1;
                    continue;
                }


                if (topics.Count > 0)
                {
                    SendMessage(TopicsQueueName, topics);
                    topics.Clear();
                }

                // Next Page
                numberOfPage += 1;
                url = String.Format(_mapURLs["SectionTopics"], sectionPieceUrl, numberOfPage);
            }
        }

        private static void ParseTopicsInitInfo(HtmlNodeCollection topicsNode, ref Section section, int numberOfPage, ref List<Topic> topics)
        {

            // Iterate over all Topics
            foreach (HtmlNode topicNode in topicsNode)
            {
                Topic topic = new Topic();
                topic.Source                = Source;
                topic.FirstCaptureDateTime  = DateTime.UtcNow;
                topic.SectionTitle          = section.Title;
                topic.NumberOfSectionPage   = numberOfPage;


                // Extract Title
                HtmlNode titleNode = topicNode.SelectSingleNode(".//a[@class='title']");
                if (titleNode != null && !String.IsNullOrWhiteSpace(titleNode.InnerText.Trim()))
                    topic.Title = Utils.Normalize(titleNode.InnerText.Trim());

                if (topic.Title != null)
                    logger.Trace("Section {0} ... Page: {1} ... Topic: {2}", section.Title, numberOfPage, topic.Title);
                

                // Extract href
                if (titleNode.Attributes["href"] != null)
                    topic.Url = titleNode.Attributes["href"].Value.Trim();


                // Complete Href
                if (!topic.Url.StartsWith("http"))
                    topic.Url = Utils.AbsoluteUri(Config.Host, topic.Url);


                // Extract Status
                HtmlNode statusNode = topicNode.SelectSingleNode(".//span[@class='prefix understate']");
                if (statusNode != null && statusNode.InnerText.IndexOf("Fixo", StringComparison.OrdinalIgnoreCase) > -1)
                    continue;
                    else if (statusNode != null && statusNode.InnerText.IndexOf("Movido", StringComparison.OrdinalIgnoreCase) > -1)
                        topic.StatusId = Enums.Status.Moved;
                        else
                            topic.StatusId = Enums.Status.Normal;



                // Extract Author and PublishDate
                HtmlNode authorNode = topicNode.SelectSingleNode(".//a[contains(@class,'username')]");
                if (authorNode != null && authorNode.Attributes["title"] != null && !String.IsNullOrWhiteSpace(authorNode.Attributes["title"].Value))
                {
                    topic.Author = authorNode.InnerText.Trim();
                    if (authorNode.Attributes["title"].Value.Contains("em"))
                    {
                        string publishDate = authorNode.Attributes["title"].Value.Split(new[] { " em " }, StringSplitOptions.RemoveEmptyEntries)[1];

                        DateTime dateTime = FormatDateTime(publishDate.Trim());
                        if (dateTime != DateTime.MinValue)
                        {
                            topic.PublishDate = dateTime;
                        }
                    }
                }

                // Extract Evaluation
                HtmlNode evaluationNode = topicNode.SelectSingleNode(".//ul[contains(@class,'threadstats')]/li[@class='hidden']");
                if (evaluationNode != null && !String.IsNullOrWhiteSpace(evaluationNode.InnerText))
                {
                    double evaluation = GetEvaluation(evaluationNode.InnerText);
                    if (evaluation > 0)
                        topic.Evaluation = evaluation;
                }


                // Insert into topics list
                topics.Add(topic);
            }
        }

        private static void InitializeAppConfig()
        {
            SectionsQueueName       = Utils.LoadConfigurationSetting("SectionsParserQueue", "");
            TopicsQueueName         = Utils.LoadConfigurationSetting("TopicsParserQueue",   "");
            ConfigurationQueueName  = Utils.LoadConfigurationSetting("ConfigurationQueue", "ForumTibiaBR_Config");
        }

        private static Section ReadQueue(string queuename)
        {
            MSMQUtils MSMQ = new MSMQUtils();

            Section section = null;
            try
            {
                // Trying to read the queue
                object response = MSMQ.ReadPrivateQueue(queuename);
                string json     = (string) response;
                section = JsonConvert.DeserializeObject<Section>(Utils.Decompress(json));
            }
            catch (MessageQueueException mqex)
            {
                logger.Fatal(mqex, mqex.Message);
                section = null;
            }

            return section;
        }


        /// <summary>
        /// Example: Avaliação4 / 5
        /// </summary>
        private static double GetEvaluation(string text)
        {
            int evaluation = -1;
            int total      = -1;

            double result  = -1.0;

            try
            {
                string[] data;
                if (text.Contains("/"))
                {
                    data       = text.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries);

                    evaluation = Int32.Parse(String.Join("", data[0].Where(c => Char.IsDigit(c))));
                    total      = Int32.Parse(String.Join("", data[1].Where(c => Char.IsDigit(c))));

                    result = (double)evaluation / total;
                }

            }
            catch(Exception ex)
            {
                logger.Error(ex,"Error to get Evaluation");
                result = -1.0;
            }

            return result;
        }

        /// <summary>
        /// Date Example:
        ///  19-09-2004 17:01
        ///  Hoje 11:32
        ///  Ontem 11:58
        /// </summary>
        private static DateTime FormatDateTime(string publishDate)
        {
            string date     = String.Empty;
            string time     = String.Empty;
            string year     = String.Empty;
            string month    = String.Empty;
            string day      = String.Empty;
            string hour     = String.Empty;
            string minute   = String.Empty;

            publishDate = publishDate.ToUpper();
            DateTime formatedDateTime;
            try
            {
                string [] separator = new string[] { " " };

                date = publishDate.Split(separator, StringSplitOptions.None)[0].Trim();
                time = publishDate.Split(separator, StringSplitOptions.None)[1].Trim();


                // Time
                if (time.Contains(":"))
                {
                    hour    = time.Split(':')[0].Trim();
                    minute  = time.Split(':')[1].Trim();
                }
                else if (time.Contains("H"))
                {
                    hour    = time.Split('H')[0].Trim();
                    minute  = time.Split('H')[1].Trim();
                }

                // Date
                if (date.ToLower().Contains("hoje"))
                {
                    year    = Convert.ToString(DateTime.UtcNow.Year);
                    month   = Convert.ToString(DateTime.UtcNow.Month);
                    day     = Convert.ToString(DateTime.UtcNow.Day);

                    formatedDateTime = new DateTime(Int32.Parse(year), Int32.Parse(month), Int32.Parse(day), Int32.Parse(hour), Int32.Parse(minute), 0);
                }
                else if (date.ToLower().Contains("ontem"))
                {
                    year    = Convert.ToString(DateTime.UtcNow.Year);
                    month   = Convert.ToString(DateTime.UtcNow.Month);
                    day     = Convert.ToString(DateTime.UtcNow.Day-1);

                    formatedDateTime = new DateTime(Int32.Parse(year), Int32.Parse(month), Int32.Parse(day), Int32.Parse(hour), Int32.Parse(minute), 0);
                }
                else
                    formatedDateTime = new DateTime(Int32.Parse(date.Split('-')[2].Trim()), Int32.Parse(date.Split('-')[1].Trim()), Int32.Parse(date.Split('-')[0].Trim()), Int32.Parse(hour), Int32.Parse(minute), 0);

                
                formatedDateTime = TimeZoneInfo.ConvertTimeToUtc(formatedDateTime);
            }
            catch
            {
                return DateTime.MinValue;
            }

            return formatedDateTime;
        }
    }
}
