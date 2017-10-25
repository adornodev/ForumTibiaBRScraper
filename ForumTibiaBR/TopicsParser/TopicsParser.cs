using HtmlAgilityPack;
using Newtonsoft.Json;
using MongoDB.Driver;
using MongoDB.Bson;
using NLog;
using SharedLibrary.Models;
using SharedLibrary.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Messaging;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WebUtilsLib;
using System.Threading;

namespace TopicsParser
{
    public class TopicsParser
    {
        #region Private Attributes

        private static string                   Source = "TibiaBR";
        private static string                   SectionsQueueName;
        private static string                   TopicsQueueName;
        private static string                   ConfigurationQueueName;
        private static string                   TopicsCollection;
        private static InputConfig              Config;
        private static MongoDBUtils<Section>    MongoUtilsSectionObj;
        private static MongoDBUtils<Topic>      MongoUtilsTopicObj;
        private static Logger                   logger = null;

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

            try
            {
                // Get Content from Configuration Queue
                logger.Debug("Getting Content from Configuration Queue...");
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


            // Initialization Mongo
            logger.Debug("Initializing MongoDB...");
            if (!InitializeMongo())
            {
                logger.Fatal("Error parsing Mongo variables! Aborting...");
                goto Exit;
            }

            try
            {
                // Main Method
                Execute(logger);
            }
            catch (Exception ex)
            {
                logger.Fatal("General Exception. \"Execute\" Method. Message.: {0}", ex.Message);
                goto Exit;
            }


            Exit:
                logger.Info("Press any key...");
                Console.ReadKey();
        }

        private static void Execute(Logger logger)
        {
            logger.Info("Start");

            // Initialization WebRequests
            WebRequests client = new WebRequests();
            InitializeWebRequest(ref client);

            // List that will be filled with compressed serialized object
            List<Topic>   topics   = new List<Topic>();
           
            while (true)
            {
                // Read messages from Section Queue
                Section section = ReadQueue(SectionsQueueName);

                // No more messages?
                if (section == null)
                {
                    logger.Debug("No more section to be processed.");
                    break;
                }

                logger.Trace("Processing \"{0}\" section...", section.Title);

                // Parse Topics from section and Save on TopicCollection/TopicQueue
                DoTheWork(section, ref topics, client);

                // Stopping for 30 minutes
                Thread.Sleep(30 * 60000);
            }
        }

        private static bool InitializeMongo()
        {
            MongoUtilsSectionObj = new MongoDBUtils<Section>(Config.MongoUser, Config.MongoPassword, Config.MongoAddress, Config.MongoDatabase);
            MongoUtilsTopicObj   = new MongoDBUtils<Topic>(Config.MongoUser, Config.MongoPassword, Config.MongoAddress, Config.MongoDatabase);

            // Sanit Check
            if (!MongoUtilsSectionObj.IsValidMongoData(Config))
                return false;

            // Invalid Collection?
            if (!MongoUtilsSectionObj.CollectionExistsAsync(Config.MongoCollection).Result || !MongoUtilsTopicObj.CollectionExistsAsync(TopicsCollection).Result)
                return false;

            // Open the Connection
            MongoUtilsSectionObj.GetCollection(Config.MongoCollection);
            MongoUtilsTopicObj.GetCollection(TopicsCollection);

            return true;
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
                var compactTopic = new { Title = topic.Title,
                                         Url   = topic.Url
                                       };

                string serializedCompactTopic = Utils.Compress(JsonConvert.SerializeObject(compactTopic));
                queue.Send(serializedCompactTopic);
            }

            queue.Dispose();
        }

