using System;
using System.Collections.Generic;
using System.Linq;
using System.Messaging;
using System.Text;
using System.Threading.Tasks;

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
                MessageQueue.Create(queuename);

                messageQueue        = new MessageQueue(queuename);
                messageQueue.Label  = String.Concat("Queue created by ", processName);
                messageQueue.DefaultPropertiesToSend.Recoverable = true;
            }

            return messageQueue;
        }

        public Object ReadPrivateQueue (string queuename, int timeoutInMinutes = 5, bool persist = false)
        {

            MessageQueue messageQueue = null;

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
    }
}
