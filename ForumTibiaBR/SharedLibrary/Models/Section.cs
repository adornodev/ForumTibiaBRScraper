using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace SharedLibrary.Models
{
    [DataContract]
    public class Section
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string _id           { get; set; }

        [Required]
        public string Source        { get; set; }

        [DataMember]
        [Required(ErrorMessageResourceType = typeof(Languages.Language),ErrorMessageResourceName = "SECTION_ERROR_VALIDATION_Title")]
        public string Title         { get; set; }

        public string Description   { get; set; }

        [DataMember]
        [Required(ErrorMessageResourceType = typeof(Languages.Language), ErrorMessageResourceName = "SECTION_ERROR_VALIDATION_Url")]
        [Url]
        public string FullUrl       { get; set; }

        [Required(ErrorMessageResourceType = typeof(Languages.Language), ErrorMessageResourceName = "SECTION_ERROR_VALIDATION_Url")]
        [Url]
        public string MainUrl       { get; set; }

        public int NumberOfTopics   { get; set; }

        public int NumberOfViews    { get; set; }

        [DataMember]
        public int NumberOfAttempts { get; set; }

        public Section ()
        {
            this.NumberOfTopics = -1;
            this.NumberOfViews  = -1;
        }
    }
}
