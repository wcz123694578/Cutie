using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cutie
{
    public class McpRequest
    {
        public string jsonrpc { get; set; }

        public JToken id { get; set; }

        public string method { get; set; }

        public JObject @params { get; set; }
    }

    public sealed class McpResponse
    {
        public string jsonrpc { get; set; } = "2.0";

        public object id { get; set; }

        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public object result { get; set; }

        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public McpError error { get; set; }
    }

    public sealed class McpError
    {
        public int code { get; set; }

        public string message { get; set; }
    }
}
