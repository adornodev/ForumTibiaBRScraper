using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SharedLibrary.Models
{
    public class Section
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string _id           { get; set; }

        [Required]
        public string Source        { get; set; }
        [Required(ErrorMessageResourceType = typeof(Languages.Language),ErrorMessageResourceName = "SECTION_ERROR_VALIDATION_Title")]
        public string Title         { get; set; }

        public string Description   { get; set; }

        [Required(ErrorMessageResourceType = typeof(Languages.Language), ErrorMessageResourceName = "SECTION_ERROR_VALIDATION_Url")]
        [Url]
        public string FullUrl       { get; set; }

        [Required(ErrorMessageResourceType = typeof(Languages.Language), ErrorMessageResourceName = "SECTION_ERROR_VALIDATION_Url")]
        [Url]
        public string MainUrl       { get; set; }

        public int NumberOfTopics   { get; set; }

        public int NumberOfViews    { get; set; }

        public Section ()
        {
            this.NumberOfTopics = -1;
            this.NumberOfViews  = -1;
        }
    }
}
