using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Models
{
    public class BootstrapperConfig
    {
        public string ProcessName        { get; set; }
        public string InitialUrl         { get; set; }
        public string SectionXPath       { get; set; }
        public string SectionsList       { get; set; }
        public string Host               { get; set; }
        public bool   KeepAlive          { get; set; }
        public string Accept             { get; set; }
        public string UserAgent          { get; set; }
        public string ContentType        { get; set; }
        public string Charset            { get; set; }
        public int    Timeout            { get; set; }
        public string AcceptEncoding     { get; set; }
        public string AcceptLanguage     { get; set; }
        public string TargetQueue        { get; set; }


        public bool VerifyMandatoryFields(BootstrapperConfig Config)
        {
            bool sucess = true;

            sucess = sucess && !String.IsNullOrWhiteSpace(ProcessName);
            sucess = sucess && !String.IsNullOrWhiteSpace(InitialUrl);
            sucess = sucess && !String.IsNullOrWhiteSpace(SectionXPath);
            sucess = sucess && !String.IsNullOrWhiteSpace(SectionsList);
            sucess = sucess && !String.IsNullOrWhiteSpace(Host);
            sucess = sucess && !String.IsNullOrWhiteSpace(Accept);
            sucess = sucess && !String.IsNullOrWhiteSpace(UserAgent);
            sucess = sucess && !String.IsNullOrWhiteSpace(AcceptEncoding);
            sucess = sucess && !String.IsNullOrWhiteSpace(AcceptLanguage);
            sucess = sucess && !String.IsNullOrWhiteSpace(TargetQueue);

            return sucess;
        }
    }
}
