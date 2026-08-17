using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CapstoneProject.Models
{
    
       

    public class ConfluenceTemplate
    {
        public string Title { get; set; } = string.Empty;
        public List<DocumentationField> Fields { get; set; } = new();
    }
}

