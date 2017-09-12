using HtmlAgilityPack;
using Newtonsoft.Json;
using NLog;
using SharedLibrary.Models;
using SharedLibrary.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Messaging;
using System.Text;
using System.Threading.Tasks;
using WebUtilsLib;

namespace Bootstrapper
{
    public class Program
    {
        private static string ConfigFilePath;
        private static Logger logger = null;
        private static BootstrapperConfig Config;

        public static void Main(string[] args)
        {
            // Loading New Logger
            logger = LogManager.GetCurrentClassLogger();

            //Initialization AppConfig
            InitializeAppConfig();

            // Load config
            logger.Trace("Loading config file");
            if (!ParseConfigurationFile(ConfigFilePath))
            {
                logger.Fatal("Error parsing configuration file! Aborting...");
                Environment.Exit(-101);
            }

            // Save input fields
            SaveFields();

            try
            {
                // Main
                Execute(logger);
            }
            catch (Exception ex)
            {
                logger.Fatal(ex,"General Exception. \"Execute\" Method");
            }

            Console.Write("Press any key...");
            Console.ReadKey();
        }

        private static void SaveFields()
        {
            MSMQUtils MSMQ = new MSMQUtils();

            // Trying to open the queue
            MessageQueue queue = MSMQ.OpenOrCreatePrivateQueue(Config.WebRequestConfigQueue, typeof(Program).Namespace);

            // Sanit check
            if (queue == null)
            {
                logger.Fatal("Error to open a private WebConfigQueue. The field \"queue\" is null.");
                return;
            }

            string serializedConfig = Utils.Compress(JsonConvert.SerializeObject(Config));
            queue.Send(serializedConfig);
        }

        private static void Execute(Logger logger)
        {
            logger.Info("Start");


            // Initialization WebRequests
            WebRequests client = new WebRequests();
            SharedLibrary.Utils.WebRequestsUtils.InitializeWebRequest(Config.InitialUrl,out client, Config);

            // Get Request
            string htmlResponse = SharedLibrary.Utils.WebRequestsUtils.Get(ref client, logger, Config.InitialUrl);

            // Checking if html response is valid
            if (String.IsNullOrWhiteSpace(htmlResponse))
            {
                logger.Fatal("HtmlResponse is null or empty");
                Environment.Exit(-101);
            }

            client.Dispose();

            // Loading HTML into MAP
            HtmlDocument map = new HtmlDocument();
            map.LoadHtml(htmlResponse);



            //Scrape Urls
            logger.Trace("Scraping section urls");
            List<Section> sections = ScrapeSections(map);

            if (sections == null || sections.Count == 0)
            {
                logger.Fatal("Couldn't build any section object. Aborting program");
                Environment.Exit(-103);
            }


            // Send messages to Queue
            SendMessage(Config.TargetQueue, sections);

            logger.Info("End");
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
                logger.Trace("Sending " + section.Title + " to " + queuename + " queue");

                string serializedSection = Utils.Compress(JsonConvert.SerializeObject(section));
                queue.Send(serializedSection);
            }

            queue.Dispose();
        }

        private static List<Section> ScrapeSections(HtmlDocument map)
        {
            List<Section> sectionObjects = new List<Section>();
           
            // Get section urls
            HtmlNodeCollection secNodes = map.DocumentNode.SelectNodes(Config.SectionXPath);

            // Sanity Check
            if (secNodes == null || secNodes.Count == 0)
            {
                logger.Error("Section nodes could not be found");
                return null;
            }

            // Iterate over all section urls
            foreach (HtmlNode node in secNodes)
            {
                // Section Parser with some information
                Section section = ParseSectionInitInfo(node);

                if (section != null)
                {
                    // Add to list if not already present
                    if (!sectionObjects.Any(x => x.Url.Equals(section.Url)))
                        sectionObjects.Add(section);
                }
            }

            return sectionObjects;
        }

        private static Section ParseSectionInitInfo(HtmlNode node)
        {
            string title        = String.Empty;
            string description  = String.Empty;
            string url          = String.Empty;
            int numberOfViews   = 0;
            int numberOfTopics  = 0;

            // Is there a section filter?
            string[] relevantSections = null;

            if (Config.SectionsList.Contains(","))
                relevantSections = Config.SectionsList.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries);
            else if (!String.IsNullOrWhiteSpace(Config.SectionsList))
                relevantSections = new[] { Config.SectionsList.Trim() };

            // Check relevantSections
            if (relevantSections == null)
            {
                logger.Warn("Relevat Sections could not be found");
                return null;
            }


            // Extract Title
            HtmlNode titleNode = node.SelectSingleNode(".//h2[@class='forumtitle']");
            if (titleNode != null && !String.IsNullOrWhiteSpace(titleNode.InnerText))
                title = titleNode.InnerText.Trim();


            logger.Trace("Processing \"{0}\" Section ...", title);

            // Extract Description
            HtmlNode descriptionNode = node.SelectSingleNode(".//p[@class='forumdescription']");
            if (descriptionNode != null && !String.IsNullOrWhiteSpace(descriptionNode.InnerText))
                description = descriptionNode.InnerText.Trim();

            // Is there restriction?
            if (relevantSections.Length >= 1)
            {
                if (!relevantSections.Any(title.Contains))
                {
                    logger.Trace("The " + title + " section wasn't processed");
                    return null;
                }
            }

            // Extract URL
            HtmlNode urlNode = node.SelectSingleNode(".//h2[@class='forumtitle']/a");
            if (urlNode != null && urlNode.Attributes["href"] != null)
            {
                url = urlNode.Attributes["href"].Value.Trim();
            }

            // Sanity Check
            if (String.IsNullOrWhiteSpace(url))
            {
                logger.Warn("Empty href value");
                return null;
            }

            // Complete URL
            if (!url.StartsWith("http"))
            {
                url = Utils.AbsoluteUri(Config.Host, url);
            }


            // Is it normal section? (some topics, comments etc)
            // Let's check
            HtmlNodeCollection topicNodes = node.SelectNodes(".//ul[contains(@class,'forumstats')]//li");
            if (topicNodes != null && topicNodes.Count > 0)
            {
                foreach (HtmlNode topicNode in topicNodes)
                {
                    int number = -1;
                    Int32.TryParse(String.Join("", Utils.Normalize(topicNode.InnerText).Where(c => Char.IsDigit(c))), out number);
                    if (number >= 0)
                    {
                        if (topicNode.InnerText.Contains("Tópicos"))
                            numberOfTopics = number;
                        else if (topicNode.InnerText.Contains("Posts"))
                            numberOfViews = number;
                    }
                }
            }
            else
                return null;


            Section section = new Section();
            section.Url             = url;
            section.Title           = title;
            section.Description     = description;
            section.NumberOfTopics  = numberOfTopics;
            section.NumberOfViews   = numberOfViews;


            return section;
        }

        private static void InitializeAppConfig()
        {
            ConfigFilePath = Utils.LoadConfigurationSetting("Config", "");
        }

        private static bool ParseConfigurationFile(string filepath)
        {

            // Valid filepath?
            if (String.IsNullOrWhiteSpace(filepath))
                return false;

            // File Exists?
            if (!File.Exists(filepath))
                return false;

            // Valid JSON?
            try {  Config = JsonConvert.DeserializeObject<BootstrapperConfig>(File.ReadAllText(ConfigFilePath)); }
            catch (Exception ex)
            {
                logger.Fatal(ex,"Error to deserialize json object");
                return false;
            }

            return true;
        }
    }
}
