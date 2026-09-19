using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cutie.Models
{
    public class SettingsModel
    {
        public SettingsModel()
        {
            McpServerPort = 57321;
        }
        public int McpServerPort { get; set; }
    }
}
