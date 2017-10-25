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
    public class User
    {
        [Required]
        public string   Source                  { get; set; }
        [Required]
        public string   Name                    { get; set; }
        public DateTime RegisterDate            { get; set; }
        [Required]
        [Range(0,Int32.MaxValue, ErrorMessageResourceType = typeof(Languages.Language), ErrorMessageResourceName = "USER_ERROR_VALIDATION_NumberOfComments")]
        public int      NumberOfComments        { get; set; }
        public string   Rank                    { get; set; }
        [Required]
        [Url]
        public string   Url                     { get; set; }
        public int      Version                 { get; set; }

        public User ()
        {
            this.NumberOfComments = 0;
            this.Version          = 0;
            this.RegisterDate     = DateTime.MinValue;
        }
    }
}
