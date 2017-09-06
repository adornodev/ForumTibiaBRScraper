using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Models
{
    public class Section
    {
        public string Title         { get; set; }
        public string Description   { get; set; }
        public string Url           { get; set; }
        public int NumberOfTopics   { get; set; }
        public int NumberOfViews    { get; set; }
        public List<Topic> Topics   { get; set; }
    }
}
