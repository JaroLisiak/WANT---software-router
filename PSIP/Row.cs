using PcapDotNet.Packets.Ethernet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Timers;
using PcapDotNet.Packets.IpV4;

namespace PSIP
{
    class Row
    {
        private char type;
        private IpV4Address ip;
        private int mask;
        private int inter;
        private IpV4Address next_hop;
        private int metric;
        private int updateTime, InvalidTime, HoltDownTime, FlushTime;
        private int Distance;

        public char Type
        {
            get
            {
                return type;
            }

            set
            {
                type = value;
            }
        }

        public IpV4Address Ip
        {
            get
            {
                return ip;
            }

            set
            {
                ip = value;
            }
        }

        public int Mask
        {
            get
            {
                return mask;
            }

            set
            {
                mask = value;
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

        public IpV4Address Next_hop
        {
            get
            {
                return next_hop;
            }

            set
            {
                next_hop = value;
            }
        }

        public int Metric
        {
            get
            {
                return metric;
            }

            set
            {
                metric = value;
            }
        }

        public int UpdateTime
        {
            get
            {
                return updateTime;
            }

            set
            {
                updateTime = value;
            }
        }

        public int InvalidTime1
        {
            get
            {
                return InvalidTime;
            }

            set
            {
                InvalidTime = value;
            }
        }

        public int HoltDownTime1
        {
            get
            {
                return HoltDownTime;
            }

            set
            {
                HoltDownTime = value;
            }
        }

        public int FlushTime1
        {
            get
            {
                return FlushTime;
            }

            set
            {
                FlushTime = value;
            }
        }

        public int Distance1
        {
            get
            {
                return Distance;
            }

            set
            {
                Distance = value;
            }
        }
    }
}
