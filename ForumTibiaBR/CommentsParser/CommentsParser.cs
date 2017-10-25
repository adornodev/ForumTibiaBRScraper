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
using System.Text;

namespace CommentsParser
{
    public class CommentsParser
    {
        #region Private Attributes

        private static string Source = "TibiaBR";
        private static string TopicsQueueName;
        private static string CommentsQueueName;
        private static string ConfigurationQueueName;
        private static string CommentsCollection;
        private static string UsersCollection;
        private static InputConfig Config;
        private static MongoDBUtils<Topic>   MongoUtilsTopicObj;
        private static MongoDBUtils<Comment> MongoUtilsCommentObj;
        private static MongoDBUtils<User>    MongoUtilsUserObj;
        private static Logger                logger = null;

        private static Dictionary<string, string> _mapURLs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // {0} - Topic Url until find "?",  {1} - Number of Page
            {"TopicComments",   "https://forums.tibiabr.com/threads/{0}/page{1}" }
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
            List<Comment> comments = new List<Comment>();

            while (true)
            {
                // Read messages from Section Queue
                Topic topic = ReadQueue(TopicsQueueName);

                // No more messages?
                if (topic == null)
                {
                    logger.Debug("No more Topic to be processed.");
                    break;
                }

                logger.Trace("Processing \"{0}\" topic...", topic.Title);

                // Parse Comment from Topic and Save on CommentCollection/CommentQueue
                bool processsed = DoTheWork(topic, ref comments, client);

                if (!processsed)
                    Thread.Sleep(10000);
                else
                    // Stopping for 30 minutes
                    Thread.Sleep(30 * 60000);
            }
        }

