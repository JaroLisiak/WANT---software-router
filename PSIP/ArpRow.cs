using PcapDotNet.Packets.Ethernet;
using PcapDotNet.Packets.IpV4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PSIP
{
    class ArpRow
    {
        private IpV4Address IP;
        private MacAddress MAC;
        private int inter;
        private int time;

        public IpV4Address IP1
        {
            get
            {
                return IP;
            }

            set
            {
                IP = value;
            }
        }

        public MacAddress MAC1
        {
            get
            {
                return MAC;
            }

            set
            {
                MAC = value;
            }
        }

        public int Inter
        {
            get
            {
                return inter;
            }

            set
            {
                inter = value;
            }
        }

        public int Time
        {
            get
            {
                return time;
            }

            set
            {
                time = value;
            }
        }
    }
}
