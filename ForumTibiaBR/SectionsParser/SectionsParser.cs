using HtmlAgilityPack;
using MongoDB.Driver;
using Newtonsoft.Json;
using NLog;
using SharedLibrary.Models;
using SharedLibrary.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Messaging;
using WebUtilsLib;

namespace SectionsParser
{
    public class SectionsParser
    {
        private static string                   ConfigFilePath;
        private static string                   Source = "TibiaBR";
        private static InputConfig              Config;
        private static MongoDBUtils<Section>    MongoUtilsObj;
        private static Logger                   logger = null;

        public static void Main(string[] args)
        {
            // Loading New Logger
            logger = LogManager.GetCurrentClassLogger();

            // Initialization AppConfig
            InitializeAppConfig();

            // Load config
            logger.Trace("Loading config file");
            if (!ParseConfigurationFile(ConfigFilePath))
            {
                logger.Fatal("Error parsing configuration file! Aborting...");
                Environment.Exit(-101);
            }

            // Initialization Mongo
            if (!InitializeMongo())
            {
                logger.Fatal("Error parsing Mongo variables! Aborting...");
                Environment.Exit(-101);
            }

            // Save configuration input fields
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
            MessageQueue queue = MSMQ.OpenOrCreatePrivateQueue(Config.WebRequestConfigQueue, typeof(SectionsParser).Namespace);

            // Sanit check
            if (queue == null)
            {
                logger.Fatal("Error to open a private WebConfigQueue. The field \"queue\" is null.");
                return;
            }

            // Is there content in the queue? Let's remove it
            MSMQ.DeleteContentPrivateQueue(Config.WebRequestConfigQueue);
            
            string serializedConfig = Utils.Compress(JsonConvert.SerializeObject(Config));
            queue.Send(serializedConfig);
        }


        private static bool InitializeMongo()
        {
            MongoUtilsObj = new MongoDBUtils<Section>(Config.MongoUser,Config.MongoPassword,Config.MongoAddress,Config.MongoDatabase);

            // Sanit Check
            if (!MongoUtilsObj.IsValidMongoData(Config))
                return false;

            // Invalid Collection?
            if (!MongoUtilsObj.CollectionExistsAsync(Config.MongoCollection).Result)
                return false;

            // Open the Connection
            MongoUtilsObj.GetCollection(Config.MongoCollection);
  
            return true;
        }

        private static void Execute(Logger logger)
        {
            logger.Info("Start");

            MSMQUtils MSMQ = new MSMQUtils();

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
            logger.Trace("Scraping section urls...");
            List<Section> sections = ScrapeSections(map);

            if (sections == null || sections.Count == 0)
            {
                logger.Fatal("Couldn't build any section object. Aborting program");
                Environment.Exit(-103);
            }

            // Let's save the information
            if (!String.IsNullOrWhiteSpace(Config.TargetQueue) && !MongoUtilsObj.IsValidMongoData(Config))
            {
                // Send messages to Queue
                logger.Trace("Sending message to configuration queue...");
                SendMessage(Config.TargetQueue, sections);
            }
            else
            {
                // Send messages to collection
                logger.Trace("Sending message to MongoCollection...");
                SendMessage(sections);
            }

            logger.Info("End");
        }

        private static void SendMessage(List<Section> sections)
        {

            // Iterate over all sections
            foreach (Section section in sections)
            {

                // Before inserting, we need to check if lot was already inserted and update it if this is the case
                UpdateResult result = MongoUtilsObj.collection.UpdateOne(   Builders<Section>.Filter.Eq(r => r.MainUrl, section.MainUrl),
                                                                            Builders<Section>.Update
                                                                            .Set("NumberOfTopics"   , section.NumberOfTopics)
                                                                            .Set("FullUrl"          , section.FullUrl)
                                                                            .Set("NumberOfViews"    , section.NumberOfViews));

                // New Register
                if (result.MatchedCount == 0)
                    MongoUtilsObj.collection.InsertOne(section);
               
            }
        }

        private static void SendMessage(string queuename, List<Section> sections)
        {
            MSMQUtils MSMQ = new MSMQUtils();

            // Trying to open the queue
            MessageQueue queue = MSMQ.OpenOrCreatePrivateQueue(queuename, typeof(SectionsParser).Namespace);

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
           
            // Get section nodes
            HtmlNodeCollection secNodes = map.DocumentNode.SelectNodes(Config.SectionXPath);

            // Sanity Check
            if (secNodes == null || secNodes.Count == 0)
            {
                logger.Error("Section nodes could not be found");
                return null;
            }

            // Is there a section filter?
            string[] relevantSections = new string[] { };

            if (Config.SectionsList.Contains(","))
                relevantSections = Config.SectionsList.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries);
            else if (!String.IsNullOrWhiteSpace(Config.SectionsList))
                relevantSections = new[] { Config.SectionsList.Trim() };

            // Check relevantSections
            if (relevantSections == null)
                logger.Debug("Relevat Sections could not be found");


            // Iterate over all section nodes
            foreach (HtmlNode node in secNodes)
            {
                // Section Parser with some information
                Section section = ParseSection(node, relevantSections);

                if (section != null)
                {
                    // Add to list if not already present
                    if (!sectionObjects.Any(x => x.MainUrl.Equals(section.MainUrl)))
                        sectionObjects.Add(section);
                }
            }

            return sectionObjects;
        }

        private static Section ParseSection(HtmlNode node, string[] relevantSections)
        {
            string title        = String.Empty;
            string description  = String.Empty;
            string url          = String.Empty;
            int numberOfViews   = -1;
            int numberOfTopics  = -1;


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
                url = Utils.AbsoluteUri(Config.Host, url);


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

            // Initialize Section Object
            Section section = new Section();
            section.Source          = Source;
            section.FullUrl         = url;
            section.Title           = title;
            section.Description     = description;
            section.NumberOfTopics  = numberOfTopics;
            section.NumberOfViews   = numberOfViews;

            if (url.Contains("?"))
                section.MainUrl = url.Substring(0, url.IndexOf("?"));
            else
                section.MainUrl = section.FullUrl;

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
            try {  Config = JsonConvert.DeserializeObject<InputConfig>(File.ReadAllText(ConfigFilePath)); }
            catch (Exception ex)
            {
                logger.Fatal(ex,"Error to deserialize json object");
                return false;
            }

            return true;
        }

    }
}
