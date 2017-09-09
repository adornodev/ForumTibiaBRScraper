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
        [Required]
        public string       Title                   { get; set; }

        [Required]
        public string       Author                  { get; set; }

        [Required]
        [Url]
        public string       Url                     { get; set; }
        
        public DateTime     PublishDate             { get; set; }

        [Required]
        public int          NumberOfComments        { get; set; }

        [Required]
        public int          NumberOfViews           { get; set; }

        public string       Evaluation              { get; set; }
        public string       LastPostUsername        { get; set; }
        public DateTime     LastPostPublishDate     { get; set; }
        public string       Status                  { get; private set; }
        public Enums.Status StatusId                { get { return _Status; } set { _Status = value; Status = EnumHelper.StatusToString(value); } }
        
        // Private attribute to convert Enum to String
        private Enums.Status _Status;

        [Required]
        public string       SectionTitle        { get; set; }

        [Required(ErrorMessageResourceType = typeof(Languages.Language), ErrorMessageResourceName = "TOPIC_ERROR_VALIDATION_NumberOfSectionPage")]
        [Range(1, int.MaxValue)]
        public int          NumberOfSectionPage { get; set; }

        [Required]
        public DateTime     CaptureDateTime     { get; set; }

        // amount of times this auction was captured
        public int          Version             { get; set; } 
    }
}
