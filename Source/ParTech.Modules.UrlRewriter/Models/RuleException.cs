using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.IO;


namespace ParTech.Modules.UrlRewriter.Models
{
    /// <summary>
    /// CG 2014/6/23 - class created to load redirect origins that are coming via the 404, like PDF, because they are not controlled by the asp.net.
    /// The new updates to the redirects module intercepts this Uri, that is going to land on the 404 page if we don't handle it here and redirect to the destination (target) specified in the module
    /// </summary>
    ///
  
    [XmlRoot("File")]
    public class RuleException
    {
        //[XmlElement("type")]
        //public string type { get; set; }

        [XmlElement("TypeException")]
        public List<TypeException> TypeExceptions { get; set; }
    }


    public class TypeException
    {
        [XmlAttribute("Name")]
        public string name { get; set; }

       // [XmlElement("subtypes")]
      //  public List<SubType> subTypes { get; set; }

        [XmlElement("SubTypes")]
        public List<SubType> subTypes { get; set; }
    }

    
    
    public class SubType
    {
         [XmlElement("SubType")]
        public string subType { get; set; }
    }

}
