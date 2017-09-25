using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SharedLibrary.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Models
{
    public class Topic
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string       _id                         { get; set; }

        [Required]
        public string       Source                      { get; set; }
        [Required]
        public string       Title                       { get; set; }
        [Required]
        public string       Author                      { get; set; }
        [Required]
        [Url]
        public string       Url                         { get; set; }
        public DateTime     PublishDate                 { get; set; }
        [Required]
        public int          NumberOfComments            { get; set; }
        [Required]
        public int          NumberOfViews               { get; set; }
        public double       Evaluation                  { get; set; }
        public string       LastPostUsername            { get; set; }
        public DateTime     LastPostPublishDate         { get; set; }
        public string       Status                      { get; private set; }
        public Enums.Status StatusId                    { get { return _Status; } set { _Status = value; Status = EnumHelper.StatusToString(value); } }
        private Enums.Status _Status;                                // Private attribute to convert Enum to String
        [Required]
        public string       SectionTitle                { get; set; }
        [Required(ErrorMessageResourceType = typeof(Languages.Language), ErrorMessageResourceName = "TOPIC_ERROR_VALIDATION_NumberOfSectionPage")]
        [Range(1, int.MaxValue)]
        public int          NumberOfSectionPage         { get; set; }
        public DateTime     FirstCaptureDateTime        { get; set; }
        public int          Version                     { get; set; } // amount of times this auction was captured
    }
}
