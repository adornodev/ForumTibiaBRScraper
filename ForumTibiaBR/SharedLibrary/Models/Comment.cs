using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Models
{
    public class Comment
    {

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string       _id                     { get; set; }
        [Required]
        public string       Source                  { get; set; }
        [Required]
        [Url]
        public string       Url                     { get; set; }
        [Required]
        public string       Author                  { get; set; }
        public DateTime     FirstCaptureDateTime    { get; set; }
        public DateTime     LastCaptureDateTime     { get; set; }
        [Required]
        public DateTime     PublishDate             { get; set; }
        [Required]
        public User         User                    { get; set; }
        [Required]
        public string       Text                    { get; set; }
        [Required]
        public int          Position                { get; set; }
        [Required]
        public string       TopicTitle              { get; set; }
        public int          NumberOfPage            { get; set; }
        public int          Version                 { get; set; } // amount of times this comment was captured

        public Comment ()
        {
            this.FirstCaptureDateTime = DateTime.UtcNow;
            this.LastCaptureDateTime  = DateTime.UtcNow;
            this.Version              = 0;
            this.NumberOfPage         = -1;
        }
    }
}
