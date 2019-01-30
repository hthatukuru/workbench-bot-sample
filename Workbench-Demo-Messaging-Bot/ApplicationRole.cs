using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Workbench_Demo_Messaging_Bot
{
    public class ApplicationRole
    {
        [JsonProperty(Required = Required.Always)]
        public int Id { get; set; }

        [JsonProperty(Required = Required.Always)]
        public string Name { get; set; }

        public ApplicationRole(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}
