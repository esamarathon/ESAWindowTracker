using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ESAWindowTracker
{
    public class Config
    {
        public string EventShort { get; set; } = "default";
        public string PCID { get; set; } = "undefined";

        public RabbitConfig RabbitConfig { get; set; } = new RabbitConfig();
    }

    public class RabbitConfig
    {
        public string Host { get; set; } = "";
        public string VHost { get; set; } = "";
        public ushort Port { get; set; } = 5671;
        public bool Tls { get; set; } = true;
        public string User { get; set; } = "";
        public string Pass { get; set; } = "";
    }
}