        private static bool InitializeMongo()
        {
            MongoUtilsTopicObj      = new MongoDBUtils<Topic>(Config.MongoUser, Config.MongoPassword, Config.MongoAddress, Config.MongoDatabase);
            MongoUtilsCommentObj    = new MongoDBUtils<Comment>(Config.MongoUser, Config.MongoPassword, Config.MongoAddress, Config.MongoDatabase);
            MongoUtilsUserObj       = new MongoDBUtils<User>(Config.MongoUser, Config.MongoPassword, Config.MongoAddress, Config.MongoDatabase);

            // Sanit Check
            if (!MongoUtilsTopicObj.IsValidMongoData(Config))
                return false;

            // Invalid Collection?
            if (!MongoUtilsTopicObj.CollectionExistsAsync(Config.MongoCollection).Result 
                || !MongoUtilsCommentObj.CollectionExistsAsync(CommentsCollection).Result
                || !MongoUtilsUserObj.CollectionExistsAsync(UsersCollection).Result)
                return false;

            // Open the Connection
            MongoUtilsTopicObj.GetCollection(Config.MongoCollection);
            MongoUtilsCommentObj.GetCollection(CommentsCollection);
            MongoUtilsUserObj.GetCollection(UsersCollection);

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

        private static void SendMessage(string queuename, List<Comment> comments)
        {
            MSMQUtils MSMQ = new MSMQUtils();

            // Trying to open the queue
            MessageQueue queue = MSMQ.OpenOrCreatePrivateQueue(queuename, typeof(CommentsParser).Namespace);

            // Sanit check
            if (queue == null)
            {
                logger.Fatal("Error to open a private queue. The field \"queue\" is null.");
                return;
            }

            // Iterate over all comments
            foreach (Comment comment in comments)
            {
                var compactComment = new
                {
                    Author = comment.Author
                };

                string serializedCompactComment = Utils.Compress(JsonConvert.SerializeObject(compactComment));
                queue.Send(serializedCompactComment);
            }

            queue.Dispose();
        }

        private static bool DoTheWork(Topic topic, ref List<Comment> comments, WebRequests client)
        {
            int numberOfPage          = 1;
            int processedLastPosition = 0;

            string url             = String.Empty;
            string topicPieceUrl   = String.Empty;

            // Find the right URL
            Regex importantPieceUrlRegex = null;

            if (topic.Url.Contains("?"))
                importantPieceUrlRegex = new Regex(@"/(\d{1,4}.*)\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            else
                importantPieceUrlRegex = new Regex(@"/(\d{1,4}.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            Match match = importantPieceUrlRegex.Match(topic.Url);
            if (match.Success)
            {
                topicPieceUrl = match.Groups[1].Value.Trim();
                url = String.Format(_mapURLs["TopicComments"], topicPieceUrl, numberOfPage);
            }

            while (!String.IsNullOrWhiteSpace(url))
            {
                logger.Trace("Topic <{0}> ... Page: {1}", topic.Title, numberOfPage);

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


                // Extract Comments
                HtmlNodeCollection commentsNode = htmlDoc.DocumentNode.SelectNodes(".//ol[@id='posts']//li[contains(@class,'postbitlegacy')]");
                if (commentsNode != null && commentsNode.Count > 0)
                {
                    int firstPositionFromTopic = -1;

                    ParseComment(commentsNode, ref topic, numberOfPage, ref comments, ref firstPositionFromTopic);

                    // Condition to know if I am on the last available page of the topic
                    if (processedLastPosition == firstPositionFromTopic)
                        return false;
                    else
                        processedLastPosition = firstPositionFromTopic;


                    if (comments.Count % 5 == 0 && comments.Count != 0)
                    {
                        SendMessage(comments);
                        comments.Clear();
                    }
                }
                else
                {
                    // Need permission to parser?
                    HtmlNode permissionNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='blockbody formcontrols']");
                    if (permissionNode != null)
                        logger.Warn("Permission Error to access the topic");
                    else
                        logger.Error("Problem to extract commentsNode. URL .: {0}", url);

                    return false;
                }

                // Has more?
                if (comments.Count > 0)
                {
                    SendMessage(comments);
                    comments.Clear();
                }

                // Is it the last Page?
                HtmlNode statsPageNode = htmlDoc.DocumentNode.SelectSingleNode(".//div[@class='threadpagestats']");
                if (statsPageNode == null)
                    statsPageNode = htmlDoc.DocumentNode.SelectSingleNode(".//div[@class='postpagestats']");
                if (statsPageNode != null)
                {
                    string stats = statsPageNode.InnerText.Trim();

                    Regex statsRegex = new Regex(@"\s\d{1,}\sa\s(\d{1,})\sde\s(\d{1,})", RegexOptions.Compiled);
                    match = statsRegex.Match(stats);
                    if (match.Success)
                    {
                        if (match.Groups.Count == 3 && match.Groups[1].Value.Trim().Equals(match.Groups[2].Value.Trim()))
                            return false;
                    }
                }

                // Next Page
                numberOfPage += 1;
                url = String.Format(_mapURLs["TopicComments"], topicPieceUrl, numberOfPage);

                // Keep Calm and do not shutdown the forum!
                Thread.Sleep(2 * 1000);
            }

            return true;
        }

        private static void SendMessage(List<Comment> comments)
        {

            // Iterate over all Comments
            foreach (Comment comment in comments)
            {
                // Before inserting, we need to check if lot was already inserted and update it if this is the case
                UpdateResult result = MongoUtilsCommentObj.collection.UpdateOne(Builders<Comment>.Filter.Eq(reg => reg.Url, comment.Url),
                                                                                Builders<Comment>.Update
                                                                                 .Set("TopicTitle"          , comment.TopicTitle)
                                                                                 .Set("Text"                , comment.Text)
                                                                                 .Set("LastCaptureDateTime" , comment.LastCaptureDateTime)
                                                                                 .Inc(r => r.Version, 1));
                // New Register
                if (result.MatchedCount == 0)
                    MongoUtilsCommentObj.collection.InsertOne(comment);

            }
        }

        private static void ParseComment(HtmlNodeCollection commentsNode, ref Topic topic, int numberOfPage, ref List<Comment> comments, ref int firstPositionFromTopic)
        {
            bool firstIteration = true;

            // Iterate over all CommentsNode
            foreach (HtmlNode commentNode in commentsNode)
            {
                Comment comment         = new Comment();
                comment.Source          = Source;
                comment.TopicTitle      = topic.Title;
                comment.NumberOfPage    = numberOfPage;

                // Get Text
                HtmlNode textNode = commentNode.SelectSingleNode(".//div[@id='divSpdInText']");
                if (textNode != null && !String.IsNullOrWhiteSpace(textNode.InnerText.Trim()))
                    comment.Text = Utils.Normalize(textNode.InnerText.Trim());

                // Trace Message
                //logger.Trace("Topic <{0}> ... Page: <{1}> ... Comment: {2}", topic.Title, numberOfPage, (comment.Text.Length > 20) ? String.Concat(comment.Text.Substring(0,20)," ...") : comment.Text);

                // Extract URL and Position
                HtmlNode urlNode = commentNode.SelectSingleNode(".//span[@class='nodecontrols']/a");
                int position = ExtractHrefCommentPosition(ref comment, urlNode);

                // Set firstPositionFromTopic 
                if (firstIteration)
                {
                    firstPositionFromTopic = position;
                    firstIteration         = false;
                }

                // Sanit check
                if (String.IsNullOrWhiteSpace(comment.Url))
                    return;


                // Get Author
                HtmlNode authorNode = commentNode.SelectSingleNode(".//div[@class='userinfo']//a[contains(@class,'username')]");
                if (authorNode != null && authorNode.Attributes["href"] != null && !String.IsNullOrWhiteSpace(authorNode.Attributes["href"].Value))
                    comment.Author = authorNode.InnerText.Trim();


                // Extract PublishDate
                HtmlNode publishDateNode = commentNode.SelectSingleNode(".//span[@class='date']");
                if (publishDateNode != null && !String.IsNullOrWhiteSpace(publishDateNode.InnerText))
                {
                    string publishDate = Utils.Normalize(publishDateNode.InnerText);

                    DateTime dateTime = FormatDateTime(publishDate.Trim());
                    if (dateTime != DateTime.MinValue)
                    {
                        comment.PublishDate = dateTime;
                    }
                }

                // Extract User Object
                HtmlNode userNOde = commentNode.SelectSingleNode(".//div[@class='userinfo']");
                ExtractUser(ref comment, userNOde);

                // Insert into comments list
                comments.Add(comment);

            }
        }

        private static void ExtractUser(ref Comment comment, HtmlNode node)
        {
            // Sanit Check
            if (node == null)
                return;

            // Initialize User Object
            User user   = new User();
            user.Source = Source;
            user.Name   = comment.Author;

            
            // Extract Url
            HtmlNode urlNode = node.SelectSingleNode(".//a");
            if (urlNode != null && urlNode.Attributes["href"] != null)
            {
                user.Url = urlNode.GetAttributeValue("href", String.Empty);

                // Fixing URL
                if (user.Url.Contains("?s"))
                    user.Url = user.Url.Substring(0, user.Url.IndexOf("?s"));

                // Complete URL
                if (!user.Url.StartsWith("http"))
                    user.Url = Utils.AbsoluteUri(Config.Host, user.Url);
            }

            // Extract RegisterDate
            HtmlNode registerDateNode = node.SelectSingleNode(".//dl[@class='userinfo_extra']/dd");
            if (registerDateNode != null)
            {
                string registerDate = registerDateNode.InnerText.Trim();

                DateTime dateTime = FormatDateTime(registerDate);
                if (dateTime != DateTime.MinValue)
                {
                    user.RegisterDate = dateTime;
                }
            }

            // Get Number of Comments
            HtmlNode numberCommentsNode = node.SelectSingleNode(".//dl[@class='userinfo_extra']/dd[last()]");
            if (numberCommentsNode != null)
            {
                int number = 0;
                Int32.TryParse(numberCommentsNode.InnerText.Trim(), out number);

                if (number > 0)
                    user.NumberOfComments = number;
            }

            // Extract Rank
            HtmlNode rankNode = node.SelectSingleNode(".//span[@class='rank']/img");
            if (rankNode != null && rankNode.Attributes["src"] != null)
            {
                Regex rankRegex = new Regex(@"\/(\w{1,}).(p|j)", RegexOptions.Compiled);
                Match match     = rankRegex.Match(rankNode.Attributes["src"].Value.Trim());

                if (match.Success)
                    user.Rank = match.Groups[1].Value;

            }

            comment.User = user;
        }

        private static int ExtractHrefCommentPosition(ref Comment comment, HtmlNode node)
        {
            // Sanit Check
            if (node == null || node.Attributes["href"] == null)
                return -1;

            // Get URL
            comment.Url = node.Attributes["href"].Value.Trim();

            // Complete URL
            if (!comment.Url.StartsWith("http"))
                comment.Url = Utils.AbsoluteUri(Config.Host, comment.Url);

            // Fixing URL
            Regex importantPieceUrlRegex = new Regex(@"\/(\d{1,4}.*)\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Match match = importantPieceUrlRegex.Match(comment.Url);
            if (match.Success)
            {
                string commentPieceUrl = match.Groups[1].Value.Trim();

                string firstPiece  = comment.Url.Substring(0, comment.Url.IndexOf(commentPieceUrl) + commentPieceUrl.Length + 1);
                string secondPiece = String.Empty;

                if (comment.Url.Contains("&p"))
                    secondPiece = comment.Url.Substring(comment.Url.IndexOf("&p"));
                else if (comment.Url.Contains("&viewfull"))
                {
                    secondPiece = comment.Url.Substring(comment.Url.IndexOf("&viewfull"));
                    firstPiece  = firstPiece.Substring(0,firstPiece.Length -1);
                }

                comment.Url = String.Concat(firstPiece, secondPiece);
            }

            // Get Position
            try
            {
                if (!String.IsNullOrWhiteSpace(node.InnerText.Trim()))
                {
                    comment.Position = Int32.Parse(node.InnerText.Replace("#", String.Empty).Trim());
                    return comment.Position;
                }
            }
            catch { }

            return -1;
        }

        private static void InitializeAppConfig()
        {
            TopicsQueueName         = Utils.LoadConfigurationSetting("TopicParserQueue", "");
            CommentsQueueName       = Utils.LoadConfigurationSetting("CommentParserQueue", "");
            ConfigurationQueueName  = Utils.LoadConfigurationSetting("ConfigurationQueue", "ForumTibiaBR_Config");
            CommentsCollection      = Utils.LoadConfigurationSetting("CommentCollection", "Comments");
            UsersCollection         = Utils.LoadConfigurationSetting("UserCollection", "Users");
        }

        private static Topic ReadQueue(string queuename)
        {
            MSMQUtils MSMQ = new MSMQUtils();

            Topic topic = null;
            try
            {
                // Trying to read the queue
                object response = MSMQ.ReadPrivateQueue(queuename, timeoutInMinutes: 1);
                string json     = (string)response;
                topic           = JsonConvert.DeserializeObject<Topic>(Utils.Decompress(json));
            }
            catch (MessageQueueException mqex)
            {
                logger.Fatal(mqex, mqex.Message);
                topic = null;
            }
            catch (Exception ex)
            {
                logger.Fatal("Error in ReadQueue Method. Message.: {0} ", ex.Message);
                topic = null;
            }

            return topic;
        }

        /// <summary>
        /// Date Example:
        ///  19-09-2004 17:01
        ///  13-10-2017, 10:41
        ///  09-09-2015
        ///  Hoje 11:32
        ///  Ontem 11:58
        ///  Ontem, 11:37
        /// </summary>
        private static DateTime FormatDateTime(string publishDate)
        {
            string date     = String.Empty;
            string time     = String.Empty;
            string year     = String.Empty;
            string month    = String.Empty;
            string day      = String.Empty;
            string hour     = Convert.ToString(DateTime.Now.Hour);
            string minute   = Convert.ToString(DateTime.Now.Minute);

            publishDate = publishDate.Replace(",", String.Empty).ToUpper();
            DateTime formatedDateTime;
            try
            {
                string[] separator = new string[] { " " };


                if (publishDate.Contains("-") && publishDate.Contains(":"))
                {
                    Regex timeRegex = new Regex(@"(\d{1,2}:\d{1,2}$)", RegexOptions.Compiled);
                    Match match = timeRegex.Match(publishDate);
                    if (match.Success)
                    {
                        time = match.Groups[1].Value.Trim();
                    }

                    Regex dateRagex = new Regex(@"(\d{1,2}-\d{1,2}-\d{2,4})", RegexOptions.Compiled);
                    match = dateRagex.Match(publishDate);
                    if (match.Success)
                    {
                        date = match.Groups[1].Value.Trim();

                        year    = date.Split('-')[2];
                        month   = date.Split('-')[1];
                        day     = date.Split('-')[0];
                    }
                }
                else
                {
                    // Example: 09-09-2015
                    if (publishDate.Contains("-") && publishDate.Split('-').Length == 3)
                    {
                        year    = publishDate.Split('-')[2];
                        month   = publishDate.Split('-')[1];
                        day     = publishDate.Split('-')[0];
                    }
                    else
                    {
                        date = publishDate.Split(separator, StringSplitOptions.None)[0].Trim();
                        time = publishDate.Split(separator, StringSplitOptions.None)[1].Trim();
                    }
                }

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

                    formatedDateTime = new DateTime(Int32.Parse(year), Int32.Parse(month), Int32.Parse(day), Int32.Parse(hour), Int32.Parse(minute), 0);
                }
                else
                    formatedDateTime = new DateTime(Int32.Parse(year), Int32.Parse(month), Int32.Parse(day), Int32.Parse(hour), Int32.Parse(minute), 0,DateTimeKind.Local);

                formatedDateTime = TimeZoneInfo.ConvertTimeToUtc(formatedDateTime);
            }
            catch (Exception ex)
            {
                return DateTime.MinValue;
            }

            return formatedDateTime;
        }
    }
}
