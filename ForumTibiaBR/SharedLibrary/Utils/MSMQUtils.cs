﻿using Newtonsoft.Json;
using SharedLibrary.Models;
using System;
using System.Collections.Generic;
using System.Messaging;

namespace SharedLibrary.Utils
{
    public class MSMQUtils
    {
        private readonly string MSMQPrivatePath = @".\Private$\";


        public MessageQueue OpenOrCreatePrivateQueue(string queuename, string processName)
        {
            MessageQueue messageQueue = null;

            queuename = String.Concat(MSMQPrivatePath, queuename);
            
            // Sanit check
            if (String.IsNullOrWhiteSpace(queuename))
                return null;

            // Does the queue exist?
            if (MessageQueue.Exists(queuename))
            {
                messageQueue = new MessageQueue(queuename);
            }
            else
            {
                // Iniatilize a new queue
                MessageQueue.Create(queuename);

                messageQueue        = new MessageQueue(queuename);
                messageQueue.Label  = String.Concat("Queue created by ", processName);
                messageQueue.DefaultPropertiesToSend.Recoverable = true;
            }

            return messageQueue;
        }

        public Object ReadPrivateQueue (string queuename, int timeoutInMinutes = 2, bool persist = false)
        {

            MessageQueue messageQueue = null;
            
            // Complete Queuename
            queuename = String.Concat(MSMQPrivatePath, queuename);

            // Sanit check
            if (String.IsNullOrWhiteSpace(queuename))
                return null;

            // Does the queue exist?
            if (!MessageQueue.Exists(queuename))
                return null;
              
            messageQueue = new MessageQueue(queuename);
            messageQueue.Formatter = new XmlMessageFormatter(new String[] { "System.String,mscorlib" });

            // Receive the message. 
            Message messageReceived = null;

            if (!persist)
                messageReceived = messageQueue.Receive(new TimeSpan(0, timeoutInMinutes, 0));
            else
                messageReceived = messageQueue.Peek(new TimeSpan(0, timeoutInMinutes, 0));
                        
            // Sanit Check
            if (messageReceived == null)
                return null;

            return messageReceived.Body;
        }

        public bool DeleteContentPrivateQueue (string queuename)
        {
            queuename = String.Concat(MSMQPrivatePath, queuename);

            // Does the queue exist?
            if (MessageQueue.Exists(queuename))
            {
                MessageQueue queue = new MessageQueue(queuename);

                queue.Purge();
                queue.Dispose();

                return true;
            }
            return false;
        }

        public bool DeletePrivateQueue(string queuename)
        {
            queuename = String.Concat(MSMQPrivatePath, queuename);

            // Does the queue exist?
            if (MessageQueue.Exists(queuename))
            {
                MessageQueue.Delete(queuename);
                return true;
            }
            return false;
        }

        public static bool SendMessage<T>(string queuename, string Namespace, List<T> objects, out string errorMessage)
        {
            errorMessage   = String.Empty;
            MSMQUtils MSMQ = new MSMQUtils();
            
            // Trying to open the queue
            MessageQueue queue = MSMQ.OpenOrCreatePrivateQueue(queuename, Namespace);
            
            // Sanit check
            if (queue == null)
            {
                errorMessage = String.Format("Error to Open or Create {0} Queue", queuename);
                return false;
            }

            try
            {
                // Iterate over all objects
                foreach (T obj in objects)
                {
                    string serializedObj = Utils.Compress(JsonConvert.SerializeObject(obj));
                    queue.Send(serializedObj);
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }

            queue.Dispose();

            return true;
        }

        public static InputConfig GetContentConfigurationQueue(string configurationQueueName)
        {
            MSMQUtils MSMQ = new MSMQUtils();

            // Read Private Queue 
            object obj  = MSMQ.ReadPrivateQueue(configurationQueueName, timeoutInMinutes: 1, persist: true);

            // Decompress string to json format
            string json = Utils.Decompress((string)obj);

            // Deserialize
            InputConfig config = JsonConvert.DeserializeObject<InputConfig>(json);

            return config;
        }
    }
}
