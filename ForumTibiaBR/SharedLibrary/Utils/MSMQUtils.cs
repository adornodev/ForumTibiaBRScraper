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


        public MessageQueue OpenPrivateQueue(string queuename, string processName)
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

                messageQueue = new MessageQueue(queuename);
                messageQueue.Label = String.Concat("Queue created by ", processName);
            }

            return messageQueue;
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
