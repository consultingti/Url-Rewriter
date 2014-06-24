using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using ParTech.Modules.UrlRewriter.Models;
using System.IO;

namespace ParTech.Modules.UrlRewriter.data
{
    /// <summary>
    /// CG-2014-6-23 class to load the XML with the rule exceptions
    /// </summary>
    class DataRepository
    {

        public DataRepository()
        {
            deserializeFormXMl();
        }

        //property
        public  RuleException ruleExceptions;    
        
        //public static List<RuleException> DeserializeFromXML()
        private void deserializeFormXMl()
        {
            if (ruleExceptions == null)
            {

                XmlSerializer deserializer = new XmlSerializer(typeof(RuleException));

                string fileLocation = Settings.RuleExceptionsFileLocation;

                if (fileLocation.Length > 0)
                {
                    TextReader textReader = new StreamReader(fileLocation);

                    //  List<RuleException> ruleExceptions;
                    ruleExceptions = (RuleException)deserializer.Deserialize(textReader);

                    textReader.Close();

                    //  return ruleExceptions;

                }
                //else
                //   return null;
            }
            
        }

         
    }
}
