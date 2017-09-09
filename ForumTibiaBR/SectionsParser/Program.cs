using Newtonsoft.Json;
using NLog;
using SharedLibrary.Models;
using SharedLibrary.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Messaging;
using System.Text;
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
                logger.Trace(String.Format("Processing \"{0}\" section ...", section.Title));

                // Get Request
                string htmlResponse = SharedLibrary.Utils.WebRequestsUtils.Get(ref client, logger, section.Url);

                // Checking if html response is valid
                if (String.IsNullOrWhiteSpace(htmlResponse))
                {
                    logger.Error("HtmlResponse is null or empty");
                    continue;
                }

                client.Dispose();

                // Parse Sections
                ParseSections(ref section);

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

        private static void ParseSections(ref Section section)
        {
            //TODO
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
