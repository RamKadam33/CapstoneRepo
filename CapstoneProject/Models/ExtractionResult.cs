using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CapstoneProject.Models
{
   

    public class ExtractionResult
    {
        public List<DocumentationField> Fields { get; set; } = new();
        public List<string> MissingFields { get; set; } = new();
    }
}
