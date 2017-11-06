using MongoDB.Bson;
using MongoDB.Driver;
using SharedLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Utils
{
    public class MongoDBUtils<T>
    {
        private static string               _connectionString;
        private static string               _dbName;
        
        public IMongoCollection<T>         collection;

        public MongoDBUtils(string user, string password, string address, string database, int connectionTimeoutMilliseconds = 30000, int socketTimeoutMilliseconds = 90000)
        {
            _dbName           = database;
            _connectionString = BuildConnectionString(user, password, true, address, connectionTimeoutMilliseconds, socketTimeoutMilliseconds);
        }

        /// <summary>
        /// Builds the connection string for MongoDB.
        /// </summary>
        /// <param name="login">The login.</param>
        /// <param name="password">The password.</param>
        /// <param name="safeMode">The safe mode. True to receive write confirmation from mongo.</param>
        /// <param name="addresses">List of addresses. Format: host1[:port1][,host2[:port2],...[,hostN[:portN]]]</param>
        /// <param name="connectionTimeoutMilliseconds">The time to attempt a connection before timing out.</param>
        /// <param name="socketTimeoutMilliseconds">The time to attempt a send or receive on a socket before the attempt times out.</param>
        private string BuildConnectionString(string login, string password, bool safeMode, string addresses, int connectionTimeoutMilliseconds, int socketTimeoutMilliseconds)
        {
            var cb              = new MongoUrlBuilder("mongodb://" + addresses.Replace("mongodb://", ""));
            cb.Username         = login;
            cb.Password         = password;
            cb.ConnectionMode   = ConnectionMode.Automatic;
            cb.W                = safeMode ? WriteConcern.W1.W : WriteConcern.Unacknowledged.W;

            if (connectionTimeoutMilliseconds < 15000)
                connectionTimeoutMilliseconds = 15000;

            if (socketTimeoutMilliseconds < 15000)
                    socketTimeoutMilliseconds = 15000;

            cb.ConnectTimeout           = TimeSpan.FromMilliseconds(connectionTimeoutMilliseconds);
            cb.SocketTimeout            = TimeSpan.FromMilliseconds(socketTimeoutMilliseconds);
            cb.MaxConnectionIdleTime    = TimeSpan.FromSeconds(30);

            // Generate final connection string
            return cb.ToString();
        }

        public bool IsValidMongoData(InputConfig Config)
        {
            bool success = true;

            success = success && !String.IsNullOrWhiteSpace(Config.MongoDatabase);
            success = success && !String.IsNullOrWhiteSpace(Config.MongoUser);
            success = success && !String.IsNullOrWhiteSpace(Config.MongoPassword);
            success = success && !String.IsNullOrWhiteSpace(Config.MongoAddress);
            success = success && !String.IsNullOrWhiteSpace(Config.MongoCollection);

            return success;
        }

        public bool CreateCollection(string collectioName)
        {
            // Sanit Check
            if (String.IsNullOrWhiteSpace(_dbName) || collection == null)
                return false;

            try
            {
                collection.Indexes.CreateOne(new BsonDocument("MainUrl", -1), new CreateIndexOptions {Background = true });
            }
            catch { return false; }

            return true;
        }

        public async Task<bool> CollectionExistsAsync(string collectionName)
        {
            var filter = new BsonDocument("name", collectionName);

            // Filter by collection name
            var collections = await GetDatabase(null).ListCollectionsAsync(new ListCollectionsOptions { Filter = filter });

            GetCollection(collectionName);

            // Check for existence
            return await collections.AnyAsync();
        }

        public void GetCollection(string collectionName)
        {
            // Open Connection
            IMongoClient client = OpenConnection();

            // Get Database
            IMongoDatabase database = GetDatabase(client);

            // Get Collection
            IMongoCollection<T> collection = database.GetCollection<T>(collectionName);

            this.collection = collection;
        }

        private static IMongoClient OpenConnection()
        {
           // Create mongo client
           IMongoClient client = new MongoClient(_connectionString);

           return client;
        }

        public static IMongoDatabase GetDatabase(IMongoClient client)
        {
            if (client == null)
            {
                // Open Connection
                client = OpenConnection();
            }

            // GetDatabase
            IMongoDatabase database = client.GetDatabase(_dbName);

            return database;
        }
    }
}