        private static void DoTheWork(Section section, ref List<Topic> topics, WebRequests client)
        {
            int numberOfPage = 1;

            string url             = String.Empty;
            string sectionPieceUrl = String.Empty;
   
            
            // Find the right URL
            Regex importantPieceUrlRegex = new Regex(@"\/(\d{1,4}.*)\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Match match = importantPieceUrlRegex.Match(section.FullUrl);
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
                    ParseTopic(topicsNode, ref section, numberOfPage, ref topics);

                    if (topics.Count % 10 == 0 && topics.Count != 0)
                    {
                        // Send messages to Queue
                        logger.Trace("Sending to Topics Collection...");
                        SendMessage(topics);

                        logger.Trace("Sending message to Topics Queue...");
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

                // Has more?
                if (topics.Count > 0)
                {
                    // Send messages to Queue
                    logger.Trace("Sending to Topics Collection...");
                    SendMessage(topics);

                    logger.Trace("Sending message to Topics Queue...");
                    SendMessage(TopicsQueueName, topics);

                    topics.Clear();
                }

                // Is it the last Page?
                HtmlNode statsPageNode = htmlDoc.DocumentNode.SelectSingleNode(".//div[@class='threadpagestats']");
                if (statsPageNode != null)
                {
                    string stats = statsPageNode.InnerText.Trim();

                    Regex statsRegex = new Regex(@"\s(\d{1,})\sa\s(\d{1,})", RegexOptions.Compiled);
                    match = statsRegex.Match(stats);

                    if (match.Success)
                    {
                        if (match.Groups.Count == 3 && match.Groups[1].Value.Trim().Equals(match.Groups[2].Value.Trim()))
                            break;
                    }
                }

                // Next Page
                numberOfPage += 1;
                url = String.Format(_mapURLs["SectionTopics"], sectionPieceUrl, numberOfPage);

                // Keep Calm and don't shutdown the forum!
                Thread.Sleep(2 * 1000);
            }
        }

        private static void SendMessage(List<Topic> topics)
        {

            // Iterate over all Topics
            foreach (Topic topic in topics)
            {
                // Before inserting, we need to check if lot was already inserted and update it if this is the case
                UpdateResult result = MongoUtilsTopicObj.collection.UpdateOne(Builders<Topic>.Filter.Eq(reg => reg.Url, topic.Url),
                                                                              Builders<Topic>.Update
                                                                                 .Set("Title"               , topic.Title               )
                                                                                 .Set("Url"                 , topic.Url                 )
                                                                                 .Set("NumberOfComments"    , topic.NumberOfComments    )
                                                                                 .Set("NumberOfViews"       , topic.NumberOfViews       )
                                                                                 .Set("Evaluation"          , topic.Evaluation          )
                                                                                 .Set("LastPostUsername"    , topic.LastPostUsername    )
                                                                                 .Set("LastPostPublishDate" , topic.LastPostPublishDate )
                                                                                 .Set("Status"              , topic.Status              )
                                                                                 .Set("StatusId"            , topic.StatusId            )
                                                                                 .Set("SectionTitle"        , topic.SectionTitle        )
                                                                                 .Set("NumberOfSectionPage" , topic.NumberOfSectionPage )
                                                                                 .Set("LastCaptureDateTime" , topic.LastCaptureDateTime )
                                                                                 .Inc(r => r.Version,1));
                // New Register
                if (result.MatchedCount == 0)
                    MongoUtilsTopicObj.collection.InsertOne(topic);

            }
        }

        private static void ParseTopic(HtmlNodeCollection topicsNode, ref Section section, int numberOfPage, ref List<Topic> topics)
        {

            // Iterate over all Topics
            foreach (HtmlNode topicNode in topicsNode)
            {
                Topic topic = new Topic();
                topic.Source                = Source;
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


                // Extract LastPostUsername and LastPostPublishDate
                HtmlNode lastPostNode = topicNode.SelectSingleNode(".//dl[contains(@class,'threadlastpost')]");
                if (lastPostNode != null)
                {
                    HtmlNode lastUserNode = lastPostNode.SelectSingleNode(".//a/strong");
                    if (lastUserNode != null)
                        topic.LastPostUsername = Utils.Normalize(lastUserNode.InnerText.Trim());

                    HtmlNode lastPostPublishDateNode = lastPostNode.SelectSingleNode(".//dd[last()]");
                    if (lastPostPublishDateNode != null)
                    {
                        string publishDate = Utils.Normalize(lastPostPublishDateNode.InnerText.Trim());

                        DateTime dateTime = FormatDateTime(publishDate.Trim()).DateTime;
                        if (dateTime != DateTime.MinValue)
                        {
                            topic.LastPostPublishDate = dateTime;
                        }
                    }
                }

                // Extract NumberOfViews and NumberOfComments
                HtmlNode numbersNode = topicNode.SelectSingleNode(".//ul[contains(@class,'threadstats')]");
                if (numbersNode != null)
                {
                    HtmlNode viewsNode = numbersNode.SelectSingleNode("./li[position()=2]");

                    int number = -1;
                    if (viewsNode != null)
                    {
                        Int32.TryParse(String.Join("", Utils.Normalize(viewsNode.InnerText).Where(c => Char.IsDigit(c))), out number);
                        if (number >= 0)
                            topic.NumberOfViews = number;
                    }

                    number = -1;

                    HtmlNode commentsNode = numbersNode.SelectSingleNode(".//dd[last()]");
                    if (commentsNode == null)
                        commentsNode = numbersNode.SelectSingleNode(".//li");

                    if (commentsNode != null)
                    {
                        Int32.TryParse(String.Join("", Utils.Normalize(commentsNode.InnerText).Where(c => Char.IsDigit(c))), out number);
                        if (number > 0)
                            topic.NumberOfComments = number;
                    }
                }

                // Extract Author and PublishDate
                HtmlNode authorNode = topicNode.SelectSingleNode(".//a[contains(@class,'username')]");
                if (authorNode != null && authorNode.Attributes["title"] != null && !String.IsNullOrWhiteSpace(authorNode.Attributes["title"].Value))
                {
                    topic.Author = authorNode.InnerText.Trim();
                    if (authorNode.Attributes["title"].Value.Contains("em"))
                    {
                        string[] data = authorNode.Attributes["title"].Value.Split(new[] { " em " }, StringSplitOptions.RemoveEmptyEntries);

                        string publishDate = data[data.Length-1];

                        DateTime dateTime = FormatDateTime(publishDate.Trim()).DateTime;
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
                    if (evaluation >= 0)
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
            TopicsCollection        = Utils.LoadConfigurationSetting("TopicCollection","Topics");
        }

        private static Section ReadQueue(string queuename)
        {
            MSMQUtils MSMQ = new MSMQUtils();

            Section section = null;
            try
            {
                // Trying to read the queue
                object response = MSMQ.ReadPrivateQueue(queuename, timeoutInMinutes:1);
                string json     = (string) response;
                section         = JsonConvert.DeserializeObject<Section>(Utils.Decompress(json));
            }
            catch (MessageQueueException mqex)
            {
                logger.Fatal(mqex, mqex.Message);
                section = null;
            }
            catch(Exception ex)
            {
                logger.Fatal("Error in ReadQueue Method. Message.: {0} ", ex.Message);
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
        ///  Ontem, 11:37
        /// </summary>
        private static DateTimeOffset FormatDateTime(string publishDate)
        {
            string date     = String.Empty;
            string time     = String.Empty;
            string year     = String.Empty;
            string month    = String.Empty;
            string day      = String.Empty;
            string hour     = String.Empty;
            string minute   = String.Empty;

            publishDate = publishDate.Replace(",",String.Empty).ToUpper();
            DateTimeOffset formatedDateTime;
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
                if (date.Contains("HOJE"))
                {
                    year    = Convert.ToString(DateTime.UtcNow.Year);
                    month   = Convert.ToString(DateTime.UtcNow.Month);
                    day     = Convert.ToString(DateTime.UtcNow.Day);

                    formatedDateTime = new DateTime(Int32.Parse(year), Int32.Parse(month), Int32.Parse(day), Int32.Parse(hour), Int32.Parse(minute), 0);
                }
                else if (date.Contains("ONTEM"))
                {
                    year    = Convert.ToString(DateTime.UtcNow.Year);
                    month   = Convert.ToString(DateTime.UtcNow.Month);
                    day     = Convert.ToString(DateTime.UtcNow.Day-1);

                    formatedDateTime = new DateTimeOffset(Int32.Parse(year), Int32.Parse(month), Int32.Parse(day), Int32.Parse(hour), Int32.Parse(minute), 0, TimeSpan.FromHours(-3));
                }
                else
                    formatedDateTime = new DateTimeOffset(Int32.Parse(date.Split('-')[2].Trim()), Int32.Parse(date.Split('-')[1].Trim()), Int32.Parse(date.Split('-')[0].Trim()), Int32.Parse(hour), Int32.Parse(minute), 0, TimeSpan.FromHours(-3));
            }
            catch
            {
                return DateTimeOffset.MinValue;
            }

            return formatedDateTime.DateTime;
        }
    }
}
