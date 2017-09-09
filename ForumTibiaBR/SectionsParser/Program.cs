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

namespace SectionsParser
{
    public class Program
    {
        private static Logger logger = null;
        private static string SectionsQueuerQueueName;
        private static string SectionsParserQueueName;
        private static string ConfigurationQueueName;
        private static BootstrapperConfig Config;
        
        private static Dictionary<string, string> _mapURLs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // {0} - Section.Url until find "?",  {1} - Number of Page
            {"SectionTopics"        ,   "https://forums.tibiabr.com/forums/{0}/page{1}?pp=50&sort=dateline&order=desc&daysprune=-1" }
        };

        public static void Main(string[] args)
        {
            // Loading New Logger
            logger = LogManager.GetCurrentClassLogger();

            //Initialization AppConfig
            InitializeAppConfig();

            // Get Content of Configuration Queue
            GetContentConfigurationQueue();

            // Sanit Check
            if (Config == null)
            {
                logger.Fatal("Error to process the Configuration Queue.");
                Console.Write("Press any key...");
                Console.ReadKey();
            }

            try
            {
                Execute(logger);
            }
            catch (Exception ex)
            {
                logger.Fatal(ex,"General Exception. \"Execute\" Method");
                Console.Write("Press any key...");
                Console.ReadKey();
            }
        }

        private static void Execute(Logger logger)
        {
            logger.Info("Start");

            // Initialization WebRequests
            WebRequests client = new WebRequests();
            InitializeWebRequest(ref client);

            List<Section> sections = new List<Section>();

            Section section;
            while ( (section = ReadQueue(SectionsQueuerQueueName)) != null)
            {
                logger.Trace("Processing \"{0}\" section ...", section.Title);

                // Parse Sections
                ParseSections(ref section, client);

                // Insert into sections list
                sections.Add(section);

                if (sections.Count % 10 == 0)
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

        private static void GetContentConfigurationQueue()
        {
            MSMQUtils MSMQ = new MSMQUtils();

            try
            {
                object obj  = MSMQ.ReadPrivateQueue(ConfigurationQueueName, persist: true);
                string json = Utils.Decompress((string)obj);
                Config      = JsonConvert.DeserializeObject<BootstrapperConfig>(json);
            }
            catch (MessageQueueException mqex)
            {
                logger.Fatal(mqex, mqex.Message);
            }
        }

        private static void SendMessage(string queuename, List<Section> sections)
        {
            MSMQUtils MSMQ = new MSMQUtils();

            // Trying to open the queue
            MessageQueue queue = MSMQ.OpenOrCreatePrivateQueue(queuename, typeof(Program).Namespace);

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

        private static void ParseSections(ref Section section, WebRequests client)
        {
            int numberOfPage = 1;

            string url = String.Empty;

            Regex locationRegex = new Regex(@"\/(\d{1,4}.*)\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Match match = locationRegex.Match(section.Url);
            if (match.Success)
            {
                string sectionPieceUrl = match.Groups[1].Value.Trim();
                url = String.Format(_mapURLs["SectionTopics"], section.Url.Substring(section.Url.LastIndexOf("/") + 1, section.Url.IndexOf("?")), numberOfPage);
            }

            while (!String.IsNullOrWhiteSpace(url))
            {
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
                    List<Topic> topics = ParseTopicsInitInfo(topicsNode, section, numberOfPage);

                    if (topics != null)
                    {
                        section.Topics = topics;
                        //TODO salvar os dados iniciais dos tópicos em uma fila
                    }
                }
                else
                {
                    logger.Warn("Problem to extract topicsNode");
                    numberOfPage += 1;
                    continue;
                }
            }
        }

        private static List<Topic> ParseTopicsInitInfo(HtmlNodeCollection topicsNode, Section section, int numberOfPage)
        {
            List<Topic> topics = new List<Topic>();

            // Iterate over all Topics
            foreach (HtmlNode topicNode in topicsNode)
            {
                Topic topic = new Topic();
                topic.CaptureDateTime       = DateTime.UtcNow;
                topic.SectionTitle          = section.Title;
                topic.NumberOfSectionPage   = numberOfPage;

                // Extract Title
                HtmlNode titleNode = topicNode.SelectSingleNode(".//a[@class='title']");
                if (titleNode != null && !String.IsNullOrWhiteSpace(titleNode.InnerText.Trim()))
                    topic.Title = titleNode.InnerText.Trim();

                // Extract href
                if (titleNode.Attributes["href"] != null)
                    topic.Url = titleNode.Attributes["href"].Value.Trim();

                // Complete Href
                if (!topic.Url.StartsWith("http"))
                {
                    topic.Url = Utils.AbsoluteUri(Config.Host, topic.Url);
                }

                // Extract Status
                HtmlNode statusNode = topicNode.SelectSingleNode(".//span[@class='prefix understate']");
                if (statusNode != null && statusNode.InnerText.IndexOf("Fixo", StringComparison.OrdinalIgnoreCase) > -1)
                    topic.StatusId = Enums.Status.Fixed;
                else
                    topic.StatusId = Enums.Status.Normal;



            }

            return null;
        }

        private static void InitializeAppConfig()
        {
            SectionsQueuerQueueName = Utils.LoadConfigurationSetting("SectionsQueuerQueue", "");
            SectionsParserQueueName = Utils.LoadConfigurationSetting("SectionsParserQueue", "");
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
            }

            return section;
        }
    }
}
