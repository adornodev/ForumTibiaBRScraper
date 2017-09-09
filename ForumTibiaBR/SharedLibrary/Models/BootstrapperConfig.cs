using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Models
{
    public class BootstrapperConfig
    {
        [Required]
        public string ProcessName           { get; set; }
        [Required]
        [Url]
        public string InitialUrl            { get; set; }
        [Required]
        public string SectionXPath          { get; set; }
        public string SectionsList          { get; set; }
        [Required]
        public string Host                  { get; set; }
        public bool   KeepAlive             { get; set; }
        [Required]
        public string Accept                { get; set; }
        [Required]
        public string UserAgent             { get; set; }
        public string ContentType           { get; set; }
        [Required]
        public string Charset               { get; set; }
        public int    Timeout               { get; set; }
        [Required]
        public string AcceptEncoding        { get; set; }
        [Required]
        public string AcceptLanguage        { get; set; }
        [Required]
        public string TargetQueue           { get; set; }
        [Required]
        public string WebRequestConfigQueue { get; set; }


        public bool VerifyMandatoryFields(BootstrapperConfig Config)
        {
            bool sucess = true;

            sucess = sucess && !String.IsNullOrWhiteSpace(ProcessName);
            sucess = sucess && !String.IsNullOrWhiteSpace(InitialUrl);
            sucess = sucess && !String.IsNullOrWhiteSpace(SectionXPath);
            sucess = sucess && !String.IsNullOrWhiteSpace(Host);
            sucess = sucess && !String.IsNullOrWhiteSpace(Accept);
            sucess = sucess && !String.IsNullOrWhiteSpace(UserAgent);
            sucess = sucess && !String.IsNullOrWhiteSpace(Charset);
            sucess = sucess && !String.IsNullOrWhiteSpace(AcceptEncoding);
            sucess = sucess && !String.IsNullOrWhiteSpace(AcceptLanguage);
            sucess = sucess && !String.IsNullOrWhiteSpace(TargetQueue);
            sucess = sucess && !String.IsNullOrWhiteSpace(WebRequestConfigQueue);

            return sucess;
        }
    }
}
